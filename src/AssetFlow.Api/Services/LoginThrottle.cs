using System.Collections.Concurrent;

namespace AssetFlow.Api.Services;

/// <summary>
/// Bloqueo temporal de una cuenta tras varios intentos fallidos seguidos.
/// </summary>
/// <remarks>
/// Complementa al limitador por IP. Ese cubre a un atacante que martillea
/// desde una maquina; este cubre el caso contrario, muchas IP distintas
/// probando contrasenas contra una misma cuenta, donde el reparto por IP no
/// llega a saltar nunca.
///
/// El contador vive en memoria a proposito. Persistirlo en la base de datos
/// convertiria cada intento fallido en una escritura, que es justo lo que un
/// atacante querria provocar. La contrapartida es que reiniciar la API borra
/// los contadores; es asumible porque el limitador por IP sigue activo.
/// </remarks>
public interface ILoginThrottle
{
    /// <summary>Indica si la cuenta esta bloqueada ahora mismo.</summary>
    bool EstaBloqueada(string usuario);

    /// <summary>Registra un intento fallido y devuelve si eso la ha bloqueado.</summary>
    bool RegistrarFallo(string usuario);

    /// <summary>Limpia el contador tras un acceso correcto.</summary>
    void RegistrarExito(string usuario);
}

public sealed class LoginThrottle : ILoginThrottle
{
    private const int IntentosMaximos = 8;

    private static readonly TimeSpan Bloqueo = TimeSpan.FromMinutes(15);

    private static readonly TimeSpan Ventana = TimeSpan.FromMinutes(15);

    private sealed record Contador(int Fallos, DateTimeOffset Ultimo, DateTimeOffset? BloqueadaHasta);

    private readonly ConcurrentDictionary<string, Contador> _contadores = new(StringComparer.OrdinalIgnoreCase);

    private readonly TimeProvider _reloj;

    public LoginThrottle(TimeProvider reloj)
    {
        _reloj = reloj;
    }

    public bool EstaBloqueada(string usuario)
    {
        if (!_contadores.TryGetValue(usuario, out Contador? contador))
        {
            return false;
        }

        return contador.BloqueadaHasta is { } hasta && _reloj.GetUtcNow() < hasta;
    }

    public bool RegistrarFallo(string usuario)
    {
        DateTimeOffset ahora = _reloj.GetUtcNow();

        Contador actualizado = _contadores.AddOrUpdate(
            usuario,
            _ => new Contador(1, ahora, null),
            (_, previo) =>
            {
                // Los fallos aislados y espaciados no deben acumularse hasta
                // bloquear a alguien que simplemente se equivoca de vez en
                // cuando: pasada la ventana, el contador vuelve a empezar.
                int fallos = ahora - previo.Ultimo > Ventana ? 1 : previo.Fallos + 1;

                DateTimeOffset? bloqueo = fallos >= IntentosMaximos
                    ? ahora + Bloqueo
                    : null;

                return new Contador(fallos, ahora, bloqueo);
            });

        LimpiarCaducados(ahora);

        return actualizado.BloqueadaHasta is not null;
    }

    public void RegistrarExito(string usuario) => _contadores.TryRemove(usuario, out _);

    /// <summary>
    /// Evita que el diccionario crezca sin limite si alguien prueba miles de
    /// nombres de usuario distintos.
    /// </summary>
    private void LimpiarCaducados(DateTimeOffset ahora)
    {
        if (_contadores.Count < 10_000)
        {
            return;
        }

        foreach (var par in _contadores)
        {
            bool caducado = ahora - par.Value.Ultimo > Ventana &&
                            (par.Value.BloqueadaHasta is null || ahora > par.Value.BloqueadaHasta);

            if (caducado)
            {
                _contadores.TryRemove(par.Key, out _);
            }
        }
    }
}
