namespace AssetFlow.Api.Entities;

/// <summary>
/// Token de refresco emitido a un cliente.
/// </summary>
/// <remarks>
/// Se almacena el SHA-256 del token, nunca el token en si. Un refresh token
/// es una credencial de larga duracion: si alguien lee esta tabla no debe
/// poder usar su contenido para obtener sesiones.
///
/// SHA-256 sin salt es correcto aqui, a diferencia de las contrasenas: el
/// token es un valor aleatorio de 256 bits, no es adivinable por fuerza bruta
/// ni por diccionario, asi que el coste de un algoritmo lento no aporta nada.
/// </remarks>
public class RefreshToken
{
    public int Id { get; set; }

    public int UserId { get; set; }

    public User User { get; set; } = null!;

    public string TokenHash { get; set; } = null!;

    public DateTime ExpiresAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? RevokedAt { get; set; }

    /// <summary>
    /// Hash del token que sustituyo a este al rotar. Permite detectar la
    /// reutilizacion de un token ya consumido, que casi siempre significa
    /// que ha sido robado.
    /// </summary>
    public string? ReplacedByTokenHash { get; set; }

    public bool IsActive => RevokedAt is null && DateTime.UtcNow < ExpiresAt;
}
