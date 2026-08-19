using System.Windows;
using System.Windows.Media;

namespace AssetFlow.Desktop.Dialogs
{
    public partial class AvisoWindow : Window
    {
        public AvisoWindow()
        {
            InitializeComponent();
        }

        internal void Configurar(string titulo, string mensaje, string icono,
                                 Brush colorIcono, Brush fondoIcono,
                                 string textoConfirmar, string textoCancelar,
                                 bool peligroso)
        {
            Titulo.Text = titulo;
            Mensaje.Text = mensaje;
            Icono.Text = icono;
            Icono.Foreground = colorIcono;
            IconoFondo.Background = fondoIcono;

            BtnConfirmar.Content = textoConfirmar;

            if (textoCancelar != null)
            {
                BtnCancelar.Content = textoCancelar;
                BtnCancelar.Visibility = Visibility.Visible;
            }

            if (peligroso)
            {
                BtnConfirmar.Style = (Style)FindResource("Btn.Danger");
                // En una acción destructiva el foco arranca en Cancelar: pulsar
                // Intro por inercia no debe borrar nada.
                BtnCancelar.IsDefault = true;
                Loaded += (s, e) => BtnCancelar.Focus();
            }
            else
            {
                BtnConfirmar.IsDefault = true;
                Loaded += (s, e) => BtnConfirmar.Focus();
            }
        }

        private void AlConfirmar(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void AlCancelar(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
