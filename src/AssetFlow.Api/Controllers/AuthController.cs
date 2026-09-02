using System.Security.Claims;
using AssetFlow.Api.Data;
using AssetFlow.Api.Dtos;
using AssetFlow.Api.Entities;
using AssetFlow.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AssetFlow.Api.Controllers;

/// <summary>
/// Inicio y cierre de sesion.
/// </summary>
[ApiController]
[Route("api/auth")]
[Produces("application/json")]
public class AuthController : ControllerBase
{
    private readonly AssetFlowDbContext _db;
    private readonly IPasswordHasher _hasher;
    private readonly ITokenService _tokens;
    private readonly ILoginThrottle _throttle;
    private readonly IPasswordResetService _recuperacion;
    private readonly IAuditor _auditor;
    private readonly JwtOptions _jwt;
    private readonly ILogger<AuthController> _log;

    public AuthController(
        AssetFlowDbContext db,
        IPasswordHasher hasher,
        ITokenService tokens,
        ILoginThrottle throttle,
        IPasswordResetService recuperacion,
        IAuditor auditor,
        IOptions<JwtOptions> jwt,
        ILogger<AuthController> log)
    {
        _db = db;
        _hasher = hasher;
        _tokens = tokens;
        _throttle = throttle;
        _recuperacion = recuperacion;
        _auditor = auditor;
        _jwt = jwt.Value;
        _log = log;
    }

    /// <summary>Valida las credenciales y abre una sesion.</summary>
    /// <response code="200">Sesion abierta.</response>
    /// <response code="401">Credenciales incorrectas o cuenta desactivada.</response>
    /// <response code="429">Demasiados intentos.</response>
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<AuthResponse>> Login(
        LoginRequest peticion, CancellationToken ct)
    {
        // El bloqueo se comprueba antes de tocar la base de datos: una cuenta
        // bajo ataque no debe generar ni una consulta mas por intento.
        if (_throttle.EstaBloqueada(peticion.Username))
        {
            _log.LogWarning("Acceso rechazado: cuenta {Username} bloqueada temporalmente",
                peticion.Username);

            return StatusCode(StatusCodes.Status429TooManyRequests, new ProblemDetails
            {
                Title = "Cuenta bloqueada temporalmente",
                Detail = "Demasiados intentos fallidos. Vuelve a intentarlo en unos minutos.",
                Status = StatusCodes.Status429TooManyRequests
            });
        }

        User? usuario = await _db.Users
            .FirstOrDefaultAsync(u => u.Username == peticion.Username, ct);

        // La verificacion se ejecuta incluso si el usuario no existe, contra un
        // hash ficticio, para que el tiempo de respuesta no revele que nombres
        // de usuario estan dados de alta.
        const string HashSenuelo =
            "$2a$12$C6UzMDM.H6dfI/f/IKcEe.7uVCG7pQVBqLdMB0sQmYb1sTBRIyBTa";

        bool correcta = _hasher.Verify(
            peticion.Password, usuario?.PasswordHash ?? HashSenuelo);

        // Una contrasena provisional caducada se rechaza igual que una
        // incorrecta: la llave dictada por telefono tiene fecha de caducidad,
        // y pasada esa fecha hay que pedir otra recuperacion. La respuesta no
        // distingue el caso para no regalar informacion sobre la cuenta.
        bool caducada = usuario is
        {
            MustChangePassword: true,
            ProvisionalPasswordExpiresAt: { } limite
        } && limite < DateTime.UtcNow;

        if (usuario is null || !correcta || !usuario.IsActive || caducada)
        {
            bool bloqueada = _throttle.RegistrarFallo(peticion.Username);

            // Un unico mensaje para los tres casos: distinguirlos permitiria
            // enumerar cuentas. Nunca se registra la contrasena, solo el
            // nombre de usuario, que es necesario para investigar un ataque.
            _log.LogWarning("Intento de acceso fallido para {Username}{Bloqueo}",
                peticion.Username, bloqueada ? " (cuenta bloqueada)" : string.Empty);

            return Unauthorized(Problema("Usuario o contraseña incorrectos."));
        }

        _throttle.RegistrarExito(peticion.Username);

        // Se anota antes de abrir la sesion porque AbrirSesionAsync es quien
        // guarda: asi el registro y la sesion se persisten en la misma
        // operacion. Se usa RegistrarComo y no Registrar porque en este punto
        // la peticion todavia no esta autenticada y no hay claims que leer.
        _auditor.RegistrarComo(usuario.Id, usuario.Username,
            AuditActions.SesionIniciada, "User", usuario.Id, $"Rol: {usuario.Role}");

        AuthResponse respuesta = await AbrirSesionAsync(usuario, ct);

        _log.LogInformation("Sesion iniciada por {Username} ({Role})",
            usuario.Username, usuario.Role);

        return Ok(respuesta);
    }

    /// <summary>Canjea un refresh token por una sesion nueva.</summary>
    /// <response code="200">Sesion renovada.</response>
    /// <response code="401">Token ausente, caducado, revocado o ya usado.</response>
    [HttpPost("refresh")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AuthResponse>> Refresh(
        RefreshRequest peticion, CancellationToken ct)
    {
        string hash = _tokens.HashRefreshToken(peticion.RefreshToken);

        RefreshToken? guardado = await _db.RefreshTokens
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.TokenHash == hash, ct);

        if (guardado is null)
        {
            return Unauthorized(Problema("Sesión no válida."));
        }

        // Reutilizacion de un token ya rotado: o bien el cliente ha reintentado
        // con uno viejo, o bien alguien ha robado la cadena. En cualquier caso
        // se cierran todas las sesiones del usuario y se obliga a entrar de
        // nuevo, que es la respuesta segura.
        if (guardado.RevokedAt is not null)
        {
            _log.LogWarning(
                "Refresh token reutilizado del usuario {UserId}. Se revocan sus sesiones.",
                guardado.UserId);

            // Se anota como el propio usuario porque la peticion es anonima:
            // quien la envia demuestra tener un token suyo, aunque sea uno ya
            // consumido. Es justo el caso que interesa poder revisar despues.
            _auditor.RegistrarComo(guardado.UserId, guardado.User.Username,
                AuditActions.SesionesRevocadas, "User", guardado.UserId,
                "Refresh token reutilizado: se revoca la cadena completa");

            await RevocarTodasAsync(guardado.UserId, ct);
            return Unauthorized(Problema("Sesión no válida."));
        }

        if (!guardado.IsActive || !guardado.User.IsActive)
        {
            return Unauthorized(Problema("Sesion caducada."));
        }

        // Rotacion: el token consumido se marca revocado y apunta al que lo
        // sustituye, de modo que la reutilizacion sea detectable.
        var (nuevo, nuevoHash) = _tokens.CreateRefreshToken();

        guardado.RevokedAt = DateTime.UtcNow;
        guardado.ReplacedByTokenHash = nuevoHash;

        var (acceso, expiraAcceso) = _tokens.CreateAccessToken(guardado.User);
        var expiraRefresco = DateTime.UtcNow.AddDays(_jwt.RefreshTokenDays);

        _db.RefreshTokens.Add(new RefreshToken
        {
            UserId = guardado.UserId,
            TokenHash = nuevoHash,
            ExpiresAt = expiraRefresco
        });

        await _db.SaveChangesAsync(ct);

        return Ok(new AuthResponse
        {
            AccessToken = acceso,
            AccessTokenExpiresAt = expiraAcceso,
            RefreshToken = nuevo,
            RefreshTokenExpiresAt = expiraRefresco,
            User = guardado.User.ToCurrentUser()
        });
    }

    /// <summary>Cierra la sesion actual revocando su refresh token.</summary>
    /// <remarks>
    /// El access token sigue siendo valido hasta que caduque (minutos): es la
    /// contrapartida conocida de JWT. Lo que se corta aqui es la capacidad de
    /// seguir renovando, que es lo que convierte una sesion en persistente.
    /// </remarks>
    [HttpPost("logout")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout(RefreshRequest peticion, CancellationToken ct)
    {
        string hash = _tokens.HashRefreshToken(peticion.RefreshToken);
        int usuarioId = this.UsuarioId();

        RefreshToken? guardado = await _db.RefreshTokens
            .FirstOrDefaultAsync(t => t.TokenHash == hash && t.UserId == usuarioId, ct);

        if (guardado is { RevokedAt: null })
        {
            guardado.RevokedAt = DateTime.UtcNow;
            _auditor.Registrar(AuditActions.SesionCerrada, "User", usuarioId);
            await _db.SaveChangesAsync(ct);
        }

        // 204 siempre: responder distinto segun si el token existia permitiria
        // usar este endpoint para comprobar si un token es valido.
        return NoContent();
    }

    /// <summary>Cierra todas las sesiones del usuario en todos los equipos.</summary>
    [HttpPost("logout-all")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> LogoutAll(CancellationToken ct)
    {
        int usuarioId = this.UsuarioId();

        _auditor.Registrar(AuditActions.SesionesRevocadas, "User", usuarioId,
            "Cierre de todas las sesiones a peticion del propio usuario");

        await RevocarTodasAsync(usuarioId, ct);
        return NoContent();
    }

    // ========================================================================
    // RECUPERACION DE CONTRASENA
    // ========================================================================

    /// <summary>Pide a un administrador que autorice recuperar la contraseña.</summary>
    /// <remarks>
    /// Responde siempre 202 y siempre lo mismo, exista o no la cuenta. Es el
    /// punto central de este endpoint: si distinguiera los casos, cualquiera
    /// podria usarlo para averiguar que correos estan dados de alta, que es el
    /// primer paso de un ataque dirigido. Por el mismo motivo responde igual
    /// cuando ya hay una solicitud pendiente o cuando se ha superado el limite.
    ///
    /// Comparte el limitador con el inicio de sesión. Sin el, este endpoint
    /// seria a la vez un enumerador de cuentas y una forma de llenar de
    /// solicitudes la bandeja del administrador.
    /// </remarks>
    /// <response code="202">Petición recibida. No indica si la cuenta existe.</response>
    [HttpPost("forgot-password")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> ForgotPassword(
        ForgotPasswordRequest peticion, CancellationToken ct)
    {
        await _recuperacion.SolicitarAsync(peticion.Email, ct);

        return Accepted(new
        {
            message = "Si existe una cuenta asociada a ese correo, un administrador " +
                      "recibirá tu solicitud y te facilitará una contraseña provisional."
        });
    }

    /// <summary>Cambia la contraseña provisional por una definitiva.</summary>
    /// <remarks>
    /// <b>Es el unico punto de la aplicacion en el que una persona fija su
    /// propia contrasena</b>, y solo funciona mientras la cuenta arrastre el
    /// cambio pendiente. Fuera de esa situacion devuelve 403: las contrasenas
    /// las gestiona la administracion.
    ///
    /// Devuelve una sesion nueva porque el token con el que se llega aqui lleva
    /// el claim de cambio pendiente y seguiria bloqueado por
    /// <c>CambioObligatorioMiddleware</c> aunque el cambio ya se hubiera hecho.
    /// </remarks>
    /// <response code="200">Contraseña cambiada. Devuelve una sesión nueva.</response>
    /// <response code="400">La contraseña actual no es correcta.</response>
    /// <response code="403">La cuenta no tiene ningún cambio pendiente.</response>
    [HttpPost("change-password")]
    [Authorize]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<AuthResponse>> ChangePassword(
        ChangePasswordRequest peticion, CancellationToken ct)
    {
        int id = this.UsuarioId();

        User? usuario = await _db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);

        if (usuario is null || !usuario.IsActive)
        {
            return Unauthorized(Problema("Sesión no válida."));
        }

        // Se comprueba contra la base de datos y no contra el claim del token:
        // el token es una copia que puede haber quedado atras, y la respuesta
        // a "¿tiene esta cuenta un cambio pendiente?" tiene que salir del dato
        // real.
        if (!usuario.MustChangePassword)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new ProblemDetails
            {
                Title = "Operación no permitida",
                Detail = "Tu cuenta no tiene ningún cambio de contraseña pendiente. " +
                         "Si necesitas cambiarla, pídeselo a un administrador.",
                Status = StatusCodes.Status403Forbidden
            });
        }

        if (!_hasher.Verify(peticion.CurrentPassword, usuario.PasswordHash))
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Contraseña incorrecta",
                Detail = "La contraseña actual no es correcta.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        // Rechazar que la nueva sea la provisional otra vez. Sin esto, el
        // usuario puede "cambiarla" por la misma y quedarse con una contrasena
        // que tambien conoce el administrador que la dicto, que es justo lo
        // que este flujo existe para impedir. Se comprueba contra el hash
        // vigente, que en este punto es siempre el de la provisional.
        if (_hasher.Verify(peticion.NewPassword, usuario.PasswordHash))
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Contraseña no válida",
                Detail = "No puedes quedarte con la contraseña provisional: " +
                         "elige una distinta que sólo conozcas tú.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        usuario.PasswordHash = _hasher.Hash(peticion.NewPassword);
        usuario.MustChangePassword = false;
        usuario.ProvisionalPasswordExpiresAt = null;

        _auditor.RegistrarComo(usuario.Id, usuario.Username,
            AuditActions.RecuperacionCompletada, "User", usuario.Id);

        // Se cierran las demas sesiones. Si la provisional llego a manos de
        // alguien mas, esta es la unica forma de echarlo.
        List<RefreshToken> sesiones = await _db.RefreshTokens
            .Where(t => t.UserId == usuario.Id && t.RevokedAt == null)
            .ToListAsync(ct);

        foreach (RefreshToken sesion in sesiones)
        {
            sesion.RevokedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(ct);

        _throttle.RegistrarExito(usuario.Username);

        _log.LogInformation(
            "Contrasena provisional cambiada por el usuario {UserId}", usuario.Id);

        _recuperacion.NotificarCambio(usuario, "la has elegido tú al entrar");

        // Sesion nueva, ya sin el claim de cambio pendiente.
        return Ok(await AbrirSesionAsync(usuario, ct));
    }

    /// <summary>Devuelve la identidad del usuario autenticado.</summary>
    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType(typeof(CurrentUserDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<CurrentUserDto>> Me(CancellationToken ct)
    {
        int id = this.UsuarioId();

        User? usuario = await _db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);

        // El usuario puede haber sido desactivado despues de emitirse el token.
        if (usuario is null || !usuario.IsActive)
        {
            return Unauthorized(Problema("Sesión no válida."));
        }

        return Ok(usuario.ToCurrentUser());
    }

    private async Task<AuthResponse> AbrirSesionAsync(User usuario, CancellationToken ct)
    {
        var (acceso, expiraAcceso) = _tokens.CreateAccessToken(usuario);
        var (refresco, hashRefresco) = _tokens.CreateRefreshToken();
        var expiraRefresco = DateTime.UtcNow.AddDays(_jwt.RefreshTokenDays);

        _db.RefreshTokens.Add(new RefreshToken
        {
            UserId = usuario.Id,
            TokenHash = hashRefresco,
            ExpiresAt = expiraRefresco
        });

        // Limpieza oportunista: sin esto la tabla crece sin limite con tokens
        // caducados que ya no sirven para nada.
        //
        // El instante se calcula fuera de la consulta: DateTime.UtcNow
        // dentro de un Where no es traducible a SQL y hace fallar la peticion
        // en tiempo de ejecucion.
        DateTime ahora = DateTime.UtcNow;

        var caducados = await _db.RefreshTokens
            .Where(t => t.UserId == usuario.Id && t.ExpiresAt < ahora)
            .ToListAsync(ct);

        _db.RefreshTokens.RemoveRange(caducados);

        await _db.SaveChangesAsync(ct);

        return new AuthResponse
        {
            AccessToken = acceso,
            AccessTokenExpiresAt = expiraAcceso,
            RefreshToken = refresco,
            RefreshTokenExpiresAt = expiraRefresco,
            User = usuario.ToCurrentUser()
        };
    }

    private async Task RevocarTodasAsync(int usuarioId, CancellationToken ct)
    {
        var vivos = await _db.RefreshTokens
            .Where(t => t.UserId == usuarioId && t.RevokedAt == null)
            .ToListAsync(ct);

        foreach (var token in vivos)
        {
            token.RevokedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(ct);
    }

    private static ProblemDetails Problema(string detalle) => new()
    {
        Title = "No autorizado",
        Detail = detalle,
        Status = StatusCodes.Status401Unauthorized
    };
}

/// <summary>Acceso tipado a los claims del usuario autenticado.</summary>
public static class ControllerBaseExtensions
{
    /// <summary>
    /// Identificador del usuario autenticado, tomado del token.
    /// </summary>
    /// <remarks>
    /// Este es el punto clave del modelo de autorizacion: la identidad SIEMPRE
    /// sale del token firmado, nunca de un parametro de la peticion. Un
    /// atacante puede escribir el UserId que quiera en el cuerpo o en la URL,
    /// pero no puede falsificar un claim sin la clave de firma.
    /// </remarks>
    public static int UsuarioId(this ControllerBase c)
    {
        string? valor = c.User.FindFirstValue(ClaimTypes.NameIdentifier);

        return int.TryParse(valor, out int id)
            ? id
            : throw new InvalidOperationException("Token sin identificador de usuario.");
    }

    public static bool EsAdministrador(this ControllerBase c) =>
        c.User.IsInRole(Security.Roles.Admin);
}
