package com.assetflow.manager

import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.height
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp
import androidx.lifecycle.viewmodel.compose.viewModel
import com.assetflow.manager.ui.AppViewModel
import com.assetflow.manager.ui.Pantalla
import com.assetflow.manager.ui.screens.PantallaAcceso
import com.assetflow.manager.ui.screens.PantallaCambioObligatorio
import com.assetflow.manager.ui.screens.PantallaPrincipal
import com.assetflow.manager.ui.screens.PantallaServidor
import com.assetflow.manager.ui.theme.AssetFlowTheme

class MainActivity : ComponentActivity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        enableEdgeToEdge()

        setContent {
            AssetFlowTheme {
                Surface(
                    modifier = Modifier.fillMaxSize(),
                    color = MaterialTheme.colorScheme.background
                ) {
                    AplicacionAssetFlow()
                }
            }
        }
    }
}

@Composable
private fun AplicacionAssetFlow(vm: AppViewModel = viewModel()) {
    val estado by vm.estado.collectAsState()

    var mostrarRecuperacion by remember { mutableStateOf(false) }
    var mensajeRecuperacion by remember { mutableStateOf<String?>(null) }

    when (estado.pantalla) {
        Pantalla.ARRANCANDO ->
            Box(Modifier.fillMaxSize(), Alignment.Center) { CircularProgressIndicator() }

        Pantalla.SERVIDOR -> PantallaServidor(
            servidorActual = estado.servidor,
            error = estado.error,
            onGuardar = vm::guardarServidor
        )

        Pantalla.ACCESO -> PantallaAcceso(
            ultimoUsuario = estado.ultimoUsuario,
            recordarInicial = estado.recordarSesion,
            servidor = estado.servidor,
            error = estado.error,
            trabajando = estado.trabajando,
            onAcceder = vm::acceder,
            onRecuperar = { mostrarRecuperacion = true },
            onCambiarServidor = vm::irAServidor
        )

        Pantalla.CAMBIO_OBLIGATORIO -> PantallaCambioObligatorio(
            nombre = estado.usuario?.firstName.orEmpty(),
            error = estado.error,
            trabajando = estado.trabajando,
            onCambiar = vm::cambiarContrasena,
            onCerrarSesion = vm::cerrarSesion
        )

        Pantalla.PRINCIPAL -> PantallaPrincipal(
            estado = estado,
            onSeccion = vm::irA,
            onBuscar = vm::buscar,
            onRefrescar = vm::refrescar,
            onSolicitar = vm::solicitarPrestamo,
            onPedirDevolucion = vm::pedirDevolucion,
            onAprobar = vm::aprobar,
            onRechazar = vm::rechazar,
            onAprobarDevolucion = vm::aprobarDevolucion,
            onRechazarDevolucion = vm::rechazarDevolucion,
            onCerrarSesion = vm::cerrarSesion
        )
    }

    if (mostrarRecuperacion) {
        DialogoRecuperacion(
            trabajando = estado.trabajando,
            onEnviar = { correo ->
                vm.solicitarRecuperacion(correo) { mensaje ->
                    mostrarRecuperacion = false
                    mensajeRecuperacion = mensaje
                }
            },
            onCerrar = { mostrarRecuperacion = false }
        )
    }

    mensajeRecuperacion?.let { mensaje ->
        AlertDialog(
            onDismissRequest = { mensajeRecuperacion = null },
            title = { Text("Solicitud enviada") },
            text = { Text(mensaje) },
            confirmButton = {
                TextButton(onClick = { mensajeRecuperacion = null }) { Text("Entendido") }
            }
        )
    }
}

/**
 * Solicitud de recuperación de contraseña.
 *
 * No dice si el correo existe, ni al enviarlo ni al fallar: el servidor
 * responde igual en los dos casos y esta ventana se limita a repetirlo.
 * Distinguirlos aquí reintroduciría por el cliente la enumeración de cuentas
 * que el servidor evita.
 */
@Composable
private fun DialogoRecuperacion(
    trabajando: Boolean,
    onEnviar: (String) -> Unit,
    onCerrar: () -> Unit
) {
    var correo by remember { mutableStateOf("") }

    AlertDialog(
        onDismissRequest = { if (!trabajando) onCerrar() },
        title = { Text("Recuperar contraseña") },
        text = {
            Column {
                Text(
                    "Un administrador recibirá tu solicitud y te dará una " +
                            "contraseña provisional.",
                    style = MaterialTheme.typography.bodyMedium
                )

                Spacer(Modifier.height(16.dp))

                OutlinedTextField(
                    value = correo,
                    onValueChange = { correo = it },
                    label = { Text("Correo de tu cuenta") },
                    singleLine = true,
                    enabled = !trabajando
                )
            }
        },
        confirmButton = {
            TextButton(
                onClick = { onEnviar(correo) },
                enabled = !trabajando && correo.isNotBlank()
            ) { Text("Enviar solicitud") }
        },
        dismissButton = {
            TextButton(onClick = onCerrar, enabled = !trabajando) { Text("Cancelar") }
        }
    )
}
