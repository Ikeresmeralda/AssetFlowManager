using AssetFlow.Core.Configuration;
using AssetFlow.Core.Diagnostics;
using AssetFlow.Core.Dtos;
using AssetFlow.Core.Http;
using AssetFlow.Core.Security;

namespace AssetFlow.Core.Services;

/// <summary>
/// Inicio y cierre de sesion contra la API.
/// </summary>
/// <remarks>
/// Toda la logica de credenciales vive en el servidor. Este servicio solo
/// transporta: envia usuario y contrasena, y guarda lo que devuelve. El
/// cliente ya no descarga usuarios, no compara hashes y no decide si alguien
/// entra, que era el defecto de fondo de la version anterior.
/// </remarks>
public sealed class AuthService
{
    private readonly ApiClient _api;
    private readonly SessionState _sesion;
    private readonly TokenStore _almacen;

    public AuthService(ApiClient api, SessionState sesion, TokenStore almacen)
    {
        _api = api;
        _sesion = sesion;
        _almacen = almacen;
    }

    public async Task<ApiResult> IniciarSesionAsync(
        string usuario, string contrasena, CancellationToken ct = default)
    {
        ApiResult<AuthResponse> resultado = await _api.PostAsync<AuthResponse>(
            "api/auth/login", new LoginRequest(usuario, contrasena), ct);

        if (!resultado.EsCorrecto || resultado.Valor is null)
        {
            return resultado;
        }

        _sesion.Establecer(resultado.Valor);

        AppSettings.UltimoUsuario = resultado.Valor.User.Username;

        if (AppSettings.RecordarSesion)
        {
            _almacen.Guardar(new SesionGuardada(
                resultado.Valor.RefreshToken,
                resultado.Valor.RefreshTokenExpiresAt,
                AppSettings.ApiServer,
                resultado.Valor.User.Username));
        }

        AppSettings.Guardar();

        Log.Info($"Sesion iniciada: {resultado.Valor.User.Username}");

        return ApiResult.Correcto();
    }

    /// <summary>
    /// Intenta reanudar la sesion guardada en el equipo.
    /// </summary>
    /// <remarks>
    /// Se comprueba que el servidor sea el mismo: reutilizar contra un
    /// servidor distinto un refresco emitido por otro no funcionaria, y ademas
    /// enviaria una credencial a una maquina que no la emitio.
    /// </remarks>
    public async Task<bool> ReanudarSesionAsync(CancellationToken ct = default)
    {
        SesionGuardada? guardada = _almacen.Cargar();

        if (guardada is null)
        {
            return false;
        }

        if (!string.Equals(guardada.Servidor, AppSettings.ApiServer, StringComparison.OrdinalIgnoreCase))
        {
            Log.Info("La sesion guardada pertenece a otro servidor; se descarta.");
            _almacen.Borrar();
            return false;
        }

        ApiResult<AuthResponse> resultado = await _api.PostAsync<AuthResponse>(
            "api/auth/refresh", new RefreshRequest(guardada.RefreshToken), ct);

        if (!resultado.EsCorrecto || resultado.Valor is null)
        {
            // Un refresco rechazado ya no vale. Si el fallo es de red, se
            // conserva para reintentarlo cuando vuelva la conexion.
            if (resultado.Status is not (ApiStatus.Offline or ApiStatus.Cancelled))
            {
                _almacen.Borrar();
            }

            return false;
        }

        _sesion.Establecer(resultado.Valor);

        _almacen.Guardar(new SesionGuardada(
            resultado.Valor.RefreshToken,
            resultado.Valor.RefreshTokenExpiresAt,
            AppSettings.ApiServer,
            resultado.Valor.User.Username));

        Log.Info($"Sesion reanudada: {resultado.Valor.User.Username}");

        return true;
    }

    public async Task CerrarSesionAsync(CancellationToken ct = default)
    {
        string? refresco = _sesion.RefreshToken;

        if (refresco is not null)
        {
            // Se avisa al servidor para que revoque el refresco. Si falla (por
            // ejemplo, sin red) se continua igualmente: el usuario ha pedido
            // salir y la sesion local debe desaparecer si o si.
            await _api.PostAsync("api/auth/logout", new RefreshRequest(refresco), ct);
        }

        _almacen.Borrar();
        _sesion.Limpiar();

        Log.Info("Sesion cerrada");
    }

    /// <summary>
    /// Pide un codigo de recuperacion para un correo.
    /// </summary>
    /// <remarks>
    /// El servidor responde siempre lo mismo, exista o no la cuenta, y este
    /// metodo no intenta averiguar mas: cualquier intento del cliente por
    /// distinguir los casos (medir tiempos, mirar cabeceras) reintroduciria por
    /// la puerta de atras la enumeracion de cuentas que el servidor evita.
    /// </remarks>
    public Task<ApiResult> SolicitarRecuperacionAsync(
        string correo, CancellationToken ct = default) =>
        _api.PostAsync("api/auth/forgot-password", new ForgotPasswordRequest(correo), ct);

    /// <summary>
    /// Cambia la contrasena provisional por una definitiva y abre sesion.
    /// </summary>
    /// <remarks>
    /// Devuelve una sesion nueva porque el token con el que se llega aqui esta
    /// limitado a este unico paso. Hay que quedarse con el que devuelve esta
    /// llamada; seguir usando el anterior deja la sesion bloqueada.
    /// </remarks>
    public async Task<ApiResult> CambiarContrasenaProvisionalAsync(
        string contrasenaActual, string contrasenaNueva, CancellationToken ct = default)
    {
        ApiResult<AuthResponse> resultado = await _api.PostAsync<AuthResponse>(
            "api/auth/change-password",
            new ChangePasswordRequest(contrasenaActual, contrasenaNueva), ct);

        if (!resultado.EsCorrecto || resultado.Valor is null)
        {
            return resultado;
        }

        // Se reemplaza la sesion por la nueva. La anterior sigue existiendo en
        // memoria del cliente pero su token esta limitado a este unico paso: si
        // no se sustituye, la aplicacion queda bloqueada aunque el cambio haya
        // funcionado.
        _sesion.Establecer(resultado.Valor);

        if (AppSettings.RecordarSesion)
        {
            _almacen.Guardar(new SesionGuardada(
                resultado.Valor.RefreshToken,
                resultado.Valor.RefreshTokenExpiresAt,
                AppSettings.ApiServer,
                resultado.Valor.User.Username));
        }

        Log.Info($"Contrasena provisional cambiada: {resultado.Valor.User.Username}");

        return ApiResult.Correcto();
    }

    /// <summary>Comprueba que la API responde. Usado por el indicador de conexion.</summary>
    public async Task<bool> ComprobarConexionAsync(CancellationToken ct = default)
    {
        ApiResult<HealthResponse> resultado =
            await _api.GetAsync<HealthResponse>("health", ct);

        return resultado.EsCorrecto;
    }

    private sealed record HealthResponse(string Status);
}
