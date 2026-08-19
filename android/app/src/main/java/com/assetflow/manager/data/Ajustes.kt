package com.assetflow.manager.data

import android.content.Context
import android.content.SharedPreferences
import androidx.security.crypto.EncryptedSharedPreferences
import androidx.security.crypto.MasterKey

/**
 * Ajustes de la aplicación y almacenamiento del token de sesión.
 *
 * Hay **dos** almacenes a propósito, y la división importa:
 *
 * - [preferencias] en claro: dirección del servidor y último usuario. No son
 *   secretos y guardarlos cifrados sólo complicaría el arranque.
 * - [seguras] cifrado con [EncryptedSharedPreferences]: el token de refresco.
 *   La clave maestra vive en el almacén de claves del sistema, respaldado por
 *   hardware en los dispositivos que lo tienen, y **nunca está en el APK**.
 *   Es el equivalente de DPAPI en el cliente de escritorio.
 *
 * Lo que esto protege y lo que no: protege frente a otra aplicación que lea el
 * directorio de datos, y frente a la extracción del almacenamiento del
 * dispositivo. No protege frente a un dispositivo rooteado con código
 * malicioso ejecutándose como esta misma aplicación.
 */
class Ajustes(contexto: Context) {

    private val preferencias: SharedPreferences =
        contexto.getSharedPreferences(FICHERO_CLARO, Context.MODE_PRIVATE)

    private val seguras: SharedPreferences by lazy {
        val clave = MasterKey.Builder(contexto)
            .setKeyScheme(MasterKey.KeyScheme.AES256_GCM)
            .build()

        EncryptedSharedPreferences.create(
            contexto,
            FICHERO_CIFRADO,
            clave,
            EncryptedSharedPreferences.PrefKeyEncryptionScheme.AES256_SIV,
            EncryptedSharedPreferences.PrefValueEncryptionScheme.AES256_GCM
        )
    }

    /**
     * Dirección de la API, sin barra final.
     *
     * Vacía la primera vez: no hay ningún servidor por defecto escrito en el
     * código. La versión anterior de esta aplicación llevaba una IP concreta
     * incrustada, que acabó publicada en el repositorio.
     */
    var servidor: String
        get() = preferencias.getString(CLAVE_SERVIDOR, "") ?: ""
        set(valor) = preferencias.edit().putString(CLAVE_SERVIDOR, valor.trimEnd('/')).apply()

    var ultimoUsuario: String
        get() = preferencias.getString(CLAVE_ULTIMO_USUARIO, "") ?: ""
        set(valor) = preferencias.edit().putString(CLAVE_ULTIMO_USUARIO, valor).apply()

    var recordarSesion: Boolean
        get() = preferencias.getBoolean(CLAVE_RECORDAR, false)
        set(valor) = preferencias.edit().putBoolean(CLAVE_RECORDAR, valor).apply()

    val hayServidor: Boolean get() = servidor.isNotBlank()

    /** Token de refresco guardado, o null si no se recuerda la sesión. */
    var tokenDeRefresco: String?
        get() = seguras.getString(CLAVE_REFRESCO, null)
        set(valor) {
            seguras.edit().apply {
                if (valor == null) remove(CLAVE_REFRESCO) else putString(CLAVE_REFRESCO, valor)
            }.apply()
        }

    /**
     * Borra el token guardado.
     *
     * Se llama al cerrar sesión y también cuando el servidor rechaza el
     * refresco: un token que ya no vale sólo puede causar un arranque fallido
     * la próxima vez.
     */
    fun olvidarSesion() {
        tokenDeRefresco = null
    }

    private companion object {
        const val FICHERO_CLARO = "assetflow_ajustes"
        const val FICHERO_CIFRADO = "assetflow_sesion"

        const val CLAVE_SERVIDOR = "servidor"
        const val CLAVE_ULTIMO_USUARIO = "ultimo_usuario"
        const val CLAVE_RECORDAR = "recordar_sesion"
        const val CLAVE_REFRESCO = "token_refresco"
    }
}
