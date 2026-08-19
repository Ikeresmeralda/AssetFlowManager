using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AssetFlow.Core.Configuration;
using AssetFlow.Core.Diagnostics;
using AssetFlow.Core.Http;

namespace AssetFlow.Desktop.Dialogs
{
    /// <summary>
    /// Configuración de la dirección del servidor.
    /// </summary>
    public partial class ServidorDialog : Window
    {
        public ServidorDialog()
        {
            InitializeComponent();

            TxtDireccion.Text = AppSettings.ApiServer;
            Loaded += (s, e) => { TxtDireccion.Focus(); TxtDireccion.SelectAll(); };
        }

        private void AlEscribir(object sender, TextChangedEventArgs e)
        {
            PanelResultado.Visibility = Visibility.Collapsed;
        }

        private async void AlProbar(object sender, RoutedEventArgs e)
        {
            string direccion = Normalizar(TxtDireccion.Text);

            if (string.IsNullOrWhiteSpace(direccion))
            {
                Resultado(false, "Introduce una dirección.");
                return;
            }

            BtnProbar.IsEnabled = false;
            BtnProbar.Content = "Probando…";

            try
            {
                (bool ok, string mensaje) = await ServerProbe.ProbarAsync(direccion);
                Resultado(ok, mensaje);
            }
            catch (Exception ex)
            {
                Log.Error("Fallo al probar el servidor", ex);
                Resultado(false, "No se ha podido conectar: " + ex.Message);
            }
            finally
            {
                BtnProbar.IsEnabled = true;
                BtnProbar.Content = "Probar conexión";
            }
        }

        private void Resultado(bool correcto, string mensaje)
        {
            PanelResultado.Style = (Style)FindResource(correcto ? "Badge.Success" : "Badge.Danger");
            IcoResultado.Text = (string)FindResource(correcto ? "Ico.Success" : "Ico.Error");

            var color = (Brush)FindResource(correcto ? "Success" : "Danger");
            IcoResultado.Foreground = color;
            TxtResultado.Foreground = color;

            TxtResultado.Text = mensaje;
            PanelResultado.Visibility = Visibility.Visible;
        }

        private void AlGuardar(object sender, RoutedEventArgs e)
        {
            string direccion = Normalizar(TxtDireccion.Text);

            if (string.IsNullOrWhiteSpace(direccion))
            {
                Resultado(false, "Introduce una dirección.");
                return;
            }

            // La comprobación de conexión sigue siendo opcional (puede
            // configurarse antes de levantar el servidor), pero la validación
            // del formato y del esquema no lo es: guardar una dirección http
            // remota significaría enviar la contraseña sin cifrar.
            (bool valida, string error) = AppSettings.Validar(direccion);

            if (!valida)
            {
                Resultado(false, error);
                return;
            }

            AppSettings.ApiServer = direccion;
            AppSettings.Guardar();

            Log.Info("Servidor configurado: " + AppSettings.ApiServer);

            DialogResult = true;
            Close();
        }

        private void AlCancelar(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private static string Normalizar(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return "";

            url = url.Trim();
            return url.EndsWith("/") ? url : url + "/";
        }
    }
}
