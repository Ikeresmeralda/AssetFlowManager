using AssetFlow.Core.Configuration;
using AssetFlow.Core.Http;
using AssetFlow.Core.Security;
using AssetFlow.Core.Services;
using Microsoft.Extensions.DependencyInjection;

namespace AssetFlow.Core;

/// <summary>
/// Registro de los servicios del cliente.
/// </summary>
public static class ServiceRegistration
{
    /// <summary>Tiempo maximo de espera. Por defecto HttpClient espera 100 s.</summary>
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    public static IServiceCollection AddAssetFlowCore(this IServiceCollection servicios)
    {
        servicios.AddSingleton<SessionState>();
        servicios.AddSingleton<TokenStore>();
        servicios.AddTransient<AuthenticationHandler>();

        // IHttpClientFactory gestiona el ciclo de vida de los manejadores y su
        // agrupacion de conexiones. Es lo que evita a la vez el agotamiento de
        // puertos (un HttpClient por peticion) y el DNS obsoleto (un unico
        // HttpClient estatico para siempre).
        servicios
            .AddHttpClient<ApiClient>(cliente =>
            {
                cliente.Timeout = Timeout;

                // La direccion se resuelve en cada creacion, no una sola vez:
                // el usuario puede cambiar de servidor sin reiniciar.
                if (AppSettings.HayServidorConfigurado)
                {
                    cliente.BaseAddress = new Uri(AppSettings.ApiServer);
                }

                cliente.DefaultRequestHeaders.Add("Accept", "application/json");
            })
            .AddHttpMessageHandler<AuthenticationHandler>()
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                // La validacion del certificado se deja como viene. Nunca se
                // sustituye por un callback que acepte cualquier certificado:
                // eso anula por completo la proteccion de HTTPS y convierte
                // cualquier intermediario en un espia legitimo.
                AutomaticDecompression = System.Net.DecompressionMethods.All
            });

        servicios.AddSingleton<AuthService>();
        servicios.AddSingleton<MaterialsService>();
        servicios.AddSingleton<LoansService>();
        servicios.AddSingleton<UsersService>();
        servicios.AddSingleton<RecuperacionesService>();

        return servicios;
    }
}
