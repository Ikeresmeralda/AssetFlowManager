package com.assetflow.manager.data

import retrofit2.Response
import retrofit2.http.Body
import retrofit2.http.GET
import retrofit2.http.POST
import retrofit2.http.Path
import retrofit2.http.Query

/**
 * Superficie de la API que usa el cliente Android.
 *
 * Es un subconjunto deliberado de lo que ofrece el servidor: aquí no están la
 * gestión de usuarios, la auditoría ni la bandeja de recuperaciones. Esas son
 * tareas de administración que se hacen desde el cliente de escritorio, y no
 * declararlas evita que un descuido en la interfaz acabe llamándolas.
 *
 * Que no estén aquí no las protege —la API las rechaza igual si el rol no es el
 * correcto—, pero mantiene el cliente pequeño y honesto sobre lo que hace.
 */
interface ApiService {

    // -----------------------------------------------------------------------
    // Autenticación
    // -----------------------------------------------------------------------

    @POST("api/auth/login")
    suspend fun login(@Body peticion: LoginRequest): Response<AuthResponse>

    @POST("api/auth/refresh")
    suspend fun refresh(@Body peticion: RefreshRequest): Response<AuthResponse>

    @POST("api/auth/logout")
    suspend fun logout(@Body peticion: RefreshRequest): Response<Unit>

    @GET("api/auth/me")
    suspend fun yo(): Response<CurrentUser>

    @POST("api/auth/forgot-password")
    suspend fun olvideContrasena(@Body peticion: ForgotPasswordRequest): Response<Unit>

    /** Cambio de la contraseña provisional. Devuelve una sesión nueva. */
    @POST("api/auth/change-password")
    suspend fun cambiarContrasena(@Body peticion: ChangePasswordRequest): Response<AuthResponse>

    // -----------------------------------------------------------------------
    // Material
    // -----------------------------------------------------------------------

    /**
     * Lista de material. Devuelve todo, sin paginar: el inventario de una
     * asociación son decenas de artículos, no miles.
     */
    @GET("api/materials")
    suspend fun materiales(
        @Query("search") busqueda: String? = null
    ): Response<List<MaterialDto>>

    // -----------------------------------------------------------------------
    // Préstamos
    // -----------------------------------------------------------------------

    /**
     * Préstamos visibles para quien llama.
     *
     * No lleva parámetro de usuario a propósito: el servidor decide qué
     * devuelve a partir del token. Un usuario normal ve los suyos y un
     * administrador los de todos. Mandar un identificador desde aquí sería
     * pedirle al servidor que confíe en el cliente.
     */
    @GET("api/loans")
    suspend fun prestamos(@Query("status") estado: String? = null): Response<List<LoanDto>>

    @POST("api/loans")
    suspend fun crearPrestamo(@Body peticion: CreateLoanRequest): Response<LoanDto>

    /** El usuario pide devolver lo que tiene. */
    @POST("api/loans/{id}/request-return")
    suspend fun pedirDevolucion(
        @Path("id") id: Int,
        @Body peticion: LoanDecisionRequest
    ): Response<LoanDto>

    // Las cuatro siguientes son decisiones de administración. La API las
    // rechaza con 403 si el rol no es el correcto, así que la interfaz sólo
    // tiene que ocuparse de no ofrecerlas.

    @POST("api/loans/{id}/approve")
    suspend fun aprobar(
        @Path("id") id: Int,
        @Body peticion: LoanDecisionRequest
    ): Response<LoanDto>

    @POST("api/loans/{id}/reject")
    suspend fun rechazar(
        @Path("id") id: Int,
        @Body peticion: LoanDecisionRequest
    ): Response<LoanDto>

    @POST("api/loans/{id}/approve-return")
    suspend fun aprobarDevolucion(
        @Path("id") id: Int,
        @Body peticion: LoanDecisionRequest
    ): Response<LoanDto>

    @POST("api/loans/{id}/reject-return")
    suspend fun rechazarDevolucion(
        @Path("id") id: Int,
        @Body peticion: LoanDecisionRequest
    ): Response<LoanDto>
}
