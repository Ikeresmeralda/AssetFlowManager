using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using AssetFlow.Api.Dtos;

namespace AssetFlow.Api.Tests;

/// <summary>
/// Integridad del inventario: que ninguna operacion deje el stock descuadrado.
/// </summary>
public class ReglasDeNegocioTests : IClassFixture<EntornoConUsuarioNormal>
{
    private readonly EntornoConUsuarioNormal _api;

    public ReglasDeNegocioTests(EntornoConUsuarioNormal api) => _api = api;

    private static readonly string Devolucion =
        DateTime.UtcNow.AddDays(30).ToString("yyyy-MM-dd");

    private async Task<MaterialDto> CrearMaterialAsync(string nombre, int unidades)
    {
        HttpResponseMessage respuesta = await _api.Admin.PostAsJsonAsync("/api/materials",
            new { name = nombre, type = "Prueba", totalQuantity = unidades, lowStockThreshold = 1 });

        respuesta.StatusCode.Should().Be(HttpStatusCode.Created);

        return (await respuesta.Content.ReadFromJsonAsync<MaterialDto>())!;
    }

    // ============================================================
    // DISPONIBILIDAD
    // ============================================================

    /// <summary>
    /// La disponibilidad se deriva de los prestamos vivos, no se guarda como
    /// un contador que se va restando. Un contador acumula errores en cuanto
    /// una operacion falla a medias; esto no puede descuadrarse.
    /// </summary>
    [Fact]
    public async Task Prestar_reduce_lo_disponible_sin_tocar_el_total()
    {
        MaterialDto material = await CrearMaterialAsync("Disponibilidad", 3);

        material.AvailableQuantity.Should().Be(3);
        material.OnLoanQuantity.Should().Be(0);

        HttpResponseMessage prestamo = await _api.Admin.PostAsJsonAsync("/api/loans", new
        {
            estimatedReturnDate = Devolucion,
            reason = "Comprobacion de disponibilidad",
            lines = new[] { new { materialId = material.Id, quantity = 2 } }
        });

        prestamo.StatusCode.Should().Be(HttpStatusCode.Created);

        MaterialDto tras = (await _api.Admin
            .GetFromJsonAsync<MaterialDto>($"/api/materials/{material.Id}"))!;

        tras.TotalQuantity.Should().Be(3, "el total es lo que se posee, y no cambia al prestar");
        tras.OnLoanQuantity.Should().Be(2);
        tras.AvailableQuantity.Should().Be(1);
    }

    [Fact]
    public async Task No_se_pueden_prestar_mas_unidades_de_las_disponibles()
    {
        MaterialDto material = await CrearMaterialAsync("Sobregiro", 3);

        HttpResponseMessage respuesta = await _api.Admin.PostAsJsonAsync("/api/loans", new
        {
            estimatedReturnDate = Devolucion,
            lines = new[] { new { materialId = material.Id, quantity = 99 } }
        });

        respuesta.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task No_se_puede_bajar_el_total_por_debajo_de_lo_ya_prestado()
    {
        MaterialDto material = await CrearMaterialAsync("Recorte", 3);

        await _api.Admin.PostAsJsonAsync("/api/loans", new
        {
            estimatedReturnDate = Devolucion,
            lines = new[] { new { materialId = material.Id, quantity = 2 } }
        });

        HttpResponseMessage respuesta = await _api.Admin.PutAsJsonAsync(
            $"/api/materials/{material.Id}",
            new { name = "Recorte", type = "Prueba", totalQuantity = 1 });

        respuesta.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "dejaria el inventario con mas unidades fuera de las que existen");
    }

    [Fact]
    public async Task No_se_puede_borrar_material_con_prestamos_registrados()
    {
        MaterialDto material = await CrearMaterialAsync("Con historial", 2);

        await _api.Admin.PostAsJsonAsync("/api/loans", new
        {
            estimatedReturnDate = Devolucion,
            lines = new[] { new { materialId = material.Id, quantity = 1 } }
        });

        HttpResponseMessage respuesta =
            await _api.Admin.DeleteAsync($"/api/materials/{material.Id}");

        respuesta.StatusCode.Should().Be(HttpStatusCode.Conflict,
            "borrarlo dejaria prestamos apuntando a un articulo inexistente");
    }

    // ============================================================
    // DEVOLUCION
    // ============================================================

    [Fact]
    public async Task Devolver_libera_las_unidades_y_no_se_puede_hacer_dos_veces()
    {
        MaterialDto material = await CrearMaterialAsync("Devolucion", 4);

        LoanDto prestamo = (await (await _api.Admin.PostAsJsonAsync("/api/loans", new
        {
            estimatedReturnDate = Devolucion,
            lines = new[] { new { materialId = material.Id, quantity = 3 } }
        })).Content.ReadFromJsonAsync<LoanDto>())!;

        HttpResponseMessage primera =
            await _api.Admin.PostAsync($"/api/loans/{prestamo.Id}/return", null);

        primera.StatusCode.Should().Be(HttpStatusCode.OK);

        MaterialDto tras = (await _api.Admin
            .GetFromJsonAsync<MaterialDto>($"/api/materials/{material.Id}"))!;

        tras.AvailableQuantity.Should().Be(4, "al devolver, las unidades vuelven a estar libres");
        tras.OnLoanQuantity.Should().Be(0);

        HttpResponseMessage segunda =
            await _api.Admin.PostAsync($"/api/loans/{prestamo.Id}/return", null);

        segunda.StatusCode.Should().Be(HttpStatusCode.Conflict,
            "una segunda devolucion duplicaria las unidades disponibles");
    }

    // ============================================================
    // FECHAS
    // ============================================================

    [Fact]
    public async Task No_se_admite_una_fecha_de_devolucion_pasada()
    {
        MaterialDto material = await CrearMaterialAsync("Fecha pasada", 1);

        HttpResponseMessage respuesta = await _api.Admin.PostAsJsonAsync("/api/loans", new
        {
            estimatedReturnDate = DateTime.UtcNow.AddDays(-1).ToString("yyyy-MM-dd"),
            lines = new[] { new { materialId = material.Id, quantity = 1 } }
        });

        respuesta.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Un_prestamo_sin_lineas_se_rechaza()
    {
        HttpResponseMessage respuesta = await _api.Admin.PostAsJsonAsync("/api/loans", new
        {
            estimatedReturnDate = Devolucion,
            lines = Array.Empty<object>()
        });

        respuesta.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Un_prestamo_de_un_articulo_inexistente_se_rechaza()
    {
        HttpResponseMessage respuesta = await _api.Admin.PostAsJsonAsync("/api/loans", new
        {
            estimatedReturnDate = Devolucion,
            lines = new[] { new { materialId = 999_999, quantity = 1 } }
        });

        respuesta.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ============================================================
    // AISLAMIENTO ENTRE USUARIOS
    // ============================================================

    [Fact]
    public async Task Un_usuario_normal_no_ve_el_prestamo_de_otro()
    {
        MaterialDto material = await CrearMaterialAsync("Prestamo ajeno", 2);

        LoanDto prestamo = (await (await _api.Admin.PostAsJsonAsync("/api/loans", new
        {
            estimatedReturnDate = Devolucion,
            lines = new[] { new { materialId = material.Id, quantity = 1 } }
        })).Content.ReadFromJsonAsync<LoanDto>())!;

        HttpResponseMessage respuesta = await _api.Normal.GetAsync($"/api/loans/{prestamo.Id}");

        respuesta.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// Pedir el listado con el identificador de otro usuario no debe devolver
    /// sus prestamos. El parametro es una comodidad para el administrador; a
    /// un usuario normal se le ignora y se le devuelven los suyos.
    /// </summary>
    [Fact]
    public async Task El_filtro_por_usuario_no_sirve_para_espiar_a_otro()
    {
        MaterialDto material = await CrearMaterialAsync("Filtro por usuario", 4);

        // Un prestamo para el administrador y otro para el usuario normal, de
        // forma que la lista tenga algo que filtrar mal si el filtro fallara.
        await _api.Admin.PostAsJsonAsync("/api/loans", new
        {
            estimatedReturnDate = Devolucion,
            lines = new[] { new { materialId = material.Id, quantity = 1 } }
        });

        await _api.Admin.PostAsJsonAsync("/api/loans", new
        {
            userId = _api.IdNormal,
            estimatedReturnDate = Devolucion,
            lines = new[] { new { materialId = material.Id, quantity = 1 } }
        });

        List<LoanDto> propios = (await _api.Normal
            .GetFromJsonAsync<List<LoanDto>>("/api/loans?userId=1"))!;

        propios.Should().NotBeEmpty("el usuario tiene un prestamo a su nombre");

        propios.Should().OnlyContain(p => p.UserId == _api.IdNormal,
            "el servidor decide de quien son los prestamos que devuelve, no el parametro");
    }
}
