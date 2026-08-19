namespace AssetFlow.Api.Entities;

/// <summary>
/// Articulo del inventario.
/// </summary>
/// <remarks>
/// Antes se llamaba <c>AssociationMaterial</c> y el stock se manipulaba
/// restando unidades a <c>TotalQuantity</c> desde un endpoint
/// <c>ReduceQuantity</c>. Ese modelo tiene dos defectos graves:
///
/// 1. Pierde informacion: una vez restadas las unidades ya no se sabe cuantas
///    se poseen realmente, solo cuantas quedan.
/// 2. Es corruptible: dos peticiones simultaneas, o una devolucion que nunca
///    se registra, dejan el numero descuadrado para siempre.
///
/// Ahora <see cref="TotalQuantity"/> es el numero de unidades que la entidad
/// posee, un dato estable, y la disponibilidad se calcula restando los
/// prestamos vivos. El inventario no puede descuadrarse porque no se edita
/// al prestar.
/// </remarks>
public class Material
{
    public int Id { get; set; }

    public string Name { get; set; } = null!;

    public string Type { get; set; } = null!;

    public string? Publisher { get; set; }

    /// <summary>Unidades que se poseen en total, prestadas o no.</summary>
    public int TotalQuantity { get; set; }

    /// <summary>
    /// Unidades a partir de las cuales el articulo se marca como stock bajo.
    /// Es por articulo y no una constante global: no tiene sentido el mismo
    /// umbral para un proyector que para un paquete de folios.
    /// </summary>
    public int LowStockThreshold { get; set; } = 5;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Testigo de concurrencia optimista. Se usa un Guid en lugar de rowversion
    /// porque rowversion es especifico de SQL Server y la aplicacion tambien
    /// funciona sobre SQLite.
    /// </summary>
    public Guid Version { get; set; } = Guid.NewGuid();

    public ICollection<LoanLine> LoanLines { get; set; } = new List<LoanLine>();
}
