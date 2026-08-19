using System.Text;
using System.Text.RegularExpressions;
using AssetFlow.Core.Configuration;

namespace AssetFlow.Core.Diagnostics;

/// <summary>
/// Registro de la aplicacion en archivo.
/// </summary>
/// <remarks>
/// Antes los fallos se escribian con Console.WriteLine, invisible en una
/// aplicacion de escritorio: cuando algo iba mal no quedaba rastro.
///
/// El registro pasa por un filtro de redaccion. Un archivo de log acaba
/// enviandose por correo para diagnosticar un problema, y no puede llevar
/// dentro tokens ni contrasenas: seria convertir la ayuda tecnica en una fuga
/// de credenciales.
/// </remarks>
public static partial class Log
{
    private const long TamanoMaximoBytes = 2 * 1024 * 1024;

    private static readonly object Candado = new();

    private static readonly string Archivo =
        Path.Combine(RutasApp.CarpetaRegistro, "inventario.log");

    public static string RutaArchivo => Archivo;

    public static void Info(string mensaje) => Escribir("INFO ", mensaje, null);

    public static void Aviso(string mensaje) => Escribir("AVISO", mensaje, null);

    public static void Error(string mensaje, Exception? ex = null) => Escribir("ERROR", mensaje, ex);

    // -----------------------------------------------------------------------
    // Redaccion
    // -----------------------------------------------------------------------

    [GeneratedRegex(@"(Bearer\s+)[A-Za-z0-9\-\._~\+/]+=*", RegexOptions.IgnoreCase)]
    private static partial Regex PatronBearer();

    [GeneratedRegex(@"(""?(?:password|contrasena|refreshToken|accessToken|token)""?\s*[:=]\s*""?)([^""',\s}]+)",
        RegexOptions.IgnoreCase)]
    private static partial Regex PatronCredencial();

    [GeneratedRegex(@"eyJ[A-Za-z0-9\-_]+\.[A-Za-z0-9\-_]+\.[A-Za-z0-9\-_]*")]
    private static partial Regex PatronJwt();

    /// <summary>
    /// Sustituye credenciales por marcadores. Es una red de seguridad, no una
    /// licencia para registrar secretos: el codigo tampoco debe pasarlos.
    /// </summary>
    public static string Redactar(string texto)
    {
        if (string.IsNullOrEmpty(texto))
        {
            return texto;
        }

        texto = PatronBearer().Replace(texto, "$1[redactado]");
        texto = PatronJwt().Replace(texto, "[jwt-redactado]");
        texto = PatronCredencial().Replace(texto, "$1[redactado]");

        return texto;
    }

    private static void Escribir(string nivel, string mensaje, Exception? ex)
    {
        try
        {
            lock (Candado)
            {
                Directory.CreateDirectory(RutasApp.CarpetaRegistro);
                Rotar();

                var sb = new StringBuilder();

                sb.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"))
                  .Append("  ").Append(nivel).Append("  ").Append(Redactar(mensaje));

                if (ex is not null)
                {
                    // Solo tipo y mensaje, no la traza completa: la traza
                    // ocupa mucho y rara vez aporta en un cliente. Si hiciera
                    // falta, se anade aqui de forma consciente.
                    sb.AppendLine()
                      .Append("    ").Append(ex.GetType().Name)
                      .Append(": ").Append(Redactar(ex.Message));
                }

                // UTF-8 con BOM: sin el, las herramientas que no detectan la
                // codificacion (el Bloc de notas, Get-Content de PowerShell)
                // muestran "conexiÃ³n" en lugar de "conexión".
                //
                // El BOM se escribe a mano al crear el archivo porque
                // File.AppendAllText no escribe nunca el preambulo, ni siquiera
                // cuando crea el archivo: pasarle un UTF8Encoding(true) no
                // basta, y el registro se quedaba sin marca.
                var codificacion = new UTF8Encoding(true);

                if (!File.Exists(Archivo))
                {
                    File.WriteAllBytes(Archivo, codificacion.GetPreamble());
                }

                File.AppendAllText(Archivo, sb.ToString() + Environment.NewLine,
                    codificacion);
            }
        }
        catch
        {
            // El registro nunca debe tumbar la aplicacion.
        }
    }

    private static void Rotar()
    {
        var info = new FileInfo(Archivo);

        if (!info.Exists || info.Length < TamanoMaximoBytes)
        {
            return;
        }

        string anterior = Archivo + ".1";

        if (File.Exists(anterior))
        {
            File.Delete(anterior);
        }

        File.Move(Archivo, anterior);
    }
}
