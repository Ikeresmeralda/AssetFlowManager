using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AssetFlow.Core.Diagnostics;
using AssetFlow.Core.Dtos;
using AssetFlow.Core.Security;

namespace AssetFlow.Core.Http;

/// <summary>
/// Anade el token a cada peticion y renueva la sesion cuando caduca.
/// </summary>
/// <remarks>
/// Al estar en un DelegatingHandler, ni los servicios ni la interfaz saben que
/// existen tokens. La alternativa, que cada llamada compruebe la caducidad y
/// renueve, garantiza que antes o despues una se olvide.
///
/// La renovacion esta serializada con un semaforo: sin el, al arrancar la
/// aplicacion lanza varias peticiones a la vez, todas reciben 401 al mismo
/// tiempo y todas intentan canjear el mismo refresh token. Como el servidor
/// lo rota, la primera lo consume y las demas fallan; peor aun, el servidor
/// interpreta la reutilizacion como robo y revoca todas las sesiones.
/// </remarks>
public sealed class AuthenticationHandler : DelegatingHandler
{
    private static readonly SemaphoreSlim Renovacion = new(1, 1);

    private readonly SessionState _sesion;
    private readonly TokenStore _almacen;

    public AuthenticationHandler(SessionState sesion, TokenStore almacen)
    {
        _sesion = sesion;
        _almacen = almacen;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage peticion, CancellationToken ct)
    {
        bool esAuth = peticion.RequestUri?.AbsolutePath.Contains("/api/auth/", StringComparison.OrdinalIgnoreCase) == true;

        // Las llamadas de autenticacion no llevan token ni se renuevan: si lo
        // hicieran, un refresh fallido dispararia otro refresh en bucle.
        if (esAuth)
        {
            return await base.SendAsync(peticion, ct);
        }

        if (_sesion.AccessTokenCaducado)
        {
            await RenovarAsync(ct);
        }

        Autorizar(peticion);

        HttpResponseMessage respuesta = await base.SendAsync(peticion, ct);

        if (respuesta.StatusCode != HttpStatusCode.Unauthorized)
        {
            return respuesta;
        }

        // 401 con un token que creiamos vigente: puede que el servidor se haya
        // reiniciado con otra clave, o que la sesion se haya revocado. Se
        // intenta renovar una vez y se reintenta la peticion.
        respuesta.Dispose();

        if (!await RenovarAsync(ct))
        {
            _sesion.Limpiar("Tu sesión ha caducado. Vuelve a iniciar sesión.");

            return new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                RequestMessage = peticion
            };
        }

        HttpRequestMessage reintento = await ClonarAsync(peticion, ct);
        Autorizar(reintento);

        return await base.SendAsync(reintento, ct);
    }

    private void Autorizar(HttpRequestMessage peticion)
    {
        string? token = _sesion.AccessToken;

        if (token is not null)
        {
            peticion.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
    }

    private async Task<bool> RenovarAsync(CancellationToken ct)
    {
        string? refresco = _sesion.RefreshToken;

        if (refresco is null || _sesion.RefreshTokenExpiraEn <= DateTime.UtcNow)
        {
            return false;
        }

        await Renovacion.WaitAsync(ct);

        try
        {
            // Otra peticion puede haber renovado mientras esperabamos el
            // semaforo: si el token ya sirve, no se gasta otro refresco.
            if (!_sesion.AccessTokenCaducado)
            {
                return true;
            }

            // El refresco se relee dentro del semaforo por el mismo motivo.
            refresco = _sesion.RefreshToken;

            if (refresco is null)
            {
                return false;
            }

            using var peticion = new HttpRequestMessage(HttpMethod.Post, "api/auth/refresh")
            {
                Content = JsonContent.Create(new RefreshRequest(refresco))
            };

            using HttpResponseMessage respuesta = await base.SendAsync(peticion, ct);

            if (!respuesta.IsSuccessStatusCode)
            {
                Log.Aviso($"No se ha podido renovar la sesion (HTTP {(int)respuesta.StatusCode}).");
                return false;
            }

            AuthResponse? nueva = await respuesta.Content
                .ReadFromJsonAsync<AuthResponse>(cancellationToken: ct);

            if (nueva is null)
            {
                return false;
            }

            _sesion.Establecer(nueva);

            _almacen.Guardar(new SesionGuardada(
                nueva.RefreshToken,
                nueva.RefreshTokenExpiresAt,
                Configuration.AppSettings.ApiServer,
                nueva.User.Username));

            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Sin red no se puede renovar. No es motivo para cerrar la sesion:
            // el refresh token sigue siendo valido cuando vuelva la conexion.
            Log.Aviso("Fallo al renovar la sesion: " + ex.GetType().Name);
            return false;
        }
        finally
        {
            Renovacion.Release();
        }
    }

    /// <summary>
    /// Un HttpRequestMessage no puede enviarse dos veces, asi que el reintento
    /// necesita una copia con el cuerpo ya materializado.
    /// </summary>
    private static async Task<HttpRequestMessage> ClonarAsync(
        HttpRequestMessage original, CancellationToken ct)
    {
        var copia = new HttpRequestMessage(original.Method, original.RequestUri);

        if (original.Content is not null)
        {
            byte[] cuerpo = await original.Content.ReadAsByteArrayAsync(ct);
            copia.Content = new ByteArrayContent(cuerpo);

            foreach (var cabecera in original.Content.Headers)
            {
                copia.Content.Headers.TryAddWithoutValidation(cabecera.Key, cabecera.Value);
            }
        }

        foreach (var cabecera in original.Headers)
        {
            copia.Headers.TryAddWithoutValidation(cabecera.Key, cabecera.Value);
        }

        copia.Version = original.Version;

        return copia;
    }
}
