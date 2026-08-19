using System.ComponentModel.DataAnnotations;

namespace AssetFlow.Api.Services;

/// <summary>
/// Configuracion de los tokens.
/// </summary>
/// <remarks>
/// <see cref="Key"/> no tiene valor por defecto a proposito: la aplicacion
/// debe negarse a arrancar si nadie la ha configurado. Una clave de firma con
/// valor por defecto es equivalente a no tener firma, porque el valor por
/// defecto esta publicado en el repositorio.
/// </remarks>
public class JwtOptions
{
    public const string Section = "Jwt";

    [Required(ErrorMessage = "Falta la clave de firma JWT. Ver docs/configuration.md.")]
    [MinLength(32, ErrorMessage = "La clave de firma JWT debe tener al menos 32 caracteres.")]
    public string Key { get; set; } = string.Empty;

    [Required]
    public string Issuer { get; set; } = "AssetFlow.Api";

    [Required]
    public string Audience { get; set; } = "AssetFlow.Desktop";

    /// <summary>
    /// Vida del access token. Corta a proposito: no es revocable, asi que su
    /// ventana de utilidad para quien lo robe debe ser pequena. La renovacion
    /// la hace el cliente de forma transparente con el refresh token.
    /// </summary>
    [Range(1, 60)]
    public int AccessTokenMinutes { get; set; } = 15;

    /// <summary>Vida del refresh token. Este si es revocable.</summary>
    [Range(1, 90)]
    public int RefreshTokenDays { get; set; } = 7;
}
