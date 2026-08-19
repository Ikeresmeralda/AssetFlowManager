using AssetFlow.Api.Data;
using AssetFlow.Api.Dtos;
using AssetFlow.Api.Entities;
using AssetFlow.Api.Security;
using AssetFlow.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AssetFlow.Api.Controllers;

/// <summary>
/// Inventario de articulos.
/// </summary>
/// <remarks>
/// Lectura: cualquier usuario autenticado. Escritura: solo administradores.
/// Esa asimetria es la razon de que la autorizacion se declare por accion y
/// no una sola vez en la clase.
/// </remarks>
[ApiController]
[Route("api/materials")]
[Authorize]
[Produces("application/json")]
public class MaterialsController : ControllerBase
{
    private readonly AssetFlowDbContext _db;
    private readonly IAuditor _auditor;
    private readonly ILogger<MaterialsController> _log;

    public MaterialsController(
        AssetFlowDbContext db, IAuditor auditor, ILogger<MaterialsController> log)
    {
        _db = db;
        _auditor = auditor;
        _log = log;
    }

    /// <summary>Lista el inventario, opcionalmente filtrado por nombre.</summary>
    /// <param name="search">Texto a buscar en el nombre o el tipo.</param>
    /// <param name="ct">Token de cancelacion.</param>
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<MaterialDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<MaterialDto>>> GetAll(
        [FromQuery] string? search, CancellationToken ct)
    {
        IQueryable<Material> consulta = _db.Materials.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(search))
        {
            // Se limita la longitud del termino: sin limite, una busqueda de
            // 100 KB obliga al motor a recorrer la tabla con un LIKE enorme.
            string termino = search.Trim();

            if (termino.Length > 100)
            {
                termino = termino[..100];
            }

            consulta = consulta.Where(m =>
                EF.Functions.Like(m.Name, $"%{termino}%") ||
                EF.Functions.Like(m.Type, $"%{termino}%"));
        }

        List<Material> materiales = await consulta
            .OrderBy(m => m.Name)
            .ToListAsync(ct);

        Dictionary<int, Compromiso> comprometido = await PrestadoPorArticuloAsync(
            materiales.Select(m => m.Id).ToList(), ct);

        return Ok(materiales
            .Select(m =>
            {
                Compromiso c = comprometido.GetValueOrDefault(m.Id);
                return m.ToDto(c.Fuera, c.Reservado);
            })
            .ToList());
    }

    /// <summary>Devuelve un articulo por su identificador.</summary>
    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(MaterialDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<MaterialDto>> Get(int id, CancellationToken ct)
    {
        Material? material = await _db.Materials
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == id, ct);

        if (material is null)
        {
            return NotFound();
        }

        Compromiso compromiso = (await PrestadoPorArticuloAsync([id], ct))
            .GetValueOrDefault(id);

        return Ok(material.ToDto(compromiso.Fuera, compromiso.Reservado));
    }

    /// <summary>Crea un articulo. Requiere administrador.</summary>
    [HttpPost]
    [Authorize(Roles = Roles.Admin)]
    [ProducesResponseType(typeof(MaterialDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<MaterialDto>> Create(
        CreateMaterialRequest peticion, CancellationToken ct)
    {
        var material = new Material
        {
            Name = peticion.Name.Trim(),
            Type = peticion.Type.Trim(),
            Publisher = string.IsNullOrWhiteSpace(peticion.Publisher)
                ? null : peticion.Publisher.Trim(),
            TotalQuantity = peticion.TotalQuantity,
            LowStockThreshold = peticion.LowStockThreshold
        };

        await using var transaccion = await _db.Database.BeginTransactionAsync(ct);

        _db.Materials.Add(material);
        await _db.SaveChangesAsync(ct);

        _auditor.Registrar(AuditActions.MaterialCreado, "Material", material.Id,
            $"«{material.Name}», {material.TotalQuantity} unidades");

        await _db.SaveChangesAsync(ct);
        await transaccion.CommitAsync(ct);

        _log.LogInformation("Articulo {MaterialId} creado por {User}",
            material.Id, User.Identity?.Name);

        return CreatedAtAction(nameof(Get), new { id = material.Id }, material.ToDto(0));
    }

    /// <summary>Actualiza un articulo. Requiere administrador.</summary>
    /// <response code="409">Otro usuario lo ha modificado entretanto.</response>
    [HttpPut("{id:int}")]
    [Authorize(Roles = Roles.Admin)]
    [ProducesResponseType(typeof(MaterialDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<MaterialDto>> Update(
        int id, UpdateMaterialRequest peticion, CancellationToken ct)
    {
        Material? material = await _db.Materials.FirstOrDefaultAsync(m => m.Id == id, ct);

        if (material is null)
        {
            return NotFound();
        }

        // Concurrencia optimista comprobada a mano: es mas claro que confiar
        // en la excepcion de EF y permite devolver un 409 con el estado actual
        // para que el cliente pueda mostrar el conflicto.
        if (peticion.Version is not null && peticion.Version != material.Version)
        {
            return Conflict(new ProblemDetails
            {
                Title = "Conflicto de edicion",
                Detail = "Otro usuario ha modificado este artículo. Recarga y vuelve a intentarlo.",
                Status = StatusCodes.Status409Conflict
            });
        }

        Compromiso compromiso = (await PrestadoPorArticuloAsync([id], ct))
            .GetValueOrDefault(id);

        // No se permite dejar el total por debajo de lo ya comprometido: seria
        // un inventario que afirma poseer menos unidades de las que tiene
        // fuera o ya prometidas.
        if (peticion.TotalQuantity < compromiso.Total)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Cantidad no válida",
                Detail = compromiso.Reservado > 0
                    ? $"Hay {compromiso.Fuera} unidades prestadas y {compromiso.Reservado} " +
                      "reservadas por solicitudes pendientes: el total no puede ser inferior."
                    : $"Hay {compromiso.Fuera} unidades prestadas: el total no puede ser inferior.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        // Asignacion campo a campo. Nunca se adjunta la entidad recibida del
        // cliente con EntityState.Modified, que era lo que hacia el codigo
        // anterior: eso permite sobrescribir cualquier columna de la tabla.
        int totalAnterior = material.TotalQuantity;

        material.Name = peticion.Name.Trim();
        material.Type = peticion.Type.Trim();
        material.Publisher = string.IsNullOrWhiteSpace(peticion.Publisher)
            ? null : peticion.Publisher.Trim();
        material.TotalQuantity = peticion.TotalQuantity;
        material.LowStockThreshold = peticion.LowStockThreshold;

        // Del cambio interesa sobre todo el total: es el dato que altera lo que
        // el resto de usuarios puede pedir prestado.
        _auditor.Registrar(AuditActions.MaterialModificado, "Material", id,
            totalAnterior == material.TotalQuantity
                ? $"«{material.Name}»"
                : $"«{material.Name}», total {totalAnterior} → {material.TotalQuantity}");

        await _db.SaveChangesAsync(ct);

        return Ok(material.ToDto(compromiso.Fuera, compromiso.Reservado));
    }

    /// <summary>Elimina un articulo. Requiere administrador.</summary>
    /// <response code="409">El articulo tiene prestamos registrados.</response>
    [HttpDelete("{id:int}")]
    [Authorize(Roles = Roles.Admin)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        Material? material = await _db.Materials.FirstOrDefaultAsync(m => m.Id == id, ct);

        if (material is null)
        {
            return NotFound();
        }

        bool tienePrestamos = await _db.LoanLines.AnyAsync(l => l.MaterialId == id, ct);

        if (tienePrestamos)
        {
            return Conflict(new ProblemDetails
            {
                Title = "No se puede eliminar",
                Detail = "El artículo aparece en préstamos registrados. " +
                         "Eliminarlo dejaria el historial incompleto.",
                Status = StatusCodes.Status409Conflict
            });
        }

        _auditor.Registrar(AuditActions.MaterialEliminado, "Material", id,
            $"«{material.Name}»");

        _db.Materials.Remove(material);
        await _db.SaveChangesAsync(ct);

        _log.LogInformation("Articulo {MaterialId} eliminado por {User}",
            id, User.Identity?.Name);

        return NoContent();
    }

    /// <summary>
    /// Unidades fuera del almacen y unidades reservadas, por articulo.
    /// </summary>
    /// <remarks>
    /// Una sola consulta agregada para toda la pagina, en lugar de una por
    /// articulo. Con el listado completo eso es la diferencia entre 1 consulta
    /// y N+1.
    ///
    /// Se distinguen dos conceptos porque no significan lo mismo:
    ///
    /// - <b>Fuera</b>: entregado y sin devolucion confirmada. Incluye los
    ///   prestamos con devolucion solicitada pero no aceptada, porque nadie ha
    ///   comprobado todavia que el material haya vuelto.
    /// - <b>Reservado</b>: comprometido por una solicitud pendiente. Sigue en
    ///   el almacen, pero no se puede prometer a nadie mas.
    ///
    /// Ambos se descuentan de lo disponible, y por eso van juntos en la misma
    /// consulta.
    /// </remarks>
    private async Task<Dictionary<int, Compromiso>> PrestadoPorArticuloAsync(
        IReadOnlyCollection<int> ids, CancellationToken ct)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        var filas = await _db.LoanLines
            .Where(l => ids.Contains(l.MaterialId) &&
                        (l.Loan.Status == LoanStatus.Active ||
                         l.Loan.Status == LoanStatus.ReturnRequested ||
                         l.Loan.Status == LoanStatus.PendingApproval))
            .GroupBy(l => new { l.MaterialId, l.Loan.Status })
            .Select(g => new
            {
                g.Key.MaterialId,
                g.Key.Status,
                Total = g.Sum(x => x.Quantity)
            })
            .ToListAsync(ct);

        return filas
            .GroupBy(f => f.MaterialId)
            .ToDictionary(
                g => g.Key,
                g => new Compromiso(
                    Fuera: g.Where(f => f.Status != LoanStatus.PendingApproval)
                            .Sum(f => f.Total),
                    Reservado: g.Where(f => f.Status == LoanStatus.PendingApproval)
                                .Sum(f => f.Total)));
    }

    /// <summary>Unidades no disponibles de un articulo, por motivo.</summary>
    private readonly record struct Compromiso(int Fuera, int Reservado)
    {
        public int Total => Fuera + Reservado;
    }
}
