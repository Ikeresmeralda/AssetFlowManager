using AssetFlow.Api.Entities;
using AssetFlow.Api.Security;
using AssetFlow.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace AssetFlow.Api.Data;

/// <summary>
/// Aplica las migraciones pendientes y crea el administrador inicial.
/// </summary>
/// <remarks>
/// El problema del administrador inicial es el clasico del huevo y la
/// gallina: hace falta un administrador para crear cuentas, pero no se puede
/// crear sin uno. Las salidas habituales son un endpoint publico de registro
/// (que deja la puerta abierta) o una contrasena escrita en el codigo (que
/// esta publicada en el repositorio).
///
/// Aqui la contrasena inicial se lee de la configuracion. Si no se ha
/// definido, en desarrollo se genera una aleatoria y se escribe en la consola
/// una unica vez, y en produccion la aplicacion se niega a arrancar. En
/// ningun caso existe una contrasena por defecto conocida.
///
/// Seed:AdminPassword tambien se comprueba en los arranques siguientes, no
/// solo cuando la base de datos esta vacia. Sin esto, cambiar la variable de
/// entorno en un servicio ya sembrado (por ejemplo, tras redesplegar en
/// Render con el mismo disco) no tenia ningun efecto: el administrador se
/// quedaba con la contrasena de la primera vez para siempre, y la variable
/// pasaba a mentir sobre cual es la contrasena real.
/// </remarks>
public static class DbInitializer
{
    public static async Task InicializarAsync(WebApplication app)
    {
        using IServiceScope ambito = app.Services.CreateScope();

        var db = ambito.ServiceProvider.GetRequiredService<AssetFlowDbContext>();
        var hasher = ambito.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var log = ambito.ServiceProvider.GetRequiredService<ILogger<Program>>();
        var config = ambito.ServiceProvider.GetRequiredService<IConfiguration>();

        await db.Database.MigrateAsync();

        string usuario = config["Seed:AdminUsername"] ?? "admin";
        string? clave = config["Seed:AdminPassword"];

        if (await db.Users.AnyAsync())
        {
            await SincronizarClaveAdminAsync(db, hasher, log, usuario, clave);
            return;
        }

        bool generada = false;

        if (string.IsNullOrWhiteSpace(clave))
        {
            if (!app.Environment.IsDevelopment())
            {
                throw new InvalidOperationException(
                    "No hay ningun usuario en la base de datos y no se ha configurado " +
                    "Seed:AdminPassword. Define la contrasena del administrador inicial " +
                    "antes de arrancar en produccion. Ver docs/configuration.md.");
            }

            clave = GenerarClave();
            generada = true;
        }

        db.Users.Add(new User
        {
            Username = usuario,
            FirstName = "Administrador",
            LastName = "del sistema",
            Email = config["Seed:AdminEmail"] ?? "admin@inventario.local",
            PasswordHash = hasher.Hash(clave),
            Role = Roles.Admin
        });

        if (app.Environment.IsDevelopment())
        {
            SembrarInventarioDeEjemplo(db);
        }

        await db.SaveChangesAsync();

        if (generada)
        {
            // Se escribe una sola vez, al crear la cuenta, y solo en
            // desarrollo. No pasa por el sistema de registro para que no
            // acabe en un archivo de log.
            Console.WriteLine();
            Console.WriteLine("========================================================");
            Console.WriteLine(" Administrador inicial creado");
            Console.WriteLine($"   usuario:    {usuario}");
            Console.WriteLine($"   contrasena: {clave}");
            Console.WriteLine(" Anotala: no se volvera a mostrar.");
            Console.WriteLine("========================================================");
            Console.WriteLine();
        }

        log.LogInformation("Administrador inicial {Username} creado", usuario);
    }

    /// <summary>
    /// Pone al dia la contrasena del administrador sembrado cuando ya existe
    /// una cuenta con ese nombre de usuario y Seed:AdminPassword trae un valor
    /// que no coincide con la actual.
    /// </summary>
    /// <remarks>
    /// No toca nada si Seed:AdminPassword esta vacia (asi el operador puede
    /// quitar la variable tras el primer arranque sin que eso borre la
    /// contrasena que el administrador haya puesto desde la aplicacion) ni si
    /// ya coincide con la que hay guardada, que es el caso normal en la
    /// inmensa mayoria de arranques.
    /// </remarks>
    private static async Task SincronizarClaveAdminAsync(
        AssetFlowDbContext db, IPasswordHasher hasher, ILogger log, string usuario, string? clave)
    {
        if (string.IsNullOrWhiteSpace(clave))
        {
            return;
        }

        User? admin = await db.Users.FirstOrDefaultAsync(u => u.Username == usuario);

        if (admin is null)
        {
            log.LogWarning(
                "Seed:AdminPassword esta definida pero no existe ninguna cuenta {Username}; se ignora",
                usuario);
            return;
        }

        if (hasher.Verify(clave, admin.PasswordHash))
        {
            return;
        }

        admin.PasswordHash = hasher.Hash(clave);
        admin.MustChangePassword = false;
        admin.ProvisionalPasswordExpiresAt = null;

        // Igual que un reinicio de contrasena desde la aplicacion: una sesion
        // abierta con la contrasena anterior no debe seguir siendo valida.
        var vivos = await db.RefreshTokens
            .Where(t => t.UserId == admin.Id && t.RevokedAt == null)
            .ToListAsync();

        foreach (var token in vivos)
        {
            token.RevokedAt = DateTime.UtcNow;
        }

        await db.SaveChangesAsync();

        log.LogWarning(
            "Contrasena de {Username} actualizada al arrancar segun Seed:AdminPassword",
            usuario);
    }

    private static string GenerarClave()
    {
        // Sin caracteres ambiguos (l, I, 1, O, 0): la contrasena se lee de una
        // consola y se teclea a mano.
        const string Alfabeto = "abcdefghijkmnopqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789";

        return string.Concat(System.Security.Cryptography.RandomNumberGenerator
            .GetItems<char>(Alfabeto, 16));
    }

    private static void SembrarInventarioDeEjemplo(AssetFlowDbContext db)
    {
        db.Materials.AddRange(
            new Material { Name = "Proyector Epson EB-S41", Type = "Audiovisual", Publisher = "Epson", TotalQuantity = 3, LowStockThreshold = 1 },
            new Material { Name = "Pantalla de proyección 200\"", Type = "Audiovisual", TotalQuantity = 2, LowStockThreshold = 1 },
            new Material { Name = "Altavoz portátil JBL", Type = "Audiovisual", Publisher = "JBL", TotalQuantity = 6, LowStockThreshold = 2 },
            new Material { Name = "Mesa de sonido Yamaha MG10", Type = "Audiovisual", Publisher = "Yamaha", TotalQuantity = 2, LowStockThreshold = 1 },
            new Material { Name = "Micrófono inalámbrico", Type = "Audiovisual", Publisher = "Shure", TotalQuantity = 2, LowStockThreshold = 1 },
            new Material { Name = "Máquina de humo", Type = "Audiovisual", TotalQuantity = 2, LowStockThreshold = 1 },
            new Material { Name = "Mesa plegable 180 cm", Type = "Mobiliario", TotalQuantity = 24, LowStockThreshold = 6 },
            new Material { Name = "Silla plegable", Type = "Mobiliario", TotalQuantity = 120, LowStockThreshold = 20 },
            new Material { Name = "Carpa 3x3 m", Type = "Mobiliario", TotalQuantity = 4, LowStockThreshold = 1 },
            new Material { Name = "Tarima modular 1x1 m", Type = "Mobiliario", TotalQuantity = 20, LowStockThreshold = 5 },
            new Material { Name = "Barra de bar plegable", Type = "Mobiliario", TotalQuantity = 3, LowStockThreshold = 1 },
            new Material { Name = "Photocall con estructura", Type = "Mobiliario", TotalQuantity = 2, LowStockThreshold = 1 },
            new Material { Name = "Alargador 25 m", Type = "Electricidad", TotalQuantity = 8, LowStockThreshold = 3 },
            new Material { Name = "Foco LED 100 W", Type = "Electricidad", TotalQuantity = 5, LowStockThreshold = 2 },
            new Material { Name = "Torre de iluminación LED", Type = "Electricidad", TotalQuantity = 3, LowStockThreshold = 1 },
            new Material { Name = "Generador eléctrico 3 kW", Type = "Electricidad", TotalQuantity = 2, LowStockThreshold = 1 },
            new Material { Name = "Nevera portátil 40 l", Type = "Cocina", TotalQuantity = 3, LowStockThreshold = 1 },
            new Material { Name = "Mesa de cátering", Type = "Cocina", TotalQuantity = 6, LowStockThreshold = 2 },
            new Material { Name = "Grifo de barril de cerveza", Type = "Cocina", TotalQuantity = 4, LowStockThreshold = 1 },
            new Material { Name = "Juego de petanca", Type = "Deporte", TotalQuantity = 10, LowStockThreshold = 3 },
            new Material { Name = "Portería de fútbol sala", Type = "Deporte", TotalQuantity = 2, LowStockThreshold = 1 },
            new Material { Name = "Red de voleibol", Type = "Deporte", TotalQuantity = 4, LowStockThreshold = 1 },
            new Material { Name = "Extintor portátil", Type = "Seguridad", TotalQuantity = 8, LowStockThreshold = 2 },
            new Material { Name = "Botiquín de primeros auxilios", Type = "Seguridad", TotalQuantity = 6, LowStockThreshold = 2 },
            new Material { Name = "Valla de contención", Type = "Seguridad", TotalQuantity = 30, LowStockThreshold = 8 });
    }
}
