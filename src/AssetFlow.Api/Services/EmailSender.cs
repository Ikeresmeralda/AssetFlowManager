using System.ComponentModel.DataAnnotations;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace AssetFlow.Api.Services;

/// <summary>Configuracion del envio de correo.</summary>
public sealed class EmailOptions
{
    public const string Section = "Email";

    /// <summary>Servidor SMTP. Si esta vacio, no se envia correo de verdad.</summary>
    public string? SmtpHost { get; set; }

    [Range(1, 65535)]
    public int SmtpPort { get; set; } = 587;

    public bool UseStartTls { get; set; } = true;

    public string? Username { get; set; }

    /// <summary>Se lee de la configuracion, nunca del codigo.</summary>
    public string? Password { get; set; }

    [EmailAddress]
    public string FromAddress { get; set; } = "no-reply@inventario.local";

    public string FromName { get; set; } = "Inventario";

    public bool EstaConfigurado => !string.IsNullOrWhiteSpace(SmtpHost);
}

public interface IEmailSender
{
    Task EnviarAsync(string destinatario, string asunto, string cuerpo, CancellationToken ct);
}

/// <summary>
/// Envio por SMTP con MailKit.
/// </summary>
/// <remarks>
/// Se usa MailKit y no el <c>System.Net.Mail.SmtpClient</c> del framework, que
/// la propia documentacion de Microsoft desaconseja para codigo nuevo. Dos
/// motivos concretos, no de estilo:
///
/// - <b>No soporta TLS implicito</b> (puerto 465). Su propiedad
///   <c>EnableSsl</c> significa STARTTLS, asi que con un proveedor que solo
///   ofrezca el 465 no hay forma de conectar.
/// - <b>No valida bien el certificado del servidor</b> en todos los casos, y
///   no permite controlar la negociacion.
///
/// Las excepciones se dejan salir. Quien llama decide que hacer, y en el flujo
/// de recuperacion de contrasena esa decision es deliberada: el fallo se anota
/// en el registro pero no cambia la respuesta que ve el cliente, porque eso
/// delataria si la cuenta existe.
/// </remarks>
public sealed class SmtpEmailSender : IEmailSender
{
    private readonly EmailOptions _opciones;
    private readonly ILogger<SmtpEmailSender> _log;

    public SmtpEmailSender(IOptions<EmailOptions> opciones, ILogger<SmtpEmailSender> log)
    {
        _opciones = opciones.Value;
        _log = log;
    }

    public async Task EnviarAsync(string destinatario, string asunto, string cuerpo,
                                  CancellationToken ct)
    {
        // Este emisor solo se registra cuando hay servidor configurado, asi que
        // llegar aqui sin el significa que alguien ha roto esa condicion en
        // Program.cs. Se falla en el sitio en lugar de dejar que MailKit lance
        // una excepcion de referencia nula mas abajo, que no diria por que.
        string servidor = _opciones.SmtpHost
            ?? throw new InvalidOperationException(
                "SmtpEmailSender se ha construido sin Email:SmtpHost configurado.");

        var mensaje = new MimeMessage
        {
            Subject = asunto,
            Body = new TextPart("plain") { Text = cuerpo }
        };

        mensaje.From.Add(new MailboxAddress(_opciones.FromName, _opciones.FromAddress));
        mensaje.To.Add(MailboxAddress.Parse(destinatario));

        using var cliente = new SmtpClient();

        await cliente.ConnectAsync(servidor, _opciones.SmtpPort, SeguridadDelPuerto(), ct);

        if (!string.IsNullOrWhiteSpace(_opciones.Username))
        {
            // Contrasena vacia y no nula: hay proveedores que autentican solo
            // con el usuario, y MailKit no admite null.
            await cliente.AuthenticateAsync(
                _opciones.Username, _opciones.Password ?? string.Empty, ct);
        }

        await cliente.SendAsync(mensaje, ct);
        await cliente.DisconnectAsync(quit: true, ct);

        // Se registra que se envio y a que cuenta, nunca el contenido: el
        // cuerpo lleva el codigo de recuperacion.
        _log.LogInformation("Correo enviado a {Destinatario}", destinatario);
    }

    /// <summary>
    /// Modo de cifrado segun el puerto y la configuracion.
    /// </summary>
    /// <remarks>
    /// El 465 es TLS implicito por convencion: se cifra desde el primer byte.
    /// El 587 usa STARTTLS, que empieza en claro y asciende. Nunca se devuelve
    /// <c>None</c>: enviar credenciales SMTP sin cifrar por una red que no
    /// controlamos es regalarlas.
    /// </remarks>
    private SecureSocketOptions SeguridadDelPuerto() =>
        _opciones.SmtpPort == 465
            ? SecureSocketOptions.SslOnConnect
            : _opciones.UseStartTls
                ? SecureSocketOptions.StartTls
                : SecureSocketOptions.StartTlsWhenAvailable;
}

/// <summary>
/// Sustituto para cuando no hay SMTP configurado.
/// </summary>
/// <remarks>
/// Escribe el correo en el registro en lugar de enviarlo, para que el flujo de
/// recuperacion se pueda probar de principio a fin sin montar un servidor de
/// correo.
///
/// <b>Solo puede usarse en desarrollo.</b> <c>Program.cs</c> aborta el arranque
/// si se seleccionaria fuera de ese entorno, y el motivo es que el cuerpo del
/// correo lleva el codigo de recuperacion en claro: acabaria escrito en el
/// registro, que normalmente se envia a un agregador que ve mas gente que la
/// base de datos. Es decir, la funcion pensada para proteger cuentas se
/// convertiria en la que las expone.
/// </remarks>
public sealed class LoggingEmailSender : IEmailSender
{
    private readonly ILogger<LoggingEmailSender> _log;

    public LoggingEmailSender(ILogger<LoggingEmailSender> log) => _log = log;

    public Task EnviarAsync(string destinatario, string asunto, string cuerpo,
                            CancellationToken ct)
    {
        _log.LogWarning(
            "SIN SMTP CONFIGURADO. El correo no se ha enviado.\n" +
            "  Para: {Destinatario}\n  Asunto: {Asunto}\n{Cuerpo}",
            destinatario, asunto, cuerpo);

        return Task.CompletedTask;
    }
}
