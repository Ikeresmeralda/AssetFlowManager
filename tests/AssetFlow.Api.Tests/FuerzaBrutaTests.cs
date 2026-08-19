using System.Net;
using System.Net.Http.Json;
using FluentAssertions;

namespace AssetFlow.Api.Tests;

/// <summary>
/// Proteccion contra el probado masivo de contrasenas.
/// </summary>
/// <remarks>
/// Clase aparte, y por tanto instancia de API aparte, porque agota el
/// presupuesto del limitador a proposito. Compartirla con el resto de la
/// bateria haria fallar tests ajenos con 429.
/// </remarks>
public class FuerzaBrutaTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _api;

    public FuerzaBrutaTests(ApiFactory api) => _api = api;

    /// <summary>
    /// El limitador reparte por origen y permite 10 intentos por ventana. Se
    /// lanzan 14 seguidos y se comprueba que en algun momento deja de atender.
    /// Sin esto, probar contrasenas contra la API sale gratis.
    /// </summary>
    [Fact]
    public async Task Un_martilleo_del_acceso_acaba_recibiendo_429()
    {
        HttpClient cliente = _api.Cliente();
        var codigos = new List<HttpStatusCode>();

        for (int intento = 0; intento < 14; intento++)
        {
            HttpResponseMessage respuesta = await cliente.PostAsJsonAsync("/api/auth/login",
                new { username = "objetivo.ficticio", password = $"intento{intento}" });

            codigos.Add(respuesta.StatusCode);
        }

        codigos.Should().Contain(HttpStatusCode.TooManyRequests,
            "tras superar el limite por ventana la API debe dejar de atender intentos");

        codigos.Should().StartWith([HttpStatusCode.Unauthorized],
            "los primeros intentos si se atienden: el limite no puede ser tan agresivo " +
            "que impida a una persona equivocarse al teclear");
    }

    /// <summary>
    /// La respuesta de rechazo tampoco puede convertirse en una fuga: debe
    /// decir que hay demasiadas peticiones, no si la cuenta existe.
    /// </summary>
    [Fact]
    public async Task El_rechazo_por_exceso_no_revela_si_la_cuenta_existe()
    {
        HttpClient cliente = _api.Cliente();
        HttpResponseMessage? rechazo = null;

        for (int intento = 0; intento < 14 && rechazo is null; intento++)
        {
            HttpResponseMessage respuesta = await cliente.PostAsJsonAsync("/api/auth/login",
                new { username = ApiFactory.UsuarioAdmin, password = $"intento{intento}" });

            if (respuesta.StatusCode == HttpStatusCode.TooManyRequests)
            {
                rechazo = respuesta;
            }
        }

        rechazo.Should().NotBeNull("el limitador debe haber saltado");

        string cuerpo = (await rechazo!.Content.ReadAsStringAsync()).ToLowerInvariant();

        cuerpo.Should().NotContain("admin");
        cuerpo.Should().NotContain("no existe");
        cuerpo.Should().NotContain("contrasena incorrecta");
    }
}
