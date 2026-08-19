package com.assetflow.manager.ui.screens

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Inventory2
import androidx.compose.material.icons.filled.Visibility
import androidx.compose.material.icons.filled.VisibilityOff
import androidx.compose.material3.Button
import androidx.compose.material3.Checkbox
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.compose.ui.text.input.VisualTransformation
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import com.assetflow.manager.ui.theme.Paleta

/**
 * Marca de la aplicación: icono en cuadrado azul más nombre.
 *
 * Es la misma composición que la cabecera del cliente de escritorio, para que
 * las dos aplicaciones se reconozcan como la misma.
 */
@Composable
fun MarcaAssetFlow(modifier: Modifier = Modifier, sobreOscuro: Boolean = false) {
    Row(modifier = modifier, verticalAlignment = Alignment.CenterVertically) {
        Surface(
            modifier = Modifier.size(38.dp),
            shape = CircleShape,
            color = Paleta.Accent
        ) {
            Box(contentAlignment = Alignment.Center) {
                Icon(
                    imageVector = Icons.Filled.Inventory2,
                    contentDescription = null,
                    tint = androidx.compose.ui.graphics.Color.White,
                    modifier = Modifier.size(20.dp)
                )
            }
        }

        Spacer(Modifier.size(12.dp))

        Text(
            text = "AssetFlow Manager",
            style = MaterialTheme.typography.titleLarge,
            color = if (sobreOscuro) {
                androidx.compose.ui.graphics.Color.White
            } else {
                MaterialTheme.colorScheme.onBackground
            }
        )
    }
}

/** Caja de aviso, en el estilo de los `Badge` del escritorio. */
@Composable
fun Aviso(
    texto: String,
    esError: Boolean = false,
    modifier: Modifier = Modifier
) {
    Surface(
        modifier = modifier.fillMaxWidth(),
        color = if (esError) Paleta.DangerSoft else Paleta.AccentSoft,
        shape = MaterialTheme.shapes.small
    ) {
        Text(
            text = texto,
            modifier = Modifier.padding(14.dp),
            style = MaterialTheme.typography.bodySmall,
            color = if (esError) Paleta.Danger else Paleta.Accent
        )
    }
}

@Composable
fun PantallaServidor(
    servidorActual: String,
    error: String?,
    onGuardar: (String) -> Unit
) {
    var direccion by remember { mutableStateOf(servidorActual) }

    Column(
        modifier = Modifier
            .fillMaxSize()
            .verticalScroll(rememberScrollState())
            .padding(24.dp),
        horizontalAlignment = Alignment.CenterHorizontally,
        verticalArrangement = Arrangement.Center
    ) {
        MarcaAssetFlow()

        Spacer(Modifier.height(28.dp))

        Text(
            text = "Configura el servidor",
            style = MaterialTheme.typography.headlineMedium,
            textAlign = TextAlign.Center
        )

        Spacer(Modifier.height(8.dp))

        Text(
            text = "Indica la dirección de la API de tu asociación. " +
                    "Puedes cambiarla más adelante.",
            style = MaterialTheme.typography.bodyMedium,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
            textAlign = TextAlign.Center,
            modifier = Modifier.widthIn(max = 400.dp)
        )

        Spacer(Modifier.height(24.dp))

        OutlinedTextField(
            value = direccion,
            onValueChange = { direccion = it },
            label = { Text("Dirección del servidor") },
            placeholder = { Text("https://servidor.ejemplo.org") },
            singleLine = true,
            keyboardOptions = KeyboardOptions(
                keyboardType = KeyboardType.Uri,
                imeAction = ImeAction.Done
            ),
            modifier = Modifier
                .fillMaxWidth()
                .widthIn(max = 400.dp)
        )

        Spacer(Modifier.height(8.dp))

        Text(
            text = "Debe empezar por https://. Sólo se admite http:// contra el " +
                    "propio equipo, para desarrollo.",
            style = MaterialTheme.typography.bodySmall,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
            modifier = Modifier.widthIn(max = 400.dp)
        )

        if (error != null) {
            Spacer(Modifier.height(16.dp))
            Aviso(error, esError = true, modifier = Modifier.widthIn(max = 400.dp))
        }

        Spacer(Modifier.height(24.dp))

        Button(
            onClick = { onGuardar(direccion) },
            modifier = Modifier
                .fillMaxWidth()
                .widthIn(max = 400.dp)
                .height(48.dp)
        ) {
            Text("Guardar")
        }
    }
}

@Composable
fun PantallaAcceso(
    ultimoUsuario: String,
    recordarInicial: Boolean,
    servidor: String,
    error: String?,
    trabajando: Boolean,
    onAcceder: (String, String, Boolean) -> Unit,
    onRecuperar: () -> Unit,
    onCambiarServidor: () -> Unit
) {
    var usuario by remember { mutableStateOf(ultimoUsuario) }
    var contrasena by remember { mutableStateOf("") }
    var recordar by remember { mutableStateOf(recordarInicial) }
    var verContrasena by remember { mutableStateOf(false) }

    Column(
        modifier = Modifier
            .fillMaxSize()
            .verticalScroll(rememberScrollState())
            .padding(24.dp),
        horizontalAlignment = Alignment.CenterHorizontally,
        verticalArrangement = Arrangement.Center
    ) {
        MarcaAssetFlow()

        Spacer(Modifier.height(28.dp))

        Text(
            text = "Inicia sesión",
            style = MaterialTheme.typography.headlineMedium
        )

        Spacer(Modifier.height(24.dp))

        OutlinedTextField(
            value = usuario,
            onValueChange = { usuario = it },
            label = { Text("Usuario") },
            singleLine = true,
            enabled = !trabajando,
            keyboardOptions = KeyboardOptions(imeAction = ImeAction.Next),
            modifier = Modifier
                .fillMaxWidth()
                .widthIn(max = 400.dp)
        )

        Spacer(Modifier.height(14.dp))

        OutlinedTextField(
            value = contrasena,
            onValueChange = { contrasena = it },
            label = { Text("Contraseña") },
            singleLine = true,
            enabled = !trabajando,
            visualTransformation = if (verContrasena) {
                VisualTransformation.None
            } else {
                PasswordVisualTransformation()
            },
            keyboardOptions = KeyboardOptions(
                keyboardType = KeyboardType.Password,
                imeAction = ImeAction.Done
            ),
            trailingIcon = {
                IconButton(onClick = { verContrasena = !verContrasena }) {
                    Icon(
                        imageVector = iconoVisibilidad(verContrasena),
                        contentDescription = if (verContrasena) {
                            "Ocultar contraseña"
                        } else {
                            "Mostrar contraseña"
                        }
                    )
                }
            },
            modifier = Modifier
                .fillMaxWidth()
                .widthIn(max = 400.dp)
        )

        Spacer(Modifier.height(8.dp))

        Row(
            modifier = Modifier
                .fillMaxWidth()
                .widthIn(max = 400.dp),
            verticalAlignment = Alignment.CenterVertically
        ) {
            Checkbox(
                checked = recordar,
                onCheckedChange = { recordar = it },
                enabled = !trabajando
            )
            Text("Mantener la sesión iniciada", style = MaterialTheme.typography.bodyMedium)
        }

        if (error != null) {
            Spacer(Modifier.height(12.dp))
            Aviso(error, esError = true, modifier = Modifier.widthIn(max = 400.dp))
        }

        Spacer(Modifier.height(20.dp))

        Button(
            onClick = { onAcceder(usuario, contrasena, recordar) },
            enabled = !trabajando,
            modifier = Modifier
                .fillMaxWidth()
                .widthIn(max = 400.dp)
                .height(48.dp)
        ) {
            if (trabajando) {
                CircularProgressIndicator(
                    modifier = Modifier.size(20.dp),
                    strokeWidth = 2.dp,
                    color = MaterialTheme.colorScheme.onPrimary
                )
            } else {
                Text("Acceder")
            }
        }

        Spacer(Modifier.height(4.dp))

        TextButton(onClick = onRecuperar, enabled = !trabajando) {
            Text("He olvidado mi contraseña")
        }

        Spacer(Modifier.height(20.dp))

        Text(
            text = servidor.ifBlank { "sin servidor configurado" },
            style = MaterialTheme.typography.bodySmall,
            color = MaterialTheme.colorScheme.onSurfaceVariant
        )

        TextButton(onClick = onCambiarServidor, enabled = !trabajando) {
            Text("Cambiar servidor")
        }
    }
}

/**
 * Cambio obligatorio de la contraseña provisional.
 *
 * No hay forma de saltarla: el único botón que no cambia la contraseña cierra
 * la sesión. Eso es comodidad de interfaz, no la medida de seguridad — el
 * servidor responde 403 a todo lo demás mientras la contraseña sea provisional.
 */
@Composable
fun PantallaCambioObligatorio(
    nombre: String,
    error: String?,
    trabajando: Boolean,
    onCambiar: (String, String, String) -> Unit,
    onCerrarSesion: () -> Unit
) {
    var actual by remember { mutableStateOf("") }
    var nueva by remember { mutableStateOf("") }
    var repetida by remember { mutableStateOf("") }

    Column(
        modifier = Modifier
            .fillMaxSize()
            .verticalScroll(rememberScrollState())
            .padding(24.dp),
        horizontalAlignment = Alignment.CenterHorizontally,
        verticalArrangement = Arrangement.Center
    ) {
        MarcaAssetFlow()

        Spacer(Modifier.height(28.dp))

        Text(
            text = "Elige tu contraseña",
            style = MaterialTheme.typography.headlineMedium,
            textAlign = TextAlign.Center
        )

        Spacer(Modifier.height(8.dp))

        Text(
            text = "Hola, $nombre. Estás usando una contraseña provisional: " +
                    "elige una propia para continuar.",
            style = MaterialTheme.typography.bodyMedium,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
            textAlign = TextAlign.Center,
            modifier = Modifier.widthIn(max = 400.dp)
        )

        Spacer(Modifier.height(24.dp))

        CampoContrasena("Contraseña provisional", actual, !trabajando) { actual = it }
        Spacer(Modifier.height(14.dp))
        CampoContrasena("Contraseña nueva", nueva, !trabajando) { nueva = it }
        Spacer(Modifier.height(14.dp))
        CampoContrasena("Repite la contraseña", repetida, !trabajando) { repetida = it }

        Spacer(Modifier.height(8.dp))

        Text(
            text = "Mínimo 10 caracteres, y distinta de la provisional. " +
                    "Al cambiarla se cerrarán las demás sesiones de tu cuenta.",
            style = MaterialTheme.typography.bodySmall,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
            modifier = Modifier.widthIn(max = 400.dp)
        )

        if (error != null) {
            Spacer(Modifier.height(12.dp))
            Aviso(error, esError = true, modifier = Modifier.widthIn(max = 400.dp))
        }

        Spacer(Modifier.height(20.dp))

        Button(
            onClick = { onCambiar(actual, nueva, repetida) },
            enabled = !trabajando,
            modifier = Modifier
                .fillMaxWidth()
                .widthIn(max = 400.dp)
                .height(48.dp)
        ) {
            Text("Guardar contraseña")
        }

        TextButton(onClick = onCerrarSesion, enabled = !trabajando) {
            Text("Cerrar sesión")
        }
    }
}

@Composable
private fun CampoContrasena(
    etiqueta: String,
    valor: String,
    habilitado: Boolean,
    onCambio: (String) -> Unit
) {
    OutlinedTextField(
        value = valor,
        onValueChange = onCambio,
        label = { Text(etiqueta) },
        singleLine = true,
        enabled = habilitado,
        visualTransformation = PasswordVisualTransformation(),
        keyboardOptions = KeyboardOptions(
            keyboardType = KeyboardType.Password,
            imeAction = ImeAction.Next
        ),
        modifier = Modifier
            .fillMaxWidth()
            .widthIn(max = 400.dp)
    )
}

private fun iconoVisibilidad(visible: Boolean): ImageVector =
    if (visible) Icons.Filled.VisibilityOff else Icons.Filled.Visibility
