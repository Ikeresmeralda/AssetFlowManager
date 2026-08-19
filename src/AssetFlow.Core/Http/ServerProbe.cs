using AssetFlow.Core.Configuration;
using AssetFlow.Core.Diagnostics;

namespace AssetFlow.Core.Http;

/// <summary>
/// Comprueba si una direccion concreta responde como API de Inventario.
/// </summary>
/// <remarks>
/// Es independiente de <see cref="ApiClient"/> porque se usa antes de haber
/// configurado nada: sirve para validar la direccion que el usuario esta
/// escribiendo, que todavia no es la direccion activa.
/// </remarks>
public static class ServerProbe
{
    public static async Task<(bool Correcto, string Mensaje)> ProbarAsync(
        string direccion, CancellationToken ct = default)
    {
        (bool valida, string? error) = AppSettings.Validar(direccion);

        if (!valida)
        {
            return (false, error!);
        }

        string url = AppSettings.Normalizar(direccion);

        // Cliente propio y efimero: es una comprobacion puntual, no una via de
        // acceso, y no debe compartir estado con el cliente autenticado.
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

        try
        {
            using HttpResponseMessage respuesta = await http.GetAsync(url + "health", ct);

            if (respuesta.IsSuccessStatusCode)
            {
                return (true, "Conexión correcta. El servidor responde.");
            }

            return (false,
                $"El servidor responde, pero con un error (HTTP {(int)respuesta.StatusCode}). " +
                "Comprueba que la dirección apunta a la API de Inventario.");
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return (false, "El servidor no ha respondido a tiempo.");
        }
        catch (HttpRequestException ex)
        {
            Log.Aviso("Fallo al probar el servidor: " + ex.Message);

            // El mensaje de la excepción menciona el host y el puerto, que es
            // justo lo que ayuda a corregir una dirección mal escrita, y no
            // revela nada que quien escribe la dirección no sepa ya.
            return (false,
                "No hay respuesta. Comprueba que la dirección es correcta y que " +
                "el servicio está en marcha.");
        }
    }
}
