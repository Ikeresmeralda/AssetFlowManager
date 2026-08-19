package com.assetflow.manager.data

import kotlinx.serialization.SerialName
import kotlinx.serialization.Serializable

/**
 * Contratos con la API.
 *
 * Son copia de los DTO de `AssetFlow.Core`. Se duplican en lugar de compartirse
 * porque no hay forma de compartir tipos entre .NET y Kotlin sin generar código,
 * y generarlo obligaría a que compilar Android dependiera del SDK de .NET.
 *
 * La contrapartida es que un cambio en la API hay que reflejarlo aquí a mano.
 * Por eso los nombres se dejan tal cual llegan del servidor, sin adaptarlos al
 * estilo Kotlin: cuanto más literal sea la copia, más fácil es cotejarla.
 *
 * Ningún DTO de respuesta lleva contraseñas ni hashes. Eso no es una decisión
 * de este fichero, es que la API no los envía.
 */

// ---------------------------------------------------------------------------
// Autenticación
// ---------------------------------------------------------------------------

@Serializable
data class LoginRequest(val username: String, val password: String)

@Serializable
data class RefreshRequest(val refreshToken: String)

@Serializable
data class ForgotPasswordRequest(val email: String)

@Serializable
data class ChangePasswordRequest(val currentPassword: String, val newPassword: String)

@Serializable
data class AuthResponse(
    val accessToken: String,
    val accessTokenExpiresAt: String,
    val refreshToken: String,
    val refreshTokenExpiresAt: String,
    val user: CurrentUser
)

@Serializable
data class CurrentUser(
    val id: Int,
    val username: String,
    val firstName: String,
    val lastName: String,
    val role: String,
    val mustChangePassword: Boolean = false
) {
    val nombreCompleto: String get() = "$firstName $lastName".trim()

    val esAdministrador: Boolean get() = role == "Admin"

    /** Iniciales para el avatar. */
    val iniciales: String
        get() = buildString {
            firstName.firstOrNull()?.let { append(it.uppercaseChar()) }
            lastName.firstOrNull()?.let { append(it.uppercaseChar()) }
        }.ifEmpty { "?" }
}

// ---------------------------------------------------------------------------
// Material
// ---------------------------------------------------------------------------

@Serializable
data class MaterialDto(
    val id: Int,
    val name: String,
    val type: String? = null,
    val publisher: String? = null,
    val totalQuantity: Int,
    val availableQuantity: Int,
    val reservedQuantity: Int = 0,
    val lowStockThreshold: Int = 0
) {
    /** Unidades fuera del almacén: total menos lo libre menos lo reservado. */
    val prestadas: Int get() = (totalQuantity - availableQuantity - reservedQuantity).coerceAtLeast(0)

    val sinStock: Boolean get() = availableQuantity <= 0

    val stockBajo: Boolean get() = !sinStock && availableQuantity <= lowStockThreshold
}

@Serializable
data class PagedMaterials(
    val items: List<MaterialDto> = emptyList(),
    val total: Int = 0,
    val page: Int = 1,
    val pageSize: Int = 20
)

// ---------------------------------------------------------------------------
// Préstamos
// ---------------------------------------------------------------------------

/** Los mismos valores que `LoanStatuses` en la API. */
object EstadosPrestamo {
    const val PENDIENTE = "PendingApproval"
    const val ACTIVO = "Active"
    const val DEVOLUCION_SOLICITADA = "ReturnRequested"
    const val DEVUELTO = "Returned"
    const val RECHAZADO = "Rejected"
}

@Serializable
data class LoanLineDto(
    val materialId: Int,
    val materialName: String = "",
    val quantity: Int
)

@Serializable
data class LoanDto(
    val id: Int,
    val userId: Int,
    val userName: String = "",
    val status: String,
    val estadoTexto: String = "",
    val estadoDetalle: String? = null,
    val requestedAt: String? = null,
    val loanDate: String? = null,
    val dueDate: String? = null,
    val returnDate: String? = null,
    val items: List<LoanLineDto> = emptyList(),
    @SerialName("estaPendiente") val pendiente: Boolean = false,
    @SerialName("estaActivo") val activo: Boolean = false,
    @SerialName("tieneDevolucionSolicitada") val devolucionSolicitada: Boolean = false,
    @SerialName("estaCerrado") val cerrado: Boolean = false,
    val isOverdue: Boolean = false
) {
    val resumenArticulos: String
        get() = items.joinToString(", ") { "${it.quantity} × ${it.materialName}" }
            .ifEmpty { "Sin artículos" }
}

@Serializable
data class CreateLoanItem(val materialId: Int, val quantity: Int)

@Serializable
data class CreateLoanRequest(
    val items: List<CreateLoanItem>,
    val dueDate: String? = null,
    val notes: String? = null
)

@Serializable
data class LoanDecisionRequest(val note: String? = null)

// ---------------------------------------------------------------------------
// Errores
// ---------------------------------------------------------------------------

/**
 * Cuerpo de error de la API, en formato RFC 7807.
 *
 * La API nunca devuelve trazas de pila ni rutas del servidor, así que `detail`
 * se puede enseñar tal cual a quien usa la aplicación.
 */
@Serializable
data class ProblemDetails(
    val type: String? = null,
    val title: String? = null,
    val detail: String? = null,
    val status: Int? = null
)
