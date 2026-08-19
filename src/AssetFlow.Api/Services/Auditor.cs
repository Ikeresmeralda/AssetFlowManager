using System.Security.Claims;
using AssetFlow.Api.Data;
using AssetFlow.Api.Entities;

namespace AssetFlow.Api.Services;

/// <summary>
/// Registro de acciones relevantes.
/// </summary>
public interface IAuditor
{
    /// <summary>
    /// Anota una accion realizada por el usuario de la peticion en curso.
    /// </summary>
    /// <remarks>
    /// No guarda: deja la entrada en el contexto para que se persista con el
    /// mismo <c>SaveChanges</c> que la operacion auditada. Asi la anotacion y
    /// el hecho anotado entran o no entran juntos, y no puede quedar registrada
    /// una aprobacion que despues fallo.
    /// </remarks>
    void Registrar(string accion, string? tipoEntidad = null, int? idEntidad = null,
                   string? detalles = null);

    /// <summary>
    /// Anota una accion sin sesion iniciada, indicando el actor a mano.
    /// Para flujos anonimos como la recuperacion de contrasena.
    /// </summary>
    void RegistrarComo(int? idActor, string nombreActor, string accion,
                       string? tipoEntidad = null, int? idEntidad = null,
                       string? detalles = null);
}

public sealed class Auditor : IAuditor
{
    private readonly AssetFlowDbContext _db;
    private readonly IHttpContextAccessor _contexto;

    public Auditor(AssetFlowDbContext db, IHttpContextAccessor contexto)
    {
        _db = db;
        _contexto = contexto;
    }

    public void Registrar(string accion, string? tipoEntidad = null, int? idEntidad = null,
                          string? detalles = null)
    {
        ClaimsPrincipal? usuario = _contexto.HttpContext?.User;

        int? id = null;

        if (int.TryParse(usuario?.FindFirstValue(ClaimTypes.NameIdentifier), out int valor))
        {
            id = valor;
        }

        RegistrarComo(id, usuario?.Identity?.Name ?? "(anónimo)", accion,
                      tipoEntidad, idEntidad, detalles);
    }

    public void RegistrarComo(int? idActor, string nombreActor, string accion,
                              string? tipoEntidad = null, int? idEntidad = null,
                              string? detalles = null)
    {
        _db.AuditEntries.Add(new AuditEntry
        {
            OccurredAt = DateTime.UtcNow,
            ActorUserId = idActor,
            ActorUsername = Recortar(nombreActor, 50) ?? "(anónimo)",
            Action = accion,
            EntityType = tipoEntidad,
            EntityId = idEntidad,
            Details = Recortar(detalles, 500)
        });
    }

    /// <summary>
    /// Corta al limite de la columna. Sin esto, un detalle largo tumbaria la
    /// operacion auditada, que es exactamente lo contrario de lo que debe
    /// hacer un registro de auditoria.
    /// </summary>
    private static string? Recortar(string? valor, int maximo)
    {
        if (string.IsNullOrEmpty(valor))
        {
            return null;
        }

        return valor.Length <= maximo ? valor : valor[..maximo];
    }
}
