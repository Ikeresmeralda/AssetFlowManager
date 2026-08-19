using System.ComponentModel.DataAnnotations;

namespace AssetFlow.Api.Dtos;

/// <summary>
/// Articulo del inventario tal y como lo ve el cliente.
/// </summary>
/// <remarks>
/// La disponibilidad se calcula en el servidor y llega ya resuelta. Antes el
/// cliente recibia solo el total y no habia forma de saber cuanto habia
/// realmente libre.
/// </remarks>
public record MaterialDto
{
    public required int Id { get; init; }

    public required string Name { get; init; }

    public required string Type { get; init; }

    public string? Publisher { get; init; }

    /// <summary>Unidades que se poseen.</summary>
    public required int TotalQuantity { get; init; }

    /// <summary>Unidades actualmente prestadas y no devueltas.</summary>
    public required int OnLoanQuantity { get; init; }

    /// <summary>
    /// Unidades comprometidas por solicitudes pendientes de aprobar. Todavia
    /// estan en el almacen, pero no se pueden prometer a nadie mas.
    /// </summary>
    public int ReservedQuantity { get; init; }

    /// <summary>Unidades que se pueden prestar ahora mismo.</summary>
    public required int AvailableQuantity { get; init; }

    public required int LowStockThreshold { get; init; }

    /// <summary>
    /// Estado en texto. Se envia calculado para que cliente y servidor no
    /// puedan discrepar sobre cuando algo esta "bajo minimos".
    /// </summary>
    public required string Status { get; init; }

    public required DateTime UpdatedAt { get; init; }

    /// <summary>
    /// Testigo de concurrencia. El cliente lo devuelve al guardar para que el
    /// servidor detecte si otro usuario ha modificado el articulo mientras
    /// tanto. Sin exponerlo, la comprobacion de concurrencia seria decorativa.
    /// </summary>
    public required Guid Version { get; init; }
}

public record CreateMaterialRequest
{
    [Required]
    [StringLength(200, MinimumLength = 1)]
    public string Name { get; init; } = string.Empty;

    [Required]
    [StringLength(100, MinimumLength = 1)]
    public string Type { get; init; } = string.Empty;

    [StringLength(200)]
    public string? Publisher { get; init; }

    [Range(0, 1_000_000, ErrorMessage = "Las unidades deben estar entre 0 y 1.000.000.")]
    public int TotalQuantity { get; init; }

    [Range(0, 10_000)]
    public int LowStockThreshold { get; init; } = 5;
}

public record UpdateMaterialRequest : CreateMaterialRequest
{
    /// <summary>
    /// Testigo de concurrencia recibido en la lectura. Si otro usuario ha
    /// modificado el articulo entretanto, la actualizacion se rechaza con 409
    /// en lugar de pisar sus cambios sin avisar.
    /// </summary>
    public Guid? Version { get; init; }
}
