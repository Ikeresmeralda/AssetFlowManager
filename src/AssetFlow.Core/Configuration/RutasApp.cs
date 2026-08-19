namespace AssetFlow.Core.Configuration;

/// <summary>Rutas de datos de la aplicacion en el perfil del usuario.</summary>
public static class RutasApp
{
    public static string CarpetaDatos { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "AssetFlow");

    public static string CarpetaRegistro { get; } = Path.Combine(CarpetaDatos, "logs");
}
