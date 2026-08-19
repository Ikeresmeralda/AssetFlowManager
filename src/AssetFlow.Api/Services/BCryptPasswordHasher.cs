using Microsoft.Extensions.Options;

namespace AssetFlow.Api.Services;

public class PasswordHashingOptions
{
    public const string Section = "PasswordHashing";

    /// <summary>
    /// Coste de BCrypt. 12 significa 2^12 iteraciones, alrededor de 250 ms en
    /// hardware actual: suficiente para que un ataque por diccionario sobre
    /// una tabla filtrada sea inviable, y despreciable en un login puntual.
    /// Los tests lo bajan a 4 para no tardar minutos.
    /// </summary>
    public int WorkFactor { get; set; } = 12;
}

/// <summary>
/// Implementacion con BCrypt.
/// </summary>
/// <remarks>
/// BCrypt genera y almacena un salt aleatorio dentro del propio hash, por lo
/// que no existe un campo de salt separado: dos usuarios con la misma
/// contrasena producen hashes distintos.
/// </remarks>
public class BCryptPasswordHasher : IPasswordHasher
{
    private readonly int _workFactor;

    public BCryptPasswordHasher(IOptions<PasswordHashingOptions> options)
    {
        _workFactor = options.Value.WorkFactor;
    }

    public string Hash(string password) =>
        BCrypt.Net.BCrypt.HashPassword(password, _workFactor);

    public bool Verify(string password, string hash)
    {
        try
        {
            return BCrypt.Net.BCrypt.Verify(password, hash);
        }
        catch (BCrypt.Net.SaltParseException)
        {
            // Hash con formato invalido (por ejemplo, un registro heredado que
            // todavia guardaba la contrasena en claro). Se trata como
            // credencial incorrecta, nunca como coincidencia.
            return false;
        }
    }
}
