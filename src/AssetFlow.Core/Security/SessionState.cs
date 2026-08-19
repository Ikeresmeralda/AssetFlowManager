using AssetFlow.Core.Dtos;

namespace AssetFlow.Core.Security;

/// <summary>
/// Estado de la sesion en memoria.
/// </summary>
/// <remarks>
/// Sustituye al antiguo <c>Sesion</c> estatico. La diferencia importante no es
/// tecnica sino de significado: aquello era la autoridad sobre si el usuario
/// era administrador, y la interfaz le hacia caso. Ahora esto es solo una
/// copia local de lo que dijo el servidor, util para decidir que pintar. La
/// autorizacion real la aplica la API en cada peticion, y si alguien
/// manipulara este objeto lo unico que conseguiria es ver botones que al
/// pulsarlos devuelven 403.
/// </remarks>
public sealed class SessionState
{
    private readonly object _candado = new();

    private string? _accessToken;
    private DateTime _accessTokenExpiraEn;
    private string? _refreshToken;
    private DateTime _refreshTokenExpiraEn;
    private CurrentUser? _usuario;

    /// <summary>Se dispara cuando la sesion deja de ser valida.</summary>
    public event Action<string>? SesionTerminada;

    public CurrentUser? Usuario
    {
        get { lock (_candado) { return _usuario; } }
    }

    public bool HaySesion
    {
        get { lock (_candado) { return _usuario is not null && _refreshToken is not null; } }
    }

    /// <summary>
    /// Copia local del rol. Solo para la interfaz: nunca para autorizar.
    /// </summary>
    public bool EsAdministrador => Usuario?.EsAdministrador ?? false;

    public string NombreCompleto => Usuario?.NombreCompleto ?? "";

    public int IdUsuario => Usuario?.Id ?? 0;

    /// <summary>Iniciales para el avatar de la barra lateral.</summary>
    public string Iniciales()
    {
        CurrentUser? u = Usuario;

        if (u is null)
        {
            return "?";
        }

        string iniciales = "";

        if (u.FirstName.Length > 0) iniciales += char.ToUpperInvariant(u.FirstName[0]);
        if (u.LastName.Length > 0) iniciales += char.ToUpperInvariant(u.LastName[0]);

        return iniciales.Length > 0 ? iniciales : "?";
    }

    internal string? AccessToken
    {
        get { lock (_candado) { return _accessToken; } }
    }

    internal string? RefreshToken
    {
        get { lock (_candado) { return _refreshToken; } }
    }

    internal DateTime RefreshTokenExpiraEn
    {
        get { lock (_candado) { return _refreshTokenExpiraEn; } }
    }

    /// <summary>
    /// Indica si el access token esta a punto de caducar. Se renueva un minuto
    /// antes para que una peticion no salga con un token que caduca a mitad de
    /// camino.
    /// </summary>
    internal bool AccessTokenCaducado
    {
        get
        {
            lock (_candado)
            {
                return _accessToken is null ||
                       DateTime.UtcNow >= _accessTokenExpiraEn.AddMinutes(-1);
            }
        }
    }

    internal void Establecer(AuthResponse respuesta)
    {
        lock (_candado)
        {
            _accessToken = respuesta.AccessToken;
            _accessTokenExpiraEn = respuesta.AccessTokenExpiresAt;
            _refreshToken = respuesta.RefreshToken;
            _refreshTokenExpiraEn = respuesta.RefreshTokenExpiresAt;
            _usuario = respuesta.User;
        }
    }

    /// <summary>Restaura solo el refresh token recuperado del disco.</summary>
    internal void EstablecerRefresco(string refreshToken, DateTime expiraEn)
    {
        lock (_candado)
        {
            _refreshToken = refreshToken;
            _refreshTokenExpiraEn = expiraEn;
        }
    }

    public void Limpiar(string? motivo = null)
    {
        lock (_candado)
        {
            _accessToken = null;
            _refreshToken = null;
            _usuario = null;
        }

        if (motivo is not null)
        {
            SesionTerminada?.Invoke(motivo);
        }
    }
}
