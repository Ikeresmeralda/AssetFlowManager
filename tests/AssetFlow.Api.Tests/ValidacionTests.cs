using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;

namespace AssetFlow.Api.Tests;

/// <summary>
/// La entrada del cliente no se cree nunca.
/// </summary>
/// <remarks>
/// El cliente de escritorio valida los formularios antes de enviarlos, pero
/// eso solo mejora la experiencia de quien usa la aplicacion. Estas peticiones
/// se construyen a mano, saltandose el cliente, que es como llegarian de un
/// atacante.
/// </remarks>
public class ValidacionTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _api;

    public ValidacionTests(ApiFactory api) => _api = api;

    public static TheoryData<string, object> MaterialesInvalidos => new()
    {
        { "cantidad negativa", new { name = "X", type = "Y", totalQuantity = -5 } },
        { "nombre vacio", new { name = "", type = "Y", totalQuantity = 1 } },
        { "nombre solo espacios", new { name = "   ", type = "Y", totalQuantity = 1 } },
        { "cantidad desorbitada", new { name = "X", type = "Y", totalQuantity = 999_999_999 } },
        { "tipo ausente", new { name = "X", totalQuantity = 1 } },
        { "umbral negativo", new { name = "X", type = "Y", totalQuantity = 1, lowStockThreshold = -1 } },
    };

    [Theory]
    [MemberData(nameof(MaterialesInvalidos))]
    public async Task Un_alta_de_material_invalida_se_rechaza(string caso, object cuerpo)
    {
        HttpClient admin = await _api.ClienteAdminAsync();

        HttpResponseMessage respuesta = await admin.PostAsJsonAsync("/api/materials", cuerpo);

        respuesta.StatusCode.Should().Be(HttpStatusCode.BadRequest, "caso: {0}", caso);
    }

    /// <summary>
    /// Todos los errores comparten formato (RFC 7807) para que el cliente solo
    /// tenga que saber interpretar uno.
    /// </summary>
    /// <remarks>
    /// Se comprueba el cuerpo y no la cabecera Content-Type. MVC rotula esta
    /// respuesta como application/json aunque el resultado pida
    /// application/problem+json: el formateador de System.Text.Json encaja el
    /// tipo por su comodin application/*+json y lo reescribe con su tipo
    /// canonico. Es una desviacion del estandar en la etiqueta, no en el
    /// contenido, y forzarla obliga a manipular la negociacion de contenido de
    /// MVC, que es mucho mas fragil que el problema que resuelve. Lo que
    /// consume cualquier cliente es el cuerpo, y eso es lo que se fija aqui.
    /// </remarks>
    [Fact]
    public async Task Los_errores_de_validacion_usan_el_formato_de_problema_estandar()
    {
        HttpClient admin = await _api.ClienteAdminAsync();

        HttpResponseMessage respuesta = await admin.PostAsJsonAsync("/api/materials",
            new { name = "", type = "", totalQuantity = -1 });

        respuesta.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        JsonElement problema = await respuesta.Content.ReadFromJsonAsync<JsonElement>();

        problema.TryGetProperty("title", out _).Should().BeTrue();
        problema.TryGetProperty("status", out JsonElement estado).Should().BeTrue();
        estado.GetInt32().Should().Be(400);

        problema.TryGetProperty("errors", out JsonElement errores).Should().BeTrue(
            "un error de validacion debe decir que campos fallan");

        errores.EnumerateObject().Select(c => c.Name)
            .Should().Contain(["Name", "Type", "TotalQuantity"]);
    }

    public static TheoryData<string, object> CuentasInvalidas => new()
    {
        {
            "correo con formato incorrecto",
            new { username = "x.uno", firstName = "A", lastName = "B",
                  email = "esto-no-es-un-correo", password = ApiFactory.ClaveDePrueba, role = "User" }
        },
        {
            "contrasena demasiado corta",
            new { username = "x.dos", firstName = "A", lastName = "B",
                  email = "b@ejemplo.local", password = "corta", role = "User" }
        },
        {
            "rol inventado",
            new { username = "x.tres", firstName = "A", lastName = "B",
                  email = "c@ejemplo.local", password = ApiFactory.ClaveDePrueba, role = "Superusuario" }
        },
        {
            "usuario vacio",
            new { username = "", firstName = "A", lastName = "B",
                  email = "d@ejemplo.local", password = ApiFactory.ClaveDePrueba, role = "User" }
        },
    };

    [Theory]
    [MemberData(nameof(CuentasInvalidas))]
    public async Task Un_alta_de_cuenta_invalida_se_rechaza(string caso, object cuerpo)
    {
        HttpClient admin = await _api.ClienteAdminAsync();

        HttpResponseMessage respuesta = await admin.PostAsJsonAsync("/api/users", cuerpo);

        respuesta.StatusCode.Should().Be(HttpStatusCode.BadRequest, "caso: {0}", caso);
    }

    /// <summary>
    /// El rol no es texto libre. Si "Superusuario" se aceptara y quedara
    /// guardado, cualquier comprobacion posterior por rol dejaria de tener
    /// sentido.
    /// </summary>
    [Fact]
    public async Task Un_rol_inventado_no_llega_a_crearse()
    {
        HttpClient admin = await _api.ClienteAdminAsync();

        await admin.PostAsJsonAsync("/api/users", new
        {
            username = "rol.inventado",
            firstName = "A",
            lastName = "B",
            email = "rol@ejemplo.local",
            password = ApiFactory.ClaveDePrueba,
            role = "Superusuario"
        });

        string cuentas = await (await admin.GetAsync("/api/users")).Content.ReadAsStringAsync();

        cuentas.Should().NotContain("rol.inventado");
    }
}
