using AssetFlow.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AssetFlow.Api.Tests;

/// <summary>
/// API con el envio de correo sustituido por un buzon en memoria.
/// </summary>
/// <remarks>
/// Es la unica pieza que se sustituye en toda la bateria, y se sustituye por
/// lo que es: un transporte SMTP hacia una maquina externa. Lo que se quiere
/// comprobar aqui es la logica de los codigos —cuantos bits tienen, cuanto
/// duran, cuantas veces sirven, que se guarda de ellos—, no que un servidor de
/// correo ajeno acepte una conexion.
///
/// Todo lo demas sigue siendo el codigo real: la generacion del codigo, su
/// hash, la transaccion de canje y la revocacion de sesiones.
/// </remarks>
public class EntornoConCorreo : ApiFactory
{
    public BuzonDePrueba Buzon { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder constructor)
    {
        base.ConfigureWebHost(constructor);

        constructor.ConfigureTestServices(servicios =>
        {
            servicios.RemoveAll<IEmailSender>();
            servicios.AddSingleton<IEmailSender>(Buzon);
        });
    }

    public HttpClient ClienteVictima { get; private set; } = null!;

    public int IdVictima { get; private set; }

    public const string CorreoVictima = "victima.prueba@ejemplo.local";

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();

        (IdVictima, ClienteVictima) = await CrearCuentaAsync("victima.prueba");
    }
}

/// <summary>Guarda en memoria los correos que se habrian enviado.</summary>
public sealed class BuzonDePrueba : IEmailSender
{
    private readonly List<(string Destinatario, string Asunto, string Cuerpo)> _enviados = [];
    private readonly object _cerrojo = new();

    public IReadOnlyList<(string Destinatario, string Asunto, string Cuerpo)> Enviados
    {
        get
        {
            lock (_cerrojo)
            {
                return _enviados.ToList();
            }
        }
    }

    public Task EnviarAsync(string destinatario, string asunto, string cuerpo,
                            CancellationToken ct = default)
    {
        lock (_cerrojo)
        {
            _enviados.Add((destinatario, asunto, cuerpo));
        }

        return Task.CompletedTask;
    }

    public void Limpiar()
    {
        lock (_cerrojo)
        {
            _enviados.Clear();
        }
    }

    /// <summary>
    /// Espera a que llegue un correo al destinatario cuyo asunto encaje.
    /// </summary>
    /// <remarks>
    /// El envio ya no ocurre dentro de la peticion: se encola y lo despacha un
    /// servicio en segundo plano, precisamente para que el tiempo de respuesta
    /// no delate si la cuenta existe. La consecuencia para los tests es que
    /// mirar el buzon justo despues de la llamada es una carrera, asi que hay
    /// que esperar de forma explicita en lugar de confiar en que llegue a
    /// tiempo.
    /// </remarks>
    /// <returns>El correo, o null si no llega dentro del plazo.</returns>
    public async Task<(string Destinatario, string Asunto, string Cuerpo)?> EsperarAsync(
        string destinatario, string asuntoContiene = "", int msMaximo = 5000)
    {
        int esperado = 0;

        while (esperado < msMaximo)
        {
            lock (_cerrojo)
            {
                int i = _enviados.FindLastIndex(c =>
                    c.Destinatario == destinatario &&
                    c.Asunto.Contains(asuntoContiene, StringComparison.OrdinalIgnoreCase));

                if (i >= 0)
                {
                    return _enviados[i];
                }
            }

            await Task.Delay(25);
            esperado += 25;
        }

        return null;
    }

}
