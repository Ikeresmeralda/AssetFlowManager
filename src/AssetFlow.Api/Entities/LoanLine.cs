namespace AssetFlow.Api.Entities;

/// <summary>
/// Linea de un prestamo: cuantas unidades de un articulo concreto.
/// </summary>
/// <remarks>
/// Las lineas solo se manipulan a traves de su prestamo. Exponerlas en un
/// controlador propio permitiria crear o borrar lineas sueltas sin pasar por
/// el prestamo al que pertenecen, y descuadrar el inventario.
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
