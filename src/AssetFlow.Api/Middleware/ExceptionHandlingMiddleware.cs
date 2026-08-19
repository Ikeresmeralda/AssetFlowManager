using System.Text.Json;
using Microsoft.AspNetCore.Mvc;

namespace AssetFlow.Api.Middleware;

/// <summary>
/// Convierte cualquier excepcion no controlada en una respuesta uniforme.
/// </summary>
/// <remarks>
/// Sin esto, ASP.NET Core devuelve en desarrollo una pagina con la traza
/// completa y en produccion una respuesta vacia. Lo primero filtra rutas del
/// servidor, nombres de clases y fragmentos de SQL; lo segundo no permite al
/// cliente distinguir un fallo del servidor de una caida de red.
///
/// Aqui el cliente recibe siempre un ProblemDetails con un identificador de
/// correlacion, y el detalle tecnico queda solo en el registro del servidor.
/// </remarks>
public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _siguiente;
    private readonly ILogger<ExceptionHandlingMiddleware> _log;
    private readonly IHostEnvironment _entorno;

    public ExceptionHandlingMiddleware(
        RequestDelegate siguiente,
        ILogger<ExceptionHandlingMiddleware> log,
        IHostEnvironment entorno)
    {
        _siguiente = siguiente;
        _log = log;
        _entorno = entorno;
    }

    public async Task InvokeAsync(HttpContext contexto)
    {
        try
        {
            await _siguiente(contexto);
        }
        catch (OperationCanceledException) when (contexto.RequestAborted.IsCancellationRequested)
        {
            // El cliente ha cortado (por ejemplo, ha seguido escribiendo en el
            // buscador). No es un error: no se registra como tal ni se intenta
            // responder a una conexion que ya no existe.
            _log.LogDebug("Peticion cancelada por el cliente: {Path}", contexto.Request.Path);
        }
        // Peticion mal formada a nivel de protocolo: cuerpo mayor del limite,
        // cabeceras invalidas, codificacion de trozos rota. Kestrel ya trae el
        // codigo correcto (413 si se pasa de tamano, 400 en el resto) y hay que
        // respetarlo: tratarlo como un error interno convertia un rechazo
        // legitimo en un 500, que es decirle al cliente que el fallo es del
        // servidor cuando el problema esta en la peticion. Ademas, un 500 barato
        // de provocar es justo lo que busca quien tantea la API.
        catch (BadHttpRequestException ex)
        {
            _log.LogWarning(
                "Peticion rechazada ({Codigo}) en {Metodo} {Ruta}: {Motivo}",
                ex.StatusCode, contexto.Request.Method, contexto.Request.Path, ex.Message);

            await ResponderAsync(contexto, ex.StatusCode,
                ex.StatusCode == StatusCodes.Status413PayloadTooLarge
                    ? "Cuerpo demasiado grande"
                    : "Petición mal formada",
                ex.StatusCode == StatusCodes.Status413PayloadTooLarge
                    ? "El cuerpo de la petición supera el tamaño máximo admitido."
                    : "La petición no se ha podido interpretar.");
        }
        catch (Exception ex)
        {
            _log.LogError(ex,
                "Error no controlado {Correlacion} en {Metodo} {Ruta}",
                contexto.TraceIdentifier, contexto.Request.Method, contexto.Request.Path);

            await ResponderAsync(contexto, StatusCodes.Status500InternalServerError,
                "Error interno",
                _entorno.IsDevelopment()
                    ? ex.Message
                    : "Se ha producido un error al procesar la petición.");
        }
    }

    private static async Task ResponderAsync(
        HttpContext contexto, int codigo, string titulo, string detalle)
    {
        if (contexto.Response.HasStarted)
        {
            // La respuesta ya iba en camino: no se puede reescribir.
            return;
        }

        var problema = new ProblemDetails
        {
            Title = titulo,
            Status = codigo,
            Detail = detalle,
            Instance = contexto.Request.Path
        };

        problema.Extensions["traceId"] = contexto.TraceIdentifier;

        contexto.Response.Clear();
        contexto.Response.StatusCode = codigo;
        contexto.Response.ContentType = "application/problem+json";

        await contexto.Response.WriteAsync(JsonSerializer.Serialize(problema));
    }
}
