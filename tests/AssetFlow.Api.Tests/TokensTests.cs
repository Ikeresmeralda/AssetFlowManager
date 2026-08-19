using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using AssetFlow.Api.Dtos;

namespace AssetFlow.Api.Tests;

/// <summary>
/// Integridad de los tokens y rotacion del token de refresco.
/// </summary>
/// <remarks>
/// Va en su propia clase, y por tanto con su propia instancia de la API, para
/// no compartir el presupuesto del limitador con el resto: el endpoint de
/// refresco esta sujeto a la misma politica que el de acceso.
/// </remarks>
public class TokensTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _api;

    public TokensTests(ApiFactory api) => _api = api;

    [Theory]
    [InlineData("esto.no.es.un.token")]
    [InlineData("")]
    [InlineData("Bearer")]
    public async Task Un_token_con_formato_invalido_no_da_acceso(string token)
    {
        HttpResponseMessage respuesta = await _api.ClienteCon(token).GetAsync("/api/materials");

        respuesta.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// Se altera la firma dejando la cabecera y el contenido intactos. Es el
    /// ataque que funciona cuando el servidor decodifica el token sin
    /// verificarlo, o cuando acepta el algoritmo "none".
    /// </summary>
    [Fact]
    public async Task Un_token_con_la_firma_manipulada_no_da_acceso()
    {
        var sesion = await _api.AccederAsync(ApiFactory.UsuarioAdmin, ApiFactory.ClaveAdmin);

        string original = sesion.AccessToken;

        // Se altera el PRIMER caracter de la firma, no el ultimo.
        //
        // La version anterior de este test cambiaba el ultimo, y fallaba de
        // forma intermitente en el 3 % de las ejecuciones. La firma HMAC-SHA256
        // son 256 bits y sus 43 caracteres Base64URL codifican 258: el ultimo
        // caracter solo aporta 4 bits utiles, y los 2 de menor peso son relleno
        // que se descarta al decodificar. Como 'A' es 000000 y 'B' es 000001,
        // se diferencian unicamente en un bit de relleno, asi que cuando la
        // firma terminaba en 'A' o en 'B' la supuesta manipulacion no cambiaba
        // ni un byte de la firma real: el token seguia siendo valido y la API
        // respondia 200 con toda la razon.
        //
        // El primer caracter de la firma si aporta sus 6 bits, de modo que
        // cambiarlo altera la firma decodificada siempre.
        int inicioFirma = original.LastIndexOf('.') + 1;
        char primero = original[inicioFirma];
        char sustituto = primero == 'x' ? 'y' : 'x';

        string manipulado = original[..inicioFirma] + sustituto + original[(inicioFirma + 1)..];

        manipulado.Should().NotBe(original);

        HttpResponseMessage respuesta = await _api.ClienteCon(manipulado).GetAsync("/api/materials");

        respuesta.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Un_token_de_refresco_inventado_se_rechaza()
    {
        HttpResponseMessage respuesta = await _api.Cliente()
            .PostAsJsonAsync("/api/auth/refresh", new { refreshToken = "me-lo-acabo-de-inventar" });

        respuesta.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// El token de refresco es de un solo uso. Al canjearlo se emite uno nuevo
    /// y el anterior queda invalidado; si el antiguo vuelve a aparecer es que
    /// alguien lo ha copiado, y la respuesta es cerrar todas las sesiones de
    /// esa cuenta en lugar de seguir sirviendo tokens.
    /// </summary>
    [Fact]
    public async Task El_token_de_refresco_rota_y_no_admite_reutilizacion()
    {
        var sesion = await _api.AccederAsync(ApiFactory.UsuarioAdmin, ApiFactory.ClaveAdmin);

        HttpResponseMessage primera = await _api.Cliente()
            .PostAsJsonAsync("/api/auth/refresh", new { refreshToken = sesion.RefreshToken });

        primera.StatusCode.Should().Be(HttpStatusCode.OK);

        var renovada = (await primera.Content.ReadFromJsonAsync<AuthResponse>())!;

        renovada.RefreshToken.Should().NotBe(sesion.RefreshToken,
            "el token de refresco debe rotar en cada canje");

        HttpResponseMessage segunda = await _api.Cliente()
            .PostAsJsonAsync("/api/auth/refresh", new { refreshToken = sesion.RefreshToken });

        segunda.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "reutilizar un token ya canjeado indica robo y debe rechazarse");

        // La reutilizacion revoca toda la cadena, incluido el token nuevo que
        // se acababa de emitir: si no fuera asi, el atacante seguiria dentro.
        HttpResponseMessage tercera = await _api.Cliente()
            .PostAsJsonAsync("/api/auth/refresh", new { refreshToken = renovada.RefreshToken });

        tercera.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "detectada la reutilizacion, tambien caen las sesiones derivadas");
    }
}
