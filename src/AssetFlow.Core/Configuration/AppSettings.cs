using System.Text.Json;
using AssetFlow.Core.Diagnostics;

namespace AssetFlow.Core.Configuration;

/// <summary>
/// Configuracion de la aplicacion, en el perfil del usuario.
/// </summary>
/// <remarks>
/// Este archivo NO contiene ningun secreto: solo la direccion del servidor y
/// preferencias de interfaz. Las credenciales viven cifradas en sesion.bin
/// (ver <see cref="Security.TokenStore"/>).
/// </remarks>
public static class AppSettings
{
    private sealed class Datos
    {
        public string ApiServer { get; set; } = "";

        public bool RecordarSesion { get; set; } = true;

        public string UltimoUsuario { get; set; } = "";
    }

    private static readonly string Archivo =
        Path.Combine(RutasApp.CarpetaDatos, "settings.json");

    private static Datos _datos = new();

    /// <summary>Direccion base de la API, con barra final.</summary>
    public static string ApiServer
    {
        get => _datos.ApiServer;
        set => _datos.ApiServer = Normalizar(value);
    }

    public static bool RecordarSesion
    {
        get => _datos.RecordarSesion;
        set => _datos.RecordarSesion = value;
    }

    /// <summary>Ultimo nombre de usuario, para precargar el formulario.</summary>
    public static string UltimoUsuario
    {
        get => _datos.UltimoUsuario;
        set => _datos.UltimoUsuario = value ?? "";
    }

    public static bool HayServidorConfigurado => !string.IsNullOrWhiteSpace(_datos.ApiServer);

    /// <summary>
    /// Comprueba que la direccion es utilizable y explica por que no lo es.
    /// </summary>
    /// <remarks>
    /// HTTP se permite solo contra la maquina local, que es el escenario de
    /// desarrollo. Contra un servidor remoto se exige HTTPS: enviar
    /// credenciales y datos personales en claro por la red no es una decision
    /// que deba poder tomarse por descuido al teclear la direccion.
    /// </remarks>
    public static (bool Valida, string? Error) Validar(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return (false, "Introduce la direccion del servidor.");
        }

        if (!Uri.TryCreate(Normalizar(url), UriKind.Absolute, out Uri? uri))
        {
            return (false, "La dirección no tiene un formato válido. Ejemplo: https://servidor:7015/");
        }

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            return (false, "La direccion debe empezar por http:// o https://");
        }

        if (uri.Scheme == Uri.UriSchemeHttp && !EsLocal(uri))
        {
            return (false,
                "Con un servidor remoto se exige https://. Por http la contraseña " +
                "y los datos viajarian sin cifrar.");
        }

        return (true, null);
    }

    private static bool EsLocal(Uri uri) =>
        uri.IsLoopback ||
        uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Garantiza la barra final: las rutas se construyen con Uri relativos y
    /// sin ella el ultimo segmento se pierde.
    /// </summary>
    public static string Normalizar(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return "";
        }

        url = url.Trim();
        return url.EndsWith('/') ? url : url + "/";
    }

    public static void Cargar()
    {
        try
        {
            if (!File.Exists(Archivo))
            {
                return;
            }

            _datos = JsonSerializer.Deserialize<Datos>(File.ReadAllText(Archivo)) ?? new Datos();
        }
        catch (Exception e)
        {
            // Una configuracion corrupta no debe impedir arrancar.
            Log.Error("No se pudo leer la configuracion", e);
            _datos = new Datos();
        }
    }

    public static void Guardar()
    {
        try
        {
            Directory.CreateDirectory(RutasApp.CarpetaDatos);

            File.WriteAllText(Archivo, JsonSerializer.Serialize(
                _datos, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception e)
        {
            Log.Error("No se pudo guardar la configuracion", e);
        }
    }
}
