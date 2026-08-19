using AssetFlow.Api.Entities;

namespace AssetFlow.Api.Dtos;

/// <summary>
/// Conversion de entidades a DTO.
/// </summary>
/// <remarks>
/// Se hace a mano y no con una libreria de mapeo automatico a proposito: el
/// mapeo automatico por convencion es precisamente el mecanismo por el que un
/// campo nuevo y sensible aparece publicado en la API sin que nadie lo haya
/// decidido. Aqui, si un campo no esta escrito abajo, no sale.
/// </remarks>
public static class Mapping
{
    public static CurrentUserDto ToCurrentUser(this User u) => new()
    {
        Id = u.Id,
        Username = u.Username,
        FirstName = u.FirstName,
        LastName = u.LastName,
        Role = u.Role,
        MustChangePassword = u.MustChangePassword
    };

    public static UserSummaryDto ToSummary(this User u) => new()
    {
        Id = u.Id,
        Username = u.Username,
        FullName = u.FullName
    };

    public static UserDto ToDto(this User u, int activeLoans) => new()
    {
        Id = u.Id,
        Username = u.Username,
        FirstName = u.FirstName,
        LastName = u.LastName,
        Email = u.Email,
        PhoneNumber = u.PhoneNumber,
        Role = u.Role,
        IsActive = u.IsActive,
        CreatedAt = u.CreatedAt,
        ActiveLoans = activeLoans
    };

    /// <param name="m">Articulo de origen.</param>
    /// <param name="onLoan">Unidades entregadas y sin devolver.</param>
    /// <param name="reserved">
    /// Unidades comprometidas por solicitudes pendientes de aprobar. Se
    /// descuentan de lo disponible aunque no hayan salido del almacen: si no
    /// se reservaran, dos solicitudes sobre la ultima unidad podrian aprobarse
    /// las dos y el inventario quedaria en negativo.
    /// </param>
    public static MaterialDto ToDto(this Material m, int onLoan, int reserved = 0)
    {
        int disponible = Math.Max(0, m.TotalQuantity - onLoan - reserved);

        return new MaterialDto
        {
            Id = m.Id,
            Name = m.Name,
            Type = m.Type,
            Publisher = m.Publisher,
            TotalQuantity = m.TotalQuantity,
            OnLoanQuantity = onLoan,
            ReservedQuantity = reserved,
            AvailableQuantity = disponible,
            LowStockThreshold = m.LowStockThreshold,
            Status = CalcularEstado(disponible, m.LowStockThreshold),
            UpdatedAt = m.UpdatedAt,
            Version = m.Version
        };
    }

    /// <summary>
    /// Unico lugar donde se decide el estado de stock. Cliente y servidor no
    /// pueden discrepar porque el cliente no lo calcula.
    /// </summary>
    public static string CalcularEstado(int disponible, int umbral)
    {
        if (disponible <= 0)
        {
            return "OutOfStock";
        }

        return disponible <= umbral ? "LowStock" : "Available";
    }

    public static LoanDto ToDto(this Loan l) => new()
    {
        Id = l.Id,
        UserId = l.UserId,
        UserFullName = l.User?.FullName ?? string.Empty,
        LoanDate = l.LoanDate,
        EstimatedReturnDate = l.EstimatedReturnDate,
        ReturnDate = l.ReturnDate,
        Reason = l.Reason,
        Status = l.Status.ToString(),
        IsOverdue = l.IsOverdue,
        RequestedAt = l.RequestedAt,
        DecidedAt = l.DecidedAt,
        // Solo el nombre de quien decidio. La ficha completa del administrador
        // no le hace falta a quien recibe esto.
        DecidedByName = l.DecidedBy?.FullName,
        DecisionNote = l.DecisionNote,
        ReturnRequestedAt = l.ReturnRequestedAt,
        ReturnDecidedAt = l.ReturnDecidedAt,
        ReturnDecidedByName = l.ReturnDecidedBy?.FullName,
        ReturnDecisionNote = l.ReturnDecisionNote,
        Lines = l.Lines.Select(x => new LoanLineDto
        {
            MaterialId = x.MaterialId,
            MaterialName = x.Material?.Name ?? string.Empty,
            Quantity = x.Quantity
        }).ToList()
    };

    public static LoanHistoryEntryDto ToHistoryDto(this AuditEntry a) => new()
    {
        OccurredAt = a.OccurredAt,
        Action = a.Action,
        ActorName = a.ActorUsername,
        Details = a.Details
    };
}
