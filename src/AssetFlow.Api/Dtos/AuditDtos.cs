namespace AssetFlow.Api.Dtos;

/// <summary>Una accion registrada en la auditoria.</summary>
/// <remarks>
/// Nota deliberada sobre lo que NO viaja: ni direccion IP, ni agente de
/// usuario, ni identificadores internos mas alla de los que ya conoce quien
/// consulta. La auditoria responde a «quien hizo que y cuando», y ampliarla
/// con datos de conexion la convertiria en un registro de seguimiento de
/// personas sin que ninguna funcionalidad lo necesite.
/// </remarks>
public record AuditEntryDto
{
    public required int Id { get; init; }

    public required DateTime OccurredAt { get; init; }

    public required string ActorUsername { get; init; }

    /// <summary>Clave estable de la accion, p. ej. "prestamo.aprobado".</summary>
    public required string Action { get; init; }

    public string? EntityType { get; init; }

    public int? EntityId { get; init; }

    public string? Details { get; init; }
}

/// <summary>Pagina de resultados de auditoria.</summary>
public record AuditPageDto
{
    public required IReadOnlyList<AuditEntryDto> Items { get; init; }

    public required int Total { get; init; }

    public required int Page { get; init; }

    public required int PageSize { get; init; }
}
