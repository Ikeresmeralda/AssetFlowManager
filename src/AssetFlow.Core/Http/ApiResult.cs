namespace AssetFlow.Core.Http;

/// <summary>
/// Por que ha terminado una llamada a la API.
/// </summary>
/// <remarks>
/// Este enum es la correccion de un defecto concreto de la version anterior:
/// el cliente HTTP capturaba cualquier excepcion y devolvia null. Como null
/// significaba a la vez "no encontrado", "servidor caido", "sin permiso" y
/// "JSON invalido", la interfaz no podia distinguirlos, y el resultado
/// visible era que con el servidor apagado el login respondia
/// "usuario o contrasena incorrectos".
/// </remarks>
public enum ApiStatus
{
    /// <summary>Todo correcto (2xx).</summary>
    Success,

    /// <summary>No se ha podido contactar con el servidor: red, DNS o timeout.</summary>
    Offline,

    /// <summary>401. Sesion ausente, caducada o revocada.</summary>
    Unauthenticated,

    /// <summary>403. Sesion valida, pero sin permiso para esta operacion.</summary>
    Forbidden,

    /// <summary>404.</summary>
    NotFound,

    /// <summary>400. Datos rechazados por la validacion del servidor.</summary>
    Invalid,

    /// <summary>409. Conflicto de estado: duplicado, edicion simultanea, etc.</summary>
    Conflict,

    /// <summary>429. Demasiadas peticiones.</summary>
    TooManyRequests,

    /// <summary>5xx o respuesta ilegible.</summary>
    ServerError,

    /// <summary>La operacion se cancelo desde el cliente.</summary>
    Cancelled
}

/// <summary>Resultado de una llamada sin valor de retorno.</summary>
public class ApiResult
{
    protected ApiResult(ApiStatus status, string? mensaje)
    {
        Status = status;
        Mensaje = mensaje;
    }

    public ApiStatus Status { get; }

    /// <summary>
    /// Mensaje ya listo para mostrar al usuario. Procede del servidor cuando
    /// este lo ha explicado (ProblemDetails), y si no, de un texto por
    /// defecto acorde al estado.
    /// </summary>
    public string? Mensaje { get; }

    public bool EsCorrecto => Status == ApiStatus.Success;

    /// <summary>
    /// Indica si conviene ofrecer un boton de reintento: los fallos de red o
    /// de servidor pueden resolverse solos, un 403 no.
    /// </summary>
    public bool MereceReintento =>
        Status is ApiStatus.Offline or ApiStatus.ServerError or ApiStatus.TooManyRequests;

    public static ApiResult Correcto() => new(ApiStatus.Success, null);

    public static ApiResult Fallo(ApiStatus status, string? mensaje) => new(status, mensaje);

    public string MensajeParaUsuario() => Mensaje ?? TextoPorDefecto(Status);

    internal static string TextoPorDefecto(ApiStatus status) => status switch
    {
        ApiStatus.Offline =>
            "No se ha podido contactar con el servidor. Comprueba la conexión y que el servicio esté en marcha.",
        ApiStatus.Unauthenticated =>
            "Tu sesión ha caducado. Vuelve a iniciar sesión.",
        ApiStatus.Forbidden =>
            "No tienes permiso para realizar esta operación.",
        ApiStatus.NotFound =>
            "El elemento solicitado ya no existe.",
        ApiStatus.Invalid =>
            "Los datos introducidos no son válidos.",
        ApiStatus.Conflict =>
            "La operación no se ha podido completar por un conflicto con el estado actual.",
        ApiStatus.TooManyRequests =>
            "Demasiados intentos. Espera unos minutos antes de volver a probar.",
        ApiStatus.ServerError =>
            "El servidor ha respondido con un error. Vuelve a intentarlo en unos momentos.",
        ApiStatus.Cancelled =>
            "Operación cancelada.",
        _ => "Se ha producido un error inesperado."
    };
}

/// <summary>Resultado de una llamada que devuelve datos.</summary>
public sealed class ApiResult<T> : ApiResult
{
    private ApiResult(ApiStatus status, T? valor, string? mensaje)
        : base(status, mensaje)
    {
        Valor = valor;
    }

    public T? Valor { get; }

    public static ApiResult<T> Correcto(T valor) => new(ApiStatus.Success, valor, null);

    public static new ApiResult<T> Fallo(ApiStatus status, string? mensaje) =>
        new(status, default, mensaje);
}
