using System.Net.Http.Json;
using AssetFlow.Api.Data.Providers;
using AssetFlow.Api.Dtos;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.TestHost;

namespace AssetFlow.Api.Tests;

/// <summary>
/// Levanta la API completa en memoria contra una base de datos propia.
/// </summary>
/// <remarks>
/// Se arranca la aplicacion real, con su tuberia entera: autenticacion,
/// autorizacion, limitador de peticiones y manejo de errores incluidos. No se
/// sustituye ninguna de esas piezas por una version de mentira, porque son
/// justamente las que se quieren comprobar. Un test que desactiva el
/// limitador para poder pasar no demuestra nada sobre el limitador.
///
/// Cada clase de test recibe su propia instancia (xUnit crea una por
/// IClassFixture), y con ella su propio contenedor de dependencias. Eso da
/// dos aislamientos que se necesitan:
///
///   - Base de datos: un archivo SQLite temporal distinto por clase, asi que
///     los datos que crea un test no aparecen en otro.
///   - Limitador de peticiones y bloqueo de cuentas: los contadores viven en
///     memoria dentro del contenedor, de modo que empiezan a cero en cada
///     clase. Sin esto, el limite de 10 intentos de acceso por ventana se
///     agotaria a mitad de la bateria y los tests fallarian por interferencia
///     entre ellos en lugar de por un fallo real.
/// </remarks>
public class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    /// <summary>Contrasena del administrador sembrado. Solo para tests.</summary>
    public const string ClaveAdmin = "AdminDePruebas2026!";

    public const string UsuarioAdmin = "admin";

    private readonly string _archivoBd =
        Path.Combine(Path.GetTempPath(), $"inventario-tests-{Guid.NewGuid():N}.db");

    protected override void ConfigureWebHost(IWebHostBuilder constructor)
    {
        // Entorno de desarrollo a proposito: es el unico en el que el
        // sembrador puebla el inventario de ejemplo, y asi los tests se
        // ejecutan sobre el mismo camino de arranque que usa quien clona el
        // repositorio.
        constructor.UseEnvironment(Environments.Development);

        constructor.ConfigureAppConfiguration((_, configuracion) =>
        {
            configuracion.AddInMemoryCollection(new Dictionary<string, string?>
            {
                // Clave fija: en desarrollo la aplicacion genera una aleatoria
                // por arranque, lo que invalidaria los tokens entre reinicios
                // dentro de la misma bateria.
                ["Jwt:Key"] = "clave-exclusiva-de-tests-no-usada-en-ningun-entorno-real-0123456789",
                ["Jwt:Issuer"] = "AssetFlow.Api.Tests",
                ["Jwt:Audience"] = "AssetFlow.Api.Tests",

                ["Seed:AdminPassword"] = ClaveAdmin,

                // Correo del administrador sembrado. Se fija por el mismo
                // motivo que el bloque de abajo: si no, lo hereda de los
                // secretos de usuario y los tests dependerian de quien los
                // ejecuta.
                ["Seed:AdminEmail"] = "admin@pruebas.local",

                // Sin servidor SMTP, a proposito y de forma explicita.
                //
                // Los tests se ejecutan en el entorno de desarrollo, que carga
                // los secretos de usuario de la API. Si quien programa tiene
                // ahi un proveedor de correo configurado -que es justo lo que
                // recomienda docs/configuration.md-, sin esta linea los tests
                // que ejercitan la recuperacion de contrasena usarian el
                // emisor SMTP de verdad: enviarian correo real desde la
                // bateria de pruebas, y su resultado dependeria de una maquina
                // concreta en lugar del codigo. Peor aun, no se notaria,
                // porque el fallo de envio se traga deliberadamente para no
                // delatar que cuentas existen.
                //
                // Los tests que necesitan inspeccionar el correo enviado no
                // dependen de esto: sustituyen IEmailSender por un buzon en
                // memoria. Ver EntornoConCorreo.
                ["Email:SmtpHost"] = string.Empty
            });
        });

        // La base de datos se sustituye reemplazando el registro, no mediante
        // configuracion. La cadena de conexion se lee en Program.cs durante la
        // composicion, antes de que este host de pruebas pueda anadir nada, de
        // modo que un ConnectionStrings:Default puesto aqui no llegaria a
        // aplicarse: los tests acabarian escribiendo en la base de datos de
        // desarrollo y compartiendola entre clases.
        constructor.ConfigureTestServices(servicios =>
        {
            servicios.RemoveAll<DbContextOptions<SqliteAssetFlowDbContext>>();
            servicios.RemoveAll<DbContextOptions>();

            // Pooling=False permite borrar el archivo al terminar. Con el pool
            // activo SQLite mantiene el descriptor abierto y el archivo
            // temporal se quedaria en el disco.
            servicios.AddDbContext<SqliteAssetFlowDbContext>(opciones =>
                opciones.UseSqlite($"Data Source={_archivoBd};Pooling=False"));
        });
    }

    public virtual Task InitializeAsync()
    {
        // Provocar la creacion del servidor aqui hace que las migraciones y
        // el sembrado ocurran antes del primer test, y que cualquier fallo de
        // arranque se vea como tal y no como un 500 suelto.
        _ = Server;
        return Task.CompletedTask;
    }

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();

        try
        {
            if (File.Exists(_archivoBd))
            {
                File.Delete(_archivoBd);
            }
        }
        catch (IOException)
        {
            // El archivo esta en la carpeta temporal; que sobreviva a un
            // bloqueo puntual no justifica hacer fallar la bateria.
        }
    }

    /// <summary>Cliente sin credenciales.</summary>
    public HttpClient Cliente() => CreateClient();

    /// <summary>Abre sesion y devuelve la respuesta completa.</summary>
    public async Task<AuthResponse> AccederAsync(string usuario, string clave)
    {
        HttpResponseMessage respuesta = await Cliente()
            .PostAsJsonAsync("/api/auth/login", new { username = usuario, password = clave });

        respuesta.EnsureSuccessStatusCode();

        return (await respuesta.Content.ReadFromJsonAsync<AuthResponse>())!;
    }

    private readonly SemaphoreSlim _cerrojoAdmin = new(1, 1);
    private string? _tokenAdmin;

    /// <summary>Cliente autenticado como el administrador sembrado.</summary>
    /// <remarks>
    /// El token se reutiliza dentro de la misma clase de test. La razon no es
    /// la velocidad: el endpoint de acceso esta limitado a 10 intentos por
    /// ventana y por origen, y bajo WebApplicationFactory no hay direccion
    /// remota, asi que todas las peticiones caen en la misma particion. Con un
    /// acceso por cada [Fact] la bateria agotaria el limite ella sola y los
    /// tests empezarian a fallar con 429 sin que hubiera ningun error real.
    /// Los tests que necesitan comprobar el acceso en si usan AccederAsync.
    /// </remarks>
    public async Task<HttpClient> ClienteAdminAsync()
    {
        await _cerrojoAdmin.WaitAsync();

        try
        {
            _tokenAdmin ??= (await AccederAsync(UsuarioAdmin, ClaveAdmin)).AccessToken;
        }
        finally
        {
            _cerrojoAdmin.Release();
        }

        return ClienteCon(_tokenAdmin);
    }

    public HttpClient ClienteCon(string token)
    {
        HttpClient cliente = CreateClient();
        cliente.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        return cliente;
    }

    /// <summary>Contrasena de las cuentas creadas por los tests.</summary>
    public const string ClaveDePrueba = "ContrasenaDePrueba2026";

    /// <summary>
    /// Da de alta una cuenta usando la API real (con un administrador) y abre
    /// sesion con ella. Se crea a traves de los endpoints y no escribiendo en
    /// la base de datos para que el alta pase por las mismas validaciones y
    /// el mismo cifrado de contrasena que en produccion.
    /// </summary>
    public async Task<(int Id, HttpClient Cliente)> CrearCuentaAsync(
        string usuario, string rol = "User")
    {
        HttpClient admin = await ClienteAdminAsync();

        HttpResponseMessage alta = await admin.PostAsJsonAsync("/api/users", new
        {
            username = usuario,
            firstName = "Cuenta",
            lastName = "De prueba",
            email = $"{usuario}@ejemplo.local",
            password = ClaveDePrueba,
            role = rol
        });

        alta.EnsureSuccessStatusCode();

        AuthResponse sesion = await AccederAsync(usuario, ClaveDePrueba);

        return (sesion.User.Id, ClienteCon(sesion.AccessToken));
    }
}
