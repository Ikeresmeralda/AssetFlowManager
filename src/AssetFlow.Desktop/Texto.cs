using System.Globalization;

namespace AssetFlow.Desktop
{
    /// <summary>
    /// Formato de textos de interfaz.
    /// </summary>
    internal static class Texto
    {
        /// <summary>
        /// Devuelve la cifra con su sustantivo concordado: «1 cuenta»,
        /// «10 cuentas».
        /// </summary>
        /// <remarks>
        /// Las tres pantallas mostraban «1 cuentas», «1 artículos» y
        /// «1 préstamos». Se resuelve en un único sitio porque el recuento
        /// aparece en el pie de tabla, en la barra de estado y en los avisos
        /// de exportación, y arreglarlo cadena a cadena garantizaba que el
        /// siguiente listado volviera a fallar.
        /// </remarks>
        public static string Contar(int cantidad, string singular, string plural)
        {
            string cifra = cantidad.ToString("N0", CultureInfo.CurrentCulture);
            return cifra + " " + (cantidad == 1 ? singular : plural);
        }

        /// <summary>
        /// Igual que <see cref="Contar"/> pero con un participio que también
        /// concuerda: «1 cuenta cargada», «10 cuentas cargadas».
        /// </summary>
        public static string Contar(int cantidad, string singular, string plural,
                                    string participioSingular, string participioPlural)
        {
            return Contar(cantidad, singular, plural) + " " +
                   (cantidad == 1 ? participioSingular : participioPlural);
        }
    }
}
