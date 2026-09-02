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
/// Gestion de cuentas.
/// </summary>
/// <remarks>
/// Este controlador sustituye al anterior, que exponia <c>GET /Users</c> sin
/// autenticacion devolviendo la entidad completa con el hash de contrasena,
/// y permitia darse de alta como administrador enviando <c>IsAdmin: 1</c>.
///
/// Reglas ahora:
/// - El listado completo y el alta son exclusivos de administradores.
/// - Un usuario normal solo puede leer y editar su propia ficha.
/// - El rol y el estado de la cuenta solo los cambia un administrador, y
///   nunca por el mismo endpoint que los datos de contacto.
/// - La contrasena no entra ni sale por ninguno de estos DTO.
/// </remarks>
[ApiController]
[Route("api/users")]
[Authorize]
[Produces("application/json")]
public class UsersController : ControllerBase
{
    private readonly AssetFlowDbContext _db;
    private readonly IPasswordHasher _hasher;
    private readonly IAuditor _auditor;
    private readonly IPasswordResetService _recuperacion;
    private readonly ILogger<UsersController> _log;

    public UsersController(
        AssetFlowDbContext db, IPasswordHasher hasher, IAuditor auditor,
        IPasswordResetService recuperacion, ILogger<UsersController> log)
    {
        _db = db;
        _hasher = hasher;
        _auditor = auditor;
        _recuperacion = recuperacion;
        _log = log;
    }

    /// <summary>Lista todas las cuentas. Requiere administrador.</summary>
    [HttpGet]
    [Authorize(Roles = Roles.Admin)]
    [ProducesResponseType(typeof(IEnumerable<UserDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IEnumerable<UserDto>>> GetAll(CancellationToken ct)
    {
        var usuarios = await _db.Users
            .AsNoTracking()
            .OrderBy(u => u.Username)
            .Select(u => new
            {
                Usuario = u,
                Activos = u.Loans.Count(l => l.Status == LoanStatus.Active)
            })
            .ToListAsync(ct);

        return Ok(usuarios.Select(x => x.Usuario.ToDto(x.Activos)).ToList());
    }

    /// <summary>
    /// Lista reducida de usuarios, para poder elegir a quien se presta.
    /// </summary>
    /// <remarks>
    /// Existe separada de <c>GET /api/users</c> porque un usuario normal
    /// necesita nombres para registrar un prestamo, pero no tiene por que ver
    /// correos, telefonos ni roles de nadie.
    /// </remarks>
    [HttpGet("summary")]
    [ProducesResponseType(typeof(IEnumerable<UserSummaryDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<UserSummaryDto>>> GetSummary(CancellationToken ct)
    {
        var usuarios = await _db.Users
            .AsNoTracking()
            .Where(u => u.IsActive)
            .OrderBy(u => u.FirstName)
            .ToListAsync(ct);

        return Ok(usuarios.Select(u => u.ToSummary()).ToList());
    }

    /// <summary>
    /// Devuelve una cuenta. Un usuario normal solo puede consultar la suya.
    /// </summary>
    /// <response code="403">Se ha solicitado la ficha de otro usuario sin ser administrador.</response>
    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<UserDto>> Get(int id, CancellationToken ct)
    {
        // Esta comprobacion es la barrera anti-IDOR: sin ella, cambiar el
        // numero de la URL da acceso a la ficha de cualquiera.
        if (!PuedeAcceder(id))
        {
            return Prohibido();
        }

        User? usuario = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id, ct);

        if (usuario is null)
        {
            return NotFound();
        }

        int activos = await _db.Loans
            .CountAsync(l => l.UserId == id && l.Status == LoanStatus.Active, ct);

        return Ok(usuario.ToDto(activos));
    }

    /// <summary>Crea una cuenta. Requiere administrador.</summary>
    /// <response code="409">El nombre de usuario o el correo ya existen.</response>
    [HttpPost]
    [Authorize(Roles = Roles.Admin)]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<UserDto>> Create(
        CreateUserRequest peticion, CancellationToken ct)
    {
        string usuario = peticion.Username.Trim();
        string correo = peticion.Email.Trim();

        bool repetido = await _db.Users.AnyAsync(
            u => u.Username == usuario || u.Email == correo, ct);

        if (repetido)
        {
            return Conflict(new ProblemDetails
            {
                Title = "Cuenta duplicada",
                Detail = "Ya existe una cuenta con ese nombre de usuario o correo.",
                Status = StatusCodes.Status409Conflict
            });
        }

        // El rol se toma del DTO, que solo admite "Admin" o "User" por
        // expresion regular, y este endpoint ya exige ser administrador. Un
        // usuario normal no puede llegar aqui para promocionarse.
        var nuevo = new User
        {
            Username = usuario,
            FirstName = peticion.FirstName.Trim(),
            LastName = peticion.LastName.Trim(),
            Email = correo,
            PhoneNumber = string.IsNullOrWhiteSpace(peticion.PhoneNumber)
                ? null : peticion.PhoneNumber.Trim(),
            PasswordHash = _hasher.Hash(peticion.Password),
            Role = peticion.Role
        };

        await using var transaccion = await _db.Database.BeginTransactionAsync(ct);

        _db.Users.Add(nuevo);
        await _db.SaveChangesAsync(ct);

        // Despues del primer guardado porque la anotacion necesita el Id
        // asignado, pero dentro de la misma transaccion: no puede quedar una
        // cuenta creada sin rastro de quien la creo.
        _auditor.Registrar(AuditActions.UsuarioCreado, "User", nuevo.Id,
            $"Alta de «{nuevo.Username}» con rol {nuevo.Role}");

        await _db.SaveChangesAsync(ct);
        await transaccion.CommitAsync(ct);

        _log.LogInformation("Cuenta {Username} creada con rol {Role} por {Admin}",
            nuevo.Username, nuevo.Role, User.Identity?.Name);

        return CreatedAtAction(nameof(Get), new { id = nuevo.Id }, nuevo.ToDto(0));
    }

    /// <summary>Actualiza los datos de contacto. Propios o, si es administrador, de cualquiera.</summary>
    [HttpPut("{id:int}")]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<UserDto>> Update(
        int id, UpdateUserRequest peticion, CancellationToken ct)
    {
        if (!PuedeAcceder(id))
        {
            return Prohibido();
        }

        User? usuario = await _db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);

        if (usuario is null)
        {
            return NotFound();
        }

        string correo = peticion.Email.Trim();

        bool correoOcupado = await _db.Users.AnyAsync(
            u => u.Email == correo && u.Id != id, ct);

        if (correoOcupado)
        {
            return Conflict(new ProblemDetails
            {
                Title = "Correo duplicado",
                Detail = "Ese correo ya pertenece a otra cuenta.",
                Status = StatusCodes.Status409Conflict
            });
        }

        usuario.FirstName = peticion.FirstName.Trim();
        usuario.LastName = peticion.LastName.Trim();
        usuario.Email = correo;
        usuario.PhoneNumber = string.IsNullOrWhiteSpace(peticion.PhoneNumber)
            ? null : peticion.PhoneNumber.Trim();

        await _db.SaveChangesAsync(ct);

        int activos = await _db.Loans
            .CountAsync(l => l.UserId == id && l.Status == LoanStatus.Active, ct);

        return Ok(usuario.ToDto(activos));
    }

    /// <summary>Cambia el rol o activa/desactiva una cuenta. Requiere administrador.</summary>
    [HttpPut("{id:int}/access")]
    [Authorize(Roles = Roles.Admin)]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<UserDto>> UpdateAccess(
        int id, UpdateUserAccessRequest peticion, CancellationToken ct)
    {
        User? usuario = await _db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);

        if (usuario is null)
        {
            return NotFound();
        }

        // Un administrador no puede degradarse ni desactivarse a si mismo: es
        // la forma mas facil de dejar el sistema sin ningun administrador.
        if (id == this.UsuarioId() && (peticion.Role != Roles.Admin || !peticion.IsActive))
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Operacion no permitida",
                Detail = "No puedes retirarte a ti mismo el acceso de administrador.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        // Tampoco puede quedar el sistema sin ningun administrador activo.
        if (usuario.Role == Roles.Admin && (peticion.Role != Roles.Admin || !peticion.IsActive))
        {
            int otrosAdmins = await _db.Users.CountAsync(
                u => u.Role == Roles.Admin && u.IsActive && u.Id != id, ct);

            if (otrosAdmins == 0)
            {
                return BadRequest(new ProblemDetails
                {
                    Title = "Operacion no permitida",
                    Detail = "Debe quedar al menos un administrador activo.",
                    Status = StatusCodes.Status400BadRequest
                });
            }
        }

        string rolAnterior = usuario.Role;
        bool activaAntes = usuario.IsActive;

        usuario.Role = peticion.Role;
        usuario.IsActive = peticion.IsActive;

        // Se anota el antes y el despues: un registro que solo diga «acceso
        // modificado» no permite reconstruir una escalada de privilegios.
        _auditor.Registrar(AuditActions.AccesoModificado, "User", id,
            $"«{usuario.Username}»: rol {rolAnterior} → {usuario.Role}, " +
            $"{(activaAntes ? "activa" : "inactiva")} → " +
            $"{(usuario.IsActive ? "activa" : "inactiva")}");

        // Desactivar una cuenta debe cortar sus sesiones abiertas. Sin esto,
        // el usuario seguiria operando hasta que caducara su refresh token.
        if (!peticion.IsActive)
        {
            var vivos = await _db.RefreshTokens
                .Where(t => t.UserId == id && t.RevokedAt == null)
                .ToListAsync(ct);

            foreach (var token in vivos)
            {
                token.RevokedAt = DateTime.UtcNow;
            }
        }

        await _db.SaveChangesAsync(ct);

        _log.LogInformation("Acceso de {Username} cambiado a {Role}/{Estado} por {Admin}",
            usuario.Username, usuario.Role,
            usuario.IsActive ? "activo" : "inactivo", User.Identity?.Name);

        int activos = await _db.Loans
            .CountAsync(l => l.UserId == id && l.Status == LoanStatus.Active, ct);

        return Ok(usuario.ToDto(activos));
    }

    /// <summary>Reinicia la contrasena de una cuenta. Requiere administrador.</summary>
    /// <remarks>
    /// Es el atajo para cuando no hace falta esperar a que la persona pida
    /// nada: asigna la contrasena provisional y obliga a cambiarla al entrar,
    /// igual que aprobar una solicitud.
    ///
    /// El administrador no elige la contrasena. Eso es deliberado: si la
    /// eligiera, la conoceria, y una contrasena que conocen dos personas ya no
    /// identifica a ninguna de las dos. Asi el unico momento en que existe una
    /// contrasena definitiva es cuando su titular la escribe.
    /// </remarks>
    /// <param name="id">Cuenta afectada.</param>
    /// <param name="ct">Token de cancelación.</param>
    /// <response code="200">Reiniciada. Devuelve la contraseña provisional.</response>
    /// <response code="404">No existe.</response>
    [HttpPost("{id:int}/password")]
    [Authorize(Roles = Roles.Admin)]
    [ProducesResponseType(typeof(PasswordResetApprovalDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PasswordResetApprovalDto>> ResetPassword(
        int id, CancellationToken ct)
    {
        User? usuario = await _db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);

        if (usuario is null)
        {
            return NotFound();
        }

        string provisional = PasswordResetService.GenerarContrasenaProvisional();

        usuario.PasswordHash = _hasher.Hash(provisional);
        usuario.MustChangePassword = true;
        usuario.ProvisionalPasswordExpiresAt =
            DateTime.UtcNow.Add(PasswordResetService.ValidezProvisional);

        _auditor.Registrar(AuditActions.ContrasenaReiniciada, "User", id,
            $"Reinicio administrativo de la contraseña de «{usuario.Username}»");

        await RevocarSesionesAsync(id, ct);
        await _db.SaveChangesAsync(ct);

        _recuperacion.NotificarCambio(usuario,
            "la ha reiniciado una persona con permisos de administración");

        _log.LogWarning("Contrasena de {Username} reiniciada por {Admin}",
            usuario.Username, User.Identity?.Name);

        return Ok(new PasswordResetApprovalDto
        {
            Username = usuario.Username,
            ContrasenaProvisional = provisional
        });
    }

    /// <summary>Desactiva una cuenta. Requiere administrador.</summary>
    /// <remarks>
    /// Se desactiva en lugar de borrarse cuando tiene historial: eliminar la
    /// fila dejaria prestamos huerfanos y falsearia el historico.
    /// </remarks>
    [HttpDelete("{id:int}")]
    [Authorize(Roles = Roles.Admin)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        if (id == this.UsuarioId())
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Operacion no permitida",
                Detail = "No puedes eliminar tu propia cuenta.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        User? usuario = await _db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);

        if (usuario is null)
        {
            return NotFound();
        }

        bool tienePrestamosVivos = await _db.Loans.AnyAsync(
            l => l.UserId == id && l.Status == LoanStatus.Active, ct);

        if (tienePrestamosVivos)
        {
            return Conflict(new ProblemDetails
            {
                Title = "No se puede eliminar",
                Detail = "El usuario tiene préstamos sin devolver.",
                Status = StatusCodes.Status409Conflict
            });
        }

        bool tieneHistorial = await _db.Loans.AnyAsync(l => l.UserId == id, ct);

        if (tieneHistorial)
        {
            usuario.IsActive = false;
            await RevocarSesionesAsync(id, ct);
        }
        else
        {
            _db.Users.Remove(usuario);
        }

        // El nombre se copia al detalle porque la fila puede desaparecer: la
        // entrada de auditoria no tiene clave ajena a Users precisamente para
        // que siga siendo legible cuando la cuenta ya no exista.
        _auditor.Registrar(AuditActions.UsuarioEliminado, "User", id,
            tieneHistorial
                ? $"«{usuario.Username}» desactivada (conserva historial de préstamos)"
                : $"«{usuario.Username}» eliminada");

        await _db.SaveChangesAsync(ct);

        _log.LogInformation("Cuenta {Username} {Accion} por {Admin}",
            usuario.Username, tieneHistorial ? "desactivada" : "eliminada",
            User.Identity?.Name);

        return NoContent();
    }

    private async Task RevocarSesionesAsync(int usuarioId, CancellationToken ct)
    {
        var vivos = await _db.RefreshTokens
            .Where(t => t.UserId == usuarioId && t.RevokedAt == null)
            .ToListAsync(ct);

        foreach (var token in vivos)
        {
            token.RevokedAt = DateTime.UtcNow;
        }
    }

    private bool PuedeAcceder(int usuarioId) =>
        this.EsAdministrador() || this.UsuarioId() == usuarioId;

    /// <summary>
    /// 403 y no 404: el usuario esta autenticado y la peticion es legitima en
    /// forma, simplemente no tiene permiso. Devolver 404 aqui no aporta
    /// seguridad porque los identificadores son secuenciales y ya se sabe que
    /// existen.
    /// </summary>
    private ObjectResult Prohibido() => StatusCode(
        StatusCodes.Status403Forbidden,
        new ProblemDetails
        {
            Title = "Acceso denegado",
            Detail = "No tienes permiso para acceder a esta cuenta.",
            Status = StatusCodes.Status403Forbidden
        });
}
