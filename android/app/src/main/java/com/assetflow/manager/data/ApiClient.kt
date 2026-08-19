package com.assetflow.manager.data

import com.jakewharton.retrofit2.converter.kotlinx.serialization.asConverterFactory
import kotlinx.serialization.json.Json
import okhttp3.HttpUrl.Companion.toHttpUrlOrNull
import okhttp3.Interceptor
import okhttp3.MediaType.Companion.toMediaType
import okhttp3.OkHttpClient
import okhttp3.Request
import okhttp3.Response
import retrofit2.Retrofit
import java.util.concurrent.TimeUnit

/**
 * Construye el cliente HTTP contra el servidor configurado.
 *
 * Tres cosas que hace, y el motivo de cada una:
 *
 * 1. **Rechaza cualquier URL que no sea https://**, salvo el bucle local. Las
 *    credenciales viajan en el cuerpo de la petición: por HTTP en claro
 *    cualquiera en la misma red las lee. La versión anterior de esta aplicación
 *    hablaba por HTTP con una dirección IP fija escrita en el código.
 * 2. **Añade el token de acceso** a cada petición y lo renueva cuando caduca,
 *    de forma transparente para la interfaz.
 * 3. **No registra los cuerpos** de las peticiones. La versión anterior tenía
 *    `HttpLoggingInterceptor.Level.BODY` activado siempre, lo que escribía las
 *    contraseñas y los tokens en el registro del sistema.
 */
object ApiClient {

    val json = Json {
        ignoreUnknownKeys = true
        coerceInputValues = true
        explicitNulls = false
    }

    /**
     * Comprueba que la dirección sea utilizable.
     *
     * @return un mensaje de error, o null si la dirección vale.
     */
    fun validarServidor(direccion: String): String? {
        val limpia = direccion.trim()

        if (limpia.isEmpty()) {
            return "Escribe la dirección del servidor."
        }

        if (!limpia.startsWith("http://") && !limpia.startsWith("https://")) {
            return "La dirección debe empezar por https://"
        }

        val url = limpia.toHttpUrlOrNull()
            ?: return "Esa dirección no es válida."

        if (!url.isHttps && !esBucleLocal(url.host)) {
            return "Sólo se admite https:// contra un servidor remoto. " +
                    "Por HTTP, cualquiera en la misma red puede leer tu contraseña."
        }

        return null
    }

    /**
     * `10.0.2.2` es la máquina anfitriona vista desde el emulador de Android.
     */
    private fun esBucleLocal(anfitrion: String): Boolean =
        anfitrion == "localhost" || anfitrion == "127.0.0.1" || anfitrion == "10.0.2.2"

    /**
     * Crea el servicio apuntando al servidor indicado.
     *
     * @param proveedorDeToken devuelve el token de acceso vigente, o null.
     * @param renovar intenta renovar la sesión; devuelve el token nuevo o null.
     */
    fun crear(
        servidor: String,
        proveedorDeToken: () -> String?,
        renovar: () -> String?
    ): ApiService {
        val cliente = OkHttpClient.Builder()
            .connectTimeout(15, TimeUnit.SECONDS)
            .readTimeout(30, TimeUnit.SECONDS)
            .addInterceptor(InterceptorDeAutenticacion(proveedorDeToken, renovar))
            .build()

        return Retrofit.Builder()
            .baseUrl(servidor.trimEnd('/') + "/")
            .client(cliente)
            .addConverterFactory(json.asConverterFactory("application/json".toMediaType()))
            .build()
            .create(ApiService::class.java)
    }
}

/**
 * Añade el token y renueva la sesión cuando el servidor responde 401.
 */
private class InterceptorDeAutenticacion(
    private val proveedorDeToken: () -> String?,
    private val renovar: () -> String?
) : Interceptor {

    override fun intercept(cadena: Interceptor.Chain): Response {
        val original = cadena.request()

        // Los endpoints de sesión no llevan token: mandarlo en el login no
        // aporta nada, y en el refresco puede confundir si está caducado.
        if (esDeSesion(original)) {
            return cadena.proceed(original)
        }

        val token = proveedorDeToken()
        val respuesta = cadena.proceed(conToken(original, token))

        if (respuesta.code != 401 || token == null) {
            return respuesta
        }

        respuesta.close()

        // Un solo intento. Reintentar en bucle contra un servidor que rechaza
        // el refresco sólo consigue agotar el limitador de peticiones y acabar
        // bloqueando la cuenta.
        val nuevo = renovar() ?: return cadena.proceed(conToken(original, null))

        return cadena.proceed(conToken(original, nuevo))
    }

    private fun conToken(peticion: Request, token: String?): Request =
        if (token == null) {
            peticion
        } else {
            peticion.newBuilder()
                .header("Authorization", "Bearer $token")
                .build()
        }

    private fun esDeSesion(peticion: Request): Boolean {
        val ruta = peticion.url.encodedPath

        return ruta.endsWith("/api/auth/login") ||
                ruta.endsWith("/api/auth/refresh") ||
                ruta.endsWith("/api/auth/forgot-password")
    }
}
