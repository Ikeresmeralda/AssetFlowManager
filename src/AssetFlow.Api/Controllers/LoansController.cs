using AssetFlow.Api.Data;
using AssetFlow.Api.Dtos;
using AssetFlow.Api.Entities;
using AssetFlow.Api.Security;
using AssetFlow.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AssetFlow.Api.Controllers;

/// <summary>
/// Prestamos de material y su flujo de aprobacion.
/// </summary>
/// <remarks>
/// El identificador del usuario nunca se acepta como parametro para decidir
/// que se devuelve: se toma del token. Un administrador puede pedir los de otro
/// de forma explicita, y a un usuario normal ese parametro simplemente se le
/// ignora.
///
/// Las transiciones de estado pasan todas por <see cref="LoanTransitions"/>.
/// Ningun metodo asigna <c>Status</c> sin comprobar antes que el salto es
/// legal, que es lo que impide aprobar dos veces la misma solicitud o devolver
/// un prestamo que fue rechazado.
/// </remarks>
[ApiController]
[Route("api/loans")]
[Authorize]
[Produces("application/json")]
public class LoansController : ControllerBase
{
    /// <summary>
    /// Tope de solicitudes vivas por usuario.
    /// </summary>
    /// <remarks>
    /// Una solicitud pendiente reserva unidades. Sin este limite, una sola
    /// cuenta podria dejar el inventario entero reservado creando solicitudes
    /// que nadie va a aprobar, y el resto de usuarios se encontraria con todo
    /// agotado. Es la proteccion contra consumo desmedido de recursos que pide
    /// el punto de OWASP sobre el asunto, aplicada al recurso que de verdad
    /// escasea aqui, que es el material.
    /// </remarks>
    private const int MaximoSolicitudesPendientes = 5;

    private readonly AssetFlowDbContext _db;
    private readonly IAuditor _auditor;
    private readonly ILogger<LoansController> _log;

    public LoansController(AssetFlowDbContext db, IAuditor auditor, ILogger<LoansController> log)
    {
        _db = db;
        _auditor = auditor;
        _log = log;
    }

    // ========================================================================
    // CONSULTA
    // ========================================================================

    /// <summary>
    /// Lista prestamos. Un usuario normal ve solo los suyos; un administrador
    /// los de todos, o los de un usuario concreto con <c>userId</c>.
    /// </summary>
    /// <param name="userId">
    /// Usuario cuyos prestamos se consultan. Solo lo atiende un administrador;
    /// para el resto se ignora y se devuelven los propios.
    /// </param>
    /// <param name="status">
    /// Filtro opcional por estado. Un valor desconocido se rechaza en lugar de
    /// ignorarse: devolver la lista entera cuando se pidio un subconjunto es
    /// peor que decir que el filtro no vale.
    /// </param>
    /// <param name="activeOnly">Deja solo los prestamos con material fuera.</param>
    /// <param name="ct">Token de cancelacion.</param>
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<LoanDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<IEnumerable<LoanDto>>> GetAll(
        [FromQuery] int? userId,
        [FromQuery] string? status,
        [FromQuery] bool activeOnly = false,
        CancellationToken ct = default)
    {
        IQueryable<Loan> consulta = ConsultaBase();

        if (this.EsAdministrador())
        {
            if (userId is not null)
            {
                consulta = consulta.Where(l => l.UserId == userId);
            }
        }
        else
        {
            // Se ignora el userId recibido y se fuerza el propio. No se
            // devuelve 403 porque el parametro no es una peticion de acceso
            // ajeno: simplemente no se le hace caso a un cliente no autorizado.
            consulta = consulta.Where(l => l.UserId == this.UsuarioId());
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse(status, ignoreCase: true, out LoanStatus estado))
            {
                return Error($"El estado «{status}» no existe.");
            }

            consulta = consulta.Where(l => l.Status == estado);
        }

        if (activeOnly)
        {
            consulta = consulta.Where(
                l => l.Status == LoanStatus.Active || l.Status == LoanStatus.ReturnRequested);
        }

        List<Loan> prestamos = await consulta
            .OrderByDescending(l => l.RequestedAt)
            .ThenByDescending(l => l.Id)
            .ToListAsync(ct);

        return Ok(prestamos.Select(l => l.ToDto()).ToList());
    }

    /// <summary>
    /// Solicitudes a la espera de decision. Solo administradores.
    /// </summary>
    /// <remarks>
    /// Existe como endpoint propio, y no como un filtro mas, porque es la
    /// consulta que el panel de administracion hace continuamente y conviene
    /// que tenga un nombre y un contrato estables.
    /// </remarks>
    [HttpGet("pending")]
    [Authorize(Roles = Roles.Admin)]
    [ProducesResponseType(typeof(IEnumerable<LoanDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IEnumerable<LoanDto>>> GetPending(CancellationToken ct)
    {
        List<Loan> pendientes = await ConsultaBase()
            .Where(l => l.Status == LoanStatus.PendingApproval ||
                        l.Status == LoanStatus.ReturnRequested)
            .OrderBy(l => l.Status == LoanStatus.PendingApproval ? 0 : 1)
            .ThenBy(l => l.RequestedAt)
            .ToListAsync(ct);

        return Ok(pendientes.Select(l => l.ToDto()).ToList());
    }

    /// <summary>Devuelve un prestamo. Propio, o cualquiera si es administrador.</summary>
    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(LoanDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<LoanDto>> Get(int id, CancellationToken ct)
    {
        Loan? prestamo = await ConsultaBase().FirstOrDefaultAsync(l => l.Id == id, ct);

        if (prestamo is null)
        {
            return NotFound();
        }

        if (!PuedeVer(prestamo))
        {
            return Prohibido();
        }

        return Ok(prestamo.ToDto());
    }

    /// <summary>
    /// Historial de acciones sobre un prestamo. Propio, o cualquiera si es
    /// administrador.
    /// </summary>
    [HttpGet("{id:int}/history")]
    [ProducesResponseType(typeof(IEnumerable<LoanHistoryEntryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IEnumerable<LoanHistoryEntryDto>>> GetHistory(
        int id, CancellationToken ct)
    {
        Loan? prestamo = await _db.Loans.AsNoTracking()
            .FirstOrDefaultAsync(l => l.Id == id, ct);

        if (prestamo is null)
        {
            return NotFound();
        }

        if (!PuedeVer(prestamo))
        {
            return Prohibido();
        }

        List<AuditEntry> historial = await _db.AuditEntries.AsNoTracking()
            .Where(a => a.EntityType == "Loan" && a.EntityId == id)
            .OrderBy(a => a.OccurredAt)
            .ToListAsync(ct);

        return Ok(historial.Select(a => a.ToHistoryDto()).ToList());
    }

    // ========================================================================
    // SOLICITUD
    // ========================================================================

    /// <summary>Crea una solicitud de prestamo.</summary>
    /// <remarks>
    /// Un usuario normal crea la solicitud en estado pendiente. Un
    /// administrador que registra un prestamo lo crea ya aprobado: es quien
    /// tendria que aprobarlo despues, y obligarle a confirmar su propia accion
    /// solo anadiria un paso sin aportar ningun control.
    ///
    /// Toda la operacion va en una transaccion: o se crean el prestamo, sus
    /// lineas y la anotacion de auditoria, o no se crea nada.
    /// </remarks>
    /// <response code="400">Datos invalidos o unidades insuficientes.</response>
    [HttpPost]
    [ProducesResponseType(typeof(LoanDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<LoanDto>> Create(
        CreateLoanRequest peticion, CancellationToken ct)
    {
        bool esAdmin = this.EsAdministrador();

        // Solo un administrador presta en nombre de otro. Para el resto, el
        // destinatario es siempre uno mismo, venga lo que venga en el cuerpo.
        // Este es el punto exacto en el que se corta la asignacion masiva: el
        // campo UserId de la peticion no llega a tocar la entidad si quien
        // llama no es administrador.
        int destinatario = esAdmin && peticion.UserId is not null
            ? peticion.UserId.Value
            : this.UsuarioId();

        var hoy = DateOnly.FromDateTime(DateTime.UtcNow);

        if (peticion.EstimatedReturnDate < hoy)
        {
            return Error("La fecha prevista de devolución no puede ser anterior a hoy.");
        }

        if (peticion.EstimatedReturnDate > hoy.AddYears(1))
        {
            return Error("La fecha prevista de devolución no puede superar un año.");
        }

        // Lineas repetidas del mismo articulo: se agregan en lugar de
        // rechazarse, pero deben quedar como una sola fila por el indice unico.
        List<LineaSolicitada> lineas = peticion.Lines
            .GroupBy(l => l.MaterialId)
            .Select(g => new LineaSolicitada(g.Key, g.Sum(x => x.Quantity)))
            .ToList();

        if (lineas.Count == 0)
        {
            return Error("Un préstamo necesita al menos un artículo.");
        }

        User? usuario = await _db.Users.FirstOrDefaultAsync(
            u => u.Id == destinatario && u.IsActive, ct);

        if (usuario is null)
        {
            return Error("El usuario indicado no existe o está desactivado.");
        }

        if (!esAdmin)
        {
            int pendientes = await _db.Loans.CountAsync(
                l => l.UserId == destinatario && l.Status == LoanStatus.PendingApproval, ct);

            if (pendientes >= MaximoSolicitudesPendientes)
            {
                return Error(
                    $"Ya tienes {pendientes} solicitudes pendientes de aprobación. " +
                    "Espera a que se resuelvan antes de pedir más material.");
            }
        }

        await using var transaccion = await _db.Database.BeginTransactionAsync(ct);

        var prestamo = new Loan
        {
            UserId = destinatario,
            EstimatedReturnDate = peticion.EstimatedReturnDate,
            Reason = string.IsNullOrWhiteSpace(peticion.Reason) ? null : peticion.Reason.Trim(),
            RequestedAt = DateTime.UtcNow,
            Status = esAdmin ? LoanStatus.Active : LoanStatus.PendingApproval,
            LoanDate = esAdmin ? hoy : null,
            DecidedAt = esAdmin ? DateTime.UtcNow : null,
            DecidedByUserId = esAdmin ? this.UsuarioId() : null
        };

        ActionResult? fallo = await AnadirLineasAsync(prestamo, lineas, ct);

        if (fallo is not null)
        {
            return fallo;
        }

        _db.Loans.Add(prestamo);
        await _db.SaveChangesAsync(ct);

        // La auditoria va despues del primer guardado porque necesita el Id
        // asignado, pero dentro de la misma transaccion.
        _auditor.Registrar(
            esAdmin ? AuditActions.PrestamoAprobado : AuditActions.PrestamoSolicitado,
            "Loan", prestamo.Id,
            esAdmin
                ? $"Registrado directamente por un administrador para {usuario.Username}"
                : Texto.Contar(lineas.Count, "artículo", "artículos",
                               "solicitado", "solicitados"));

        await _db.SaveChangesAsync(ct);
        await transaccion.CommitAsync(ct);

        _log.LogInformation(
            "Prestamo {LoanId} creado para el usuario {UserId} en estado {Estado}",
            prestamo.Id, destinatario, prestamo.Status);

        Loan creado = await ConsultaBase().FirstAsync(l => l.Id == prestamo.Id, ct);

        return CreatedAtAction(nameof(Get), new { id = prestamo.Id }, creado.ToDto());
    }

    // ========================================================================
    // DECISION SOBRE LA SOLICITUD
    // ========================================================================

    /// <summary>Aprueba una solicitud pendiente. Requiere administrador.</summary>
    /// <response code="409">La solicitud ya no esta pendiente.</response>
    [HttpPost("{id:int}/approve")]
    [Authorize(Roles = Roles.Admin)]
    [ProducesResponseType(typeof(LoanDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<LoanDto>> Approve(
        int id, LoanDecisionRequest peticion, CancellationToken ct)
    {
        Loan? prestamo = await ConsultaEditable().FirstOrDefaultAsync(l => l.Id == id, ct);

        if (prestamo is null)
        {
            return NotFound();
        }

        if (!LoanTransitions.EsValida(prestamo.Status, LoanStatus.Active))
        {
            return Conflicto(prestamo.Status, "aprobar la solicitud");
        }

        await using var transaccion = await _db.Database.BeginTransactionAsync(ct);

        // Se vuelve a comprobar la disponibilidad. Entre la solicitud y la
        // aprobacion puede haberse reducido el total del articulo, y aprobar a
        // ciegas dejaria mas unidades fuera de las que existen. La reserva de
        // la propia solicitud se excluye del calculo para no contarla contra
        // si misma.
        ActionResult? fallo = await ComprobarDisponibilidadAsync(prestamo, ct);

        if (fallo is not null)
        {
            return fallo;
        }

        prestamo.Status = LoanStatus.Active;
        prestamo.LoanDate = DateOnly.FromDateTime(DateTime.UtcNow);
        prestamo.DecidedAt = DateTime.UtcNow;
        prestamo.DecidedByUserId = this.UsuarioId();
        prestamo.DecisionNote = Limpiar(peticion.Note);

        _auditor.Registrar(AuditActions.PrestamoAprobado, "Loan", prestamo.Id,
            Limpiar(peticion.Note));

        await _db.SaveChangesAsync(ct);
        await transaccion.CommitAsync(ct);

        _log.LogInformation("Prestamo {LoanId} aprobado", id);

        return Ok(await RecargarAsync(id, ct));
    }

    /// <summary>Rechaza una solicitud pendiente. Requiere administrador.</summary>
    [HttpPost("{id:int}/reject")]
    [Authorize(Roles = Roles.Admin)]
    [ProducesResponseType(typeof(LoanDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<LoanDto>> Reject(
        int id, LoanDecisionRequest peticion, CancellationToken ct)
    {
        Loan? prestamo = await ConsultaEditable().FirstOrDefaultAsync(l => l.Id == id, ct);

        if (prestamo is null)
        {
            return NotFound();
        }

        if (!LoanTransitions.EsValida(prestamo.Status, LoanStatus.Rejected))
        {
            return Conflicto(prestamo.Status, "rechazar la solicitud");
        }

        prestamo.Status = LoanStatus.Rejected;
        prestamo.DecidedAt = DateTime.UtcNow;
        prestamo.DecidedByUserId = this.UsuarioId();
        prestamo.DecisionNote = Limpiar(peticion.Note);

        _auditor.Registrar(AuditActions.PrestamoRechazado, "Loan", prestamo.Id,
            Limpiar(peticion.Note));

        await _db.SaveChangesAsync(ct);

        _log.LogInformation("Prestamo {LoanId} rechazado", id);

        return Ok(await RecargarAsync(id, ct));
    }

    // ========================================================================
    // DEVOLUCION
    // ========================================================================

    /// <summary>
    /// Solicita la devolucion de un prestamo activo. El propietario o un
    /// administrador.
    /// </summary>
    [HttpPost("{id:int}/request-return")]
    [ProducesResponseType(typeof(LoanDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<LoanDto>> RequestReturn(int id, CancellationToken ct)
    {
        Loan? prestamo = await ConsultaEditable().FirstOrDefaultAsync(l => l.Id == id, ct);

        if (prestamo is null)
        {
            return NotFound();
        }

        if (!PuedeVer(prestamo))
        {
            return Prohibido();
        }

        if (!LoanTransitions.EsValida(prestamo.Status, LoanStatus.ReturnRequested))
        {
            return Conflicto(prestamo.Status, "solicitar la devolución");
        }

        prestamo.Status = LoanStatus.ReturnRequested;
        prestamo.ReturnRequestedAt = DateTime.UtcNow;

        // Una solicitud de devolucion nueva borra la nota del rechazo anterior:
        // si no, el usuario seguiria viendo el motivo por el que se le denego
        // la vez pasada como si fuera actual.
        prestamo.ReturnDecisionNote = null;
        prestamo.ReturnDecidedAt = null;
        prestamo.ReturnDecidedByUserId = null;

        _auditor.Registrar(AuditActions.DevolucionSolicitada, "Loan", prestamo.Id);

        await _db.SaveChangesAsync(ct);

        return Ok(await RecargarAsync(id, ct));
    }

    /// <summary>Confirma una devolucion. Requiere administrador.</summary>
    [HttpPost("{id:int}/approve-return")]
    [Authorize(Roles = Roles.Admin)]
    [ProducesResponseType(typeof(LoanDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<LoanDto>> ApproveReturn(
        int id, LoanDecisionRequest peticion, CancellationToken ct)
    {
        Loan? prestamo = await ConsultaEditable().FirstOrDefaultAsync(l => l.Id == id, ct);

        if (prestamo is null)
        {
            return NotFound();
        }

        if (!LoanTransitions.EsValida(prestamo.Status, LoanStatus.Returned))
        {
            return Conflicto(prestamo.Status, "confirmar la devolución");
        }

        prestamo.Status = LoanStatus.Returned;
        prestamo.ReturnDate = DateOnly.FromDateTime(DateTime.UtcNow);
        prestamo.ReturnDecidedAt = DateTime.UtcNow;
        prestamo.ReturnDecidedByUserId = this.UsuarioId();
        prestamo.ReturnDecisionNote = Limpiar(peticion.Note);

        _auditor.Registrar(AuditActions.DevolucionAprobada, "Loan", prestamo.Id,
            Limpiar(peticion.Note));

        await _db.SaveChangesAsync(ct);

        _log.LogInformation("Devolucion del prestamo {LoanId} confirmada", id);

        return Ok(await RecargarAsync(id, ct));
    }

    /// <summary>
    /// Rechaza una devolucion: el material no ha vuelto o no esta completo.
    /// El prestamo regresa a activo. Requiere administrador.
    /// </summary>
    [HttpPost("{id:int}/reject-return")]
    [Authorize(Roles = Roles.Admin)]
    [ProducesResponseType(typeof(LoanDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<LoanDto>> RejectReturn(
        int id, LoanDecisionRequest peticion, CancellationToken ct)
    {
        Loan? prestamo = await ConsultaEditable().FirstOrDefaultAsync(l => l.Id == id, ct);

        if (prestamo is null)
        {
            return NotFound();
        }

        if (prestamo.Status != LoanStatus.ReturnRequested ||
            !LoanTransitions.EsValida(prestamo.Status, LoanStatus.Active))
        {
            return Conflicto(prestamo.Status, "rechazar la devolución");
        }

        prestamo.Status = LoanStatus.Active;
        prestamo.ReturnRequestedAt = null;
        prestamo.ReturnDecidedAt = DateTime.UtcNow;
        prestamo.ReturnDecidedByUserId = this.UsuarioId();
        prestamo.ReturnDecisionNote = Limpiar(peticion.Note);

        _auditor.Registrar(AuditActions.DevolucionRechazada, "Loan", prestamo.Id,
            Limpiar(peticion.Note));

        await _db.SaveChangesAsync(ct);

        return Ok(await RecargarAsync(id, ct));
    }

    /// <summary>
    /// Devolucion directa, sin pasar por solicitud.
    /// </summary>
    /// <remarks>
    /// Se conserva la ruta de la version anterior de la API porque hay clientes
    /// que ya la usan. El comportamiento depende de quien llama, y no por
    /// comodidad sino porque es lo que significa la accion en cada caso: un
    /// administrador da por devuelto el material porque lo tiene delante, y un
    /// usuario solo puede pedir que se lo den por devuelto.
    /// </remarks>
    [HttpPost("{id:int}/return")]
    [ProducesResponseType(typeof(LoanDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<LoanDto>> Return(int id, CancellationToken ct)
    {
        if (!this.EsAdministrador())
        {
            return await RequestReturn(id, ct);
        }

        Loan? prestamo = await ConsultaEditable().FirstOrDefaultAsync(l => l.Id == id, ct);

        if (prestamo is null)
        {
            return NotFound();
        }

        if (!LoanTransitions.EsValida(prestamo.Status, LoanStatus.Returned))
        {
            return Conflicto(prestamo.Status, "dar por devuelto");
        }

        prestamo.Status = LoanStatus.Returned;
        prestamo.ReturnDate = DateOnly.FromDateTime(DateTime.UtcNow);
        prestamo.ReturnDecidedAt = DateTime.UtcNow;
        prestamo.ReturnDecidedByUserId = this.UsuarioId();

        _auditor.Registrar(AuditActions.DevolucionAprobada, "Loan", prestamo.Id,
            "Devolución directa");

        await _db.SaveChangesAsync(ct);

        return Ok(await RecargarAsync(id, ct));
    }

    /// <summary>Elimina un prestamo del historial. Requiere administrador.</summary>
    [HttpDelete("{id:int}")]
    [Authorize(Roles = Roles.Admin)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        Loan? prestamo = await _db.Loans.FirstOrDefaultAsync(l => l.Id == id, ct);

        if (prestamo is null)
        {
            return NotFound();
        }

        // Las lineas caen en cascada por la configuracion del modelo. La
        // anotacion de auditoria sobrevive: no tiene clave ajena hacia el
        // prestamo justamente para que el rastro no desaparezca con el.
        _db.Loans.Remove(prestamo);

        _auditor.Registrar(AuditActions.PrestamoEliminado, "Loan", id,
            $"Estado en el momento de eliminarlo: {prestamo.Status}");

        await _db.SaveChangesAsync(ct);

        _log.LogWarning("Prestamo {LoanId} eliminado por {Admin}", id, User.Identity?.Name);

        return NoContent();
    }

    // ========================================================================
    // APOYO
    // ========================================================================

    /// <summary>
    /// Quien puede ver o tocar un prestamo: su propietario o un administrador.
    /// </summary>
    private bool PuedeVer(Loan prestamo) =>
        this.EsAdministrador() || prestamo.UserId == this.UsuarioId();

    /// <summary>
    /// Comprueba la disponibilidad y anade las lineas al prestamo.
    /// </summary>
    /// <returns>Un resultado de error, o null si todo cuadra.</returns>
    private async Task<ActionResult?> AnadirLineasAsync(
        Loan prestamo, IReadOnlyList<LineaSolicitada> solicitadas, CancellationToken ct)
    {
        List<int> ids = solicitadas.Select(l => l.MaterialId).ToList();

        Dictionary<int, Material> materiales = await _db.Materials
            .Where(m => ids.Contains(m.Id))
            .ToDictionaryAsync(m => m.Id, ct);

        Dictionary<int, int> comprometido = await ComprometidoAsync(ids, null, ct);

        foreach (var linea in solicitadas)
        {
            if (!materiales.TryGetValue(linea.MaterialId, out Material? material))
            {
                return Error($"El artículo {linea.MaterialId} no existe.");
            }

            int disponible = material.TotalQuantity - comprometido.GetValueOrDefault(material.Id);

            if (linea.Quantity > disponible)
            {
                return Error(
                    $"«{material.Name}»: se piden {linea.Quantity} unidades y solo hay " +
                    $"{Math.Max(0, disponible)} disponibles.");
            }

            prestamo.Lines.Add(new LoanLine
            {
                MaterialId = material.Id,
                Quantity = linea.Quantity
            });
        }

        return null;
    }

    /// <summary>
    /// Comprueba que las unidades de un prestamo pendiente siguen disponibles
    /// en el momento de aprobarlo.
    /// </summary>
    private async Task<ActionResult?> ComprobarDisponibilidadAsync(
        Loan prestamo, CancellationToken ct)
    {
        List<int> ids = prestamo.Lines.Select(l => l.MaterialId).ToList();

        Dictionary<int, Material> materiales = await _db.Materials
            .Where(m => ids.Contains(m.Id))
            .ToDictionaryAsync(m => m.Id, ct);

        // Se excluye el propio prestamo: sus unidades ya estan contadas como
        // reserva y restarlas otra vez lo haria imposible de aprobar.
        Dictionary<int, int> comprometido = await ComprometidoAsync(ids, prestamo.Id, ct);

        foreach (LoanLine linea in prestamo.Lines)
        {
            if (!materiales.TryGetValue(linea.MaterialId, out Material? material))
            {
                return Error("Alguno de los artículos solicitados ya no existe.");
            }

            int disponible = material.TotalQuantity - comprometido.GetValueOrDefault(material.Id);

            if (linea.Quantity > disponible)
            {
                return Error(
                    $"«{material.Name}»: ya no hay unidades suficientes. " +
                    $"Se solicitaron {linea.Quantity} y quedan {Math.Max(0, disponible)}.");
            }
        }

        return null;
    }

    /// <summary>
    /// Unidades comprometidas por artículo: entregadas y sin devolver, mas las
    /// reservadas por solicitudes pendientes.
    /// </summary>
    private async Task<Dictionary<int, int>> ComprometidoAsync(
        List<int> ids, int? excluirPrestamo, CancellationToken ct)
    {
        return await _db.LoanLines
            .Where(l => ids.Contains(l.MaterialId) &&
                        l.LoanId != excluirPrestamo &&
                        (l.Loan.Status == LoanStatus.Active ||
                         l.Loan.Status == LoanStatus.ReturnRequested ||
                         l.Loan.Status == LoanStatus.PendingApproval))
            .GroupBy(l => l.MaterialId)
            .Select(g => new { g.Key, Total = g.Sum(x => x.Quantity) })
            .ToDictionaryAsync(x => x.Key, x => x.Total, ct);
    }

    private async Task<LoanDto> RecargarAsync(int id, CancellationToken ct) =>
        (await ConsultaBase().FirstAsync(l => l.Id == id, ct)).ToDto();

    private IQueryable<Loan> ConsultaBase() => _db.Loans
        .AsNoTracking()
        .Include(l => l.User)
        .Include(l => l.DecidedBy)
        .Include(l => l.ReturnDecidedBy)
        .Include(l => l.Lines)
        .ThenInclude(l => l.Material);

    private IQueryable<Loan> ConsultaEditable() => _db.Loans
        .Include(l => l.User)
        .Include(l => l.Lines)
        .ThenInclude(l => l.Material);

    private static string? Limpiar(string? nota) =>
        string.IsNullOrWhiteSpace(nota) ? null : nota.Trim();

    private BadRequestObjectResult Error(string detalle) => BadRequest(new ProblemDetails
    {
        Title = "Préstamo no válido",
        Detail = detalle,
        Status = StatusCodes.Status400BadRequest
    });

    /// <summary>
    /// Transicion imposible. Se responde 409 y no 400 porque la peticion en si
    /// es correcta: lo que no encaja es el estado en el que esta el prestamo,
    /// normalmente porque alguien se ha adelantado.
    /// </summary>
    private ObjectResult Conflicto(LoanStatus estado, string accion) => Conflict(
        new ProblemDetails
        {
            Title = "Operación no disponible",
            Detail = $"No se puede {accion}: el préstamo está en estado «{Describir(estado)}».",
            Status = StatusCodes.Status409Conflict
        });

    private static string Describir(LoanStatus estado) => estado switch
    {
        LoanStatus.PendingApproval => "pendiente de aprobación",
        LoanStatus.Active => "activo",
        LoanStatus.Rejected => "rechazado",
        LoanStatus.ReturnRequested => "devolución solicitada",
        LoanStatus.Returned => "devuelto",
        _ => estado.ToString()
    };

    private ObjectResult Prohibido() => StatusCode(
        StatusCodes.Status403Forbidden,
        new ProblemDetails
        {
            Title = "Acceso denegado",
            Detail = "No tienes permiso para acceder a este préstamo.",
            Status = StatusCodes.Status403Forbidden
        });

    /// <summary>Linea ya agrupada, lista para comprobar disponibilidad.</summary>
    private readonly record struct LineaSolicitada(int MaterialId, int Quantity);
}
