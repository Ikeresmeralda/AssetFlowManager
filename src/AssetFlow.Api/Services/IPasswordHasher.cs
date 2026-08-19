namespace AssetFlow.Api.Services;

/// <summary>
/// Hashing y verificacion de contrasenas.
/// </summary>
/// <remarks>
/// Existe como interfaz por dos motivos concretos, no por costumbre:
/// permite sustituir el algoritmo sin tocar los controladores, y permite que
/// los tests usen un coste bajo (BCrypt con factor 12 tarda a proposito unos
/// 250 ms, lo que haria lentisima una bateria de tests).
/// </remarks>
public interface IPasswordHasher
{
    string Hash(string password);

    bool Verify(string password, string hash);
}
