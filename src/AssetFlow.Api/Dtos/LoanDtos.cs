using System.ComponentModel.DataAnnotations;

namespace AssetFlow.Api.Dtos;

public record LoanLineDto
{
    public required int MaterialId { get; init; }

    public required string MaterialName { get; init; }

    public required int Quantity { get; init; }
}

public record LoanDto
{
    public required int Id { get; init; }

    public required int UserId { get; init; }

    public required string UserFullName { get; init; }

    /// <summary>
    /// Fecha de entrega. Nula mientras la solicitud siga pendiente o si fue
    /// rechazada: hasta que se aprueba, el material no ha salido.
    /// </summary>
    public DateOnly? LoanDate { get; init; }

    public required DateOnly EstimatedReturnDate { get; init; }

    public DateOnly? ReturnDate { get; init; }

    public string? Reason { get; init; }

    /// <summary>
    /// "PendingApproval", "Active", "Rejected", "ReturnRequested" o "Returned".
    /// </summary>
    public required string Status { get; init; }

    public required bool IsOverdue { get; init; }

    public required DateTime RequestedAt { get; init; }

    public DateTime? DecidedAt { get; init; }

    /// <summary>
    /// Nombre del administrador que decidio. Se envia el nombre y no la ficha
    /// para que el solicitante sepa quien resolvio sin recibir de paso el
    /// correo ni el telefono de esa persona.
    /// </summary>
    public string? DecidedByName { get; init; }

    /// <summary>Motivo del rechazo o nota de la aprobacion.</summary>
    public string? DecisionNote { get; init; }

    public DateTime? ReturnRequestedAt { get; init; }

    public DateTime? ReturnDecidedAt { get; init; }

    public string? ReturnDecidedByName { get; init; }

    public string? ReturnDecisionNote { get; init; }

    public required IReadOnlyList<LoanLineDto> Lines { get; init; }
}

/// <summary>Una accion registrada sobre un prestamo.</summary>
public record LoanHistoryEntryDto
{
    public required DateTime OccurredAt { get; init; }

    public required string Action { get; init; }

    public required string ActorName { get; init; }

    public string? Details { get; init; }
}

public record CreateLoanLineRequest
{
    [Range(1, int.MaxValue, ErrorMessage = "El identificador de artículo no es válido.")]
    public int MaterialId { get; init; }

    [Range(1, 10_000, ErrorMessage = "La cantidad debe ser al menos 1.")]
    public int Quantity { get; init; }
}

public record CreateLoanRequest
{
    /// <summary>
    /// Usuario al que se presta. Solo un administrador puede indicar un
    /// usuario distinto de si mismo; para el resto se ignora y se usa el
    /// propio. La comprobacion esta en el servidor.
    /// </summary>
    public int? UserId { get; init; }

    [Required]
    public DateOnly EstimatedReturnDate { get; init; }

    [StringLength(255)]
    public string? Reason { get; init; }

    [Required]
    [MinLength(1, ErrorMessage = "Un préstamo necesita al menos un artículo.")]
    [MaxLength(50, ErrorMessage = "Un préstamo no puede tener más de 50 líneas.")]
    public IReadOnlyList<CreateLoanLineRequest> Lines { get; init; } = [];
}

/// <summary>
/// Nota que acompana a una decision del administrador.
/// </summary>
/// <remarks>
/// Es un tipo propio y no un simple string en el cuerpo para que la longitud
/// se valide como el resto de entradas, y para que anadir campos mas adelante
/// no cambie la forma de la peticion.
/// </remarks>
public record LoanDecisionRequest
{
    [StringLength(255, ErrorMessage = "La nota no puede superar los 255 caracteres.")]
    public string? Note { get; init; }
}
