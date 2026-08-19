using System.Collections.Concurrent;

namespace AssetFlow.Api.Services;

/// <summary>
/// Limite de solicitudes de recuperacion por cuenta, independiente del
/// limitador por IP.
/// </summary>
/// <remarks>
/// El limitador "login" de <c>Program.cs</c> reparte por IP y cubre a quien
/// martillea el endpoint desde una maquina. No cubre el caso contrario: varias
/// peticiones para la misma cuenta desde IP distintas, o simplemente alguien
/// pulsando "reenviar código" varias veces seguidas. Sin este limite, cada
/// solicitud encola un correo nuevo y el buzon del titular -o el proveedor de
/// correo saliente, que suele tarificar o suspender por volumen- se satura.
///
/// Vive en memoria por el mismo motivo que <see cref="LoginThrottle"/>: contar
/// en la base de datos convertiria cada intento en una escritura, que es
/// justo lo que se quiere evitar. La clave es el identificador de la cuenta,
/// no el correo tecleado, para no anadir un segundo camino de comprobacion:
/// solo se consulta despues de que <see cref="PasswordResetService"/> ya ha
/// encontrado una cuenta activa con ese correo.
/// </remarks>
public interface IPasswordResetThrottle
{
    /// <summary>
    /// Indica si esta cuenta puede recibir una solicitud mas ahora mismo, y si
    /// es asi, la cuenta.
    /// </summary>
    bool PermiteSolicitud(int usuarioId);
}

public sealed class PasswordResetThrottle : IPasswordResetThrottle
{
    private const int SolicitudesMaximas = 2;

    private static readonly TimeSpan Ventana = TimeSpan.FromMinutes(30);

    private sealed class Historial
    {
        public readonly List<DateTimeOffset> Solicitudes = [];
    }

    // Acotado por el numero de cuentas reales, no por lo que alguien pueda
    // teclear: a diferencia de LoginThrottle, aqui no hace falta purgar
    // entradas caducadas.
    private readonly ConcurrentDictionary<int, Historial> _historiales = new();

    private readonly TimeProvider _reloj;

    public PasswordResetThrottle(TimeProvider reloj) => _reloj = reloj;

    public bool PermiteSolicitud(int usuarioId)
    {
        Historial historial = _historiales.GetOrAdd(usuarioId, _ => new Historial());
        DateTimeOffset ahora = _reloj.GetUtcNow();

        lock (historial)
        {
            historial.Solicitudes.RemoveAll(t => ahora - t > Ventana);

            if (historial.Solicitudes.Count >= SolicitudesMaximas)
            {
                return false;
            }

            historial.Solicitudes.Add(ahora);
            return true;
        }
    }
}
