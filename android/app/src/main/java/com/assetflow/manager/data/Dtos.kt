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
    val onLoanQuantity: Int = 0,
    val reservedQuantity: Int = 0,
    val availableQuantity: Int,
    val lowStockThreshold: Int = 0,
    val status: String? = null,
    val version: String? = null
) {
    val sinStock: Boolean get() = availableQuantity <= 0

    val stockBajo: Boolean get() = !sinStock && availableQuantity <= lowStockThreshold
}

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

/**
 * Préstamo tal y como lo devuelve la API.
 *
 * El servidor **no** manda el texto del estado ni banderas del tipo
 * «estaPendiente»: manda `status` en crudo y el cliente decide cómo
 * presentarlo. El cliente de escritorio hace lo mismo en `AssetFlow.Core`.
 */
@Serializable
data class LoanDto(
    val id: Int,
    val userId: Int,
    val userFullName: String = "",
    val status: String,
    val reason: String? = null,
    val requestedAt: String? = null,
    val loanDate: String? = null,
    val estimatedReturnDate: String? = null,
    val returnDate: String? = null,
    val returnRequestedAt: String? = null,
    val decidedByName: String? = null,
    val decisionNote: String? = null,
    val lines: List<LoanLineDto> = emptyList(),
    val isOverdue: Boolean = false
) {
    val resumenArticulos: String
        get() = lines.joinToString(", ") { "${it.quantity} × ${it.materialName}" }
            .ifEmpty { "Sin artículos" }

    /** Estado en palabras, para no depender sólo del color. */
    val estadoTexto: String
        get() = when (status) {
            EstadosPrestamo.PENDIENTE -> "Pendiente"
            EstadosPrestamo.ACTIVO -> "En curso"
            EstadosPrestamo.DEVOLUCION_SOLICITADA -> "Devolución pendiente"
            EstadosPrestamo.DEVUELTO -> "Devuelto"
            EstadosPrestamo.RECHAZADO -> "Rechazado"
            else -> status
        }
}

@Serializable
data class CreateLoanLine(val materialId: Int, val quantity: Int)

/**
 * Alta de préstamo.
 *
 * `userId` se omite a propósito: el servidor lo ignora salvo para
 * administración y usa el del token. Mandarlo desde aquí sería pedirle que
 * confíe en el cliente.
 */
@Serializable
data class CreateLoanRequest(
    val estimatedReturnDate: String,
    val lines: List<CreateLoanLine>,
    val reason: String? = null
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
