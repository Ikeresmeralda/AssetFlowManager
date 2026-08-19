namespace AssetFlow.Api.Entities;

/// <summary>
/// Estado de una solicitud de recuperacion de contrasena.
/// </summary>
public enum PasswordResetRequestStatus
{
    /// <summary>Esperando a que un administrador decida.</summary>
    Pending = 0,

    /// <summary>Aprobada: se asigno la contrasena provisional. Estado final.</summary>
    Approved = 1,

    /// <summary>Denegada por un administrador. Estado final.</summary>
    Rejected = 2
}

/// <summary>
/// Peticion de recuperacion de contrasena, resuelta por un administrador
/// dentro de la propia aplicacion.
/// </summary>
/// <remarks>
/// Sustituye al codigo de un solo uso enviado por correo. El cambio no es
/// gratuito y conviene tenerlo presente:
///
/// - <b>Se gana</b> independencia del correo, que es un canal que esta
///   aplicacion no controla y que en la practica falla (dominio sin verificar,
///   SPF/DKIM, carpeta de no deseado). Y se gana una decision humana explicita:
///   nadie recupera una cuenta sin que un administrador lo apruebe.
/// - <b>Se pierde</b> la prueba de posesion del buzon. Antes, quien recuperaba
///   la cuenta demostraba tener acceso al correo registrado; ahora esa
///   comprobacion la hace una persona. Es responsabilidad del administrador
///   confirmar por un canal aparte que quien pide el cambio es quien dice ser.
///
/// La fila se conserva despues de resolverse: es el historial de quien pidio
/// que y quien lo autorizo.
/// </remarks>
public class PasswordResetRequest
{
    public int Id { get; set; }

    /// <summary>Cuenta cuya contrasena se quiere recuperar.</summary>
    public int UserId { get; set; }

    public User User { get; set; } = null!;

    public DateTime RequestedAt { get; set; } = DateTime.UtcNow;

    public PasswordResetRequestStatus Status { get; set; } = PasswordResetRequestStatus.Pending;

    /// <summary>Momento de la decision. Nulo mientras esta pendiente.</summary>
    public DateTime? ResolvedAt { get; set; }

    /// <summary>Administrador que aprobo o denego.</summary>
    public int? ResolvedByUserId { get; set; }

    /// <summary>
    /// Copia del nombre del administrador que decidio.
    /// </summary>
    /// <remarks>
    /// Igual que en <see cref="AuditEntry"/>: si esa cuenta se elimina mas
    /// adelante, el historial debe seguir diciendo quien autorizo el cambio.
    /// </remarks>
    public string? ResolvedByUsername { get; set; }

    public bool EstaPendiente => Status == PasswordResetRequestStatus.Pending;
}
