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
/// Bandeja de solicitudes de recuperacion de contrasena. Solo administradores.
/// </summary>
/// <remarks>
/// Sustituye al codigo enviado por correo: quien olvida su contrasena deja una
/// solicitud y un administrador la autoriza desde la propia aplicacion.
///
/// La decision es de una persona, y eso traslada una responsabilidad que antes
/// era del sistema: antes, quien recuperaba la cuenta demostraba tener acceso
/// al buzon registrado; ahora tiene que ser el administrador quien confirme,
/// por un canal aparte, que quien pide el cambio es quien dice ser. Aprobar sin
/// comprobarlo es entregar la cuenta.
/// </remarks>
[ApiController]
[Route("api/password-reset-requests")]
[Authorize(Roles = Roles.Admin)]
[Produces("application/json")]
public class PasswordResetRequestsController : ControllerBase
{
    private readonly AssetFlowDbContext _db;
    private readonly IPasswordHasher _hasher;
    private readonly IPasswordResetService _recuperacion;
    private readonly IAuditor _auditor;
    private readonly ILogger<PasswordResetRequestsController> _log;

    public PasswordResetRequestsController(
        AssetFlowDbContext db,
        IPasswordHasher hasher,
        IPasswordResetService recuperacion,
        IAuditor auditor,
        ILogger<PasswordResetRequestsController> log)
    {
        _db = db;
        _hasher = hasher;
        _recuperacion = recuperacion;
        _auditor = auditor;
        _log = log;
    }

    /// <summary>Lista las solicitudes, las pendientes primero.</summary>
    /// <param name="soloPendientes">Si es true, omite las ya resueltas.</param>
    /// <param name="ct">Token de cancelación.</param>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<PasswordResetRequestDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<PasswordResetRequestDto>>> Listar(
        [FromQuery] bool soloPendientes, CancellationToken ct)
    {
        IQueryable<PasswordResetRequest> consulta = _db.PasswordResetRequests
            .AsNoTracking()
            .Include(s => s.User);

        if (soloPendientes)
        {
            consulta = consulta.Where(s => s.Status == PasswordResetRequestStatus.Pending);
        }

        List<PasswordResetRequest> solicitudes = await consulta
            .OrderBy(s => s.Status == PasswordResetRequestStatus.Pending ? 0 : 1)
            .ThenByDescending(s => s.RequestedAt)
            // Cota dura: esta tabla crece con el uso y la bandeja no necesita
            // el historial completo para funcionar.
            .Take(200)
            .ToListAsync(ct);

        return Ok(solicitudes.Select(ADto).ToList());
    }

    /// <summary>Cuántas solicitudes hay esperando decisión.</summary>
    /// <remarks>
    /// Existe aparte del listado porque el cliente lo consulta de forma
    /// periodica para pintar el aviso, y traerse las filas enteras para
    /// contarlas seria gastar ancho de banda en algo que resuelve un COUNT.
    /// </remarks>
    /// <param name="ct">Token de cancelación.</param>
    [HttpGet("pending-count")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> ContarPendientes(CancellationToken ct)
    {
        int total = await _db.PasswordResetRequests
            .CountAsync(s => s.Status == PasswordResetRequestStatus.Pending, ct);

        return Ok(new { pendientes = total });
    }

    /// <summary>Autoriza la solicitud y asigna la contraseña provisional.</summary>
    /// <remarks>
    /// Devuelve la contrasena provisional para que el administrador pueda
    /// comunicarsela a la persona. No es un secreto: es deducible del nombre de
    /// usuario y deja de valer en cuanto se usa, porque la cuenta queda
    /// obligada a cambiarla.
    /// </remarks>
    /// <param name="id">Identificador de la solicitud.</param>
    /// <param name="ct">Token de cancelación.</param>
    /// <response code="200">Aprobada. Devuelve la contraseña provisional.</response>
    /// <response code="404">No existe.</response>
    /// <response code="409">Ya estaba resuelta.</response>
    [HttpPost("{id:int}/approve")]
    [ProducesResponseType(typeof(PasswordResetApprovalDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PasswordResetApprovalDto>> Aprobar(
        int id, CancellationToken ct)
    {
        PasswordResetRequest? solicitud = await _db.PasswordResetRequests
            .Include(s => s.User)
            .FirstOrDefaultAsync(s => s.Id == id, ct);

        if (solicitud is null)
        {
            return NotFound();
        }

        if (!solicitud.EstaPendiente)
        {
            return Conflict(new ProblemDetails
            {
                Title = "Solicitud ya resuelta",
                Detail = "Esta solicitud ya se había decidido. Actualiza la lista.",
                Status = StatusCodes.Status409Conflict
            });
        }

        if (!solicitud.User.IsActive)
        {
            return Conflict(new ProblemDetails
            {
                Title = "Cuenta desactivada",
                Detail = "La cuenta está desactivada. Actívala antes de darle acceso.",
                Status = StatusCodes.Status409Conflict
            });
        }

        string provisional = PasswordResetService.ContrasenaProvisional(solicitud.User.Username);

        // Todo junto o nada: si esto no fuera atomico, un fallo a mitad podria
        // dejar la contrasena provisional puesta sin la marca de cambio
        // obligatorio, es decir, una contrasena deducible y permanente.
        await using var transaccion = await _db.Database.BeginTransactionAsync(ct);

        solicitud.User.PasswordHash = _hasher.Hash(provisional);
        solicitud.User.MustChangePassword = true;

        solicitud.Status = PasswordResetRequestStatus.Approved;
        solicitud.ResolvedAt = DateTime.UtcNow;
        solicitud.ResolvedByUserId = this.UsuarioId();
        solicitud.ResolvedByUsername = User.Identity?.Name;

        // Se cierran las sesiones abiertas de esa cuenta. Es el motivo
        // principal por el que alguien recupera una contrasena: haber perdido
        // el control de la cuenta.
        List<RefreshToken> sesiones = await _db.RefreshTokens
            .Where(t => t.UserId == solicitud.UserId && t.RevokedAt == null)
            .ToListAsync(ct);

        foreach (RefreshToken sesion in sesiones)
        {
            sesion.RevokedAt = DateTime.UtcNow;
        }

        _auditor.Registrar(AuditActions.RecuperacionAprobada, "User", solicitud.UserId,
            $"Recuperación aprobada para «{solicitud.User.Username}». " +
            Texto.Contar(sesiones.Count, "sesión", "sesiones", "revocada", "revocadas"));

        await _db.SaveChangesAsync(ct);
        await transaccion.CommitAsync(ct);

        _log.LogWarning("Recuperacion de {Username} aprobada por {Admin}",
            solicitud.User.Username, User.Identity?.Name);

        _recuperacion.NotificarCambio(solicitud.User,
            "la ha reiniciado una persona con permisos de administración");

        return Ok(new PasswordResetApprovalDto
        {
            Username = solicitud.User.Username,
            ContrasenaProvisional = provisional
        });
    }

    /// <summary>Deniega la solicitud sin tocar la contraseña.</summary>
    /// <param name="id">Identificador de la solicitud.</param>
    /// <param name="ct">Token de cancelación.</param>
    /// <response code="204">Denegada.</response>
    /// <response code="404">No existe.</response>
    /// <response code="409">Ya estaba resuelta.</response>
    [HttpPost("{id:int}/reject")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Denegar(int id, CancellationToken ct)
    {
        PasswordResetRequest? solicitud = await _db.PasswordResetRequests
            .Include(s => s.User)
            .FirstOrDefaultAsync(s => s.Id == id, ct);

        if (solicitud is null)
        {
            return NotFound();
        }

        if (!solicitud.EstaPendiente)
        {
            return Conflict(new ProblemDetails
            {
                Title = "Solicitud ya resuelta",
                Detail = "Esta solicitud ya se había decidido. Actualiza la lista.",
                Status = StatusCodes.Status409Conflict
            });
        }

        solicitud.Status = PasswordResetRequestStatus.Rejected;
        solicitud.ResolvedAt = DateTime.UtcNow;
        solicitud.ResolvedByUserId = this.UsuarioId();
        solicitud.ResolvedByUsername = User.Identity?.Name;

        _auditor.Registrar(AuditActions.RecuperacionDenegada, "User", solicitud.UserId,
            $"Recuperación denegada para «{solicitud.User.Username}»");

        await _db.SaveChangesAsync(ct);

        _log.LogInformation("Recuperacion de {Username} denegada por {Admin}",
            solicitud.User.Username, User.Identity?.Name);

        return NoContent();
    }

    private static PasswordResetRequestDto ADto(PasswordResetRequest s) => new()
    {
        Id = s.Id,
        UserId = s.UserId,
        Username = s.User.Username,
        FullName = s.User.FullName,
        RequestedAt = s.RequestedAt,
        Estado = s.Status switch
        {
            PasswordResetRequestStatus.Pending => "Pendiente",
            PasswordResetRequestStatus.Approved => "Aprobada",
            PasswordResetRequestStatus.Rejected => "Denegada",
            _ => "Desconocido"
        },
        ResolvedAt = s.ResolvedAt,
        ResolvedByUsername = s.ResolvedByUsername,
        EstaPendiente = s.EstaPendiente
    };
}
