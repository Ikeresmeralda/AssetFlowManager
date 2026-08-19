using System.Net;
using FluentAssertions;

namespace AssetFlow.Api.Tests;

/// <summary>
/// Comprobaciones de que la aplicacion levanta y siembra correctamente.
/// </summary>
public class ArranqueTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _api;

    public ArranqueTests(ApiFactory api) => _api = api;

    [Fact]
    public async Task La_sonda_de_vida_responde_sin_autenticar()
    {
        HttpResponseMessage respuesta = await _api.Cliente().GetAsync("/health");

        respuesta.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task El_administrador_sembrado_puede_acceder()
    {
        var sesion = await _api.AccederAsync(ApiFactory.UsuarioAdmin, ApiFactory.ClaveAdmin);

        sesion.AccessToken.Should().NotBeNullOrWhiteSpace();
        sesion.User.Role.Should().Be("Admin");
    }
}
