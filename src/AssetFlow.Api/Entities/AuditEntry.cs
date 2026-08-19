namespace AssetFlow.Api.Entities;

/// <summary>
/// Acciones que se registran en la auditoria.
/// </summary>
/// <remarks>
/// Constantes y no un enum porque el valor se guarda como texto: una entrada
/// de auditoria de hace un ano debe seguir leyendose aunque el codigo haya
/// cambiado, y un entero obliga a mantener para siempre la tabla de
/// equivalencias para poder interpretarla.
/// </remarks>
public static class AuditActions
{
    public const string SesionIniciada = "sesion.iniciada";
    public const string SesionCerrada = "sesion.cerrada";
    public const string SesionesRevocadas = "sesion.revocadas";

    public const string ContrasenaReiniciada = "contrasena.reiniciada";
    public const string RecuperacionSolicitada = "contrasena.recuperacion_solicitada";
    public const string RecuperacionAprobada = "contrasena.recuperacion_aprobada";
    public const string RecuperacionDenegada = "contrasena.recuperacion_denegada";
    public const string RecuperacionCompletada = "contrasena.recuperacion_completada";

    public const string PrestamoSolicitado = "prestamo.solicitado";
    public const string PrestamoAprobado = "prestamo.aprobado";
    public const string PrestamoRechazado = "prestamo.rechazado";
    public const string DevolucionSolicitada = "devolucion.solicitada";
    public const string DevolucionAprobada = "devolucion.aprobada";
    public const string DevolucionRechazada = "devolucion.rechazada";
    public const string PrestamoEliminado = "prestamo.eliminado";

    public const string UsuarioCreado = "usuario.creado";
    public const string UsuarioEliminado = "usuario.eliminado";
    public const string AccesoModificado = "usuario.acceso_modificado";

    public const string MaterialCreado = "material.creado";
    public const string MaterialModificado = "material.modificado";
    public const string MaterialEliminado = "material.eliminado";
}

/// <summary>
/// Registro inmutable de una accion relevante.
/// </summary>
/// <remarks>
/// Que se guarda y que no:
///
/// - <b>Se guarda</b> quien, que, sobre que y cuando. Es lo que permite
///   responder a "quien aprobo este prestamo" meses despues.
/// - <b>No se guarda la direccion IP.</b> Es un dato personal y esta
///   aplicacion no la necesita: la auditoria responde a "quien hizo que", no
///   a "desde donde", y el abuso por origen ya lo corta el limitador de
///   peticiones sin necesidad de almacenar nada.
/// - <b>Nunca se guardan secretos</b>: ni contrasenas, ni tokens, ni sus
///   hashes. <see cref="Details"/> es texto descriptivo corto, y lo que se
///   escribe ahi esta acotado en los puntos de llamada.
///
/// El nombre del actor se guarda como copia ademas de la clave ajena: si la
/// cuenta se elimina, el registro debe seguir diciendo quien fue.
/// </remarks>
public class AuditEntry
{
    public int Id { get; set; }

    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Autor de la accion. Nulo cuando no hay sesion (por ejemplo, una
    /// solicitud de recuperacion de contrasena).
    /// </summary>
    public int? ActorUserId { get; set; }

    /// <summary>Copia del nombre del actor en el momento de la accion.</summary>
    public string ActorUsername { get; set; } = "(anónimo)";

    /// <summary>Una de las constantes de <see cref="AuditActions"/>.</summary>
    public string Action { get; set; } = null!;

    /// <summary>Tipo de entidad afectada: "Loan", "User", "Material".</summary>
    public string? EntityType { get; set; }

    public int? EntityId { get; set; }

    /// <summary>Descripcion corta y sin datos sensibles.</summary>
    public string? Details { get; set; }
}
