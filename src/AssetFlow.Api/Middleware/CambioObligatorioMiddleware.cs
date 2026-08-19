using AssetFlow.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace AssetFlow.Api.Middleware;

/// <summary>
/// Bloquea a las cuentas que arrastran una contrasena provisional hasta que la
/// cambian.
/// </summary>
/// <remarks>
/// <b>Esta clase es lo que hace segura la contrasena provisional.</b> Sin ella,
/// <c>usuario + "123@"</c> seria una contrasena permanente y deducible del
/// nombre de usuario; con ella, es una llave de un solo uso que solo sirve para
/// abrir la puerta de "elige tu contrasena".
///
/// Va en un middleware y no en un filtro de MVC a proposito: un filtro solo
/// cubre los controladores, y aqui interesa cubrir <em>todo</em> lo que cuelgue
/// de la aplicacion, incluidos los endpoints minimos. La lista de lo permitido
/// es blanca, no negra: un endpoint nuevo nace bloqueado para estas cuentas,
/// que es el lado correcto en el que equivocarse.
///
/// La comprobacion se hace sobre un claim del token y no consultando la base de
/// datos, de modo que no cuesta una consulta por peticion. La contrapartida es
/// que el token sobrevive al cambio, y por eso el endpoint de cambio emite
/// tokens nuevos.
/// </remarks>
public sealed class CambioObligatorioMiddleware
{
    /// <summary>
    /// Lo unico que puede hacer una cuenta con la contrasena sin cambiar.
    /// </summary>
    /// <remarks>
    /// - El cambio en si, que es la unica salida.
    /// - Consultar su propia identidad, que la interfaz necesita para saber a
    ///   quien esta pidiendo la contrasena nueva.
    /// - Cerrar sesion y renovar el token, para que quien se arrepienta pueda
    ///   salir y para que la sesion no muera a mitad del formulario.
    ///
    /// Ninguno de los cuatro lee ni modifica datos del inventario.
    /// </remarks>
    private static readonly string[] RutasPermitidas =
    [
        "/api/auth/change-password",
        "/api/auth/me",
        "/api/auth/logout",
        "/api/auth/refresh"
    ];

    private readonly RequestDelegate _siguiente;

    public CambioObligatorioMiddleware(RequestDelegate siguiente) => _siguiente = siguiente;

    public async Task InvokeAsync(HttpContext contexto)
    {
        if (!DebeBloquearse(contexto))
        {
            await _siguiente(contexto);
            return;
        }

        // 403 y no 401: las credenciales son correctas y la sesion es valida.
        // Lo que falta es un paso obligatorio, no autenticarse otra vez. Con un
        // 401 el cliente intentaria renovar el token en bucle.
        contexto.Response.StatusCode = StatusCodes.Status403Forbidden;
        contexto.Response.ContentType = "application/problem+json";

        await contexto.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Title = "Cambio de contraseña pendiente",
            Detail = "Tu contraseña es provisional. Debes cambiarla antes de " +
                     "poder usar la aplicación.",
            Status = StatusCodes.Status403Forbidden,
            // Tipo propio para que el cliente pueda distinguir este 403 de una
            // falta de permisos normal y abrir el formulario en lugar de
            // mostrar "no tienes acceso".
            Type = "urn:assetflow:cambio-de-contrasena-pendiente"
        });
    }

    private static bool DebeBloquearse(HttpContext contexto)
    {
        if (contexto.User.Identity?.IsAuthenticated != true)
        {
            return false;
        }

        bool pendiente = contexto.User.HasClaim(
            ClaimsExtra.CambioDeContrasenaPendiente, "true");

        if (!pendiente)
        {
            return false;
        }

        return !EsRutaPermitida(contexto.Request.Path);
    }

    private static bool EsRutaPermitida(PathString ruta)
    {
        foreach (string permitida in RutasPermitidas)
        {
            // Comparacion de segmento completo: "/api/auth/mexico" no debe
            // colarse por empezar igual que "/api/auth/me".
            if (ruta.Equals(permitida, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
