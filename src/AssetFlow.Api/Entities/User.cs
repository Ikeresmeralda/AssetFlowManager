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
    /// una contrasena. Mientras este puesto, la sesion que abre la provisional
    /// no puede hacer nada salvo cambiarla; sin este campo, una contrasena que
    /// conocen dos personas (quien la dicto y su titular) seguiria valiendo
    /// indefinidamente.
    ///
    /// El token de acceso lleva el mismo dato como claim para que la
    /// comprobacion no cueste una consulta por peticion; ver
    /// <c>CambioObligatorioMiddleware</c>.
    /// </remarks>
    public bool MustChangePassword { get; set; }

    /// <summary>
    /// Instante en que la contrasena provisional deja de abrir sesion.
    /// </summary>
    /// <remarks>
    /// Solo tiene valor mientras <see cref="MustChangePassword"/> este activo.
    /// Pasado el plazo, el acceso con la provisional se rechaza igual que una
    /// contrasena incorrecta y hay que pedir otra recuperacion: una llave
    /// dictada por telefono no debe quedarse esperando semanas a que alguien
    /// la use, sea quien sea ese alguien.
    /// </remarks>
    public DateTime? ProvisionalPasswordExpiresAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<Loan> Loans { get; set; } = new List<Loan>();

    public ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();

    public string FullName => $"{FirstName} {LastName}".Trim();
}
