using System.ComponentModel.DataAnnotations;

namespace AssetFlow.Api.Dtos;

/// <summary>Credenciales de acceso. Viajan en el cuerpo, nunca en la URL.</summary>
public record LoginRequest
{
    [Required]
    [StringLength(50, MinimumLength = 3)]
    public string Username { get; init; } = string.Empty;

    [Required]
    [StringLength(128, MinimumLength = 1)]
    public string Password { get; init; } = string.Empty;
}

public record RefreshRequest
{
    [Required]
    public string RefreshToken { get; init; } = string.Empty;
}

/// <summary>Peticion de codigo de recuperacion.</summary>
public record ForgotPasswordRequest
{
    [Required]
    [EmailAddress(ErrorMessage = "El correo no tiene un formato válido.")]
    [StringLength(255)]
    public string Email { get; init; } = string.Empty;
}

/// <summary>
/// Cambio obligatorio de la contrasena provisional.
/// </summary>
/// <remarks>
/// Es el unico punto en el que una persona fija su propia contrasena, y solo
/// funciona mientras la cuenta arrastre el cambio pendiente. Pide la actual
/// ademas de la nueva porque el token no basta: si alguien se dejara la sesion
/// abierta en este formulario, quien pasara por delante podria quedarse la
/// cuenta sin saber la provisional.
/// </remarks>
public record ChangePasswordRequest
{
    [Required]
    [StringLength(128, MinimumLength = 1)]
    public string CurrentPassword { get; init; } = string.Empty;

    [Required]
    [StringLength(128, MinimumLength = 10,
        ErrorMessage = "La contraseña debe tener al menos 10 caracteres.")]
    public string NewPassword { get; init; } = string.Empty;
}

/// <summary>
/// Sesion abierta. Contiene lo justo para que el cliente pueda operar y
/// pintar la interfaz sin volver a preguntar quien es.
/// </summary>
public record AuthResponse
{
    public required string AccessToken { get; init; }

    public required DateTime AccessTokenExpiresAt { get; init; }

    public required string RefreshToken { get; init; }

    public required DateTime RefreshTokenExpiresAt { get; init; }

    public required CurrentUserDto User { get; init; }
}

/// <summary>Identidad del usuario autenticado.</summary>
public record CurrentUserDto
{
    public required int Id { get; init; }

    public required string Username { get; init; }

    public required string FirstName { get; init; }

    public required string LastName { get; init; }

    public required string Role { get; init; }

    /// <summary>
    /// La contrasena es provisional y hay que cambiarla antes de nada.
    /// </summary>
    /// <remarks>
    /// El cliente lo usa para abrir el formulario de cambio nada mas entrar.
    /// Es comodidad de interfaz, no seguridad: quien ignore este campo y llame
    /// a la API directamente se encuentra igualmente con un 403 en todo lo
    /// demas. Ver <c>CambioObligatorioMiddleware</c>.
    /// </remarks>
    public bool MustChangePassword { get; init; }
}
