using System.Threading.Channels;

namespace AssetFlow.Api.Services;

/// <summary>Un correo pendiente de salir.</summary>
public sealed record EmailMessage(string Destinatario, string Asunto, string Cuerpo);

/// <summary>
/// Buzon de salida. Acepta el correo y devuelve el control de inmediato.
/// </summary>
/// <remarks>
/// Esta interfaz existe por seguridad, no por rendimiento.
///
/// El endpoint de recuperacion se cuida de responder exactamente lo mismo
/// exista o no la cuenta. Eso no sirve de nada si el camino de la cuenta que
/// existe tarda mas, porque entonces se distingue por el reloj en lugar de por
/// el cuerpo. Y esperar a que un servidor SMTP acepte el mensaje dentro de la
/// peticion anade entre 100 ms y varios segundos <b>solo</b> en ese camino.
///
/// Medido sobre esta misma aplicacion, sin servidor de correo real, la
/// diferencia ya era de 4,6 ms frente a 2,0 ms: mas del doble, y separable con
/// cuatro muestras. Con SMTP de verdad se ve a simple vista.
///
/// Encolando, el trabajo que distingue los dos casos deja de ocurrir dentro de
/// la peticion.
/// </remarks>
public interface IEmailQueue
{
    /// <summary>
    /// Deja el correo en la cola. No bloquea y no lanza: un fallo al encolar no
    /// puede propagarse al endpoint, porque un error visible solo en el caso de
    /// la cuenta que existe volveria a delatarla.
    /// </summary>
    void Encolar(EmailMessage mensaje);
}

public sealed class EmailQueue : IEmailQueue
{
    /// <summary>
    /// Tope de la cola. Acotado a proposito: sin limite, alguien pidiendo
    /// recuperaciones en bucle haria crecer la memoria sin freno. Al llenarse
    /// se descarta lo mas antiguo, que es lo que menos duele: un codigo de
    /// recuperacion viejo es justamente el que ya no interesa.
    /// </summary>
    private const int Capacidad = 1_000;

    private readonly Channel<EmailMessage> _canal = Channel.CreateBounded<EmailMessage>(
        new BoundedChannelOptions(Capacidad)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true
        });

    private readonly ILogger<EmailQueue> _log;

    public EmailQueue(ILogger<EmailQueue> log) => _log = log;

    public ChannelReader<EmailMessage> Lector => _canal.Reader;

    public void Encolar(EmailMessage mensaje)
    {
        // TryWrite y no WriteAsync: con FullMode.DropOldest nunca espera, asi
        // que el metodo termina en tiempo constante pase lo que pase.
        if (!_canal.Writer.TryWrite(mensaje))
        {
            _log.LogError("La cola de correo ha rechazado un mensaje para {Destinatario}",
                mensaje.Destinatario);
        }
    }
}

/// <summary>
/// Vacia la cola de correo en segundo plano.
/// </summary>
/// <remarks>
/// Un fallo de envio se registra y se sigue con el siguiente. Nunca se
/// reintenta ni se propaga: si el servidor SMTP esta caido, reintentar en
/// bucle solo consigue que la cola no avance, y la aplicacion tiene que seguir
/// funcionando sin correo.
///
/// El registro de ese fallo es la unica senal de que la recuperacion de
/// contrasena no esta llegando a nadie, porque el endpoint la oculta a
/// proposito. Conviene vigilarlo. Ver docs/configuration.md.
/// </remarks>
public sealed class EmailBackgroundService : BackgroundService
{
    private readonly EmailQueue _cola;
    private readonly IServiceScopeFactory _ambitos;
    private readonly ILogger<EmailBackgroundService> _log;

    public EmailBackgroundService(
        EmailQueue cola, IServiceScopeFactory ambitos, ILogger<EmailBackgroundService> log)
    {
        _cola = cola;
        _ambitos = ambitos;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await foreach (EmailMessage mensaje in _cola.Lector.ReadAllAsync(ct))
        {
            try
            {
                // Ambito propio por mensaje: IEmailSender esta registrado como
                // scoped y este servicio es singleton, asi que no puede
                // resolverlo del contenedor raiz.
                using IServiceScope ambito = _ambitos.CreateScope();

                var emisor = ambito.ServiceProvider.GetRequiredService<IEmailSender>();

                await emisor.EnviarAsync(
                    mensaje.Destinatario, mensaje.Asunto, mensaje.Cuerpo, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Solo el destinatario, nunca el cuerpo: lleva el codigo.
                _log.LogError(ex, "No se ha podido enviar el correo a {Destinatario}",
                    mensaje.Destinatario);
            }
        }
    }
}
