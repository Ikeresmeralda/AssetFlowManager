using System.Net;
using System.Net.Http.Json;
using FluentAssertions;

namespace AssetFlow.Api.Tests;

/// <summary>
/// Puerta de entrada: que no se pueda pasar sin credenciales y que fallar no
/// revele mas de lo debido.
/// </summary>
public class AutenticacionTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _api;

    public AutenticacionTests(ApiFactory api) => _api = api;

    /// <summary>
    /// La lista cubre lectura, escritura y borrado en los tres recursos. Es la
    /// comprobacion que sostiene todo lo demas: la politica por defecto exige
    /// usuario autenticado, de modo que un endpoint nuevo nace protegido y hay
    /// que abrirlo a proposito, no al reves.
    /// </summary>
    [Theory]
    [InlineData("GET", "/api/users")]
    [InlineData("GET", "/api/materials")]
    [InlineData("GET", "/api/loans")]
    [InlineData("POST", "/api/materials")]
    [InlineData("POST", "/api/loans")]
    [InlineData("PUT", "/api/materials/1")]
    [InlineData("DELETE", "/api/users/1")]
    [InlineData("DELETE", "/api/materials/1")]
    [InlineData("GET", "/api/auth/me")]
    public async Task Sin_token_todo_responde_401(string metodo, string ruta)
    {
        var peticion = new HttpRequestMessage(new HttpMethod(metodo), ruta);

        if (metodo is "POST" or "PUT")
        {
            peticion.Content = JsonContent.Create(new { });
        }

        HttpResponseMessage respuesta = await _api.Cliente().SendAsync(peticion);

        respuesta.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "ningun recurso de negocio debe atenderse sin credenciales");
    }

    [Fact]
    public async Task La_sonda_de_vida_es_el_unico_recurso_anonimo()
    {
        HttpResponseMessage respuesta = await _api.Cliente().GetAsync("/health");

        respuesta.StatusCode.Should().Be(HttpStatusCode.OK);

        // No revela version, rutas ni estado interno.
        string cuerpo = await respuesta.Content.ReadAsStringAsync();
        cuerpo.Should().Be("""{"status":"ok"}""");
    }

    [Fact]
    public async Task Una_contrasena_incorrecta_no_abre_sesion()
    {
        HttpResponseMessage respuesta = await _api.Cliente()
            .PostAsJsonAsync("/api/auth/login",
                new { username = ApiFactory.UsuarioAdmin, password = "estaNoEsLaBuena" });

        respuesta.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// Un usuario inexistente y una contrasena incorrecta deben responder
    /// exactamente igual. Si difirieran, la API serviria para averiguar que
    /// cuentas existen antes de atacarlas.
    /// </summary>
    [Fact]
    public async Task Un_usuario_inexistente_responde_igual_que_una_clave_incorrecta()
    {
        HttpResponseMessage claveMala = await _api.Cliente()
            .PostAsJsonAsync("/api/auth/login",
                new { username = ApiFactory.UsuarioAdmin, password = "estaNoEsLaBuena" });

        HttpResponseMessage usuarioInexistente = await _api.Cliente()
            .PostAsJsonAsync("/api/auth/login",
                new { username = "nadie.de.aqui", password = "estaNoEsLaBuena" });

        usuarioInexistente.StatusCode.Should().Be(claveMala.StatusCode);

        string cuerpoUno = await claveMala.Content.ReadAsStringAsync();
        string cuerpoDos = await usuarioInexistente.Content.ReadAsStringAsync();

        cuerpoDos.Should().Be(cuerpoUno,
            "el mensaje tampoco puede distinguir un caso del otro");
    }

    [Fact]
    public async Task Una_peticion_de_acceso_mal_formada_se_rechaza_con_400()
    {
        HttpResponseMessage respuesta = await _api.Cliente()
            .PostAsJsonAsync("/api/auth/login", new { username = "a", password = "" });

        respuesta.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Las_credenciales_correctas_devuelven_una_sesion_utilizable()
    {
        var sesion = await _api.AccederAsync(ApiFactory.UsuarioAdmin, ApiFactory.ClaveAdmin);

        sesion.AccessToken.Should().NotBeNullOrWhiteSpace();
        sesion.RefreshToken.Should().NotBeNullOrWhiteSpace();
        sesion.User.Username.Should().Be(ApiFactory.UsuarioAdmin);
        sesion.User.Role.Should().Be("Admin");

        sesion.AccessTokenExpiresAt.Should().BeAfter(DateTime.UtcNow);
        sesion.AccessTokenExpiresAt.Should().BeBefore(DateTime.UtcNow.AddHours(1),
            "un token de acceso de vida larga alarga la ventana de un robo");

        HttpResponseMessage yo = await _api.ClienteCon(sesion.AccessToken).GetAsync("/api/auth/me");
        yo.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
