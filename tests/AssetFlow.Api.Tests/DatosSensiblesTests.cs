using System.Net.Http.Json;
using FluentAssertions;

namespace AssetFlow.Api.Tests;

/// <summary>
/// Lo que la API no debe contar nunca.
/// </summary>
/// <remarks>
/// Se inspecciona el JSON en crudo, no un objeto ya deserializado. Un DTO
/// tipado solo ensena los campos que el test conoce: si manana alguien
/// devolviera la entidad de base de datos en lugar del DTO, la propiedad
/// PasswordHash viajaria al cliente y una comprobacion sobre el tipo no se
/// enteraria. Sobre el texto, si.
/// </remarks>
public class DatosSensiblesTests : IClassFixture<EntornoConUsuarioNormal>
{
    private readonly EntornoConUsuarioNormal _api;

    public DatosSensiblesTests(EntornoConUsuarioNormal api) => _api = api;

    /// <summary>
    /// "$2a$" y "$2b$" son el prefijo de un hash BCrypt: si aparece, es que se
    /// esta enviando el hash aunque el campo no se llame "password".
    /// </summary>
    private static readonly string[] Terminos =
    [
        "password", "passwordhash", "hash", "salt", "$2a$", "$2b$", "dni",
        "refreshtoken", "accesstoken"
    ];

    public static TheoryData<string> Prohibidos => [.. Terminos];

    [Theory]
    [MemberData(nameof(Prohibidos))]
    public async Task El_listado_de_cuentas_no_expone_datos_sensibles(string prohibido)
    {
        string cuerpo = await (await _api.Admin.GetAsync("/api/users"))
            .Content.ReadAsStringAsync();

        cuerpo.Should().NotBeEmpty("el listado debe traer al menos el administrador");

        cuerpo.ToLowerInvariant().Should().NotContain(prohibido,
            "ningun endpoint publico puede devolver credenciales ni material para descifrarlas");
    }

    [Theory]
    [MemberData(nameof(Prohibidos))]
    public async Task La_ficha_de_una_cuenta_no_expone_datos_sensibles(string prohibido)
    {
        string cuerpo = await (await _api.Admin.GetAsync($"/api/users/{_api.IdNormal}"))
            .Content.ReadAsStringAsync();

        cuerpo.ToLowerInvariant().Should().NotContain(prohibido);
    }

    /// <summary>
    /// La respuesta del acceso si contiene tokens, obviamente. Lo que no puede
    /// contener es nada relativo a la contrasena.
    /// </summary>
    [Fact]
    public async Task La_respuesta_de_acceso_no_devuelve_nada_de_la_contrasena()
    {
        HttpResponseMessage respuesta = await _api.Cliente().PostAsJsonAsync(
            "/api/auth/login",
            new { username = ApiFactory.UsuarioAdmin, password = ApiFactory.ClaveAdmin });

        string cuerpo = (await respuesta.Content.ReadAsStringAsync()).ToLowerInvariant();

        cuerpo.Should().NotContain("passwordhash");
        cuerpo.Should().NotContain("salt");
        cuerpo.Should().NotContain("$2a$");
        cuerpo.Should().NotContain(ApiFactory.ClaveAdmin.ToLowerInvariant(),
            "la contrasena enviada no debe rebotar en la respuesta");
    }

    /// <summary>
    /// Barrido de los endpoints anadidos con el flujo de aprobacion.
    /// </summary>
    /// <remarks>
    /// Se recorren todos de golpe en lugar de escribir un test por ruta porque
    /// lo que se comprueba es identico en todos: que el JSON en crudo no lleve
    /// credenciales. Anadir una ruta a la lista cuesta una linea, y esa es la
    /// forma de que un endpoint nuevo no se quede fuera del barrido.
    /// </remarks>
    [Fact]
    public async Task Los_endpoints_del_flujo_de_prestamos_no_exponen_datos_sensibles()
    {
        string[] rutas =
        [
            "/api/loans",
            "/api/loans/pending",
            "/api/materials",
            "/api/users/summary",
            "/api/audit?pageSize=50"
        ];

        foreach (string ruta in rutas)
        {
            string cuerpo = (await (await _api.Admin.GetAsync(ruta))
                .Content.ReadAsStringAsync()).ToLowerInvariant();

            foreach (string prohibido in Terminos)
            {
                cuerpo.Should().NotContain(prohibido,
                    $"«{ruta}» no puede devolver «{prohibido}»");
            }

            // Especifico de la recuperacion: el hash del codigo tampoco sale
            // por ningun sitio, ni siquiera para un administrador.
            cuerpo.Should().NotContain("tokenhash");
            cuerpo.Should().NotContain("resettoken");
        }
    }

    /// <summary>
    /// Un error interno no puede convertirse en un mapa del servidor. Se pide
    /// una ruta inexistente y se comprueba que no salen rastros de pila,
    /// nombres de clase ni rutas de archivos.
    /// </summary>
    [Fact]
    public async Task Los_errores_no_revelan_detalles_internos()
    {
        HttpResponseMessage respuesta = await _api.Admin.GetAsync("/api/materials/999999");

        string cuerpo = await respuesta.Content.ReadAsStringAsync();

        cuerpo.Should().NotContain("Microsoft.EntityFrameworkCore");
        cuerpo.Should().NotContain("StackTrace");
        cuerpo.Should().NotContain("C:\\");
        cuerpo.Should().NotContain("SELECT ");
    }
}
