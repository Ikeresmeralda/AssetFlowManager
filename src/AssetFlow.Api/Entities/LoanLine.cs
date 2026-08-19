namespace AssetFlow.Api.Entities;

/// <summary>
/// Linea de un prestamo: cuantas unidades de un articulo concreto.
/// </summary>
/// <remarks>
/// Antes se llamaba <c>LoanDetail</c> y tenia su propio controlador CRUD
/// publico, lo que permitia crear o borrar lineas sueltas sin pasar por el
/// prestamo al que pertenecen y descuadrar el inventario. Ahora las lineas
/// solo se manipulan a traves de su prestamo.
/// </remarks>
public class LoanLine
{
    public int Id { get; set; }

    public int LoanId { get; set; }

    public Loan Loan { get; set; } = null!;

    public int MaterialId { get; set; }

    public Material Material { get; set; } = null!;

    public int Quantity { get; set; }
}
