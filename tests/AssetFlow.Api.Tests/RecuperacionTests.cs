using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using AssetFlow.Api.Data;
using AssetFlow.Api.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AssetFlow.Api.Tests;

/// <summary>
/// Recuperacion de contrasena con autorizacion de un administrador.
/// </summary>
/// <remarks>
/// El flujo completo: la persona deja una solicitud, un administrador la
/// aprueba, la cuenta recibe una contrasena provisional predecible y no puede
/// hacer nada hasta cambiarla.
///
/// El grueso de esta clase vigila esa ultima parte. La contrasena provisional
/// es <c>usuario + "123@"</c>, deducible por cualquiera que vea un nombre de
/// usuario; lo unico que impide que eso sea una via de acceso publica es que
/// caduque en el primer uso. Si alguna de estas comprobaciones deja de pasar,
/// la aplicacion tiene un agujero de autenticacion, no un fallo de comodidad.
/// </remarks>
public class RecuperacionTests : IClassFixture<EntornoConCorreo>
{
    private const string CorreoConCuenta = EntornoConCorreo.CorreoVictima;
    private const string CorreoSinCuenta = "no.existe.nadie.asi@ejemplo.local";
    private const string UsuarioVictima = "victima.prueba";

    private readonly EntornoConCorreo _api;

    public RecuperacionTests(EntornoConCorreo api) => _api = api;

    // ========================================================================
    // ENUMERACION DE CUENTAS
    // ========================================================================

    [Fact]
    public async Task La_respuesta_es_identica_exista_o_no_la_cuenta()
    {
        HttpResponseMessage conCuenta = await Solicitar(CorreoConCuenta);
        HttpResponseMessage sinCuenta = await Solicitar(CorreoSinCuenta);

        conCuenta.StatusCode.Should().Be(HttpStatusCode.Accepted);
        sinCuenta.StatusCode.Should().Be(HttpStatusCode.Accepted);

        string cuerpoCon = await conCuenta.Content.ReadAsStringAsync();
        string cuerpoSin = await sinCuenta.Content.ReadAsStringAsync();

        // Este es el test central de todo el flujo. Cualquier diferencia
        // observable —codigo, cuerpo, cabeceras— convierte el formulario de
        // recuperacion en un comprobador de que correos estan dados de alta,
        // que es el primer paso de un ataque dirigido.
        cuerpoSin.Should().Be(cuerpoCon);

        cuerpoCon.Should().Contain("Si existe una cuenta asociada a ese correo");
    }

    /// <summary>
    /// Las dos respuestas deben tardar lo mismo, no solo decir lo mismo.
    /// </summary>
    /// <remarks>
    /// Este test existe por un fallo real que tuvo esta aplicacion. El cuerpo
    /// de la respuesta era identico en los dos casos, pero el camino de la
    /// cuenta que existe hacia mas consultas y esperaba al envio SMTP dentro de
    /// la peticion. Medido, daba 4,6 ms frente a 2,0 ms: mas del doble, y
    /// separable con cuatro muestras.
    ///
    /// El envio por correo ya no forma parte de este camino, pero las consultas
    /// de mas siguen ahi, asi que la comprobacion sigue haciendo falta.
    ///
    /// Se comparan medianas y no medias porque una sola pausa del recolector de
    /// basura desplaza la media entera.
    /// </remarks>
    [Fact]
    public async Task Las_dos_respuestas_tardan_lo_mismo()
    {
        var entorno = new EntornoConCorreo();
        await entorno.InitializeAsync();

        try
        {
            // Una llamada previa de calentamiento: la primera peticion paga la
            // compilacion JIT y el plan de consulta, y falsearia la medida.
            await Medir(entorno, CorreoConCuenta);

            var conCuenta = new List<double>();
            var sinCuenta = new List<double>();

            // Alternadas para que una ralentizacion pasajera de la maquina
            // afecte por igual a las dos series.
            for (int i = 0; i < 4; i++)
            {
                conCuenta.Add(await Medir(entorno, CorreoConCuenta));
                sinCuenta.Add(await Medir(entorno, CorreoSinCuenta));
            }

            double medianaCon = Mediana(conCuenta);
            double medianaSin = Mediana(sinCuenta);
            double diferencia = Math.Abs(medianaCon - medianaSin);

            diferencia.Should().BeLessThan(80,
                $"la diferencia de tiempo delata qué correos tienen cuenta " +
                $"(con cuenta: {medianaCon:0} ms, sin cuenta: {medianaSin:0} ms)");
        }
        finally
        {
            await entorno.DisposeAsync();
        }

        static async Task<double> Medir(EntornoConCorreo entorno, string correo)
        {
            long inicio = Stopwatch.GetTimestamp();

            await entorno.Cliente().PostAsJsonAsync(
                "/api/auth/forgot-password", new { email = correo });

            return Stopwatch.GetElapsedTime(inicio).TotalMilliseconds;
        }

        static double Mediana(List<double> valores)
        {
            valores.Sort();
            return valores[valores.Count / 2];
        }
    }

    [Fact]
    public async Task No_se_crea_solicitud_para_una_direccion_sin_cuenta()
    {
        var entorno = new EntornoConCorreo();
        await entorno.InitializeAsync();

        try
        {
            await entorno.Cliente().PostAsJsonAsync(
                "/api/auth/forgot-password", new { email = CorreoSinCuenta });

            using IServiceScope ambito = entorno.Services.CreateScope();
            var db = ambito.ServiceProvider.GetRequiredService<AssetFlowDbContext>();

            (await db.PasswordResetRequests.CountAsync()).Should().Be(0);
        }
        finally
        {
            await entorno.DisposeAsync();
        }
    }

    [Fact]
    public async Task Una_cuenta_desactivada_no_genera_solicitud_pero_la_respuesta_no_cambia()
    {
        var entorno = new EntornoConCorreo();
        await entorno.InitializeAsync();

        try
        {
            HttpClient admin = await entorno.ClienteAdminAsync();

            HttpResponseMessage baja = await admin.PutAsJsonAsync(
                $"/api/users/{entorno.IdVictima}/access",
                new { role = "User", isActive = false });

            baja.EnsureSuccessStatusCode();

            HttpResponseMessage respuesta = await entorno.Cliente()
                .PostAsJsonAsync("/api/auth/forgot-password",
                    new { email = EntornoConCorreo.CorreoVictima });

            respuesta.StatusCode.Should().Be(HttpStatusCode.Accepted);

            using IServiceScope ambito = entorno.Services.CreateScope();
            var db = ambito.ServiceProvider.GetRequiredService<AssetFlowDbContext>();

            (await db.PasswordResetRequests.CountAsync()).Should().Be(0);
        }
        finally
        {
            await entorno.DisposeAsync();
        }
    }

    [Fact]
    public async Task Pedirlo_dos_veces_no_duplica_la_solicitud()
    {
        var entorno = new EntornoConCorreo();
        await entorno.InitializeAsync();

        try
        {
            await Solicitar(entorno, CorreoConCuenta);
            await Solicitar(entorno, CorreoConCuenta);

            using IServiceScope ambito = entorno.Services.CreateScope();
            var db = ambito.ServiceProvider.GetRequiredService<AssetFlowDbContext>();

            (await db.PasswordResetRequests.CountAsync())
                .Should().Be(1, "una solicitud pendiente ya sirve; duplicarlas sólo " +
                                "ensucia la bandeja del administrador");
        }
        finally
        {
            await entorno.DisposeAsync();
        }
    }

    // ========================================================================
    // AUTORIZACION
    // ========================================================================

    [Fact]
    public async Task Un_usuario_normal_no_puede_ver_ni_resolver_solicitudes()
    {
        var entorno = new EntornoConCorreo();
        await entorno.InitializeAsync();

        try
        {
            int id = await CrearSolicitudAsync(entorno);

            // La bandeja da acceso a cuentas ajenas: es de administracion.
            (await entorno.ClienteVictima.GetAsync("/api/password-reset-requests"))
                .StatusCode.Should().Be(HttpStatusCode.Forbidden);

            (await entorno.ClienteVictima.PostAsync(
                $"/api/password-reset-requests/{id}/approve", null))
                .StatusCode.Should().Be(HttpStatusCode.Forbidden);

            (await entorno.ClienteVictima.PostAsync(
                $"/api/password-reset-requests/{id}/reject", null))
                .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }
        finally
        {
            await entorno.DisposeAsync();
        }
    }

    [Fact]
    public async Task Sin_sesion_no_se_llega_a_la_bandeja()
    {
        (await _api.Cliente().GetAsync("/api/password-reset-requests"))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Una_solicitud_no_se_puede_resolver_dos_veces()
    {
        var entorno = new EntornoConCorreo();
        await entorno.InitializeAsync();

        try
        {
            int id = await CrearSolicitudAsync(entorno);
            HttpClient admin = await entorno.ClienteAdminAsync();

            (await admin.PostAsync($"/api/password-reset-requests/{id}/approve", null))
                .StatusCode.Should().Be(HttpStatusCode.OK);

            // Sin esto, dos administradores mirando la misma pantalla podrian
            // reiniciar la contrasena dos veces seguidas.
            (await admin.PostAsync($"/api/password-reset-requests/{id}/approve", null))
                .StatusCode.Should().Be(HttpStatusCode.Conflict);

            (await admin.PostAsync($"/api/password-reset-requests/{id}/reject", null))
                .StatusCode.Should().Be(HttpStatusCode.Conflict);
        }
        finally
        {
            await entorno.DisposeAsync();
        }
    }

    [Fact]
    public async Task Denegar_no_toca_la_contrasena()
    {
        var entorno = new EntornoConCorreo();
        await entorno.InitializeAsync();

        try
        {
            int id = await CrearSolicitudAsync(entorno);
            HttpClient admin = await entorno.ClienteAdminAsync();

            (await admin.PostAsync($"/api/password-reset-requests/{id}/reject", null))
                .StatusCode.Should().Be(HttpStatusCode.NoContent);

            // La contrasena original sigue valiendo.
            (await Acceder(entorno, UsuarioVictima, ApiFactory.ClaveDePrueba))
                .StatusCode.Should().Be(HttpStatusCode.OK);

            // Y la provisional no.
            (await Acceder(entorno, UsuarioVictima, Provisional(UsuarioVictima)))
                .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }
        finally
        {
            await entorno.DisposeAsync();
        }
    }

    // ========================================================================
    // LA CONTRASENA PROVISIONAL
    // ========================================================================

    [Fact]
    public async Task Aprobar_asigna_la_provisional_y_revoca_las_sesiones()
    {
        var entorno = new EntornoConCorreo();
        await entorno.InitializeAsync();

        try
        {
            // Sesion abierta antes de la aprobacion: representa a quien tenia
            // la cuenta cuando su titular la recupera.
            var sesion = await AbrirSesionAsync(entorno);

            int id = await CrearSolicitudAsync(entorno);
            HttpClient admin = await entorno.ClienteAdminAsync();

            HttpResponseMessage aprobacion =
                await admin.PostAsync($"/api/password-reset-requests/{id}/approve", null);

            aprobacion.StatusCode.Should().Be(HttpStatusCode.OK);

            var datos = await aprobacion.Content.ReadFromJsonAsync<AprobacionDePrueba>();

            datos!.ContrasenaProvisional.Should().Be(Provisional(UsuarioVictima));

            // La anterior deja de valer.
            (await Acceder(entorno, UsuarioVictima, ApiFactory.ClaveDePrueba))
                .StatusCode.Should().Be(HttpStatusCode.Unauthorized);

            // Y quien estuviera dentro queda fuera.
            (await entorno.Cliente().PostAsJsonAsync(
                "/api/auth/refresh", new { refreshToken = sesion.RefreshToken }))
                .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }
        finally
        {
            await entorno.DisposeAsync();
        }
    }

    /// <summary>
    /// Con la provisional se entra, pero no se puede hacer absolutamente nada.
    /// </summary>
    /// <remarks>
    /// <b>Es el test que sostiene todo el diseno.</b> La contrasena provisional
    /// es publica de hecho: cualquiera que vea el nombre de usuario la deduce.
    /// Que eso no sea un agujero depende por completo de que la sesion que abre
    /// no sirva para nada mas que para cambiarla.
    /// </remarks>
    [Theory]
    [InlineData("/api/materials")]
    [InlineData("/api/loans")]
    [InlineData("/api/users")]
    [InlineData("/api/audit")]
    [InlineData("/api/password-reset-requests")]
    public async Task La_sesion_provisional_no_puede_hacer_nada_mas(string ruta)
    {
        var entorno = new EntornoConCorreo();
        await entorno.InitializeAsync();

        try
        {
            HttpClient cliente = await AprobarYAccederAsync(entorno);

            HttpResponseMessage respuesta = await cliente.GetAsync(ruta);

            respuesta.StatusCode.Should().Be(HttpStatusCode.Forbidden,
                $"«{ruta}» debe estar cerrado mientras la contraseña sea provisional");
        }
        finally
        {
            await entorno.DisposeAsync();
        }
    }

    [Fact]
    public async Task La_sesion_provisional_tampoco_puede_escribir()
    {
        var entorno = new EntornoConCorreo();
        await entorno.InitializeAsync();

        try
        {
            HttpClient cliente = await AprobarYAccederAsync(entorno);

            // Un GET bloqueado no demuestra que lo esten los POST: son ramas
            // distintas del enrutado.
            (await cliente.PostAsJsonAsync("/api/loans",
                new { items = new[] { new { materialId = 1, quantity = 1 } } }))
                .StatusCode.Should().Be(HttpStatusCode.Forbidden);

            (await cliente.PostAsJsonAsync("/api/materials",
                new { name = "Cosa", quantity = 1 }))
                .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }
        finally
        {
            await entorno.DisposeAsync();
        }
    }

    [Fact]
    public async Task Cambiar_la_provisional_devuelve_una_sesion_utilizable()
    {
        var entorno = new EntornoConCorreo();
        await entorno.InitializeAsync();

        try
        {
            HttpClient cliente = await AprobarYAccederAsync(entorno);

            HttpResponseMessage cambio = await cliente.PostAsJsonAsync(
                "/api/auth/change-password",
                new
                {
                    currentPassword = Provisional(UsuarioVictima),
                    newPassword = "MiClavePropia2026!"
                });

            cambio.StatusCode.Should().Be(HttpStatusCode.OK);

            var sesion = await cambio.Content.ReadFromJsonAsync<AuthResponseDePrueba>();

            sesion!.User.MustChangePassword.Should().BeFalse();

            // La sesion nueva ya sirve para trabajar.
            HttpClient renovado = entorno.ClienteCon(sesion.AccessToken);

            (await renovado.GetAsync("/api/materials"))
                .StatusCode.Should().Be(HttpStatusCode.OK);

            // Y se entra con la contrasena elegida.
            (await Acceder(entorno, UsuarioVictima, "MiClavePropia2026!"))
                .StatusCode.Should().Be(HttpStatusCode.OK);
        }
        finally
        {
            await entorno.DisposeAsync();
        }
    }

    /// <summary>
    /// No se puede «cambiar» la provisional por ella misma.
    /// </summary>
    /// <remarks>
    /// Sin esta comprobacion, el formulario obligatorio se puede pasar dejando
    /// la misma contrasena, y la cuenta se queda con una clave deducible del
    /// nombre de usuario. Es decir, el agujero que todo este flujo evita.
    /// </remarks>
    [Fact]
    public async Task No_se_puede_quedar_con_la_provisional()
    {
        var entorno = new EntornoConCorreo();
        await entorno.InitializeAsync();

        try
        {
            HttpClient cliente = await AprobarYAccederAsync(entorno);
            string provisional = Provisional(UsuarioVictima);

            HttpResponseMessage cambio = await cliente.PostAsJsonAsync(
                "/api/auth/change-password",
                new { currentPassword = provisional, newPassword = provisional });

            cambio.StatusCode.Should().Be(HttpStatusCode.BadRequest);

            // Y la cuenta sigue bloqueada.
            (await cliente.GetAsync("/api/materials"))
                .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }
        finally
        {
            await entorno.DisposeAsync();
        }
    }

    [Fact]
    public async Task El_cambio_exige_la_contrasena_actual()
    {
        var entorno = new EntornoConCorreo();
        await entorno.InitializeAsync();

        try
        {
            HttpClient cliente = await AprobarYAccederAsync(entorno);

            HttpResponseMessage cambio = await cliente.PostAsJsonAsync(
                "/api/auth/change-password",
                new { currentPassword = "no-es-la-suya", newPassword = "OtraDistinta2026!" });

            cambio.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }
        finally
        {
            await entorno.DisposeAsync();
        }
    }

    /// <summary>
    /// Sin cambio pendiente, nadie fija su propia contrasena.
    /// </summary>
    /// <remarks>
    /// El endpoint de cambio es la unica puerta por la que una persona elige su
    /// contrasena, y esta pensado solo para el cambio obligatorio. Si
    /// funcionara siempre, seria el cambio de contrasena propia que se ha
    /// retirado a proposito de esta aplicacion.
    /// </remarks>
    [Fact]
    public async Task Sin_cambio_pendiente_el_endpoint_esta_cerrado()
    {
        var entorno = new EntornoConCorreo();
        await entorno.InitializeAsync();

        try
        {
            HttpResponseMessage cambio = await entorno.ClienteVictima.PostAsJsonAsync(
                "/api/auth/change-password",
                new
                {
                    currentPassword = ApiFactory.ClaveDePrueba,
                    newPassword = "IntentoDeCambio2026!"
                });

            cambio.StatusCode.Should().Be(HttpStatusCode.Forbidden);

            // La contrasena no ha cambiado.
            (await Acceder(entorno, UsuarioVictima, ApiFactory.ClaveDePrueba))
                .StatusCode.Should().Be(HttpStatusCode.OK);
        }
        finally
        {
            await entorno.DisposeAsync();
        }
    }

    [Fact]
    public async Task El_reinicio_administrativo_tambien_obliga_a_cambiarla()
    {
        var entorno = new EntornoConCorreo();
        await entorno.InitializeAsync();

        try
        {
            HttpClient admin = await entorno.ClienteAdminAsync();

            HttpResponseMessage reinicio = await admin.PostAsync(
                $"/api/users/{entorno.IdVictima}/password", null);

            reinicio.StatusCode.Should().Be(HttpStatusCode.OK);

            var datos = await reinicio.Content.ReadFromJsonAsync<AprobacionDePrueba>();

            datos!.ContrasenaProvisional.Should().Be(Provisional(UsuarioVictima));

            var sesion = await AccederYLeer(entorno, UsuarioVictima, Provisional(UsuarioVictima));

            sesion.User.MustChangePassword.Should().BeTrue();

            (await entorno.ClienteCon(sesion.AccessToken).GetAsync("/api/materials"))
                .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }
        finally
        {
            await entorno.DisposeAsync();
        }
    }

    [Fact]
    public async Task Aprobar_avisa_por_correo_al_titular()
    {
        var entorno = new EntornoConCorreo();
        await entorno.InitializeAsync();

        try
        {
            int id = await CrearSolicitudAsync(entorno);
            HttpClient admin = await entorno.ClienteAdminAsync();

            await admin.PostAsync($"/api/password-reset-requests/{id}/approve", null);

            // Sin este aviso, un robo de cuenta es silencioso para el titular.
            var aviso = await entorno.Buzon.EsperarAsync(
                EntornoConCorreo.CorreoVictima, "Tu contraseña ha cambiado");

            aviso.Should().NotBeNull("el titular debe enterarse de que le han reiniciado la cuenta");

            aviso!.Value.Cuerpo.Should().Contain("SI NO HAS SIDO TÚ");

            // El aviso no puede llevar la contrasena ni un enlace: un enlace en
            // un correo es justo lo que usaria quien acaba de robar la cuenta.
            aviso.Value.Cuerpo.Should().NotContain(Provisional(UsuarioVictima));
            aviso.Value.Cuerpo.Should().NotContain("http://");
            aviso.Value.Cuerpo.Should().NotContain("https://");
        }
        finally
        {
            await entorno.DisposeAsync();
        }
    }

    // ========================================================================
    // LIMITE DE PETICIONES
    // ========================================================================

    [Fact]
    public async Task El_endpoint_de_recuperacion_esta_limitado()
    {
        var entorno = new EntornoConCorreo();
        await entorno.InitializeAsync();

        try
        {
            HttpStatusCode ultimo = HttpStatusCode.Accepted;

            // Sin límite, este endpoint es a la vez un enumerador de cuentas y
            // una forma de llenar de solicitudes la bandeja del administrador.
            for (int intento = 0; intento < 15 && ultimo != HttpStatusCode.TooManyRequests;
                 intento++)
            {
                HttpResponseMessage respuesta = await entorno.Cliente().PostAsJsonAsync(
                    "/api/auth/forgot-password",
                    new { email = EntornoConCorreo.CorreoVictima });

                ultimo = respuesta.StatusCode;
            }

            ultimo.Should().Be(HttpStatusCode.TooManyRequests);
        }
        finally
        {
            await entorno.DisposeAsync();
        }
    }

    // ========================================================================
    // AUXILIARES
    // ========================================================================

    /// <summary>La misma regla que aplica el servidor, escrita aparte a propósito.</summary>
    /// <remarks>
    /// Si el test la calculara llamando al código de producción, los dos
    /// podrían cambiar a la vez y el test seguiría en verde sin comprobar nada.
    /// </remarks>
    private static string Provisional(string usuario) => usuario + "123@";

    private Task<HttpResponseMessage> Solicitar(string correo) =>
        _api.Cliente().PostAsJsonAsync("/api/auth/forgot-password", new { email = correo });

    private static Task<HttpResponseMessage> Solicitar(ApiFactory api, string correo) =>
        api.Cliente().PostAsJsonAsync("/api/auth/forgot-password", new { email = correo });

    private static Task<HttpResponseMessage> Acceder(
        ApiFactory api, string usuario, string clave) =>
        api.Cliente().PostAsJsonAsync(
            "/api/auth/login", new { username = usuario, password = clave });

    private static async Task<AuthResponseDePrueba> AccederYLeer(
        ApiFactory api, string usuario, string clave)
    {
        HttpResponseMessage respuesta = await Acceder(api, usuario, clave);

        respuesta.EnsureSuccessStatusCode();

        return (await respuesta.Content.ReadFromJsonAsync<AuthResponseDePrueba>())!;
    }

    /// <summary>Deja una solicitud pendiente y devuelve su identificador.</summary>
    private static async Task<int> CrearSolicitudAsync(EntornoConCorreo entorno)
    {
        await Solicitar(entorno, CorreoConCuenta);

        using IServiceScope ambito = entorno.Services.CreateScope();
        var db = ambito.ServiceProvider.GetRequiredService<AssetFlowDbContext>();

        PasswordResetRequest solicitud = await db.PasswordResetRequests
            .OrderByDescending(s => s.Id)
            .FirstAsync();

        return solicitud.Id;
    }

    /// <summary>Aprueba una solicitud y devuelve un cliente con la sesión provisional.</summary>
    private static async Task<HttpClient> AprobarYAccederAsync(EntornoConCorreo entorno)
    {
        int id = await CrearSolicitudAsync(entorno);

        HttpClient admin = await entorno.ClienteAdminAsync();

        HttpResponseMessage aprobacion =
            await admin.PostAsync($"/api/password-reset-requests/{id}/approve", null);

        aprobacion.EnsureSuccessStatusCode();

        var sesion = await AccederYLeer(entorno, UsuarioVictima, Provisional(UsuarioVictima));

        sesion.User.MustChangePassword.Should().BeTrue(
            "entrar con la provisional debe marcar la sesión como pendiente de cambio");

        return entorno.ClienteCon(sesion.AccessToken);
    }

    private static async Task<AuthResponseDePrueba> AbrirSesionAsync(EntornoConCorreo entorno) =>
        await AccederYLeer(entorno, UsuarioVictima, ApiFactory.ClaveDePrueba);

    private sealed record AprobacionDePrueba(string Username, string ContrasenaProvisional);

    private sealed record UsuarioDePrueba(string Username, bool MustChangePassword);

    private sealed record AuthResponseDePrueba(
        string AccessToken, string RefreshToken, UsuarioDePrueba User);
}
