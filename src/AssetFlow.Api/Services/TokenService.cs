using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using AssetFlow.Api.Entities;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace AssetFlow.Api.Services;

/// <summary>Claims propios de esta aplicacion.</summary>
public static class ClaimsExtra
{
    /// <summary>
    /// Presente y con valor "true" cuando la cuenta arrastra una contrasena
    /// provisional y no puede operar hasta cambiarla.
    /// </summary>
    public const string CambioDeContrasenaPendiente = "pwd_change_required";
}

public interface ITokenService
{
    /// <summary>Emite un access token firmado para el usuario indicado.</summary>
    (string Token, DateTime ExpiresAt) CreateAccessToken(User user);

    /// <summary>Genera un refresh token aleatorio y su hash de almacenamiento.</summary>
    (string Token, string Hash) CreateRefreshToken();

    /// <summary>Hash de almacenamiento de un refresh token recibido del cliente.</summary>
    string HashRefreshToken(string token);
}

public class TokenService : ITokenService
{
    private readonly JwtOptions _options;

    public TokenService(IOptions<JwtOptions> options)
    {
        _options = options.Value;
    }

    public (string Token, DateTime ExpiresAt) CreateAccessToken(User user)
    {
        var expira = DateTime.UtcNow.AddMinutes(_options.AccessTokenMinutes);

        // Solo lo imprescindible para autorizar. El token viaja en cada
        // peticion y su contenido es legible por cualquiera que lo intercepte
        // (va firmado, no cifrado): no se meten datos personales.
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.Username),
            new(ClaimTypes.Role, user.Role)
        };

        // La cuenta lleva una contrasena provisional. Va en el token para que
        // el middleware no tenga que consultar la base de datos en cada
        // peticion, y solo se anade cuando es cierto: los tokens normales no
        // cargan un claim que casi siempre valdria "false".
        //
        // Que este dentro del token tiene una consecuencia buscada: al cambiar
        // la contrasena hay que emitir tokens nuevos, porque el antiguo sigue
        // diciendo que el cambio esta pendiente. Eso es correcto: ese token se
        // emitio para una sesion que no podia hacer nada mas.
        if (user.MustChangePassword)
        {
            claims.Add(new Claim(ClaimsExtra.CambioDeContrasenaPendiente, "true"));
        }

        var credenciales = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.Key)),
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: expira,
            signingCredentials: credenciales);

        return (new JwtSecurityTokenHandler().WriteToken(token), expira);
    }

    public (string Token, string Hash) CreateRefreshToken()
    {
        // 256 bits de aleatoriedad criptografica. No se usa Guid: los Guid
        // no estan disenados para ser impredecibles.
        byte[] bytes = RandomNumberGenerator.GetBytes(32);
        string token = Convert.ToBase64String(bytes);

        return (token, HashRefreshToken(token));
    }

    public string HashRefreshToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
