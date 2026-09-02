using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using AssetFlow.Api.Data;
using AssetFlow.Api.Data.Providers;
using AssetFlow.Api.Middleware;
using AssetFlow.Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------------------
// CONFIGURACION
//
// Ningun secreto vive en el repositorio. En desarrollo se usan los user
// secrets de .NET; en produccion, variables de entorno. Ver docs/configuration.md.
// ---------------------------------------------------------------------------
builder.Configuration.AddEnvironmentVariables("ASSETFLOW_");

// Clave de firma en desarrollo.
//
// El repositorio no puede contener una clave real: estaria publicada. Pero
// exigir configurarla a mano rompe el "clonar y ejecutar", asi que en
// desarrollo se genera una aleatoria por arranque. Consecuencia buscada: al
// reiniciar la API caducan las sesiones, lo que recuerda que esto no es una
// configuracion valida para produccion.
//
// En produccion no hay red de seguridad: si falta la clave, la validacion de
// JwtOptions impide arrancar.
if (builder.Environment.IsDevelopment() &&
    string.IsNullOrWhiteSpace(builder.Configuration["Jwt:Key"]))
{
    string clave = Convert.ToBase64String(
        System.Security.Cryptography.RandomNumberGenerator.GetBytes(48));

    builder.Configuration["Jwt:Key"] = clave;

    Console.WriteLine(
        "[aviso] Jwt:Key no configurada. Se ha generado una clave temporal para " +
        "esta ejecucion. Configurala con 'dotnet user-secrets set \"Jwt:Key\" \"...\"'.");
}

builder.Services
    .AddOptions<JwtOptions>()
    .Bind(builder.Configuration.GetSection(JwtOptions.Section))
    .ValidateDataAnnotations()
    // Se valida al arrancar y no en la primera peticion: es preferible que la
    // aplicacion no llegue a levantarse a que atienda peticiones y falle al
    // emitir el primer token.
    .ValidateOnStart();

builder.Services.Configure<PasswordHashingOptions>(
    builder.Configuration.GetSection(PasswordHashingOptions.Section));

// ---------------------------------------------------------------------------
// BASE DE DATOS
//
// SQLite por defecto: quien clone el repositorio puede arrancar sin instalar
// nada. SQL Server se activa cambiando Database:Provider en la configuracion.
// ---------------------------------------------------------------------------
string proveedor = builder.Configuration["Database:Provider"] ?? "Sqlite";
string? conexion = builder.Configuration.GetConnectionString("Default");

// Se registra el contexto concreto del proveedor elegido y se expone la clase
// base para que controladores y servicios no sepan sobre que motor corren.
if (proveedor.Equals("SqlServer", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddDbContext<SqlServerAssetFlowDbContext>(opciones =>
        opciones.UseSqlServer(
            conexion ?? throw new InvalidOperationException(
                "Falta ConnectionStrings:Default para el proveedor SqlServer."),
            sql => sql.EnableRetryOnFailure()));

    builder.Services.AddScoped<AssetFlowDbContext>(sp =>
        sp.GetRequiredService<SqlServerAssetFlowDbContext>());
}
else
{
    builder.Services.AddDbContext<SqliteAssetFlowDbContext>(opciones =>
        opciones.UseSqlite(conexion ?? "Data Source=assetflow.db"));

    builder.Services.AddScoped<AssetFlowDbContext>(sp =>
        sp.GetRequiredService<SqliteAssetFlowDbContext>());
}

// ---------------------------------------------------------------------------
// SERVICIOS DE DOMINIO
// ---------------------------------------------------------------------------
builder.Services.AddScoped<IPasswordHasher, BCryptPasswordHasher>();
builder.Services.AddSingleton<ITokenService, TokenService>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<ILoginThrottle, LoginThrottle>();
builder.Services.AddSingleton<IPasswordResetThrottle, PasswordResetThrottle>();

// El auditor necesita saber quien hace la peticion en curso.
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IAuditor, Auditor>();
builder.Services.AddScoped<IPasswordResetService, PasswordResetService>();

builder.Services.Configure<EmailOptions>(
    builder.Configuration.GetSection(EmailOptions.Section));

// El correo es OPCIONAL y la aplicacion arranca sin el.
//
// Lo fue obligatorio mientras la recuperacion de contrasena viajaba por ahi: sin
// SMTP, el sustituto escribia el codigo en claro en el registro, asi que
// arrancar sin configurarlo era publicar codigos de recuperacion. Ahora la
// recuperacion se resuelve dentro de la aplicacion y por correo solo sale el
// aviso de "tu contrasena ha cambiado", que no contiene ningun secreto.
//
// Queda como una capa mas, no como un requisito: si esta configurado, el
// titular se entera de que alguien le ha reiniciado la cuenta.

// Envio de correo: SMTP si esta configurado, y si no un sustituto que escribe
// el mensaje en el registro.
builder.Services.AddScoped<IEmailSender>(sp =>
{
    var opciones = sp.GetRequiredService<IOptions<EmailOptions>>();

    return opciones.Value.EstaConfigurado
        ? ActivatorUtilities.CreateInstance<SmtpEmailSender>(sp)
        : ActivatorUtilities.CreateInstance<LoggingEmailSender>(sp);
});

// La cola es singleton porque el canal debe ser uno solo para toda la
// aplicacion, y el servicio en segundo plano es quien lo vacia.
builder.Services.AddSingleton<EmailQueue>();
builder.Services.AddSingleton<IEmailQueue>(sp => sp.GetRequiredService<EmailQueue>());
builder.Services.AddHostedService<EmailBackgroundService>();

// ---------------------------------------------------------------------------
// AUTENTICACION
// ---------------------------------------------------------------------------
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer();

// La configuracion del validador se resuelve desde IOptions<JwtOptions>, la
// misma instancia que usa TokenService para firmar. Antes se leia aqui con un
// Get<JwtOptions>() inmediato, lo que creaba dos fuentes de verdad para el
// mismo ajuste: cualquier origen de configuracion anadido despues de esta
// linea dejaba al emisor firmando con una clave y al validador comprobando con
// otra. El fallo es cerrado (todo responde 401), pero silencioso y caro de
// diagnosticar.
builder.Services
    .AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IOptions<JwtOptions>>((opciones, opcionesJwt) =>
    {
        JwtOptions jwt = opcionesJwt.Value;

        opciones.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt.Issuer,

            ValidateAudience = true,
            ValidAudience = jwt.Audience,

            ValidateIssuerSigningKey = true,
            // Sin valor de reserva: JwtOptions exige la clave y la valida al
            // arrancar, asi que aqui no puede llegar vacia. Un relleno por
            // defecto solo serviria para que una API mal configurada arrancase
            // firmando con una clave conocida en lugar de negarse a hacerlo.
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key)),

            // El algoritmo se fija de forma explicita. Sin esta linea, la
            // libreria acepta cualquier algoritmo que declare el propio token,
            // que es la puerta de entrada a los ataques de confusion de
            // algoritmo.
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],

            ValidateLifetime = true,
            // Por defecto se toleran 5 minutos de desfase de reloj, lo que
            // alarga la vida real de un token de 15 minutos a 20.
            ClockSkew = TimeSpan.FromSeconds(30),

            RoleClaimType = ClaimTypes.Role,
            NameClaimType = ClaimTypes.Name
        };

        // En desarrollo, sobre HTTP local, no se puede exigir metadatos por
        // HTTPS. En produccion si.
        opciones.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
    });

// ---------------------------------------------------------------------------
// AUTORIZACION
//
// Politica por defecto: exigir autenticacion en TODO. Un endpoint nuevo nace
// protegido y hay que abrirlo a proposito con [AllowAnonymous]. Lo contrario
// (proteger uno a uno) hace que el olvido se traduzca en un agujero, que es
// exactamente lo que le pasaba a la version anterior de esta API.
// ---------------------------------------------------------------------------
builder.Services.AddAuthorization(opciones =>
{
    opciones.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});

// ---------------------------------------------------------------------------
// LIMITACION DE PETICIONES
//
// Dos niveles: uno estricto sobre el login, que es lo que se ataca por fuerza
// bruta, y uno general que evita que un cliente monopolice la API.
// ---------------------------------------------------------------------------
builder.Services.AddRateLimiter(opciones =>
{
    opciones.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    opciones.AddPolicy("login", contexto =>
    {
        // Reparto por IP. Cubre el caso habitual: un atacante martilleando el
        // login desde una maquina. El caso complementario, muchas IP contra una
        // sola cuenta, no se puede repartir aqui porque el nombre de usuario va
        // en el cuerpo y el limitador se ejecuta antes de leerlo; de eso se
        // encarga ILoginThrottle dentro del controlador.
        string ip = contexto.Connection.RemoteIpAddress?.ToString() ?? "desconocida";

        return RateLimitPartition.GetFixedWindowLimiter(ip, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(5),
            QueueLimit = 0
        });
    });

    opciones.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(contexto =>
    {
        string clave = contexto.User.FindFirstValue(ClaimTypes.NameIdentifier)
                       ?? contexto.Connection.RemoteIpAddress?.ToString()
                       ?? "desconocida";

        return RateLimitPartition.GetFixedWindowLimiter(clave, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 300,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        });
    });

    opciones.OnRejected = async (contexto, ct) =>
    {
        contexto.HttpContext.Response.ContentType = "application/problem+json";

        await contexto.HttpContext.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Title = "Demasiadas peticiones",
            Detail = "Has superado el limite de intentos. Espera unos minutos.",
            Status = StatusCodes.Status429TooManyRequests
        }, ct);
    };
});

// ---------------------------------------------------------------------------
// MVC
// ---------------------------------------------------------------------------
builder.Services
    .AddControllers()
    .ConfigureApiBehaviorOptions(opciones =>
    {
        // Los errores de validacion se devuelven como ProblemDetails, igual
        // que el resto de errores, para que el cliente solo tenga que saber
        // interpretar un formato.
        opciones.InvalidModelStateResponseFactory = contexto =>
        {
            var problema = new ValidationProblemDetails(contexto.ModelState)
            {
                Title = "Datos no válidos",
                Status = StatusCodes.Status400BadRequest,
                Instance = contexto.HttpContext.Request.Path
            };

            return new BadRequestObjectResult(problema)
            {
                ContentTypes = { "application/problem+json" }
            };
        };
    });

builder.Services.Configure<Microsoft.AspNetCore.Http.Json.JsonOptions>(o =>
{
    o.SerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
});

// Limite de tamano del cuerpo. Por defecto son 30 MB: esta API no recibe
// ficheros, y un cuerpo grande solo puede ser un intento de agotar memoria.
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(o =>
{
    o.MultipartBodyLengthLimit = 1024 * 1024;
});

builder.WebHost.ConfigureKestrel(o =>
{
    o.Limits.MaxRequestBodySize = 1024 * 1024;
    o.Limits.KeepAliveTimeout = TimeSpan.FromMinutes(2);
    o.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(30);
    // No anunciar el servidor: no ayuda a nadie salvo a quien busca versiones
    // con vulnerabilidades conocidas.
    o.AddServerHeader = false;
});

// ---------------------------------------------------------------------------
// SWAGGER
// ---------------------------------------------------------------------------
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(opciones =>
{
    opciones.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "AssetFlow Manager API",
        Version = "v1",
        Description =
            "API de gestión de inventario y préstamos de material.\n\n" +
            "Todos los endpoints requieren autenticación salvo `/api/auth/login`, " +
            "`/api/auth/refresh`, `/api/auth/forgot-password` y `/health`. " +
            "Usa **Authorize** con el access token que devuelve el login.\n\n" +
            "Una sesión abierta con una contraseña provisional sólo puede llamar a " +
            "`/api/auth/change-password`, `/api/auth/me`, `/api/auth/logout` y " +
            "`/api/auth/refresh`; el resto responde 403 hasta que la cambie."
    });

    opciones.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Pega aqui el access token. Swagger anade el prefijo Bearer."
    });

    opciones.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        [new OpenApiSecurityScheme
        {
            Reference = new OpenApiReference
            {
                Type = ReferenceType.SecurityScheme,
                Id = "Bearer"
            }
        }] = Array.Empty<string>()
    });

    string xml = Path.Combine(AppContext.BaseDirectory,
        $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml");

    if (File.Exists(xml))
    {
        opciones.IncludeXmlComments(xml);
    }
});

// No se configura CORS: el unico cliente es una aplicacion de escritorio, que
// no es un navegador y por tanto no aplica la politica de mismo origen.
// Anadir AllowAnyOrigin "por si acaso" solo abriria la API a cualquier pagina
// web sin necesidad alguna.

var app = builder.Build();

// ---------------------------------------------------------------------------
// CANALIZACION
// ---------------------------------------------------------------------------

// El primero de todos: cualquier excepcion de los middlewares posteriores
// tiene que pasar por aqui.
app.UseMiddleware<ExceptionHandlingMiddleware>();

// Cabeceras de un proxy inverso.
//
// Por defecto solo se confia en el bucle local, asi que detras de un proxy en
// otra maquina las cabeceras se ignoran y RemoteIpAddress pasa a ser siempre
// la del proxy. Eso no rompe nada de forma visible, pero convierte el
// limitador de acceso por IP en uno global: todos los clientes caen en la
// misma particion y basta un atacante para agotarle el cupo a los demas.
//
// Por eso las redes de confianza se declaran en la configuracion. Si no hay
// ninguna, se mantiene el valor por defecto (solo bucle local), que es el
// correcto cuando no hay proxy delante. Ver docs/configuration.md.
var opcionesProxy = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
};

string[] proxiesDeConfianza =
    builder.Configuration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>() ?? [];

if (proxiesDeConfianza.Length > 0)
{
    // Se limpian los valores por defecto: declarar proxies concretos y dejar
    // ademas el bucle local abierto seria confiar en mas de lo que se ha
    // pedido.
    opcionesProxy.KnownProxies.Clear();
    opcionesProxy.KnownNetworks.Clear();

    foreach (string proxy in proxiesDeConfianza)
    {
        if (System.Net.IPAddress.TryParse(proxy, out System.Net.IPAddress? direccion))
        {
            opcionesProxy.KnownProxies.Add(direccion);
        }
        else
        {
            throw new InvalidOperationException(
                $"ForwardedHeaders:KnownProxies contiene «{proxy}», que no es una " +
                "direccion IP valida.");
        }
    }
}

app.UseForwardedHeaders(opcionesProxy);

if (app.Environment.IsDevelopment())
{
    // Swagger solo en desarrollo: en produccion publicaria el mapa completo de
    // la API, incluidos los endpoints de administracion.
    app.UseSwagger();
    app.UseSwaggerUI(o =>
    {
        o.SwaggerEndpoint("/swagger/v1/swagger.json", "AssetFlow Manager API v1");
        o.DocumentTitle = "AssetFlow Manager API";
    });
}
else
{
    // HSTS solo en produccion: en local obligaria al navegador a recordar
    // localhost como HTTPS y estorbaria en cualquier otro proyecto.
    app.UseHsts();
}

// Render (y PaaS similares) termina el TLS en su borde y ya redirige HTTP a
// HTTPS antes de reenviar la peticion al contenedor por HTTP simple. Sin esta
// excepcion, UseHttpsRedirection vería siempre esquema "http" y devolvería un
// redirect a https que el borde vuelve a reenviar por http: bucle infinito.
// RENDER es una variable que la plataforma inyecta ella misma, no algo que se
// configure aqui.
bool detrasDeRender = builder.Configuration["RENDER"] is not null;

if (!detrasDeRender)
{
    app.UseHttpsRedirection();
}

// Cabeceras de seguridad. La API devuelve JSON, no HTML, asi que solo se
// aplican las que tienen efecto real en ese contexto.
app.Use(async (contexto, siguiente) =>
{
    // Evita que un navegador reinterprete una respuesta JSON como HTML o
    // script, que es el vector de los ataques de sniffing de contenido.
    contexto.Response.Headers["X-Content-Type-Options"] = "nosniff";

    // La API no debe poder embeberse en un marco.
    contexto.Response.Headers["X-Frame-Options"] = "DENY";

    // Sin referencia a rutas internas al saltar a otro origen.
    contexto.Response.Headers["Referrer-Policy"] = "no-referrer";

    await siguiente();
});

app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

// Despues de autenticar, porque necesita saber quien es; antes de los
// endpoints, porque su trabajo es que no lleguen. Deja pasar solo el cambio de
// contrasena y lo imprescindible para llegar a el.
app.UseMiddleware<CambioObligatorioMiddleware>();

app.MapControllers();

// Sonda de vida. Es el unico endpoint anonimo ademas del login: el cliente de
// escritorio la usa para mostrar el estado de conexion, y no revela nada.
app.MapGet("/health", () => Results.Ok(new { status = "ok" }))
   .AllowAnonymous()
   .WithTags("Sistema");

await DbInitializer.InicializarAsync(app);

app.Run();

/// <summary>
/// Declarado publico para que el proyecto de tests de integracion pueda
/// instanciar la aplicacion con WebApplicationFactory.
/// </summary>
public partial class Program;
