using System.Net.Http.Json;
using AssetFlow.Api.Data;
using AssetFlow.Api.Dtos;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AssetFlow.Api.Tests;

/// <summary>
/// API con un administrador y dos usuarios normales distintos.
/// </summary>
/// <remarks>
/// Hacen falta dos cuentas normales para poder comprobar lo importante: que
/// una no llega a los datos de la otra. Con una sola no se puede distinguir
/// «el servidor filtra por propietario» de «el servidor devuelve todo y da la
/// casualidad de que solo hay un propietario».
/// </remarks>
public class EntornoConDosUsuarios : ApiFactory
{
    public HttpClient ClienteAna { get; private set; } = null!;

    public HttpClient ClienteBruno { get; private set; } = null!;

    public int IdAna { get; private set; }

    public int IdBruno { get; private set; }

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();

        (IdAna, ClienteAna) = await CrearCuentaAsync("ana.prueba");
        (IdBruno, ClienteBruno) = await CrearCuentaAsync("bruno.prueba");
    }

    /// <summary>
    /// Vacía la tabla de préstamos entre tests.
    /// </summary>
    /// <remarks>
    /// Las cuentas se comparten en toda la clase, pero los préstamos no pueden
    /// compartirse: existe un tope de solicitudes pendientes por usuario, así
    /// que sin este borrado el sexto test de la clase empezaría a recibir un
    /// 400 por acumulación y el fallo parecería del código en lugar de la
    /// batería.
    ///
    /// Se borra por el contexto y no por la API a propósito: es preparación
    /// del escenario, no una operación bajo prueba. Hacerlo con endpoints
    /// obligaría a resolver cada préstamo con su transición válida, que es
    /// justo lo que los tests deben comprobar y no dar por hecho.
    /// </remarks>
    public async Task LimpiarPrestamosAsync()
    {
        using IServiceScope ambito = Services.CreateScope();
        var db = ambito.ServiceProvider.GetRequiredService<AssetFlowDbContext>();

        db.LoanLines.RemoveRange(await db.LoanLines.ToListAsync());
        db.Loans.RemoveRange(await db.Loans.ToListAsync());

        await db.SaveChangesAsync();
    }

    /// <summary>Un artículo del inventario sembrado, para usarlo de cobaya.</summary>
    public async Task<MaterialDto> MaterialAsync(HttpClient cliente, int? id = null)
    {
        if (id is not null)
        {
            return (await cliente.GetFromJsonAsync<MaterialDto>($"/api/materials/{id}"))!;
        }

        List<MaterialDto> materiales =
            await cliente.GetFromJsonAsync<List<MaterialDto>>("/api/materials") ?? [];

        return materiales.First(m => m.AvailableQuantity >= 3);
    }

    /// <summary>Crea una solicitud y devuelve la respuesta sin interpretarla.</summary>
    public async Task<HttpResponseMessage> SolicitarRespuestaAsync(
        HttpClient cliente, int? materialId = null, int cantidad = 1, int? paraUsuario = null)
    {
        int material = materialId ?? (await MaterialAsync(await ClienteAdminAsync())).Id;

        return await cliente.PostAsJsonAsync("/api/loans", new
        {
            userId = paraUsuario,
            estimatedReturnDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)),
            reason = "Prueba automatizada",
            lines = new[] { new { materialId = material, quantity = cantidad } }
        });
    }

    /// <summary>Crea una solicitud y exige que salga bien.</summary>
    public async Task<LoanDto> SolicitarAsync(
        HttpClient cliente, int? materialId = null, int cantidad = 1, int? paraUsuario = null)
    {
        HttpResponseMessage respuesta =
            await SolicitarRespuestaAsync(cliente, materialId, cantidad, paraUsuario);

        respuesta.EnsureSuccessStatusCode();

        return (await respuesta.Content.ReadFromJsonAsync<LoanDto>())!;
    }

    /// <summary>Ejecuta una decisión administrativa y exige que salga bien.</summary>
    public async Task<LoanDto> DecidirAsync(
        HttpClient cliente, int prestamoId, string accion, string? nota = null)
    {
        HttpResponseMessage respuesta = await cliente.PostAsJsonAsync(
            $"/api/loans/{prestamoId}/{accion}", new { note = nota });

        respuesta.EnsureSuccessStatusCode();

        return (await respuesta.Content.ReadFromJsonAsync<LoanDto>())!;
    }
}
