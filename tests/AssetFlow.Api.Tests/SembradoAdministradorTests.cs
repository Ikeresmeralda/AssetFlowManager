using AssetFlow.Api.Data;
using AssetFlow.Api.Data.Providers;
using AssetFlow.Api.Entities;
using AssetFlow.Api.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace AssetFlow.Api.Tests;

/// <summary>
/// Comprueba el comportamiento de <see cref="DbInitializer"/> frente a
/// arranques sucesivos sobre la misma base de datos: justo lo que ocurre en
/// un redeploy con disco persistente, o simplemente al reiniciar el proceso.
/// </summary>
public class SembradoAdministradorTests
{
    [Fact]
    public async Task Si_Seed_AdminPassword_cambia_la_cuenta_admin_existente_adopta_la_nueva_clave()
    {
        string archivoBd = RutaTemporal();

        try
        {
            await ArrancarYSembrarAsync(archivoBd, "ClaveInicial-2026!");

            User antes = await ObtenerAdminAsync(archivoBd);
            int token = await CrearRefreshTokenVivoAsync(archivoBd, antes.Id);

            // Segundo arranque: la base de datos ya tiene al administrador,
            // pero la variable de entorno (aqui, la configuracion) trae una
            // clave distinta, tal como pasa al cambiarla en el panel de Render
            // y volver a desplegar.
            await ArrancarYSembrarAsync(archivoBd, "ClaveNueva-2026!");

            User despues = await ObtenerAdminAsync(archivoBd);
            var hasher = Hasher();

            hasher.Verify("ClaveNueva-2026!", despues.PasswordHash).Should().BeTrue(
                "el segundo arranque configuro una contrasena distinta y debe sustituir a la anterior");
            hasher.Verify("ClaveInicial-2026!", despues.PasswordHash).Should().BeFalse();

            (await RevocadoEnAsync(archivoBd, token)).Should().NotBeNull(
                "una sesion abierta con la contrasena anterior no debe seguir siendo valida");
        }
        finally
        {
            BorrarSiExiste(archivoBd);
        }
    }

    [Fact]
    public async Task Si_Seed_AdminPassword_esta_vacia_en_un_arranque_posterior_no_se_toca_nada()
    {
        string archivoBd = RutaTemporal();

        try
        {
            await ArrancarYSembrarAsync(archivoBd, "ClaveInicial-2026!");

            User antes = await ObtenerAdminAsync(archivoBd);
            int token = await CrearRefreshTokenVivoAsync(archivoBd, antes.Id);

            // Escenario habitual: el operador retiro la variable tras el
            // primer arranque, tal como recomienda la documentacion. No debe
            // borrar ni tocar la contrasena que ya tiene la cuenta.
            await ArrancarYSembrarAsync(archivoBd, claveAdmin: null);

            User despues = await ObtenerAdminAsync(archivoBd);

            despues.PasswordHash.Should().Be(antes.PasswordHash);
            despues.MustChangePassword.Should().Be(antes.MustChangePassword);
            (await RevocadoEnAsync(archivoBd, token)).Should().BeNull(
                "sin una clave nueva configurada no hay motivo para cerrar sesiones abiertas");
        }
        finally
        {
            BorrarSiExiste(archivoBd);
        }
    }

    [Fact]
    public async Task Si_Seed_AdminPassword_coincide_con_la_actual_no_se_rehashea_ni_se_revocan_sesiones()
    {
        string archivoBd = RutaTemporal();
        const string Clave = "ClaveSinCambios-2026!";

        try
        {
            await ArrancarYSembrarAsync(archivoBd, Clave);

            User antes = await ObtenerAdminAsync(archivoBd);
            int token = await CrearRefreshTokenVivoAsync(archivoBd, antes.Id);

            // Escenario habitual: el operador deja la variable puesta con el
            // mismo valor. Un re-hash aqui (bcrypt genera un salt distinto en
            // cada llamada) seria trabajo perdido, y revocar sesiones sin que
            // la contrasena haya cambiado de verdad cerraria la sesion de
            // todo el mundo en cada simple reinicio del servicio.
            await ArrancarYSembrarAsync(archivoBd, Clave);

            User despues = await ObtenerAdminAsync(archivoBd);

            despues.PasswordHash.Should().Be(antes.PasswordHash);
            (await RevocadoEnAsync(archivoBd, token)).Should().BeNull();
        }
        finally
        {
            BorrarSiExiste(archivoBd);
        }
    }

    private static string RutaTemporal() =>
        Path.Combine(Path.GetTempPath(), $"sembrado-tests-{Guid.NewGuid():N}.db");

    private static void BorrarSiExiste(string archivoBd)
    {
        if (File.Exists(archivoBd))
        {
            File.Delete(archivoBd);
        }
    }

    private static BCryptPasswordHasher Hasher() =>
        new(Options.Create(new PasswordHashingOptions { WorkFactor = 4 }));

    private static AssetFlowDbContext CrearContexto(string archivoBd)
    {
        DbContextOptions<SqliteAssetFlowDbContext> opciones = new DbContextOptionsBuilder<SqliteAssetFlowDbContext>()
            .UseSqlite($"Data Source={archivoBd};Pooling=False")
            .Options;

        return new SqliteAssetFlowDbContext(opciones);
    }

    private static async Task<User> ObtenerAdminAsync(string archivoBd)
    {
        using AssetFlowDbContext db = CrearContexto(archivoBd);
        return await db.Users.AsNoTracking().SingleAsync(u => u.Username == "admin");
    }

    private static async Task<int> CrearRefreshTokenVivoAsync(string archivoBd, int userId)
    {
        using AssetFlowDbContext db = CrearContexto(archivoBd);

        var token = new RefreshToken
        {
            UserId = userId,
            TokenHash = $"hash-de-prueba-{Guid.NewGuid():N}",
            ExpiresAt = DateTime.UtcNow.AddDays(7)
        };

        db.RefreshTokens.Add(token);
        await db.SaveChangesAsync();

        return token.Id;
    }

    private static async Task<DateTime?> RevocadoEnAsync(string archivoBd, int tokenId)
    {
        using AssetFlowDbContext db = CrearContexto(archivoBd);
        RefreshToken token = await db.RefreshTokens.AsNoTracking().SingleAsync(t => t.Id == tokenId);
        return token.RevokedAt;
    }

    private static async Task ArrancarYSembrarAsync(string archivoBd, string? claveAdmin)
    {
        WebApplicationBuilder constructor = WebApplication.CreateBuilder();
        constructor.Environment.EnvironmentName = Environments.Production;

        constructor.Configuration["Seed:AdminPassword"] = claveAdmin;

        constructor.Services.AddDbContext<SqliteAssetFlowDbContext>(o =>
            o.UseSqlite($"Data Source={archivoBd};Pooling=False"));
        constructor.Services.AddScoped<AssetFlowDbContext>(sp =>
            sp.GetRequiredService<SqliteAssetFlowDbContext>());

        constructor.Services.Configure<PasswordHashingOptions>(o => o.WorkFactor = 4);
        constructor.Services.AddScoped<IPasswordHasher, BCryptPasswordHasher>();

        await using WebApplication app = constructor.Build();

        await DbInitializer.InicializarAsync(app);
    }
}
