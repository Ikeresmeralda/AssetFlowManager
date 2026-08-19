namespace AssetFlow.Api.Entities;

/// <summary>Estado de un prestamo.</summary>
/// <remarks>
/// Los valores numericos de <see cref="Active"/> y <see cref="Returned"/> se
/// conservan de la version anterior del modelo para no tener que reescribir
/// las filas existentes al migrar. Por eso los estados nuevos no siguen el
/// orden logico del flujo: el orden de presentacion es cosa de la interfaz,
/// no del numero que se guarda.
/// </remarks>
public enum LoanStatus
{
    /// <summary>Aprobado y entregado. El material esta fuera.</summary>
    Active = 0,

    /// <summary>Devuelto y confirmado por un administrador. Estado final.</summary>
    Returned = 1,

    /// <summary>Solicitado por el usuario, a la espera de decision.</summary>
    PendingApproval = 2,

    /// <summary>Solicitud denegada por un administrador. Estado final.</summary>
    Rejected = 3,

    /// <summary>Devolucion solicitada, a la espera de confirmacion.</summary>
    ReturnRequested = 4
}

/// <summary>
/// Prestamo de uno o varios articulos a un usuario.
/// </summary>
/// <remarks>
/// El ciclo de vida es una maquina de estados explicita:
///
/// <code>
///                     (usuario solicita)
///                            |
///                            v
///                     PendingApproval --(admin rechaza)--> Rejected [final]
///                            |
///                      (admin acepta)
///                            v
///                         Active &lt;--(admin rechaza devolucion)--+
///                            |                                  |
///               (usuario solicita devolucion)                   |
///                            v                                  |
///                     ReturnRequested ---------------------------+
///                            |
///                   (admin acepta devolucion)
///                            v
///                        Returned [final]
/// </code>
///
/// Las transiciones validas estan centralizadas en
/// <see cref="LoanTransitions"/>. Ningun controlador asigna
/// <see cref="Status"/> directamente sin pasar por esa comprobacion: es lo que
/// impide que una peticion repetida o fuera de orden deje el prestamo en un
/// estado imposible (por ejemplo, devolver algo que nunca llego a aprobarse).
/// </remarks>
public class Loan
{
    public int Id { get; set; }

    public int UserId { get; set; }

    public User User { get; set; } = null!;

    /// <summary>
    /// Fecha de entrega efectiva. Se fija al aprobar, no al solicitar: hasta
    /// entonces el material no ha salido del almacen.
    /// </summary>
    public DateOnly? LoanDate { get; set; }

    public DateOnly EstimatedReturnDate { get; set; }

    public DateOnly? ReturnDate { get; set; }

    public string? Reason { get; set; }

    public LoanStatus Status { get; set; } = LoanStatus.PendingApproval;

    /// <summary>Momento en que el usuario creo la solicitud.</summary>
    public DateTime RequestedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Momento de la decision sobre la solicitud de prestamo.</summary>
    public DateTime? DecidedAt { get; set; }

    /// <summary>Administrador que acepto o rechazo la solicitud.</summary>
    public int? DecidedByUserId { get; set; }

    public User? DecidedBy { get; set; }

    /// <summary>Motivo del rechazo, si lo hubo. Lo escribe el administrador.</summary>
    public string? DecisionNote { get; set; }

    /// <summary>Momento en que se solicito la devolucion.</summary>
    public DateTime? ReturnRequestedAt { get; set; }

    /// <summary>Momento de la decision sobre la devolucion.</summary>
    public DateTime? ReturnDecidedAt { get; set; }

    /// <summary>Administrador que acepto o rechazo la devolucion.</summary>
    public int? ReturnDecidedByUserId { get; set; }

    public User? ReturnDecidedBy { get; set; }

    public string? ReturnDecisionNote { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<LoanLine> Lines { get; set; } = new List<LoanLine>();

    /// <summary>
    /// El material esta fuera del almacen: entregado y sin devolucion
    /// confirmada. Una devolucion solicitada pero no aceptada todavia cuenta
    /// como fuera, porque nadie ha comprobado aun que el material haya vuelto.
    /// </summary>
    public bool EstaFuera =>
        Status is LoanStatus.Active or LoanStatus.ReturnRequested;

    /// <summary>
    /// Reserva unidades sin haberlas entregado. Se descuentan de lo disponible
    /// para que dos solicitudes simultaneas no puedan aprobarse las dos sobre
    /// el mismo ultimo articulo.
    /// </summary>
    public bool EstaReservado => Status == LoanStatus.PendingApproval;

    /// <summary>Vencido: sigue fuera y la fecha prevista ya ha pasado.</summary>
    public bool IsOverdue =>
        EstaFuera && EstimatedReturnDate < DateOnly.FromDateTime(DateTime.UtcNow);
}

/// <summary>
/// Transiciones permitidas del ciclo de vida de un prestamo.
/// </summary>
/// <remarks>
/// Se declara en un solo sitio a proposito. Repartir estas comprobaciones por
/// los controladores es como se acaba teniendo un prestamo devuelto que vuelve
/// a activo, o una solicitud rechazada que alguien aprueba despues.
/// </remarks>
public static class LoanTransitions
{
    private static readonly Dictionary<LoanStatus, LoanStatus[]> Permitidas = new()
    {
        [LoanStatus.PendingApproval] = [LoanStatus.Active, LoanStatus.Rejected],
        [LoanStatus.Active] = [LoanStatus.ReturnRequested, LoanStatus.Returned],
        [LoanStatus.ReturnRequested] = [LoanStatus.Returned, LoanStatus.Active],
        [LoanStatus.Rejected] = [],
        [LoanStatus.Returned] = []
    };

    public static bool EsValida(LoanStatus desde, LoanStatus hasta) =>
        Permitidas.TryGetValue(desde, out LoanStatus[]? destinos) &&
        destinos.Contains(hasta);

    /// <summary>Estados desde los que ya no se puede hacer nada.</summary>
    public static bool EsFinal(LoanStatus estado) =>
        estado is LoanStatus.Rejected or LoanStatus.Returned;
}
