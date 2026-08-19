using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace AssetFlow.Desktop.Dialogs
{
    /// <summary>
    /// Confirma una acción y recoge la nota que la acompaña.
    /// </summary>
    public partial class NotaDialog : Window
    {
        private int _maximo = 255;

        public NotaDialog()
        {
            InitializeComponent();
            Loaded += (s, e) => TxtNota.Focus();
        }

        /// <summary>Texto escrito, ya recortado. Cadena vacía si no se escribió nada.</summary>
        public string Nota { get; private set; } = "";

        /// <summary>
        /// Pide confirmación con una nota opcional.
        /// </summary>
        /// <returns>
        /// El texto introducido, o <c>null</c> si se canceló. Se distingue
        /// <c>null</c> de la cadena vacía a propósito: «he cancelado» y «he
        /// confirmado sin escribir nada» son dos respuestas distintas, y
        /// confundirlas ejecutaría la acción que el usuario acababa de
        /// rechazar.
        /// </returns>
        public static string Pedir(
            Window propietario, string titulo, string mensaje, string etiqueta,
            string textoAccion = "Continuar", bool peligroso = false, int maximo = 255)
        {
            var w = new NotaDialog();

            if (propietario != null && propietario.IsLoaded)
            {
                w.Owner = propietario;
            }

            w.Titulo.Text = titulo;
            w.Mensaje.Text = mensaje;
            w.EtiquetaNota.Text = etiqueta;
            w.BtnConfirmar.Content = textoAccion;
            w._maximo = maximo;
            w.TxtNota.MaxLength = maximo;

            AutomationProperties.SetName(w.TxtNota, etiqueta);
            AutomationProperties.SetLabeledBy(w.TxtNota, w.EtiquetaNota);

            if (peligroso)
            {
                w.Icono.Text = (string)Application.Current.FindResource("Ico.Warning");
                w.Icono.Foreground = (Brush)Application.Current.FindResource("Danger");
                w.IconoFondo.Background = (Brush)Application.Current.FindResource("DangerSoft");
                w.BtnConfirmar.Style = (Style)Application.Current.FindResource("Btn.Danger");

                // En una acción sin vuelta atrás el foco arranca en Cancelar:
                // pulsar Intro por inercia no debe ejecutarla.
                w.Loaded += (s, e) => w.BtnCancelar.Focus();
            }

            w.ActualizarRestantes();

            return w.ShowDialog() == true ? w.Nota : null;
        }

        private void AlEscribir(object sender, TextChangedEventArgs e) => ActualizarRestantes();

        private void ActualizarRestantes()
        {
            int restantes = _maximo - TxtNota.Text.Length;

            // El contador sólo aparece cerca del límite: enseñarlo siempre es
            // ruido, y no enseñarlo nunca deja al usuario sin saber por qué
            // deja de poder escribir.
            TxtRestantes.Text = restantes <= 40
                ? $"{restantes} caracteres restantes"
                : "";
        }

        private void AlConfirmar(object sender, RoutedEventArgs e)
        {
            Nota = TxtNota.Text.Trim();
            DialogResult = true;
        }

        private void AlCancelar(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}
