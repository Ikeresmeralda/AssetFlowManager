using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using AssetFlow.Api.Dtos;

namespace AssetFlow.Api.Tests;

/// <summary>
/// Autorizacion real en el servidor: por rol y por propiedad del recurso.
/// </summary>
/// <remarks>
/// Estas comprobaciones son el nucleo del encargo. El cliente de escritorio
/// oculta los botones que un usuario normal no puede usar, pero eso es una
/// comodidad de interfaz, no una defensa: un atacante no abre la aplicacion,
/// llama a la API directamente. Aqui se comprueba exactamente eso, hablando
/// con la API sin pasar por el cliente.
/// </remarks>
public class AutorizacionTests : IClassFixture<EntornoConUsuarioNormal>
{
    private readonly EntornoConUsuarioNormal _api;

    private HttpClient _admin => _api.Admin;
    private HttpClient _normal => _api.Normal;
    private int _idNormal => _api.IdNormal;

    public AutorizacionTests(EntornoConUsuarioNormal api) => _api = api;

    // ============================================================
    // ROL
    // ============================================================

    [Fact]
    public async Task Un_usuario_normal_no_puede_listar_las_cuentas()
    {
        HttpResponseMessage respuesta = await _normal.GetAsync("/api/users");

        respuesta.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Un_usuario_normal_no_puede_dar_de_alta_material()
    {
        HttpResponseMessage respuesta = await _normal.PostAsJsonAsync("/api/materials",
            new { name = "Intruso", type = "Prueba", totalQuantity = 1 });

        respuesta.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Un_usuario_normal_no_puede_borrar_material()
    {
        HttpResponseMessage respuesta = await _normal.DeleteAsync("/api/materials/1");

        respuesta.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Un_usuario_normal_si_puede_consultar_el_inventario()
    {
        HttpResponseMessage respuesta = await _normal.GetAsync("/api/materials");

        respuesta.StatusCode.Should().Be(HttpStatusCode.OK,
            "consultar el material es precisamente para lo que existe la cuenta");
    }

    // ============================================================
    // ESCALADA DE PRIVILEGIOS
    // ============================================================

    [Fact]
    public async Task Un_usuario_normal_no_puede_crear_un_administrador()
    {
        HttpResponseMessage respuesta = await _normal.PostAsJsonAsync("/api/users", new
        {
            username = "colado",
            firstName = "X",
            lastName = "Y",
            email = "colado@ejemplo.local",
            password = ApiFactory.ClaveDePrueba,
            role = "Admin"
        });

        respuesta.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Un_usuario_normal_no_puede_ascenderse_a_si_mismo()
    {
        HttpResponseMessage respuesta = await _normal.PutAsJsonAsync(
            $"/api/users/{_idNormal}/access", new { role = "Admin", isActive = true });

        respuesta.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "el rol lo decide el servidor, nunca un campo enviado por el cliente");
    }

    /// <summary>
    /// El alta la hace un administrador, pero el rol solicitado no puede ser
    /// lo unico que decida el resultado si quien pide no tiene permiso. Aqui
    /// se comprueba el caso legitimo para dejar constancia de que el rol se
    /// aplica desde el servidor y se refleja en la lectura.
    /// </summary>
    [Fact]
    public async Task El_rol_asignado_por_un_administrador_se_respeta()
    {
        (int id, _) = await _api.CrearCuentaAsync("otra.administradora", rol: "Admin");

        UserDto? cuenta = await _admin.GetFromJsonAsync<UserDto>($"/api/users/{id}");

        cuenta.Should().NotBeNull();
        cuenta!.Role.Should().Be("Admin");
    }

    // ============================================================
    // IDOR: CAMBIAR EL IDENTIFICADOR DE LA URL
    // ============================================================

    [Fact]
    public async Task Un_usuario_normal_no_puede_leer_la_ficha_de_otro()
    {
        // El administrador sembrado es el identificador 1.
        HttpResponseMessage respuesta = await _normal.GetAsync("/api/users/1");

        respuesta.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Un_usuario_normal_si_puede_leer_su_propia_ficha()
    {
        HttpResponseMessage respuesta = await _normal.GetAsync($"/api/users/{_idNormal}");

        respuesta.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Un_usuario_normal_no_puede_editar_la_ficha_de_otro()
    {
        HttpResponseMessage respuesta = await _normal.PutAsJsonAsync("/api/users/1", new
        {
            firstName = "Secuestrado",
            lastName = "Por otro",
            email = "atacante@ejemplo.local"
        });

        respuesta.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Un_usuario_normal_no_puede_borrar_cuentas()
    {
        HttpResponseMessage respuesta = await _normal.DeleteAsync("/api/users/1");

        respuesta.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
