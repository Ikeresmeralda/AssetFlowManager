using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using AssetFlow.Api.Dtos;

namespace AssetFlow.Api.Tests;

/// <summary>
/// Flujo de aprobacion de prestamos y devoluciones.
/// </summary>
/// <remarks>
/// Estos tests atacan la API directamente, sin pasar por el cliente WPF, que
/// es exactamente lo que haria alguien que quisiera saltarse las restricciones
/// de la interfaz. Lo que se comprueba no es que la aplicacion oculte botones,
/// sino que el servidor rechace la peticion aunque el boton no exista.
/// </remarks>
public class FlujoPrestamosTests : IClassFixture<EntornoConDosUsuarios>, IAsyncLifetime
{
    private readonly EntornoConDosUsuarios _api;

    public FlujoPrestamosTests(EntornoConDosUsuarios api) => _api = api;

    /// <summary>
    /// Cada test arranca sin préstamos previos. Las cuentas sí se comparten:
    /// crearlas cuesta un acceso, y el limitador de intentos de la propia
    /// aplicación se agotaría a mitad de la clase.
    /// </summary>
    public Task InitializeAsync() => _api.LimpiarPrestamosAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    // ========================================================================
    // MAQUINA DE ESTADOS
    // ========================================================================

    [Fact]
    public async Task Un_usuario_normal_crea_la_solicitud_en_estado_pendiente()
    {
        LoanDto prestamo = await _api.SolicitarAsync(_api.ClienteAna);

        prestamo.Status.Should().Be("PendingApproval");

        // Sin aprobar no hay entrega, y por tanto no hay fecha de entrega.
        prestamo.LoanDate.Should().BeNull();
    }

    [Fact]
    public async Task Un_administrador_registra_el_prestamo_ya_activo()
    {
        HttpClient admin = await _api.ClienteAdminAsync();

        LoanDto prestamo = await _api.SolicitarAsync(admin);

        prestamo.Status.Should().Be("Active");
        prestamo.LoanDate.Should().NotBeNull();
    }

    [Fact]
    public async Task El_ciclo_completo_recorre_los_estados_en_orden()
    {
        HttpClient admin = await _api.ClienteAdminAsync();

        LoanDto prestamo = await _api.SolicitarAsync(_api.ClienteAna);
        prestamo.Status.Should().Be("PendingApproval");

        prestamo = await _api.DecidirAsync(admin, prestamo.Id, "approve");
        prestamo.Status.Should().Be("Active");
        prestamo.LoanDate.Should().NotBeNull();
        prestamo.DecidedByName.Should().NotBeNullOrWhiteSpace();

        HttpResponseMessage pedida = await _api.ClienteAna
            .PostAsync($"/api/loans/{prestamo.Id}/request-return", null);

        pedida.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Leer(pedida)).Status.Should().Be("ReturnRequested");

        prestamo = await _api.DecidirAsync(admin, prestamo.Id, "approve-return");
        prestamo.Status.Should().Be("Returned");
        prestamo.ReturnDate.Should().NotBeNull();
    }

    [Fact]
    public async Task Una_solicitud_no_se_puede_aprobar_dos_veces()
    {
        HttpClient admin = await _api.ClienteAdminAsync();

        LoanDto prestamo = await _api.SolicitarAsync(_api.ClienteAna);

        await _api.DecidirAsync(admin, prestamo.Id, "approve");

        HttpResponseMessage segunda = await admin.PostAsJsonAsync(
            $"/api/loans/{prestamo.Id}/approve", new { note = (string?)null });

        // 409 y no 400: la peticion es correcta, el estado es el que ya no
        // admite esa transicion.
        segunda.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Una_solicitud_rechazada_no_admite_ninguna_transicion()
    {
        HttpClient admin = await _api.ClienteAdminAsync();

        LoanDto prestamo = await _api.SolicitarAsync(_api.ClienteAna);
        await _api.DecidirAsync(admin, prestamo.Id, "reject");

        foreach (string accion in new[] { "approve", "approve-return", "reject-return" })
        {
            HttpResponseMessage respuesta = await admin.PostAsJsonAsync(
                $"/api/loans/{prestamo.Id}/{accion}", new { note = (string?)null });

            respuesta.StatusCode.Should().Be(HttpStatusCode.Conflict,
                $"un préstamo rechazado no admite «{accion}»");
        }
    }

    [Fact]
    public async Task Rechazar_una_devolucion_devuelve_el_prestamo_a_activo()
    {
        HttpClient admin = await _api.ClienteAdminAsync();

        LoanDto prestamo = await _api.SolicitarAsync(_api.ClienteAna);
        await _api.DecidirAsync(admin, prestamo.Id, "approve");

        await _api.ClienteAna.PostAsync($"/api/loans/{prestamo.Id}/request-return", null);

        prestamo = await _api.DecidirAsync(admin, prestamo.Id, "reject-return",
            "Falta el cable de alimentación");

        prestamo.Status.Should().Be("Active");
        prestamo.ReturnDecisionNote.Should().Be("Falta el cable de alimentación");
    }

    // ========================================================================
    // AISLAMIENTO ENTRE USUARIOS
    // ========================================================================

    [Fact]
    public async Task Un_usuario_no_ve_los_prestamos_de_otro_en_el_listado()
    {
        await _api.SolicitarAsync(_api.ClienteAna);
        await _api.SolicitarAsync(_api.ClienteBruno);

        List<LoanDto> deAna = await _api.ClienteAna
            .GetFromJsonAsync<List<LoanDto>>("/api/loans") ?? [];

        deAna.Should().NotBeEmpty();
        deAna.Should().OnlyContain(p => p.UserId == _api.IdAna);
    }

    [Fact]
    public async Task El_parametro_userId_no_sirve_para_ver_los_prestamos_de_otro()
    {
        LoanDto deBruno = await _api.SolicitarAsync(_api.ClienteBruno);

        // Ana necesita tener algo suyo: si no, una respuesta vacia no
        // distinguiria «el servidor filtra bien» de «el servidor no ha
        // encontrado nada por cualquier otro motivo».
        LoanDto deAna = await _api.SolicitarAsync(_api.ClienteAna);

        // Manipulacion directa del parametro, ignorando por completo la
        // interfaz: es lo primero que probaria alguien con las herramientas
        // de desarrollo abiertas.
        List<LoanDto> respuesta = await _api.ClienteAna
            .GetFromJsonAsync<List<LoanDto>>($"/api/loans?userId={_api.IdBruno}") ?? [];

        respuesta.Should().Contain(p => p.Id == deAna.Id,
            "el servidor ignora userId y devuelve los propios");

        respuesta.Should().NotContain(p => p.Id == deBruno.Id);
        respuesta.Should().OnlyContain(p => p.UserId == _api.IdAna);
    }

    [Fact]
    public async Task Un_usuario_no_puede_leer_el_prestamo_de_otro_por_su_identificador()
    {
        LoanDto deBruno = await _api.SolicitarAsync(_api.ClienteBruno);

        HttpResponseMessage respuesta =
            await _api.ClienteAna.GetAsync($"/api/loans/{deBruno.Id}");

        respuesta.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Un_usuario_no_puede_leer_el_historial_del_prestamo_de_otro()
    {
        LoanDto deBruno = await _api.SolicitarAsync(_api.ClienteBruno);

        HttpResponseMessage respuesta =
            await _api.ClienteAna.GetAsync($"/api/loans/{deBruno.Id}/history");

        respuesta.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Un_usuario_no_puede_pedir_la_devolucion_del_prestamo_de_otro()
    {
        HttpClient admin = await _api.ClienteAdminAsync();

        LoanDto deBruno = await _api.SolicitarAsync(_api.ClienteBruno);
        await _api.DecidirAsync(admin, deBruno.Id, "approve");

        HttpResponseMessage respuesta = await _api.ClienteAna
            .PostAsync($"/api/loans/{deBruno.Id}/request-return", null);

        respuesta.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ========================================================================
    // ESCALADA DE PRIVILEGIOS
    // ========================================================================

    [Theory]
    [InlineData("approve")]
    [InlineData("reject")]
    [InlineData("approve-return")]
    [InlineData("reject-return")]
    public async Task Un_usuario_normal_no_puede_decidir_ni_sobre_su_propio_prestamo(string accion)
    {
        LoanDto propio = await _api.SolicitarAsync(_api.ClienteAna);

        HttpResponseMessage respuesta = await _api.ClienteAna.PostAsJsonAsync(
            $"/api/loans/{propio.Id}/{accion}", new { note = (string?)null });

        // Aprobarse a uno mismo la solicitud es la escalada mas evidente:
        // convertiria el flujo de aprobacion en un tramite decorativo.
        respuesta.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Un_usuario_normal_no_puede_consultar_las_solicitudes_pendientes()
    {
        HttpResponseMessage respuesta = await _api.ClienteAna.GetAsync("/api/loans/pending");

        respuesta.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Un_usuario_normal_no_puede_eliminar_prestamos_del_historial()
    {
        LoanDto propio = await _api.SolicitarAsync(_api.ClienteAna);

        HttpResponseMessage respuesta =
            await _api.ClienteAna.DeleteAsync($"/api/loans/{propio.Id}");

        respuesta.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task El_campo_userId_del_cuerpo_no_permite_prestar_en_nombre_de_otro()
    {
        // Asignacion masiva: se envia el identificador de Bruno desde la sesion
        // de Ana. Si el servidor lo aceptara, cualquiera podria endosar
        // prestamos a nombre de otra persona.
        LoanDto creado = await _api.SolicitarAsync(_api.ClienteAna, paraUsuario: _api.IdBruno);

        creado.UserId.Should().Be(_api.IdAna);
    }

    // ========================================================================
    // REGLAS DE NEGOCIO
    // ========================================================================

    [Fact]
    public async Task Las_solicitudes_pendientes_reservan_unidades()
    {
        HttpClient admin = await _api.ClienteAdminAsync();

        MaterialDto antes = await _api.MaterialAsync(admin);

        await _api.SolicitarAsync(_api.ClienteAna, materialId: antes.Id, cantidad: 2);

        MaterialDto despues = await _api.MaterialAsync(admin, antes.Id);

        despues.ReservedQuantity.Should().Be(antes.ReservedQuantity + 2);
        despues.OnLoanQuantity.Should().Be(antes.OnLoanQuantity,
            "una solicitud pendiente todavía no ha salido del almacén");
        despues.AvailableQuantity.Should().Be(antes.AvailableQuantity - 2,
            "lo reservado deja de poder prometerse a nadie más");
    }

    [Fact]
    public async Task Rechazar_una_solicitud_libera_las_unidades_reservadas()
    {
        HttpClient admin = await _api.ClienteAdminAsync();

        MaterialDto antes = await _api.MaterialAsync(admin);

        LoanDto prestamo = await _api.SolicitarAsync(
            _api.ClienteAna, materialId: antes.Id, cantidad: 2);

        await _api.DecidirAsync(admin, prestamo.Id, "reject");

        MaterialDto despues = await _api.MaterialAsync(admin, antes.Id);

        despues.ReservedQuantity.Should().Be(antes.ReservedQuantity);
        despues.AvailableQuantity.Should().Be(antes.AvailableQuantity);
    }

    [Fact]
    public async Task No_se_pueden_acumular_solicitudes_pendientes_sin_limite()
    {
        // Sin tope, una sola cuenta puede dejar el inventario entero reservado
        // con solicitudes que nadie va a aprobar.
        var entorno = new EntornoConDosUsuarios();
        await entorno.InitializeAsync();

        try
        {
            HttpResponseMessage ultima = null!;

            for (int intento = 0; intento < 7; intento++)
            {
                ultima = await entorno.SolicitarRespuestaAsync(entorno.ClienteAna);

                if (ultima.StatusCode == HttpStatusCode.BadRequest)
                {
                    break;
                }
            }

            ultima.StatusCode.Should().Be(HttpStatusCode.BadRequest);

            string cuerpo = await ultima.Content.ReadAsStringAsync();
            cuerpo.Should().Contain("pendientes");
        }
        finally
        {
            await entorno.DisposeAsync();
        }
    }

    [Fact]
    public async Task El_historial_registra_quien_decidio_y_cuando()
    {
        HttpClient admin = await _api.ClienteAdminAsync();

        LoanDto prestamo = await _api.SolicitarAsync(_api.ClienteAna);
        await _api.DecidirAsync(admin, prestamo.Id, "approve", "Material disponible");

        List<LoanHistoryEntryDto> historial = await _api.ClienteAna
            .GetFromJsonAsync<List<LoanHistoryEntryDto>>(
                $"/api/loans/{prestamo.Id}/history") ?? [];

        historial.Should().Contain(e => e.Action == "prestamo.solicitado");
        historial.Should().Contain(e => e.Action == "prestamo.aprobado");

        historial.Should().OnlyContain(e => !string.IsNullOrWhiteSpace(e.ActorName));
    }

    private static async Task<LoanDto> Leer(HttpResponseMessage respuesta) =>
        (await respuesta.Content.ReadFromJsonAsync<LoanDto>())!;
}
