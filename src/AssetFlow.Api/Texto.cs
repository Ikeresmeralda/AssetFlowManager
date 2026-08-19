namespace AssetFlow.Api;

/// <summary>
/// Redaccion de cantidades para los textos que ve una persona.
/// </summary>
/// <remarks>
/// Existe porque interpolar la cifra y anadir el plural a mano produce «1
/// sesiones revocadas» en cuanto la cuenta vale uno, y eso acaba leyendose en
/// la pantalla de auditoria. El cliente de escritorio tiene su propia copia:
/// son dos proyectos sin referencia entre si, y compartir una clase de tres
/// lineas no justifica crear un ensamblado comun.
/// </remarks>
public static class Texto
{
    /// <summary>
    /// Concuerda el sustantivo con la cifra: <c>Contar(1, "sesión", "sesiones")</c>
    /// devuelve «1 sesión».
    /// </summary>
    public static string Contar(int cantidad, string singular, string plural) =>
        $"{cantidad} {(cantidad == 1 ? singular : plural)}";

    /// <summary>
    /// Igual, concordando ademas el participio:
    /// <c>Contar(1, "sesión", "sesiones", "revocada", "revocadas")</c> devuelve
    /// «1 sesión revocada».
    /// </summary>
    public static string Contar(int cantidad, string singular, string plural,
                                string participioSingular, string participioPlural) =>
        cantidad == 1
            ? $"{cantidad} {singular} {participioSingular}"
            : $"{cantidad} {plural} {participioPlural}";
}
