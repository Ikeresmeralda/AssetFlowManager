package com.assetflow.manager.data

import android.content.Context
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.runBlocking
import kotlinx.coroutines.withContext
import retrofit2.Response

/**
 * Resultado de una llamada a la API, sin excepciones para el flujo normal.
 *
 * Que un servidor responda 401 no es excepcional: es una respuesta prevista.
 * Modelarlo como resultado y no como excepción obliga a tratarlo en el punto
 * de llamada en lugar de dejarlo subir hasta un `catch` genérico.
 */
sealed interface Resultado<out T> {
    data class Ok<T>(val valor: T) : Resultado<T>

    data class Error(
        val mensaje: String,
        val codigo: Int? = null,
        val tipo: String? = null
    ) : Resultado<Nothing> {

        val sinConexion: Boolean get() = codigo == null

        /** El servidor exige cambiar la contraseña provisional antes de nada. */
        val cambioPendiente: Boolean
            get() = codigo == 403 && tipo == "urn:assetflow:cambio-de-contrasena-pendiente"
    }
}

/**
 * Estado de la sesión y operaciones de autenticación.
 *
 * Guarda el token de acceso **sólo en memoria**: vive quince minutos y
 * persistirlo no ahorraría nada. Lo que se guarda cifrado es el token de
 * refresco, y sólo si se marca «recordar sesión».
 */
class Sesion(private val contexto: Context) {

    val ajustes = Ajustes(contexto)

    var usuario: CurrentUser? = null
        private set

    private var tokenDeAcceso: String? = null
    private var tokenDeRefresco: String? = null

    val hayServidor: Boolean get() = ajustes.hayServidor

    val esAdministrador: Boolean get() = usuario?.esAdministrador == true

    /**
     * Cliente contra el servidor configurado.
     *
     * Se reconstruye cada vez que cambia la dirección. La renovación se pasa
     * como función y no como dependencia para evitar el ciclo
     * cliente → sesión → cliente.
     */
    private var _api: ApiService? = null

    val api: ApiService
        get() = _api ?: crearApi().also { _api = it }

    private fun crearApi(): ApiService = ApiClient.crear(
        servidor = ajustes.servidor,
        proveedorDeToken = { tokenDeAcceso },
        renovar = { renovarBloqueante() }
    )

    fun cambiarServidor(direccion: String) {
        ajustes.servidor = direccion
        _api = null
    }

    // -----------------------------------------------------------------------
    // Autenticación
    // -----------------------------------------------------------------------

    suspend fun iniciarSesion(
        nombreUsuario: String,
        contrasena: String,
        recordar: Boolean
    ): Resultado<CurrentUser> = withContext(Dispatchers.IO) {
        llamar { api.login(LoginRequest(nombreUsuario, contrasena)) }
            .map { respuesta ->
                aplicar(respuesta, recordar)
                respuesta.user
            }
    }

    /**
     * Cambia la contraseña provisional y adopta la sesión que devuelve.
     *
     * Hay que quedarse con los tokens nuevos: el anterior sigue marcado como
     * pendiente de cambio y el servidor lo rechazaría en todo lo demás.
     */
    suspend fun cambiarContrasena(
        actual: String,
        nueva: String
    ): Resultado<CurrentUser> = withContext(Dispatchers.IO) {
        llamar { api.cambiarContrasena(ChangePasswordRequest(actual, nueva)) }
            .map { respuesta ->
                aplicar(respuesta, ajustes.recordarSesion)
                respuesta.user
            }
    }

    suspend fun solicitarRecuperacion(correo: String): Resultado<Unit> =
        withContext(Dispatchers.IO) {
            llamarSinCuerpo { api.olvideContrasena(ForgotPasswordRequest(correo)) }
        }

    /**
     * Intenta reanudar la sesión guardada al arrancar.
     *
     * @return el usuario, o null si no hay sesión guardada o ya no vale.
     */
    suspend fun reanudar(): CurrentUser? = withContext(Dispatchers.IO) {
        if (!ajustes.hayServidor) return@withContext null

        val guardado = ajustes.tokenDeRefresco ?: return@withContext null

        when (val resultado = llamar { api.refresh(RefreshRequest(guardado)) }) {
            is Resultado.Ok -> {
                aplicar(resultado.valor, recordar = true)
                resultado.valor.user
            }

            is Resultado.Error -> {
                // Un token que el servidor rechaza sólo puede provocar otro
                // arranque fallido. Se olvida, salvo que el fallo sea de red:
                // en ese caso puede seguir siendo válido.
                if (!resultado.sinConexion) {
                    ajustes.olvidarSesion()
                }
                null
            }
        }
    }

    suspend fun cerrarSesion() = withContext(Dispatchers.IO) {
        val refresco = tokenDeRefresco

        if (refresco != null) {
            // Si falla se continúa igualmente: quien ha pedido salir debe
            // salir, con o sin red.
            runCatching { api.logout(RefreshRequest(refresco)) }
        }

        limpiar()
    }

    fun limpiar() {
        usuario = null
        tokenDeAcceso = null
        tokenDeRefresco = null
        ajustes.olvidarSesion()
    }

    private fun aplicar(respuesta: AuthResponse, recordar: Boolean) {
        usuario = respuesta.user
        tokenDeAcceso = respuesta.accessToken
        tokenDeRefresco = respuesta.refreshToken

        ajustes.ultimoUsuario = respuesta.user.username
        ajustes.recordarSesion = recordar

        // El token de refresco sólo se persiste si se ha pedido recordar la
        // sesión. Si no, vive en memoria y desaparece al cerrar la aplicación.
        ajustes.tokenDeRefresco = if (recordar) respuesta.refreshToken else null
    }

    /**
     * Renovación síncrona, para el interceptor de OkHttp.
     *
     * `runBlocking` está justificado aquí y sólo aquí: el interceptor de OkHttp
     * es síncrono por contrato y ya se ejecuta en un hilo de entrada/salida, no
     * en el principal.
     */
    private fun renovarBloqueante(): String? {
        val refresco = tokenDeRefresco ?: return null

        return runBlocking {
            val respuesta = runCatching { api.refresh(RefreshRequest(refresco)) }.getOrNull()
            val cuerpo = respuesta?.body()

            if (respuesta?.isSuccessful == true && cuerpo != null) {
                aplicar(cuerpo, ajustes.recordarSesion)
                cuerpo.accessToken
            } else {
                // La cadena de refresco ya no vale: puede haber caducado, o
                // haberse revocado por reutilización.
                limpiar()
                null
            }
        }
    }

    // -----------------------------------------------------------------------
    // Envoltorio de llamadas
    // -----------------------------------------------------------------------

    /**
     * Ejecuta una llamada y traduce la respuesta a [Resultado].
     *
     * Los mensajes de error salen del `ProblemDetails` que envía la API, que
     * está redactado para poder enseñarse: no lleva trazas de pila, SQL ni
     * rutas del servidor.
     */
    suspend fun <T> llamar(bloque: suspend () -> Response<T>): Resultado<T> = try {
        val respuesta = bloque()
        val cuerpo = respuesta.body()

        if (respuesta.isSuccessful && cuerpo != null) {
            Resultado.Ok(cuerpo)
        } else if (respuesta.isSuccessful) {
            Resultado.Error("El servidor ha devuelto una respuesta vacía.", respuesta.code())
        } else {
            aError(respuesta)
        }
    } catch (e: Exception) {
        Resultado.Error(mensajeDeRed(e))
    }

    /** Como [llamar], para endpoints que no devuelven cuerpo. */
    suspend fun llamarSinCuerpo(bloque: suspend () -> Response<Unit>): Resultado<Unit> = try {
        val respuesta = bloque()

        if (respuesta.isSuccessful) Resultado.Ok(Unit) else aError(respuesta)
    } catch (e: Exception) {
        Resultado.Error(mensajeDeRed(e))
    }

    private fun <T> aError(respuesta: Response<T>): Resultado.Error {
        val problema = runCatching {
            respuesta.errorBody()?.string()?.takeIf { it.isNotBlank() }?.let {
                ApiClient.json.decodeFromString<ProblemDetails>(it)
            }
        }.getOrNull()

        val mensaje = problema?.detail
            ?: problema?.title
            ?: when (respuesta.code()) {
                401 -> "Usuario o contraseña incorrectos."
                403 -> "No tienes permiso para hacer eso."
                404 -> "No se ha encontrado."
                409 -> "Ese cambio ya no se puede aplicar. Actualiza la lista."
                429 -> "Demasiados intentos. Espera unos minutos."
                in 500..599 -> "El servidor ha tenido un problema. Inténtalo más tarde."
                else -> "No se ha podido completar la operación."
            }

        return Resultado.Error(mensaje, respuesta.code(), problema?.type)
    }

    private fun mensajeDeRed(e: Exception): String = when (e) {
        is java.net.UnknownHostException ->
            "No se encuentra el servidor. Comprueba la dirección y tu conexión."

        is java.net.SocketTimeoutException ->
            "El servidor no responde. Inténtalo de nuevo."

        is javax.net.ssl.SSLException ->
            "No se ha podido establecer una conexión segura con el servidor."

        else -> "Sin conexión con el servidor."
    }
}

/** Transforma el valor de un [Resultado] correcto. */
inline fun <T, R> Resultado<T>.map(transformar: (T) -> R): Resultado<R> = when (this) {
    is Resultado.Ok -> Resultado.Ok(transformar(valor))
    is Resultado.Error -> this
}
