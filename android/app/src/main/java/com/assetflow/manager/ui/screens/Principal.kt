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
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Inventory2
import androidx.compose.material.icons.filled.Refresh
import androidx.compose.material.icons.filled.SwapHoriz
import androidx.compose.material3.Button
import androidx.compose.material3.Card
import androidx.compose.material3.CardDefaults
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.NavigationBar
import androidx.compose.material3.NavigationBarItem
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.TopAppBar
import androidx.compose.material3.TopAppBarDefaults
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import com.assetflow.manager.data.EstadosPrestamo
import com.assetflow.manager.data.LoanDto
import com.assetflow.manager.data.MaterialDto
import com.assetflow.manager.ui.EstadoApp
import com.assetflow.manager.ui.Seccion
import com.assetflow.manager.ui.theme.Paleta

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun PantallaPrincipal(
    estado: EstadoApp,
    onSeccion: (Seccion) -> Unit,
    onBuscar: (String) -> Unit,
    onRefrescar: () -> Unit,
    onSolicitar: (MaterialDto, Int) -> Unit,
    onPedirDevolucion: (LoanDto) -> Unit,
    onAprobar: (LoanDto) -> Unit,
    onRechazar: (LoanDto) -> Unit,
    onAprobarDevolucion: (LoanDto) -> Unit,
    onRechazarDevolucion: (LoanDto) -> Unit,
    onCerrarSesion: () -> Unit
) {
    Scaffold(
        topBar = {
            TopAppBar(
                title = {
                    Column {
                        Text(
                            text = when (estado.seccion) {
                                Seccion.INVENTARIO -> "Inventario"
                                Seccion.PRESTAMOS -> "Préstamos"
                            },
                            style = MaterialTheme.typography.titleLarge
                        )
                        Text(
                            text = estado.usuario?.nombreCompleto.orEmpty() +
                                    if (estado.esAdministrador) " · Administrador" else "",
                            style = MaterialTheme.typography.bodySmall
                        )
                    }
                },
                actions = {
                    IconButton(onClick = onRefrescar, enabled = !estado.cargando) {
                        Icon(Icons.Filled.Refresh, contentDescription = "Actualizar")
                    }
                    TextButton(onClick = onCerrarSesion) { Text("Salir") }
                },
                colors = TopAppBarDefaults.topAppBarColors(
                    containerColor = MaterialTheme.colorScheme.surface
                )
            )
        },
        bottomBar = {
            NavigationBar {
                NavigationBarItem(
                    selected = estado.seccion == Seccion.INVENTARIO,
                    onClick = { onSeccion(Seccion.INVENTARIO) },
                    icon = { Icon(Icons.Filled.Inventory2, contentDescription = null) },
                    label = { Text("Inventario") }
                )
                NavigationBarItem(
                    selected = estado.seccion == Seccion.PRESTAMOS,
                    onClick = { onSeccion(Seccion.PRESTAMOS) },
                    icon = { Icon(Icons.Filled.SwapHoriz, contentDescription = null) },
                    label = { Text("Préstamos") }
                )
            }
        }
    ) { relleno ->
        Column(
            modifier = Modifier
                .fillMaxSize()
                .padding(relleno)
        ) {
            val error = estado.error

            if (error != null) {
                Aviso(
                    error,
                    esError = true,
                    modifier = Modifier.padding(16.dp, 12.dp, 16.dp, 0.dp)
                )
            }

            if (estado.cargando) {
                Box(Modifier.fillMaxWidth().padding(16.dp), Alignment.Center) {
                    CircularProgressIndicator(Modifier.size(28.dp), strokeWidth = 3.dp)
                }
            }

            when (estado.seccion) {
                Seccion.INVENTARIO -> SeccionInventario(estado, onBuscar, onRefrescar, onSolicitar)
                Seccion.PRESTAMOS -> SeccionPrestamos(
                    estado,
                    onPedirDevolucion,
                    onAprobar,
                    onRechazar,
                    onAprobarDevolucion,
                    onRechazarDevolucion
                )
            }
        }
    }
}

// ---------------------------------------------------------------------------
// Inventario
// ---------------------------------------------------------------------------

@Composable
private fun SeccionInventario(
    estado: EstadoApp,
    onBuscar: (String) -> Unit,
    onRefrescar: () -> Unit,
    onSolicitar: (MaterialDto, Int) -> Unit
) {
    Column(Modifier.fillMaxSize()) {
        OutlinedTextField(
            value = estado.busqueda,
            onValueChange = onBuscar,
            label = { Text("Buscar material") },
            singleLine = true,
            trailingIcon = {
                IconButton(onClick = onRefrescar) {
                    Icon(Icons.Filled.Refresh, contentDescription = "Buscar")
                }
            },
            modifier = Modifier
                .fillMaxWidth()
                .padding(16.dp, 12.dp, 16.dp, 8.dp)
        )

        if (estado.materiales.isEmpty() && !estado.cargando) {
            Vacio("No hay material que mostrar.")
            return@Column
        }

        LazyColumn(
            contentPadding = androidx.compose.foundation.layout.PaddingValues(16.dp, 4.dp, 16.dp, 16.dp),
            verticalArrangement = Arrangement.spacedBy(10.dp)
        ) {
            items(estado.materiales, key = { it.id }) { material ->
                TarjetaMaterial(material, estado.trabajando, onSolicitar)
            }
        }
    }
}

@Composable
private fun TarjetaMaterial(
    material: MaterialDto,
    trabajando: Boolean,
    onSolicitar: (MaterialDto, Int) -> Unit
) {
    var cantidad by remember(material.id) { mutableStateOf(1) }

    Card(colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.surface)) {
        Column(Modifier.padding(16.dp)) {
            Text(material.name, style = MaterialTheme.typography.titleMedium)

            if (!material.type.isNullOrBlank()) {
                Text(
                    material.type,
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant
                )
            }

            Spacer(Modifier.height(10.dp))

            Row(verticalAlignment = Alignment.CenterVertically) {
                Etiqueta(
                    texto = "${material.availableQuantity} libres",
                    color = when {
                        material.sinStock -> Paleta.Danger
                        material.stockBajo -> Paleta.Warning
                        else -> Paleta.Success
                    },
                    fondo = when {
                        material.sinStock -> Paleta.DangerSoft
                        material.stockBajo -> Paleta.WarningSoft
                        else -> Paleta.SuccessSoft
                    }
                )

                Spacer(Modifier.size(8.dp))

                Text(
                    "de ${material.totalQuantity}" +
                            if (material.reservedQuantity > 0) {
                                " · ${material.reservedQuantity} reservadas"
                            } else "",
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant
                )
            }

            if (material.sinStock) return@Column

            Spacer(Modifier.height(12.dp))

            Row(verticalAlignment = Alignment.CenterVertically) {
                OutlinedButton(
                    onClick = { if (cantidad > 1) cantidad-- },
                    enabled = !trabajando && cantidad > 1
                ) { Text("−") }

                Text(
                    text = cantidad.toString(),
                    modifier = Modifier.padding(horizontal = 16.dp),
                    style = MaterialTheme.typography.titleMedium
                )

                OutlinedButton(
                    onClick = { if (cantidad < material.availableQuantity) cantidad++ },
                    enabled = !trabajando && cantidad < material.availableQuantity
                ) { Text("+") }

                Spacer(Modifier.weight(1f))

                Button(
                    onClick = { onSolicitar(material, cantidad) },
                    enabled = !trabajando
                ) { Text("Solicitar") }
            }
        }
    }
}

// ---------------------------------------------------------------------------
// Préstamos
// ---------------------------------------------------------------------------

@Composable
private fun SeccionPrestamos(
    estado: EstadoApp,
    onPedirDevolucion: (LoanDto) -> Unit,
    onAprobar: (LoanDto) -> Unit,
    onRechazar: (LoanDto) -> Unit,
    onAprobarDevolucion: (LoanDto) -> Unit,
    onRechazarDevolucion: (LoanDto) -> Unit
) {
    if (estado.prestamos.isEmpty() && !estado.cargando) {
        Vacio(
            if (estado.esAdministrador) {
                "No hay préstamos registrados."
            } else {
                "No tienes ningún préstamo."
            }
        )
        return
    }

    LazyColumn(
        contentPadding = androidx.compose.foundation.layout.PaddingValues(16.dp),
        verticalArrangement = Arrangement.spacedBy(10.dp)
    ) {
        items(estado.prestamos, key = { it.id }) { prestamo ->
            TarjetaPrestamo(
                prestamo,
                estado.esAdministrador,
                estado.trabajando,
                onPedirDevolucion,
                onAprobar,
                onRechazar,
                onAprobarDevolucion,
                onRechazarDevolucion
            )
        }
    }
}

@Composable
private fun TarjetaPrestamo(
    prestamo: LoanDto,
    esAdministrador: Boolean,
    trabajando: Boolean,
    onPedirDevolucion: (LoanDto) -> Unit,
    onAprobar: (LoanDto) -> Unit,
    onRechazar: (LoanDto) -> Unit,
    onAprobarDevolucion: (LoanDto) -> Unit,
    onRechazarDevolucion: (LoanDto) -> Unit
) {
    Card(colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.surface)) {
        Column(Modifier.padding(16.dp)) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                // El estado se dice con palabras además de con color: quien no
                // distingue rojo de verde tiene que poder leerlo igual.
                Etiqueta(
                    texto = prestamo.estadoTexto.ifBlank { prestamo.status },
                    color = colorDeEstado(prestamo.status),
                    fondo = fondoDeEstado(prestamo.status)
                )

                if (prestamo.isOverdue) {
                    Spacer(Modifier.size(8.dp))
                    Etiqueta("Fuera de plazo", Paleta.Danger, Paleta.DangerSoft)
                }
            }

            Spacer(Modifier.height(10.dp))

            Text(prestamo.resumenArticulos, style = MaterialTheme.typography.bodyLarge)

            if (esAdministrador && prestamo.userName.isNotBlank()) {
                Text(
                    prestamo.userName,
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant
                )
            }

            if (!prestamo.estadoDetalle.isNullOrBlank()) {
                Spacer(Modifier.height(4.dp))
                Text(
                    prestamo.estadoDetalle,
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant
                )
            }

            val acciones = accionesDisponibles(prestamo, esAdministrador)

            if (acciones.isEmpty()) return@Column

            Spacer(Modifier.height(14.dp))

            Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                acciones.forEach { accion ->
                    when (accion) {
                        Accion.APROBAR -> Button(
                            onClick = { onAprobar(prestamo) }, enabled = !trabajando
                        ) { Text("Aprobar") }

                        Accion.RECHAZAR -> OutlinedButton(
                            onClick = { onRechazar(prestamo) }, enabled = !trabajando
                        ) { Text("Rechazar") }

                        Accion.PEDIR_DEVOLUCION -> Button(
                            onClick = { onPedirDevolucion(prestamo) }, enabled = !trabajando
                        ) { Text("Devolver") }

                        Accion.APROBAR_DEVOLUCION -> Button(
                            onClick = { onAprobarDevolucion(prestamo) }, enabled = !trabajando
                        ) { Text("Confirmar devolución") }

                        Accion.RECHAZAR_DEVOLUCION -> OutlinedButton(
                            onClick = { onRechazarDevolucion(prestamo) }, enabled = !trabajando
                        ) { Text("Rechazar") }
                    }
                }
            }
        }
    }
}

private enum class Accion {
    APROBAR, RECHAZAR, PEDIR_DEVOLUCION, APROBAR_DEVOLUCION, RECHAZAR_DEVOLUCION
}

/**
 * Qué botones se dibujan para un préstamo.
 *
 * Ocultar un botón **no es una medida de seguridad**: la API rechaza igual la
 * operación si el rol no corresponde. Esto sólo evita ofrecer acciones que
 * acabarían en un 403.
 */
private fun accionesDisponibles(prestamo: LoanDto, esAdministrador: Boolean): List<Accion> =
    when {
        prestamo.status == EstadosPrestamo.PENDIENTE && esAdministrador ->
            listOf(Accion.APROBAR, Accion.RECHAZAR)

        prestamo.status == EstadosPrestamo.ACTIVO ->
            listOf(Accion.PEDIR_DEVOLUCION)

        prestamo.status == EstadosPrestamo.DEVOLUCION_SOLICITADA && esAdministrador ->
            listOf(Accion.APROBAR_DEVOLUCION, Accion.RECHAZAR_DEVOLUCION)

        else -> emptyList()
    }

private fun colorDeEstado(estado: String) = when (estado) {
    EstadosPrestamo.PENDIENTE -> Paleta.Warning
    EstadosPrestamo.ACTIVO -> Paleta.Accent
    EstadosPrestamo.DEVOLUCION_SOLICITADA -> Paleta.Warning
    EstadosPrestamo.DEVUELTO -> Paleta.Success
    else -> Paleta.TextMuted
}

private fun fondoDeEstado(estado: String) = when (estado) {
    EstadosPrestamo.PENDIENTE -> Paleta.WarningSoft
    EstadosPrestamo.ACTIVO -> Paleta.AccentSoft
    EstadosPrestamo.DEVOLUCION_SOLICITADA -> Paleta.WarningSoft
    EstadosPrestamo.DEVUELTO -> Paleta.SuccessSoft
    else -> Paleta.SurfaceSunken
}

// ---------------------------------------------------------------------------
// Piezas comunes
// ---------------------------------------------------------------------------

@Composable
private fun Etiqueta(
    texto: String,
    color: androidx.compose.ui.graphics.Color,
    fondo: androidx.compose.ui.graphics.Color
) {
    Surface(color = fondo, shape = MaterialTheme.shapes.small) {
        Text(
            text = texto,
            modifier = Modifier.padding(horizontal = 10.dp, vertical = 4.dp),
            style = MaterialTheme.typography.bodySmall,
            color = color
        )
    }
}

@Composable
private fun Vacio(mensaje: String) {
    Box(
        modifier = Modifier.fillMaxSize().padding(32.dp),
        contentAlignment = Alignment.Center
    ) {
        Text(
            text = mensaje,
            style = MaterialTheme.typography.bodyLarge,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
            textAlign = TextAlign.Center
        )
    }
}
