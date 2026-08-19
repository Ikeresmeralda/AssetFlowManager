using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using AssetFlow.Api.Dtos;

namespace AssetFlow.Api.Tests;

/// <summary>
/// Registro de auditoria: que se anota, quien puede leerlo y que nunca aparece.
/// </summary>
public class AuditoriaTests : IClassFixture<EntornoConDosUsuarios>
{
    private readonly EntornoConDosUsuarios _api;

    public AuditoriaTests(EntornoConDosUsuarios api) => _api = api;

    [Fact]
    public async Task Un_usuario_normal_no_puede_leer_la_auditoria()
    {
        HttpResponseMessage respuesta = await _api.ClienteAna.GetAsync("/api/audit");

        respuesta.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Sin_autenticar_tampoco()
    {
        HttpResponseMessage respuesta = await _api.Cliente().GetAsync("/api/audit");

        respuesta.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task El_inicio_de_sesion_queda_registrado()
    {
        var entorno = new EntornoConDosUsuarios();
        await entorno.InitializeAsync();

        try
        {
            HttpClient admin = await entorno.ClienteAdminAsync();

            AuditPageDto pagina = await Consultar(admin, "action=sesion.iniciada");

            pagina.Items.Should().NotBeEmpty();
            pagina.Items.Should().OnlyContain(e => e.Action == "sesion.iniciada");
        }
        finally
        {
            await entorno.DisposeAsync();
        }
    }

    [Fact]
    public async Task Las_decisiones_sobre_un_prestamo_quedan_registradas_con_su_autor()
    {
        HttpClient admin = await _api.ClienteAdminAsync();

        LoanDto prestamo = await _api.SolicitarAsync(_api.ClienteAna);
        await _api.DecidirAsync(admin, prestamo.Id, "approve");

        AuditPageDto pagina = await Consultar(
            admin, $"entityType=Loan&entityId={prestamo.Id}");

        pagina.Items.Should().Contain(e => e.Action == "prestamo.solicitado"
                                        && e.ActorUsername == "ana.prueba");

        pagina.Items.Should().Contain(e => e.Action == "prestamo.aprobado"
                                        && e.ActorUsername == ApiFactory.UsuarioAdmin);
    }

    [Fact]
    public async Task El_cambio_de_rol_deja_constancia_del_antes_y_el_despues()
    {
        var entorno = new EntornoConDosUsuarios();
        await entorno.InitializeAsync();

        try
        {
            HttpClient admin = await entorno.ClienteAdminAsync();

            HttpResponseMessage cambio = await admin.PutAsJsonAsync(
                $"/api/users/{entorno.IdAna}/access",
                new { role = "Admin", isActive = true });

            cambio.EnsureSuccessStatusCode();

            AuditPageDto pagina = await Consultar(admin, "action=usuario.acceso_modificado");

            AuditEntryDto entrada = pagina.Items.First(e => e.EntityId == entorno.IdAna);

            // Un registro que solo diga «acceso modificado» no permite
            // reconstruir una escalada de privilegios meses despues.
            entrada.Details.Should().Contain("User");
            entrada.Details.Should().Contain("Admin");
        }
        finally
        {
            await entorno.DisposeAsync();
        }
    }

    [Fact]
    public async Task La_auditoria_no_contiene_contrasenas_ni_tokens()
    {
        var entorno = new EntornoConDosUsuarios();
        await entorno.InitializeAsync();

        try
        {
            HttpClient admin = await entorno.ClienteAdminAsync();

            // Se ejercitan las acciones que manejan secretos, para que si
            // alguna los anotara aparecieran aqui.
            await admin.PostAsJsonAsync($"/api/users/{entorno.IdAna}/password",
                new { newPassword = "ReiniciadaPorAdmin2026!" });

            await entorno.Cliente().PostAsJsonAsync("/api/auth/forgot-password",
                new { email = "ana.prueba@ejemplo.local" });

            AuditPageDto pagina = await Consultar(admin, "pageSize=100");

            string todo = string.Join("\n", pagina.Items.Select(e =>
                $"{e.ActorUsername} {e.Action} {e.Details}"));

            todo.Should().NotContain(ApiFactory.ClaveDePrueba);
            todo.Should().NotContain(ApiFactory.ClaveAdmin);
            todo.Should().NotContain("ReiniciadaPorAdmin2026!");
            todo.Should().NotContain("$2a$", "un hash de contraseña tampoco pinta nada aquí");
            todo.Should().NotContain("eyJ", "ni el prefijo de un JWT");
        }
        finally
        {
            await entorno.DisposeAsync();
        }
    }

    [Fact]
    public async Task El_tamano_de_pagina_esta_acotado()
    {
        HttpClient admin = await _api.ClienteAdminAsync();

        // Sin tope, una sola peticion se lleva la tabla entera.
        AuditPageDto pagina = await Consultar(admin, "pageSize=100000");

        pagina.PageSize.Should().BeLessThanOrEqualTo(100);
        pagina.Items.Count.Should().BeLessThanOrEqualTo(100);
    }

    [Fact]
    public async Task No_existe_ninguna_forma_de_escribir_ni_borrar_la_auditoria()
    {
        HttpClient admin = await _api.ClienteAdminAsync();

        // Un registro que se pueda editar desde fuera no sirve como registro,
        // ni siquiera para un administrador.
        HttpResponseMessage alta = await admin.PostAsJsonAsync("/api/audit",
            new { action = "inventada", details = "no deberia entrar" });

        HttpResponseMessage baja = await admin.DeleteAsync("/api/audit/1");

        alta.StatusCode.Should().BeOneOf(
            HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed);

        baja.StatusCode.Should().BeOneOf(
            HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed);
    }

    private static async Task<AuditPageDto> Consultar(HttpClient cliente, string consulta) =>
        (await cliente.GetFromJsonAsync<AuditPageDto>($"/api/audit?{consulta}"))!;
}
