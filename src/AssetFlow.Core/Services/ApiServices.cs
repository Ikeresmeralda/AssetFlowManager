using AssetFlow.Core.Dtos;
using AssetFlow.Core.Http;

namespace AssetFlow.Core.Services;

/// <summary>
/// Inventario de articulos.
/// </summary>
public sealed class MaterialsService
{
    private readonly ApiClient _api;

    public MaterialsService(ApiClient api) => _api = api;

    /// <summary>
    /// Lista el inventario. La busqueda se resuelve en el servidor: descargar
    /// todo el inventario para filtrarlo en el cliente no escala.
    /// </summary>
    public Task<ApiResult<List<MaterialDto>>> ListarAsync(
        string? busqueda = null, CancellationToken ct = default)
    {
        string ruta = string.IsNullOrWhiteSpace(busqueda)
            ? "api/materials"
            : $"api/materials?search={Uri.EscapeDataString(busqueda.Trim())}";

        return _api.GetAsync<List<MaterialDto>>(ruta, ct);
    }

    public Task<ApiResult<MaterialDto>> ObtenerAsync(int id, CancellationToken ct = default) =>
        _api.GetAsync<MaterialDto>($"api/materials/{id}", ct);

    public Task<ApiResult<MaterialDto>> CrearAsync(
        SaveMaterialRequest peticion, CancellationToken ct = default) =>
        _api.PostAsync<MaterialDto>("api/materials", peticion, ct);

    public Task<ApiResult<MaterialDto>> ActualizarAsync(
        int id, SaveMaterialRequest peticion, CancellationToken ct = default) =>
        _api.PutAsync<MaterialDto>($"api/materials/{id}", peticion, ct);

    public Task<ApiResult> EliminarAsync(int id, CancellationToken ct = default) =>
        _api.DeleteAsync($"api/materials/{id}", ct);
}

/// <summary>
/// Prestamos de material.
/// </summary>
/// <remarks>
/// No hace falta pasar el identificador del usuario para consultar los
/// propios: el servidor lo deduce del token. Es lo que impide que cambiando
/// un numero se vea el historial de otro.
/// </remarks>
public sealed class LoansService
{
    private readonly ApiClient _api;

    public LoansService(ApiClient api) => _api = api;

    public Task<ApiResult<List<LoanDto>>> ListarAsync(
        bool soloActivos = false, int? usuarioId = null, string? estado = null,
        CancellationToken ct = default)
    {
        var parametros = new List<string>();

        if (soloActivos)
        {
            parametros.Add("activeOnly=true");
        }

        if (!string.IsNullOrWhiteSpace(estado))
        {
            parametros.Add($"status={Uri.EscapeDataString(estado)}");
        }

        // Solo lo envia la pantalla de administracion. Si quien pregunta no es
        // administrador, el servidor lo ignora y devuelve los propios.
        if (usuarioId is not null)
        {
            parametros.Add($"userId={usuarioId}");
        }

        string ruta = "api/loans" + (parametros.Count > 0 ? "?" + string.Join("&", parametros) : "");

        return _api.GetAsync<List<LoanDto>>(ruta, ct);
    }

    /// <summary>
    /// Solicitudes a la espera de decision. Solo responde a administradores.
    /// </summary>
    public Task<ApiResult<List<LoanDto>>> ListarPendientesAsync(CancellationToken ct = default) =>
        _api.GetAsync<List<LoanDto>>("api/loans/pending", ct);

    /// <summary>
    /// Devoluciones pedidas y aun sin confirmar. Solo responde a administradores.
    /// </summary>
    public Task<ApiResult<List<LoanDto>>> ListarDevolucionesPendientesAsync(
        CancellationToken ct = default) =>
        ListarAsync(estado: LoanStatuses.DevolucionSolicitada, ct: ct);

    /// <summary>Acciones registradas sobre un prestamo, en orden cronologico.</summary>
    public Task<ApiResult<List<LoanHistoryEntryDto>>> HistorialAsync(
        int id, CancellationToken ct = default) =>
        _api.GetAsync<List<LoanHistoryEntryDto>>($"api/loans/{id}/history", ct);

    public Task<ApiResult<LoanDto>> CrearAsync(
        CreateLoanRequest peticion, CancellationToken ct = default) =>
        _api.PostAsync<LoanDto>("api/loans", peticion, ct);

    public Task<ApiResult<LoanDto>> AprobarAsync(
        int id, string? nota = null, CancellationToken ct = default) =>
        _api.PostAsync<LoanDto>($"api/loans/{id}/approve", new LoanDecisionRequest(nota), ct);

    public Task<ApiResult<LoanDto>> RechazarAsync(
        int id, string? nota = null, CancellationToken ct = default) =>
        _api.PostAsync<LoanDto>($"api/loans/{id}/reject", new LoanDecisionRequest(nota), ct);

    /// <summary>Pide devolver un prestamo propio. La confirma un administrador.</summary>
    public Task<ApiResult<LoanDto>> SolicitarDevolucionAsync(
        int id, CancellationToken ct = default) =>
        _api.PostAsync<LoanDto>($"api/loans/{id}/request-return", null, ct);

    public Task<ApiResult<LoanDto>> ConfirmarDevolucionAsync(
        int id, string? nota = null, CancellationToken ct = default) =>
        _api.PostAsync<LoanDto>($"api/loans/{id}/approve-return", new LoanDecisionRequest(nota), ct);

    /// <summary>
    /// Rechaza una devolucion: el material no ha vuelto y el prestamo sigue
    /// activo.
    /// </summary>
    public Task<ApiResult<LoanDto>> RechazarDevolucionAsync(
        int id, string? nota = null, CancellationToken ct = default) =>
        _api.PostAsync<LoanDto>($"api/loans/{id}/reject-return", new LoanDecisionRequest(nota), ct);

    /// <summary>
    /// Da un prestamo por devuelto directamente. Solo tiene efecto inmediato
    /// para un administrador; para el resto el servidor lo trata como una
    /// solicitud de devolucion.
    /// </summary>
    public Task<ApiResult<LoanDto>> DevolverAsync(int id, CancellationToken ct = default) =>
        _api.PostAsync<LoanDto>($"api/loans/{id}/return", null, ct);

    public Task<ApiResult> EliminarAsync(int id, CancellationToken ct = default) =>
        _api.DeleteAsync($"api/loans/{id}", ct);
}

/// <summary>
/// Cuentas de usuario.
/// </summary>
public sealed class UsersService
{
    private readonly ApiClient _api;

    public UsersService(ApiClient api) => _api = api;

    /// <summary>Listado completo. Solo responde a administradores.</summary>
    public Task<ApiResult<List<UserDto>>> ListarAsync(CancellationToken ct = default) =>
        _api.GetAsync<List<UserDto>>("api/users", ct);

    /// <summary>Listado reducido, para elegir destinatario de un prestamo.</summary>
    public Task<ApiResult<List<UserSummaryDto>>> ListarResumenAsync(CancellationToken ct = default) =>
        _api.GetAsync<List<UserSummaryDto>>("api/users/summary", ct);

    public Task<ApiResult<UserDto>> CrearAsync(
        CreateUserRequest peticion, CancellationToken ct = default) =>
        _api.PostAsync<UserDto>("api/users", peticion, ct);

    public Task<ApiResult<UserDto>> ActualizarAsync(
        int id, UpdateUserRequest peticion, CancellationToken ct = default) =>
        _api.PutAsync<UserDto>($"api/users/{id}", peticion, ct);

    public Task<ApiResult<UserDto>> CambiarAccesoAsync(
        int id, UpdateUserAccessRequest peticion, CancellationToken ct = default) =>
        _api.PutAsync<UserDto>($"api/users/{id}/access", peticion, ct);

    /// <summary>
    /// Reinicia la contrasena de una cuenta y devuelve la provisional.
    /// </summary>
    /// <remarks>
    /// No se envia ninguna contrasena: la elige el servidor y la persona queda
    /// obligada a cambiarla al entrar. Lo que vuelve es la provisional, para
    /// poder comunicarsela.
    /// </remarks>
    public Task<ApiResult<PasswordResetApprovalDto>> ReiniciarContrasenaAsync(
        int id, CancellationToken ct = default) =>
        _api.PostAsync<PasswordResetApprovalDto>($"api/users/{id}/password", null, ct);

    public Task<ApiResult> EliminarAsync(int id, CancellationToken ct = default) =>
        _api.DeleteAsync($"api/users/{id}", ct);
}

/// <summary>
/// Bandeja de solicitudes de recuperacion de contrasena. Solo administradores.
/// </summary>
public sealed class RecuperacionesService
{
    private readonly ApiClient _api;

    public RecuperacionesService(ApiClient api) => _api = api;

    public Task<ApiResult<List<PasswordResetRequestDto>>> ListarAsync(
        bool soloPendientes = false, CancellationToken ct = default) =>
        _api.GetAsync<List<PasswordResetRequestDto>>(
            $"api/password-reset-requests?soloPendientes={(soloPendientes ? "true" : "false")}", ct);

    public Task<ApiResult<PendingCountDto>> ContarPendientesAsync(
        CancellationToken ct = default) =>
        _api.GetAsync<PendingCountDto>("api/password-reset-requests/pending-count", ct);

    public Task<ApiResult<PasswordResetApprovalDto>> AprobarAsync(
        int id, CancellationToken ct = default) =>
        _api.PostAsync<PasswordResetApprovalDto>(
            $"api/password-reset-requests/{id}/approve", null, ct);

    public Task<ApiResult> DenegarAsync(int id, CancellationToken ct = default) =>
        _api.PostAsync($"api/password-reset-requests/{id}/reject", null, ct);
}
