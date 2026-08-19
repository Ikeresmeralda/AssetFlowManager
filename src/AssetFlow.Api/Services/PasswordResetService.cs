using System.Diagnostics;
using AssetFlow.Api.Data;
using AssetFlow.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace AssetFlow.Api.Services;

/// <summary>
/// Solicitudes de recuperacion de contrasena y su resolucion por un
/// administrador.
/// </summary>
public interface IPasswordResetService
{
    /// <summary>
    /// Registra una solicitud de recuperacion para el correo indicado.
    /// </summary>
    /// <remarks>
    /// No devuelve nada y no falla si el correo no existe: quien llama debe
    /// responder siempre lo mismo. Si esta operacion distinguiera los casos,
    /// el formulario de recuperacion se convertiria en un comprobador de que
    /// cuentas hay dadas de alta.
    /// </remarks>
    Task SolicitarAsync(string correo, CancellationToken ct);

    /// <summary>
    /// Avisa por correo al titular de que su contrasena ha cambiado.
    /// </summary>
    /// <param name="usuario">Cuenta afectada.</param>
    /// <param name="motivo">
    /// Frase corta que completa «la contraseña acaba de cambiar: ...».
    /// </param>
    void NotificarCambio(User usuario, string motivo);
}

public sealed class PasswordResetService : IPasswordResetService
{
    /// <summary>
    /// Contrasena provisional que se asigna al aprobar una solicitud.
    /// </summary>
    /// <remarks>
    /// Es predecible a proposito: el administrador tiene que poder decirsela a
    /// la persona por telefono sin leer una cadena aleatoria.
    ///
    /// <b>Eso solo es aceptable porque caduca en el primer uso.</b> La cuenta
    /// queda marcada con <see cref="User.MustChangePassword"/> y no puede hacer
    /// absolutamente nada hasta cambiarla. Si se quitara esa marca, esta
    /// contrasena seria permanente y derivable del nombre de usuario, que
    /// figura en la lista de usuarios: cualquiera podria entrar en cualquier
    /// cuenta que hubiera pasado por una recuperacion, con un solo intento y
    /// sin que saltara el limitador.
    /// </remarks>
    public static string ContrasenaProvisional(string usuario) => $"{usuario}123@";

    /// <summary>
    /// Duracion minima de una solicitud, exista o no la cuenta.
    /// </summary>
    /// <remarks>
    /// Defensa contra la enumeracion de cuentas por tiempo. El camino de la
    /// cuenta que existe hace consultas y una insercion que el otro no hace, y
    /// esa diferencia se puede cronometrar: sin este suelo, el formulario
    /// vuelve a delatar que correos estan dados de alta aunque la respuesta
    /// sea identica.
    ///
    /// 250 ms es holgado frente a lo que tarda el trabajo real (unos pocos
    /// milisegundos) y sigue siendo imperceptible para quien rellena el
    /// formulario.
    /// </remarks>
    private static readonly TimeSpan DuracionMinima = TimeSpan.FromMilliseconds(250);

    private readonly AssetFlowDbContext _db;
    private readonly IEmailQueue _correo;
    private readonly IAuditor _auditor;
    private readonly IPasswordResetThrottle _throttle;
    private readonly TimeProvider _reloj;
    private readonly ILogger<PasswordResetService> _log;

    public PasswordResetService(
        AssetFlowDbContext db,
        IEmailQueue correo,
        IAuditor auditor,
        IPasswordResetThrottle throttle,
        TimeProvider reloj,
        ILogger<PasswordResetService> log)
    {
        _db = db;
        _correo = correo;
        _auditor = auditor;
        _throttle = throttle;
        _reloj = reloj;
        _log = log;
    }

    public async Task SolicitarAsync(string correo, CancellationToken ct)
    {
        long inicio = Stopwatch.GetTimestamp();

        try
        {
            await SolicitarInternoAsync(correo, ct);
        }
        finally
        {
            await IgualarDuracionAsync(inicio, ct);
        }
    }

    private async Task SolicitarInternoAsync(string correo, CancellationToken ct)
    {
        string normalizado = correo.Trim();

        User? usuario = await _db.Users
            .FirstOrDefaultAsync(u => u.Email == normalizado && u.IsActive, ct);

        if (usuario is null)
        {
            // Se registra el intento sin decir a quien iba dirigido mas alla
            // del correo tecleado, y se termina en silencio. El que llama
            // respondera lo mismo que si hubiera existido.
            _log.LogInformation("Recuperacion solicitada para un correo sin cuenta activa");
            return;
        }

        if (!_throttle.PermiteSolicitud(usuario.Id))
        {
            _log.LogInformation(
                "Recuperacion solicitada para el usuario {UserId}, " +
                "rechazada por limite de solicitudes", usuario.Id);
            return;
        }

        // Una sola solicitud viva por cuenta. Pulsar el boton varias veces no
        // debe llenar la bandeja del administrador de entradas identicas: la
        // que ya esta pendiente sirve igual.
        bool yaHayPendiente = await _db.PasswordResetRequests.AnyAsync(
            s => s.UserId == usuario.Id && s.Status == PasswordResetRequestStatus.Pending, ct);

        if (yaHayPendiente)
        {
            _log.LogInformation(
                "El usuario {UserId} ya tiene una solicitud pendiente", usuario.Id);
            return;
        }

        _db.PasswordResetRequests.Add(new PasswordResetRequest
        {
            UserId = usuario.Id,
            RequestedAt = _reloj.GetUtcNow().UtcDateTime,
            Status = PasswordResetRequestStatus.Pending
        });

        _auditor.RegistrarComo(usuario.Id, usuario.Username,
            AuditActions.RecuperacionSolicitada, "User", usuario.Id);

        await _db.SaveChangesAsync(ct);

        _log.LogInformation(
            "Solicitud de recuperacion registrada para el usuario {UserId}", usuario.Id);
    }

    /// <summary>
    /// Espera hasta completar <see cref="DuracionMinima"/> desde el instante
    /// indicado.
    /// </summary>
    /// <remarks>
    /// Se usa <see cref="Stopwatch"/> y no <c>TimeProvider</c> a proposito: aqui
    /// interesa el tiempo real transcurrido, que es lo que puede cronometrar un
    /// atacante, no el reloj logico que los tests pueden adelantar.
    /// </remarks>
    private static async Task IgualarDuracionAsync(long inicio, CancellationToken ct)
    {
        TimeSpan transcurrido = Stopwatch.GetElapsedTime(inicio);

        if (transcurrido >= DuracionMinima)
        {
            return;
        }

        try
        {
            await Task.Delay(DuracionMinima - transcurrido, ct);
        }
        catch (OperationCanceledException)
        {
            // Si el cliente corta la conexion no hay nadie midiendo nada.
        }
    }

    /// <summary>
    /// Avisa al titular de que su contrasena ha cambiado.
    /// </summary>
    /// <remarks>
    /// El correo no lleva la contrasena nueva. Nunca. Ni la vieja, ni un enlace
    /// para deshacer el cambio: un enlace en un correo es exactamente lo que
    /// usaria quien acaba de robar la cuenta para revertir la reaccion del
    /// titular.
    ///
    /// Este aviso es lo unico que queda del correo en el flujo de contrasenas,
    /// y es opcional: si no hay SMTP configurado no se envia y no pasa nada,
    /// porque la recuperacion ya no depende de el.
    /// </remarks>
    public void NotificarCambio(User usuario, string motivo)
    {
        string cuerpo =
            $"""
             Hola, {usuario.FirstName}:

             La contraseña de tu cuenta de Inventario ({usuario.Username}) acaba de
             cambiar: {motivo}.

             También se han cerrado todas las sesiones que tuvieras abiertas.

             Si has sido tú, no tienes que hacer nada.

             SI NO HAS SIDO TÚ, alguien tiene acceso a tu cuenta. Avisa cuanto antes
             a la persona que administra el sistema para que la desactive.

             Este mensaje es solo un aviso: no contiene ninguna contraseña ni ningún
             enlace que haya que abrir.
             """;

        _correo.Encolar(new EmailMessage(
            usuario.Email, "Tu contraseña ha cambiado · Inventario", cuerpo));
    }
}
