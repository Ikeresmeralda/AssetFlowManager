namespace AssetFlow.Api.Entities;

/// <summary>
/// Cuenta de usuario del sistema.
/// </summary>
/// <remarks>
/// Cambios respecto al modelo heredado:
///
/// - <c>Password</c> pasa a llamarse <see cref="PasswordHash"/>. El nombre
///   importa: deja claro en el punto de uso que ahi nunca va una contrasena.
/// - <c>IsAdmin int?</c> se sustituye por <see cref="Role"/>. Un entero
///   anulable con tres estados posibles (0, 1, null) para representar dos
///   roles era una fuente de errores silenciosos.
/// - Se elimina el campo DNI. Un sistema de prestamo de material no necesita
///   el documento de identidad para funcionar, y almacenarlo obliga a
///   protegerlo sin obtener nada a cambio (minimizacion de datos).
/// </remarks>
public class User
{
    public int Id { get; set; }

    public string Username { get; set; } = null!;

    public string FirstName { get; set; } = null!;

    public string LastName { get; set; } = null!;

    public string Email { get; set; } = null!;

    public string? PhoneNumber { get; set; }

    /// <summary>Hash BCrypt. Nunca sale de la API.</summary>
    public string PasswordHash { get; set; } = null!;

    /// <summary>Uno de los valores de <see cref="Security.Roles"/>.</summary>
    public string Role { get; set; } = Security.Roles.User;

    /// <summary>
    /// Desactivar en lugar de borrar: un usuario con prestamos historicos no
    /// puede eliminarse sin perder el historial.
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// La cuenta lleva una contrasena provisional y debe cambiarla antes de
    /// poder hacer nada mas.
    /// </summary>
    /// <remarks>
    /// Se activa cuando un administrador aprueba una recuperacion o reinicia
    /// una contrasena. Es lo que hace aceptable que la contrasena provisional
    /// sea predecible (<c>usuario + "123@"</c>): en cuanto se usa, deja de
    /// valer. Sin este campo, esa contrasena seria permanente y derivable del
    /// nombre de usuario, es decir, una via de acceso publica a la cuenta.
    ///
    /// El token de acceso lleva el mismo dato como claim para que la
    /// comprobacion no cueste una consulta por peticion; ver
    /// <c>CambioObligatorioMiddleware</c>.
    /// </remarks>
    public bool MustChangePassword { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<Loan> Loans { get; set; } = new List<Loan>();

    public ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();

    public string FullName => $"{FirstName} {LastName}".Trim();
}
