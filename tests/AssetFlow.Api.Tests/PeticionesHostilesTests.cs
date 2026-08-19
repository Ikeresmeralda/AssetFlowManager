using System.Net;
using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using AssetFlow.Api.Dtos;
using Microsoft.AspNetCore.Http;

namespace AssetFlow.Api.Tests;

/// <summary>
/// Peticiones que no vienen del cliente y buscan romper algo.
/// </summary>
public class PeticionesHostilesTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _api;

    public PeticionesHostilesTests(ApiFactory api) => _api = api;

    /// <summary>
    /// EF Core parametriza las consultas, asi que estas cadenas se buscan
    /// literalmente en lugar de ejecutarse. Se comprueba que la API responde
    /// con normalidad y que despues las tablas siguen ahi.
    /// </summary>
    [Theory]
    [InlineData("' OR 1=1--")]
    [InlineData("'; DROP TABLE Users;--")]
    [InlineData("%")]
    [InlineData("\" OR \"\"=\"")]
    public async Task La_busqueda_no_es_vulnerable_a_inyeccion(string entrada)
    {
        HttpClient admin = await _api.ClienteAdminAsync();

        HttpResponseMessage busqueda =
            await admin.GetAsync($"/api/materials?buscar={Uri.EscapeDataString(entrada)}");

        busqueda.StatusCode.Should().Be(HttpStatusCode.OK);

        // Si algo se hubiera ejecutado, esta consulta fallaria.
        HttpResponseMessage cuentas = await admin.GetAsync("/api/users");
        cuentas.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// Un cuerpo desmedido debe rechazarse sin error interno.
    /// </summary>
    /// <remarks>
    /// Ojo con lo que demuestra este test: el limite de tamano lo aplica
    /// Kestrel, y estas pruebas corren sobre TestServer, que no es Kestrel. Lo
    /// que se fija aqui es que la peticion no acabe en 500 por ninguna via.
    ///
    /// El 413 propiamente dicho se comprobo lanzando la API real y enviandole
    /// 5 MB con curl: antes devolvia 500, porque el middleware de errores
    /// capturaba la BadHttpRequestException de Kestrel y la trataba como fallo
    /// interno, tapando el codigo correcto que la excepcion ya traia.
    /// </remarks>
    [Fact]
    public async Task Un_cuerpo_por_encima_del_limite_se_rechaza_sin_error_interno()
    {
        HttpClient admin = await _api.ClienteAdminAsync();

        string enorme = new('A', 3 * 1024 * 1024);
        var contenido = new StringContent(
            $$"""{"name":"{{enorme}}","type":"x","totalQuantity":1}""",
            Encoding.UTF8, "application/json");

        HttpResponseMessage respuesta =
            await admin.PostAsync("/api/materials", contenido);

        respuesta.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError,
            "un cuerpo demasiado grande es un problema de la peticion, no del servidor");

        ((int)respuesta.StatusCode).Should().BeOneOf(
            StatusCodes.Status413PayloadTooLarge,
            StatusCodes.Status400BadRequest);
    }

    [Fact]
    public async Task Un_json_malformado_se_rechaza_sin_revelar_internos()
    {
        HttpClient admin = await _api.ClienteAdminAsync();

        var roto = new StringContent("""{"name": "x", """, Encoding.UTF8, "application/json");

        HttpResponseMessage respuesta = await admin.PostAsync("/api/materials", roto);

        respuesta.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        string cuerpo = await respuesta.Content.ReadAsStringAsync();

        cuerpo.Should().NotContain("at Inventario.");
        cuerpo.Should().NotContain("StackTrace");
        cuerpo.Should().NotContain("C:\\");
    }

    /// <summary>
    /// El identificador del usuario se toma del token, nunca del cuerpo. Si se
    /// tomara del cuerpo, bastaria con enviar otro para actuar en nombre ajeno.
    /// </summary>
    [Fact]
    public async Task Los_campos_de_identidad_enviados_en_el_cuerpo_se_ignoran()
    {
        (int idNormal, HttpClient normal) = await _api.CrearCuentaAsync("manipulador");

        HttpClient admin = await _api.ClienteAdminAsync();

        MaterialDto material = (await (await admin.PostAsJsonAsync("/api/materials",
            new { name = "Material manipulado", type = "Prueba", totalQuantity = 5 }))
            .Content.ReadFromJsonAsync<MaterialDto>())!;

        // El usuario normal pide un prestamo declarando que es para el
        // administrador (identificador 1).
        HttpResponseMessage respuesta = await normal.PostAsJsonAsync("/api/loans", new
        {
            userId = 1,
            estimatedReturnDate = DateTime.UtcNow.AddDays(7).ToString("yyyy-MM-dd"),
            lines = new[] { new { materialId = material.Id, quantity = 1 } }
        });

        respuesta.StatusCode.Should().Be(HttpStatusCode.Created);

        LoanDto prestamo = (await respuesta.Content.ReadFromJsonAsync<LoanDto>())!;

        prestamo.UserId.Should().Be(idNormal,
            "el destinatario sale del token, no del campo userId del cuerpo");
    }

    /// <summary>
    /// La API no debe anunciar con que se ha construido: no ayuda a nadie
    /// salvo a quien busca versiones con fallos conocidos.
    /// </summary>
    [Fact]
    public async Task Las_respuestas_no_anuncian_el_servidor_y_traen_cabeceras_de_seguridad()
    {
        HttpResponseMessage respuesta = await _api.Cliente().GetAsync("/health");

        respuesta.Headers.Contains("Server").Should().BeFalse();
        respuesta.Headers.Contains("X-Powered-By").Should().BeFalse();

        respuesta.Headers.GetValues("X-Content-Type-Options").Should().Contain("nosniff");
        respuesta.Headers.GetValues("X-Frame-Options").Should().Contain("DENY");
        respuesta.Headers.GetValues("Referrer-Policy").Should().Contain("no-referrer");
    }
}
