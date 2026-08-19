package com.assetflow.manager.ui

import android.app.Application
import androidx.lifecycle.AndroidViewModel
import androidx.lifecycle.viewModelScope
import com.assetflow.manager.data.CreateLoanItem
import com.assetflow.manager.data.CreateLoanRequest
import com.assetflow.manager.data.CurrentUser
import com.assetflow.manager.data.LoanDecisionRequest
import com.assetflow.manager.data.LoanDto
import com.assetflow.manager.data.MaterialDto
import com.assetflow.manager.data.Resultado
import com.assetflow.manager.data.Sesion
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch

/** Pantalla que se está mostrando. */
enum class Pantalla {
    ARRANCANDO,
    SERVIDOR,
    ACCESO,
    CAMBIO_OBLIGATORIO,
    PRINCIPAL
}

/** Pestaña de la pantalla principal. */
enum class Seccion { INVENTARIO, PRESTAMOS }

data class EstadoApp(
    val pantalla: Pantalla = Pantalla.ARRANCANDO,
    val seccion: Seccion = Seccion.INVENTARIO,
    val usuario: CurrentUser? = null,
    val servidor: String = "",
    val ultimoUsuario: String = "",
    val recordarSesion: Boolean = false,

    val materiales: List<MaterialDto> = emptyList(),
    val prestamos: List<LoanDto> = emptyList(),
    val busqueda: String = "",

    val cargando: Boolean = false,
    val trabajando: Boolean = false,
    val error: String? = null,
    val aviso: String? = null
) {
    val esAdministrador: Boolean get() = usuario?.esAdministrador == true
}

/**
 * Estado de la aplicación y llamadas a la API.
 *
 * Es deliberadamente un único ViewModel: la aplicación tiene dos pantallas de
 * datos y repartirlas en varios obligaría a coordinar la sesión entre ellos sin
 * ganar nada.
 *
 * **Aquí no se decide nada sobre permisos.** [EstadoApp.esAdministrador] sólo
 * sirve para elegir qué botones se dibujan; quien manipule la aplicación para
 * mostrarlos se encontrará con que la API responde 403.
 */
class AppViewModel(aplicacion: Application) : AndroidViewModel(aplicacion) {

    private val sesion = Sesion(aplicacion.applicationContext)

    private val _estado = MutableStateFlow(EstadoApp())
    val estado: StateFlow<EstadoApp> = _estado.asStateFlow()

    init {
        arrancar()
    }

    // -----------------------------------------------------------------------
    // Arranque
    // -----------------------------------------------------------------------

    private fun arrancar() {
        viewModelScope.launch {
            _estado.update {
                it.copy(
                    servidor = sesion.ajustes.servidor,
                    ultimoUsuario = sesion.ajustes.ultimoUsuario,
                    recordarSesion = sesion.ajustes.recordarSesion
                )
            }

            if (!sesion.hayServidor) {
                _estado.update { it.copy(pantalla = Pantalla.SERVIDOR) }
                return@launch
            }

            val usuario = sesion.reanudar()

            if (usuario == null) {
                _estado.update { it.copy(pantalla = Pantalla.ACCESO) }
                return@launch
            }

            entrarCon(usuario)
        }
    }

    /**
     * Decide a dónde va alguien recién autenticado.
     *
     * Una cuenta con contraseña provisional no puede ir a la pantalla
     * principal: el servidor rechazaría todas sus peticiones con 403.
     */
    private fun entrarCon(usuario: CurrentUser) {
        if (usuario.mustChangePassword) {
            _estado.update {
                it.copy(pantalla = Pantalla.CAMBIO_OBLIGATORIO, usuario = usuario, error = null)
            }
            return
        }

        _estado.update {
            it.copy(pantalla = Pantalla.PRINCIPAL, usuario = usuario, error = null)
        }

        refrescar()
    }

    // -----------------------------------------------------------------------
    // Servidor
    // -----------------------------------------------------------------------

    fun guardarServidor(direccion: String) {
        val problema = com.assetflow.manager.data.ApiClient.validarServidor(direccion)

        if (problema != null) {
            _estado.update { it.copy(error = problema) }
            return
        }

        sesion.cambiarServidor(direccion.trim())

        _estado.update {
            it.copy(
                servidor = sesion.ajustes.servidor,
                pantalla = Pantalla.ACCESO,
                error = null
            )
        }
    }

    fun irAServidor() {
        _estado.update { it.copy(pantalla = Pantalla.SERVIDOR, error = null) }
    }

    // -----------------------------------------------------------------------
    // Acceso
    // -----------------------------------------------------------------------

    fun acceder(usuario: String, contrasena: String, recordar: Boolean) {
        if (usuario.isBlank() || contrasena.isEmpty()) {
            _estado.update { it.copy(error = "Escribe tu usuario y tu contraseña.") }
            return
        }

        viewModelScope.launch {
            _estado.update { it.copy(trabajando = true, error = null) }

            when (val resultado = sesion.iniciarSesion(usuario.trim(), contrasena, recordar)) {
                is Resultado.Ok -> {
                    _estado.update {
                        it.copy(
                            trabajando = false,
                            ultimoUsuario = sesion.ajustes.ultimoUsuario,
                            recordarSesion = recordar
                        )
                    }
                    entrarCon(resultado.valor)
                }

                is Resultado.Error ->
                    _estado.update { it.copy(trabajando = false, error = resultado.mensaje) }
            }
        }
    }

    fun cambiarContrasena(actual: String, nueva: String, repetida: String) {
        when {
            actual.isEmpty() -> {
                _estado.update { it.copy(error = "Escribe la contraseña provisional.") }
                return
            }

            nueva.length < LONGITUD_MINIMA -> {
                _estado.update {
                    it.copy(error = "La contraseña debe tener al menos $LONGITUD_MINIMA caracteres.")
                }
                return
            }

            nueva != repetida -> {
                _estado.update { it.copy(error = "Las dos contraseñas no coinciden.") }
                return
            }

            // El servidor lo rechaza igual; comprobarlo aquí ahorra la petición.
            nueva == actual -> {
                _estado.update {
                    it.copy(error = "La contraseña nueva no puede ser la provisional.")
                }
                return
            }
        }

        viewModelScope.launch {
            _estado.update { it.copy(trabajando = true, error = null) }

            when (val resultado = sesion.cambiarContrasena(actual, nueva)) {
                is Resultado.Ok -> {
                    _estado.update {
                        it.copy(trabajando = false, aviso = "Contraseña actualizada.")
                    }
                    entrarCon(resultado.valor)
                }

                is Resultado.Error ->
                    _estado.update { it.copy(trabajando = false, error = resultado.mensaje) }
            }
        }
    }

    fun solicitarRecuperacion(correo: String, alTerminar: (String) -> Unit) {
        viewModelScope.launch {
            _estado.update { it.copy(trabajando = true, error = null) }

            val resultado = sesion.solicitarRecuperacion(correo.trim())

            _estado.update { it.copy(trabajando = false) }

            // El mensaje es el mismo exista o no la cuenta: es el propio
            // servidor el que responde igual en los dos casos, y distinguirlos
            // aquí convertiría esta pantalla en un comprobador de qué correos
            // están dados de alta.
            when (resultado) {
                is Resultado.Ok -> alTerminar(
                    "Si existe una cuenta asociada a ese correo, un administrador " +
                            "recibirá tu solicitud. Ponte en contacto con esa persona " +
                            "para que te dé la contraseña provisional."
                )

                is Resultado.Error -> _estado.update { it.copy(error = resultado.mensaje) }
            }
        }
    }

    fun cerrarSesion() {
        viewModelScope.launch {
            sesion.cerrarSesion()

            _estado.update {
                EstadoApp(
                    pantalla = Pantalla.ACCESO,
                    servidor = sesion.ajustes.servidor,
                    ultimoUsuario = sesion.ajustes.ultimoUsuario
                )
            }
        }
    }

    // -----------------------------------------------------------------------
    // Datos
    // -----------------------------------------------------------------------

    fun irA(seccion: Seccion) {
        _estado.update { it.copy(seccion = seccion, error = null) }
    }

    fun buscar(texto: String) {
        _estado.update { it.copy(busqueda = texto) }
    }

    fun refrescar() {
        viewModelScope.launch {
            _estado.update { it.copy(cargando = true, error = null) }

            val texto = _estado.value.busqueda.trim()

            val materiales = sesion.llamar {
                sesion.api.materiales(
                    busqueda = texto.takeIf { it.isNotEmpty() },
                    pagina = 1,
                    tamano = 100
                )
            }

            val prestamos = sesion.llamar { sesion.api.prestamos() }

            // Se desempaquetan con `when` y no con `as?`: con `as?` el tipo
            // genérico se pierde (`Resultado.Ok<*>`) y el compilador ya no sabe
            // qué hay dentro.
            var listaMateriales: List<MaterialDto>? = null
            var listaPrestamos: List<LoanDto>? = null
            var fallo: String? = null

            when (materiales) {
                is Resultado.Ok -> listaMateriales = materiales.valor.items
                is Resultado.Error -> fallo = materiales.mensaje
            }

            when (prestamos) {
                is Resultado.Ok -> listaPrestamos = prestamos.valor
                is Resultado.Error -> if (fallo == null) fallo = prestamos.mensaje
            }

            _estado.update { actual ->
                actual.copy(
                    cargando = false,
                    materiales = listaMateriales ?: actual.materiales,
                    prestamos = listaPrestamos ?: actual.prestamos,
                    error = fallo
                )
            }
        }
    }

    fun solicitarPrestamo(material: MaterialDto, cantidad: Int) {
        operar {
            sesion.llamar {
                sesion.api.crearPrestamo(
                    CreateLoanRequest(items = listOf(CreateLoanItem(material.id, cantidad)))
                )
            }
        }
    }

    fun pedirDevolucion(prestamo: LoanDto) = operar {
        sesion.llamar { sesion.api.pedirDevolucion(prestamo.id, LoanDecisionRequest()) }
    }

    fun aprobar(prestamo: LoanDto) = operar {
        sesion.llamar { sesion.api.aprobar(prestamo.id, LoanDecisionRequest()) }
    }

    fun rechazar(prestamo: LoanDto) = operar {
        sesion.llamar { sesion.api.rechazar(prestamo.id, LoanDecisionRequest()) }
    }

    fun aprobarDevolucion(prestamo: LoanDto) = operar {
        sesion.llamar { sesion.api.aprobarDevolucion(prestamo.id, LoanDecisionRequest()) }
    }

    fun rechazarDevolucion(prestamo: LoanDto) = operar {
        sesion.llamar { sesion.api.rechazarDevolucion(prestamo.id, LoanDecisionRequest()) }
    }

    /**
     * Ejecuta una operación de escritura y refresca al terminar.
     *
     * [EstadoApp.trabajando] bloquea la interfaz mientras dura: sin eso, dos
     * toques seguidos mandan dos peticiones y la segunda recibe un 409 del
     * servidor, que es un error correcto pero desconcertante.
     */
    private fun operar(bloque: suspend () -> Resultado<*>) {
        if (_estado.value.trabajando) return

        viewModelScope.launch {
            _estado.update { it.copy(trabajando = true, error = null) }

            when (val resultado = bloque()) {
                is Resultado.Ok -> {
                    _estado.update { it.copy(trabajando = false) }
                    refrescar()
                }

                is Resultado.Error -> {
                    // Si el servidor exige cambiar la contraseña, la sesión no
                    // sirve para nada: se manda a esa pantalla en lugar de
                    // dejar a la persona pulsando botones que dan 403.
                    if (resultado.cambioPendiente) {
                        _estado.update {
                            it.copy(trabajando = false, pantalla = Pantalla.CAMBIO_OBLIGATORIO)
                        }
                    } else {
                        _estado.update { it.copy(trabajando = false, error = resultado.mensaje) }
                    }
                }
            }
        }
    }

    fun descartarError() {
        _estado.update { it.copy(error = null, aviso = null) }
    }

    private companion object {
        const val LONGITUD_MINIMA = 10
    }
}
