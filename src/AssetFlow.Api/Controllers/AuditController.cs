using AssetFlow.Api.Data;
using AssetFlow.Api.Dtos;
using AssetFlow.Api.Entities;
using AssetFlow.Api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AssetFlow.Api.Controllers;

/// <summary>
/// Consulta del registro de auditoria.
/// </summary>
/// <remarks>
/// Solo lectura y solo para administradores. No existe ningun endpoint para
/// escribir ni para borrar entradas: las anota el servidor dentro de la misma
/// transaccion que la operacion auditada, y un registro que se pueda editar
/// desde fuera no sirve como registro.
/// </remarks>
[ApiController]
[Route("api/audit")]
[Authorize(Roles = Roles.Admin)]
[Produces("application/json")]
public class AuditController : ControllerBase
{
    private const int TamanoPaginaMaximo = 100;

    private readonly AssetFlowDbContext _db;

    public AuditController(AssetFlowDbContext db) => _db = db;

    /// <summary>Lista las acciones registradas, de la mas reciente a la mas antigua.</summary>
    /// <param name="action">Filtro por clave de accion exacta, p. ej. "prestamo.aprobado".</param>
    /// <param name="entityType">Filtro por tipo de entidad: "Loan", "User" o "Material".</param>
    /// <param name="entityId">Filtro por identificador de entidad.</param>
    /// <param name="page">Pagina, empezando en 1.</param>
    /// <param name="pageSize">Entradas por pagina. Maximo 100.</param>
    /// <param name="ct">Token de cancelacion.</param>
    [HttpGet]
    [ProducesResponseType(typeof(AuditPageDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<AuditPageDto>> GetAll(
        [FromQuery] string? action,
        [FromQuery] string? entityType,
        [FromQuery] int? entityId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        // Los parametros de paginacion se acotan en lugar de rechazarse: el
        // limite superior existe para que nadie pueda pedir la tabla entera en
        // una sola peticion, no para castigar una URL mal escrita.
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, TamanoPaginaMaximo);

        IQueryable<AuditEntry> consulta = _db.AuditEntries.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(action))
        {
            string clave = action.Trim();
            consulta = consulta.Where(e => e.Action == clave);
        }

        if (!string.IsNullOrWhiteSpace(entityType))
        {
            string tipo = entityType.Trim();
            consulta = consulta.Where(e => e.EntityType == tipo);
        }

        if (entityId is not null)
        {
            consulta = consulta.Where(e => e.EntityId == entityId);
        }

        int total = await consulta.CountAsync(ct);

        List<AuditEntry> entradas = await consulta
            .OrderByDescending(e => e.OccurredAt)
            .ThenByDescending(e => e.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return Ok(new AuditPageDto
        {
            Items = entradas.Select(e => new AuditEntryDto
            {
                Id = e.Id,
                OccurredAt = e.OccurredAt,
                ActorUsername = e.ActorUsername,
                Action = e.Action,
                EntityType = e.EntityType,
                EntityId = e.EntityId,
                Details = e.Details
            }).ToList(),
            Total = total,
            Page = page,
            PageSize = pageSize
        });
    }
}
