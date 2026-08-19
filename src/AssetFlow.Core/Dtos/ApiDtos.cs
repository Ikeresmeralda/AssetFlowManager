using System.Text.Json.Serialization;

namespace AssetFlow.Core.Dtos;

// ---------------------------------------------------------------------------
// Contratos con la API.
//
// Son deliberadamente independientes de las entidades del servidor: el cliente
// solo conoce lo que la API publica. Si manana el servidor anade una columna,
// aqui no cambia nada mientras no se publique.
// ---------------------------------------------------------------------------

public sealed record LoginRequest(string Username, string Password);

public sealed record RefreshRequest(string RefreshToken);

/// <summary>Solicitud de recuperacion, dirigida a un administrador.</summary>
public sealed record ForgotPasswordRequest(string Email);

/// <summary>Cambio de la contrasena provisional por una definitiva.</summary>
public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

/// <summary>Solicitud de recuperacion vista por un administrador.</summary>
public sealed record PasswordResetRequestDto(
    int Id,
    int UserId,
    string Username,
    string FullName,
    DateTime RequestedAt,
    string Estado,
    DateTime? ResolvedAt,
    string? ResolvedByUsername,
    bool EstaPendiente);

/// <summary>Contrasena provisional devuelta al aprobar o reiniciar.</summary>
public sealed record PasswordResetApprovalDto(string Username, string ContrasenaProvisional);

/// <summary>Numero de solicitudes esperando decision.</summary>
public sealed record PendingCountDto(int Pendientes);

public sealed record AuthResponse(
    string AccessToken,
    DateTime AccessTokenExpiresAt,
    string RefreshToken,
    DateTime RefreshTokenExpiresAt,
    CurrentUser User);

public sealed record CurrentUser(
    int Id,
    string Username,
    string FirstName,
    string LastName,
    string Role,
    bool MustChangePassword = false)
{
    public string NombreCompleto => $"{FirstName} {LastName}".Trim();

    public bool EsAdministrador => Role == "Admin";
}

// ---------------------------------------------------------------------------
// Inventario
// ---------------------------------------------------------------------------

public sealed record MaterialDto(
    int Id,
    string Name,
    string Type,
    string? Publisher,
    int TotalQuantity,
    int OnLoanQuantity,
    // Unidades comprometidas por solicitudes aun sin aprobar. Siguen en el
    // almacen, pero ya no se pueden prometer a nadie mas.
    int ReservedQuantity,
    int AvailableQuantity,
    int LowStockThreshold,
    string Status,
    DateTime UpdatedAt,
    Guid Version)
{
    /// <summary>
    /// Estado en castellano para la interfaz. El servidor envia un valor
    /// estable ("Available") y el cliente solo lo traduce: asi cambiar el
    /// texto visible no altera la logica.
    /// </summary>
    public string EstadoTexto => Status switch
    {
        "OutOfStock" => "Agotado",
        "LowStock" => "Stock bajo",
        _ => "Disponible"
    };

    public bool EstaAgotado => Status == "OutOfStock";

    public bool NecesitaReposicion => Status is "OutOfStock" or "LowStock";
}

public sealed record SaveMaterialRequest(
    string Name,
    string Type,
    string? Publisher,
    int TotalQuantity,
    int LowStockThreshold,
    Guid? Version = null);

// ---------------------------------------------------------------------------
// Prestamos
// ---------------------------------------------------------------------------

public sealed record LoanLineDto(int MaterialId, string MaterialName, int Quantity);

/// <summary>
/// Estados por los que pasa un prestamo.
/// </summary>
/// <remarks>
/// Las claves son las que envia el servidor y no se traducen aqui: el codigo
/// compara contra estas constantes y la interfaz muestra
/// <see cref="LoanDto.EstadoTexto"/>. Asi cambiar un texto visible no puede
/// romper una comparacion.
/// </remarks>
public static class LoanStatuses
{
    public const string PendienteAprobacion = "PendingApproval";
    public const string Activo = "Active";
    public const string Rechazado = "Rejected";
    public const string DevolucionSolicitada = "ReturnRequested";
    public const string Devuelto = "Returned";
}

public sealed record LoanDto(
    int Id,
    int UserId,
    string UserFullName,
    DateOnly? LoanDate,
    DateOnly EstimatedReturnDate,
    DateOnly? ReturnDate,
    string? Reason,
    string Status,
    bool IsOverdue,
    DateTime RequestedAt,
    DateTime? DecidedAt,
    string? DecidedByName,
    string? DecisionNote,
    DateTime? ReturnRequestedAt,
    DateTime? ReturnDecidedAt,
    string? ReturnDecidedByName,
    string? ReturnDecisionNote,
    IReadOnlyList<LoanLineDto> Lines)
{
    public bool EstaPendiente => Status == LoanStatuses.PendienteAprobacion;

    public bool EstaActivo => Status == LoanStatuses.Activo;

    public bool TieneDevolucionSolicitada => Status == LoanStatuses.DevolucionSolicitada;

    /// <summary>Estados que ya no admiten ninguna transicion.</summary>
    public bool EstaCerrado =>
        Status is LoanStatuses.Devuelto or LoanStatuses.Rechazado;

    /// <summary>
    /// Etiqueta del estado. El vencimiento se muestra como estado propio
    /// porque para quien mira la lista es la informacion que manda, aunque en
    /// el servidor sea un prestamo activo con la fecha pasada.
    /// </summary>
    public string EstadoTexto => Status switch
    {
        LoanStatuses.PendienteAprobacion => "Pendiente",
        LoanStatuses.Rechazado => "Rechazada",
        LoanStatuses.Devuelto => "Devuelto",
        LoanStatuses.DevolucionSolicitada => "Devolución pendiente",
        _ => IsOverdue ? "Vencido" : "En curso"
    };

    /// <summary>
    /// Texto que acompana al estado y explica que se espera a continuacion.
    /// Existe para que el estado se entienda sin depender del color, que es
    /// inservible para quien no distingue los tonos.
    /// </summary>
    public string EstadoDetalle => Status switch
    {
        LoanStatuses.PendienteAprobacion => "A la espera de que un administrador la revise",
        LoanStatuses.Rechazado => string.IsNullOrWhiteSpace(DecisionNote)
            ? "Solicitud rechazada"
            : $"Motivo: {DecisionNote}",
        LoanStatuses.Devuelto => ReturnDate is null
            ? "Material devuelto"
            : $"Devuelto el {ReturnDate:dd/MM/yyyy}",
        LoanStatuses.DevolucionSolicitada => "Has pedido devolverlo; falta que lo confirmen",
        _ => IsOverdue
            ? $"Debía devolverse el {EstimatedReturnDate:dd/MM/yyyy}"
            : $"A devolver antes del {EstimatedReturnDate:dd/MM/yyyy}"
    };

    /// <summary>Fecha que tiene sentido mostrar en un listado segun el estado.</summary>
    public DateTime FechaRelevante => Status switch
    {
        LoanStatuses.PendienteAprobacion => RequestedAt,
        LoanStatuses.DevolucionSolicitada => ReturnRequestedAt ?? RequestedAt,
        LoanStatuses.Rechazado => DecidedAt ?? RequestedAt,
        _ => LoanDate?.ToDateTime(TimeOnly.MinValue) ?? RequestedAt
    };

    public int UnidadesTotales => Lines.Sum(l => l.Quantity);

    public string ResumenArticulos => Lines.Count switch
    {
        0 => "",
        1 => $"{Lines[0].Quantity} x {Lines[0].MaterialName}",
        _ => $"{Lines[0].Quantity} x {Lines[0].MaterialName} y {Lines.Count - 1} más"
    };
}

/// <summary>Una accion registrada sobre un prestamo.</summary>
public sealed record LoanHistoryEntryDto(
    DateTime OccurredAt,
    string Action,
    string ActorName,
    string? Details)
{
    /// <summary>
    /// Traduccion de la clave del servidor. El <c>_</c> del final devuelve la
    /// clave cruda en lugar de un texto inventado: si manana el servidor anade
    /// una accion, el historial la muestra tal cual en vez de ocultarla.
    /// </summary>
    public string AccionTexto => Action switch
    {
        "prestamo.solicitado" => "Solicitud creada",
        "prestamo.aprobado" => "Solicitud aprobada",
        "prestamo.rechazado" => "Solicitud rechazada",
        "devolucion.solicitada" => "Devolución solicitada",
        "devolucion.aprobada" => "Devolución confirmada",
        "devolucion.rechazada" => "Devolución rechazada",
        "prestamo.eliminado" => "Préstamo eliminado",
        _ => Action
    };
}

public sealed record CreateLoanLineRequest(int MaterialId, int Quantity);

public sealed record CreateLoanRequest(
    int? UserId,
    DateOnly EstimatedReturnDate,
    string? Reason,
    IReadOnlyList<CreateLoanLineRequest> Lines);

/// <summary>Nota opcional que acompana a una decision del administrador.</summary>
public sealed record LoanDecisionRequest(string? Note);

// ---------------------------------------------------------------------------
// Usuarios
// ---------------------------------------------------------------------------

public sealed record UserSummaryDto(int Id, string Username, string FullName);

public sealed record UserDto(
    int Id,
    string Username,
    string FirstName,
    string LastName,
    string Email,
    string? PhoneNumber,
    string Role,
    bool IsActive,
    DateTime CreatedAt,
    int ActiveLoans)
{
    public string NombreCompleto => $"{FirstName} {LastName}".Trim();

    public bool EsAdministrador => Role == "Admin";

    public string RolTexto => EsAdministrador ? "Administrador" : "Usuario";
}

public sealed record CreateUserRequest(
    string Username,
    string FirstName,
    string LastName,
    string Email,
    string? PhoneNumber,
    string Password,
    string Role);

public sealed record UpdateUserRequest(
    string FirstName,
    string LastName,
    string Email,
    string? PhoneNumber);

public sealed record UpdateUserAccessRequest(string Role, bool IsActive);

// ---------------------------------------------------------------------------
// Errores
// ---------------------------------------------------------------------------

/// <summary>
/// Formato de error de la API (RFC 7807). Todas las respuestas de error usan
/// este formato, asi que el cliente solo necesita saber leer uno.
/// </summary>
public sealed record ProblemDetails
{
    public string? Title { get; init; }

    public string? Detail { get; init; }

    public int? Status { get; init; }

    /// <summary>Errores de validacion por campo, cuando los hay.</summary>
    [JsonPropertyName("errors")]
    public Dictionary<string, string[]>? Errors { get; init; }

    /// <summary>
    /// Texto mas util disponible: el detalle si existe, y si no el primer
    /// error de validacion concreto, que dice mas que un titulo generico.
    /// </summary>
    public string? MejorMensaje()
    {
        if (Errors is { Count: > 0 })
        {
            string[]? primero = Errors.Values.FirstOrDefault(v => v.Length > 0);

            if (primero is { Length: > 0 })
            {
                return primero[0];
            }
        }

        return string.IsNullOrWhiteSpace(Detail) ? Title : Detail;
    }
}
