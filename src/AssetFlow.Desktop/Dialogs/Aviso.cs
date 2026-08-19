using System.Windows;
using System.Windows.Media;

namespace AssetFlow.Desktop.Dialogs
{
    /// <summary>
    /// Punto único para pedir confirmación o informar al usuario.
    /// </summary>
    /// <remarks>
    /// Antes cada pantalla llamaba a MessageBox.Show con textos distintos
    /// ("Error al eliminar el cliente" en la pantalla de materiales) y sin
    /// iconos coherentes. Aquí el tono y la apariencia son siempre los mismos.
    /// </remarks>
    public static class Aviso
    {
        /// <summary>
        /// Pregunta antes de una acción con consecuencias.
        /// </summary>
        /// <param name="textoAccion">
        /// Debe nombrar la acción ("Eliminar", "Guardar"), no "Aceptar".
        /// </param>
        /// <param name="peligroso">
        /// Si es true, el botón es rojo y el foco arranca en Cancelar.
        /// </param>
        public static bool Confirmar(Window propietario, string titulo, string mensaje,
                                     string textoAccion = "Continuar", bool peligroso = false)
        {
            var w = Crear(propietario);

            w.Configurar(titulo, mensaje,
                Rec<string>(peligroso ? "Ico.Warning" : "Ico.Info"),
                Rec<Brush>(peligroso ? "Danger" : "Accent"),
                Rec<Brush>(peligroso ? "DangerSoft" : "AccentSoft"),
                textoAccion, "Cancelar", peligroso);

            return w.ShowDialog() == true;
        }

        public static void Error(Window propietario, string titulo, string mensaje)
        {
            var w = Crear(propietario);
            w.Configurar(titulo, mensaje,
                Rec<string>("Ico.Error"), Rec<Brush>("Danger"), Rec<Brush>("DangerSoft"),
                "Entendido", null, false);
            w.ShowDialog();
        }

        public static void Info(Window propietario, string titulo, string mensaje)
        {
            var w = Crear(propietario);
            w.Configurar(titulo, mensaje,
                Rec<string>("Ico.Info"), Rec<Brush>("Accent"), Rec<Brush>("AccentSoft"),
                "Entendido", null, false);
            w.ShowDialog();
        }

        public static void Exito(Window propietario, string titulo, string mensaje)
        {
            var w = Crear(propietario);
            w.Configurar(titulo, mensaje,
                Rec<string>("Ico.Success"), Rec<Brush>("Success"), Rec<Brush>("SuccessSoft"),
                "Entendido", null, false);
            w.ShowDialog();
        }

        private static AvisoWindow Crear(Window propietario)
        {
            var w = new AvisoWindow();
            if (propietario != null && propietario.IsLoaded) w.Owner = propietario;
            return w;
        }

        private static T Rec<T>(string clave) => (T)Application.Current.FindResource(clave);
    }
}
