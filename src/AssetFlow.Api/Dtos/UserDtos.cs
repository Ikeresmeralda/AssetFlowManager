using System.ComponentModel.DataAnnotations;

namespace AssetFlow.Api.Dtos;

/// <summary>
/// Vista publica de un usuario: lo que puede ver otro usuario autenticado.
/// </summary>
/// <remarks>
/// Sin email, sin telefono y sin rol. Para saber quien tiene prestado un
/// articulo basta con el nombre.
/// </remarks>
public record UserSummaryDto
{
    public required int Id { get; init; }

    public required string Username { get; init; }

    public required string FullName { get; init; }
}

/// <summary>
/// Vista administrativa. Incluye los datos de contacto, que solo un
/// administrador necesita para reclamar material sin devolver.
/// </summary>
public record UserDto
{
    public required int Id { get; init; }

    public required string Username { get; init; }

    public required string FirstName { get; init; }

    public required string LastName { get; init; }

    public required string Email { get; init; }

    public string? PhoneNumber { get; init; }

    public required string Role { get; init; }

    public required bool IsActive { get; init; }

    public required DateTime CreatedAt { get; init; }

    public required int ActiveLoans { get; init; }
}

public record CreateUserRequest
{
    [Required]
    [StringLength(50, MinimumLength = 3)]
    [RegularExpression("^[a-zA-Z0-9._-]+$",
        ErrorMessage = "El nombre de usuario solo admite letras, numeros, punto, guion y guion bajo.")]
    public string Username { get; init; } = string.Empty;

    [Required]
    [StringLength(100, MinimumLength = 1)]
    public string FirstName { get; init; } = string.Empty;

    [Required]
    [StringLength(100, MinimumLength = 1)]
    public string LastName { get; init; } = string.Empty;

    [Required]
    [EmailAddress]
    [StringLength(255)]
    public string Email { get; init; } = string.Empty;

    [Phone]
    [StringLength(30)]
    public string? PhoneNumber { get; init; }

    /// <summary>
    /// Longitud minima 10 siguiendo la recomendacion actual de NIST: la
    /// longitud protege mas que la obligacion de mezclar simbolos, que solo
    /// produce contrasenas predecibles del tipo "Password1!".
    /// </summary>
    [Required]
    [StringLength(128, MinimumLength = 10,
        ErrorMessage = "La contraseña debe tener al menos 10 caracteres.")]
    public string Password { get; init; } = string.Empty;

    [Required]
    [RegularExpression("^(Admin|User)$", ErrorMessage = "Rol no válido.")]
    public string Role { get; init; } = "User";
}

/// <summary>
/// Actualizacion de un usuario. No incluye contrasena ni rol: cambiarlos son
/// operaciones distintas, con sus propios endpoints y sus propias reglas.
/// </summary>
public record UpdateUserRequest
{
    [Required]
    [StringLength(100, MinimumLength = 1)]
    public string FirstName { get; init; } = string.Empty;

    [Required]
    [StringLength(100, MinimumLength = 1)]
    public string LastName { get; init; } = string.Empty;

    [Required]
    [EmailAddress]
    [StringLength(255)]
    public string Email { get; init; } = string.Empty;

    [Phone]
    [StringLength(30)]
    public string? PhoneNumber { get; init; }
}

/// <summary>Cambio de rol o de estado. Solo administradores.</summary>
public record UpdateUserAccessRequest
{
    [Required]
    [RegularExpression("^(Admin|User)$", ErrorMessage = "Rol no válido.")]
    public string Role { get; init; } = "User";

    public bool IsActive { get; init; } = true;
}

/// <summary>Reinicio de contrasena por un administrador.</summary>
/// <remarks>
/// No lleva contrasena: el administrador no elige una, se asigna la
/// provisional del sistema y la cuenta queda obligada a cambiarla. Asi el
/// administrador nunca conoce la contrasena definitiva de nadie.
/// </remarks>
public record ResetPasswordRequest;
