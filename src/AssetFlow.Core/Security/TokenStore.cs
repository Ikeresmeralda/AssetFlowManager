using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AssetFlow.Core.Configuration;
using AssetFlow.Core.Diagnostics;

namespace AssetFlow.Core.Security;

/// <summary>Sesion persistida entre ejecuciones.</summary>
public sealed record SesionGuardada(
    string RefreshToken,
    DateTime RefreshTokenExpiresAt,
    string Servidor,
    string Username);

/// <summary>
/// Guarda el refresh token en disco protegido con DPAPI.
/// </summary>
/// <remarks>
/// Que protege y que no:
///
/// - SI protege frente a otra cuenta de Windows del mismo equipo y frente a
///   quien copie el archivo a otra maquina: DPAPI con ambito CurrentUser deriva
///   la clave de las credenciales de la sesion de Windows, asi que el archivo
///   es indescifrable fuera de ella.
/// - NO protege frente a codigo que ya se ejecuta como el propio usuario. Nada
///   en una aplicacion de escritorio puede hacerlo: ese codigo puede pedirle a
///   DPAPI que descifre igual que hacemos nosotros. Prometer lo contrario seria
///   falso.
///
/// Por eso lo unico que se guarda es el refresh token, que es revocable desde
/// el servidor, y nunca la contrasena, que no lo es. El access token no se
/// persiste: vive en memoria y caduca en 15 minutos.
/// </remarks>
public sealed class TokenStore
{
    private static readonly byte[] Entropia =
        Encoding.UTF8.GetBytes("AssetFlow.Desktop.SesionLocal.v1");

    private readonly string _archivo;

    public TokenStore()
    {
        _archivo = Path.Combine(RutasApp.CarpetaDatos, "sesion.bin");
    }

    public SesionGuardada? Cargar()
    {
        try
        {
            if (!File.Exists(_archivo))
            {
                return null;
            }

            byte[] protegido = File.ReadAllBytes(_archivo);

            byte[] plano = ProtectedData.Unprotect(
                protegido, Entropia, DataProtectionScope.CurrentUser);

            var sesion = JsonSerializer.Deserialize<SesionGuardada>(plano);

            // Un token ya caducado no sirve para nada: se descarta aqui en
            // lugar de enviarlo al servidor para que lo rechace.
            if (sesion is null || sesion.RefreshTokenExpiresAt <= DateTime.UtcNow)
            {
                Borrar();
                return null;
            }

            return sesion;
        }
        catch (CryptographicException)
        {
            // Archivo de otra cuenta de Windows o corrupto. No es un error que
            // deba interrumpir nada: simplemente no hay sesion recordada.
            Log.Info("No se ha podido descifrar la sesion guardada; se descarta.");
            Borrar();
            return null;
        }
        catch (Exception ex)
        {
            Log.Error("Error al leer la sesion guardada", ex);
            Borrar();
            return null;
        }
    }

    public void Guardar(SesionGuardada sesion)
    {
        try
        {
            Directory.CreateDirectory(RutasApp.CarpetaDatos);

            byte[] plano = JsonSerializer.SerializeToUtf8Bytes(sesion);

            byte[] protegido = ProtectedData.Protect(
                plano, Entropia, DataProtectionScope.CurrentUser);

            File.WriteAllBytes(_archivo, protegido);
        }
        catch (Exception ex)
        {
            // No poder recordar la sesion es una molestia, no un fallo: la
            // aplicacion sigue funcionando pidiendo credenciales.
            Log.Error("No se ha podido guardar la sesion", ex);
        }
    }

    public void Borrar()
    {
        try
        {
            if (File.Exists(_archivo))
            {
                File.Delete(_archivo);
            }
        }
        catch (Exception ex)
        {
            Log.Error("No se ha podido borrar la sesion guardada", ex);
        }
    }
}
