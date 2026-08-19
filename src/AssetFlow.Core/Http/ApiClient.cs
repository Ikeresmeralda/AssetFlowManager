using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AssetFlow.Core.Diagnostics;
using AssetFlow.Core.Dtos;

namespace AssetFlow.Core.Http;

/// <summary>
/// Punto unico de acceso HTTP a la API.
/// </summary>
/// <remarks>
/// Sustituye a los seis repositorios anteriores, cada uno con su propia copia
/// del mismo bloque de HttpWebRequest y su propio criterio (inexistente) de
/// tratamiento de errores. Aqui la traduccion de codigo HTTP a resultado
/// ocurre en un solo sitio, asi que corregirla se hace una vez.
///
/// El HttpClient llega inyectado por IHttpClientFactory: ni se crea ni se
/// desecha aqui. Crear uno por peticion, como hacia el codigo anterior en
/// trece sitios, agota los puertos del sistema bajo uso continuado.
/// </remarks>
public sealed class ApiClient
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;

    public ApiClient(HttpClient http)
    {
        _http = http;
    }

    public Task<ApiResult<T>> GetAsync<T>(string ruta, CancellationToken ct = default) =>
        EnviarAsync<T>(() => new HttpRequestMessage(HttpMethod.Get, ruta), ct);

    public Task<ApiResult<T>> PostAsync<T>(string ruta, object? cuerpo, CancellationToken ct = default) =>
        EnviarAsync<T>(() => Construir(HttpMethod.Post, ruta, cuerpo), ct);

    public Task<ApiResult> PostAsync(string ruta, object? cuerpo, CancellationToken ct = default) =>
        EnviarAsync(() => Construir(HttpMethod.Post, ruta, cuerpo), ct);

    public Task<ApiResult<T>> PutAsync<T>(string ruta, object? cuerpo, CancellationToken ct = default) =>
        EnviarAsync<T>(() => Construir(HttpMethod.Put, ruta, cuerpo), ct);

    public Task<ApiResult> DeleteAsync(string ruta, CancellationToken ct = default) =>
        EnviarAsync(() => new HttpRequestMessage(HttpMethod.Delete, ruta), ct);

    // -----------------------------------------------------------------------

    private HttpRequestMessage Construir(HttpMethod metodo, string ruta, object? cuerpo)
    {
        var peticion = new HttpRequestMessage(metodo, ruta);

        if (cuerpo is not null)
        {
            peticion.Content = JsonContent.Create(cuerpo, options: Json);
        }

        return peticion;
    }

    private async Task<ApiResult<T>> EnviarAsync<T>(
        Func<HttpRequestMessage> fabrica, CancellationToken ct)
    {
        try
        {
            using HttpRequestMessage peticion = fabrica();
            using HttpResponseMessage respuesta = await _http.SendAsync(peticion, ct);

            if (!respuesta.IsSuccessStatusCode)
            {
                return ApiResult<T>.Fallo(
                    Traducir(respuesta.StatusCode),
                    await LeerMensajeAsync(respuesta, ct));
            }

            T? valor = await respuesta.Content.ReadFromJsonAsync<T>(Json, ct);

            return valor is null
                ? ApiResult<T>.Fallo(ApiStatus.ServerError, "El servidor ha devuelto una respuesta vacia.")
                : ApiResult<T>.Correcto(valor);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Cancelacion pedida por el usuario (por ejemplo, ha seguido
            // escribiendo en el buscador). No es un error.
            return ApiResult<T>.Fallo(ApiStatus.Cancelled, null);
        }
        catch (TaskCanceledException)
        {
            // Cancelacion no pedida por nosotros: es el timeout del HttpClient.
            return ApiResult<T>.Fallo(ApiStatus.Offline,
                "El servidor ha tardado demasiado en responder.");
        }
        catch (HttpRequestException ex)
        {
            Log.Aviso("Sin conexion con la API: " + ex.Message);
            return ApiResult<T>.Fallo(ApiStatus.Offline, null);
        }
        catch (JsonException ex)
        {
            Log.Error("Respuesta de la API ilegible", ex);
            return ApiResult<T>.Fallo(ApiStatus.ServerError,
                "La respuesta del servidor no se ha podido interpretar.");
        }
    }

    private async Task<ApiResult> EnviarAsync(
        Func<HttpRequestMessage> fabrica, CancellationToken ct)
    {
        try
        {
            using HttpRequestMessage peticion = fabrica();
            using HttpResponseMessage respuesta = await _http.SendAsync(peticion, ct);

            return respuesta.IsSuccessStatusCode
                ? ApiResult.Correcto()
                : ApiResult.Fallo(Traducir(respuesta.StatusCode),
                    await LeerMensajeAsync(respuesta, ct));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return ApiResult.Fallo(ApiStatus.Cancelled, null);
        }
        catch (TaskCanceledException)
        {
            return ApiResult.Fallo(ApiStatus.Offline,
                "El servidor ha tardado demasiado en responder.");
        }
        catch (HttpRequestException ex)
        {
            Log.Aviso("Sin conexion con la API: " + ex.Message);
            return ApiResult.Fallo(ApiStatus.Offline, null);
        }
    }

    private static ApiStatus Traducir(HttpStatusCode codigo) => codigo switch
    {
        HttpStatusCode.BadRequest => ApiStatus.Invalid,
        HttpStatusCode.Unauthorized => ApiStatus.Unauthenticated,
        HttpStatusCode.Forbidden => ApiStatus.Forbidden,
        HttpStatusCode.NotFound => ApiStatus.NotFound,
        HttpStatusCode.Conflict => ApiStatus.Conflict,
        HttpStatusCode.TooManyRequests => ApiStatus.TooManyRequests,
        _ => ApiStatus.ServerError
    };

    /// <summary>
    /// Extrae el mensaje del ProblemDetails devuelto por la API. Si el cuerpo
    /// no es interpretable se devuelve null y el consumidor usara el texto por
    /// defecto: nunca se muestra al usuario un cuerpo HTTP crudo.
    /// </summary>
    private static async Task<string?> LeerMensajeAsync(
        HttpResponseMessage respuesta, CancellationToken ct)
    {
        try
        {
            var problema = await respuesta.Content
                .ReadFromJsonAsync<ProblemDetails>(Json, ct);

            return problema?.MejorMensaje();
        }
        catch
        {
            return null;
        }
    }
}
