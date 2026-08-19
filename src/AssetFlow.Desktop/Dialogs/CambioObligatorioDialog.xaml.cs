using System;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows;
using AssetFlow.Core.Http;
using AssetFlow.Core.Services;

namespace AssetFlow.Desktop.Dialogs
{
    /// <summary>
    /// Cambio obligatorio de la contraseña provisional.
    /// </summary>
    /// <remarks>
    /// No se puede cerrar sin resolverla: o se cambia la contraseña o se cierra
    /// la sesión. Ver el comentario del XAML para el porqué.
    /// </remarks>
    public partial class CambioObligatorioDialog : Window
    {
        private const int LongitudMinimaClave = 10;

        private readonly AuthService _auth = App.Obtener<AuthService>();

        private bool _trabajando;
        private bool _puedeCerrarse;

        public CambioObligatorioDialog(string nombreUsuario)
        {
            InitializeComponent();

            Subtitulo.Text = $"Hola, {nombreUsuario}. Estás usando una contraseña " +
                             "provisional: elige una propia para continuar.";

            Loaded += (s, e) => TxtActual.Focus();
        }

        /// <summary>Cierto si la contraseña llegó a cambiarse.</summary>
        public bool ContrasenaCambiada { get; private set; }

        /// <summary>
        /// Impide cerrar la ventana con Alt+F4 o con la X del sistema.
        /// </summary>
        /// <remarks>
        /// Sin esto, cerrar la ventana dejaría la aplicación abierta con una
        /// sesión que no puede hacer nada: cada pantalla respondería con el
        /// error del servidor y no habría forma de volver aquí.
        /// </remarks>
        protected override void OnClosing(CancelEventArgs e)
        {
            if (!_puedeCerrarse)
            {
                e.Cancel = true;
                return;
            }

            base.OnClosing(e);
        }

        private async void AlAceptar(object sender, RoutedEventArgs e)
        {
            if (_trabajando) return;

            await CambiarAsync();
        }

        private async Task CambiarAsync()
        {
            string actual = TxtActual.Password;
            string clave = TxtClave.Password;
            string repetida = TxtRepetir.Password;

            if (actual.Length == 0)
            {
                MostrarError("Escribe la contraseña provisional que te han dado.");
                TxtActual.Focus();
                return;
            }

            if (clave.Length < LongitudMinimaClave)
            {
                MostrarError($"La contraseña debe tener al menos {LongitudMinimaClave} caracteres.");
                TxtClave.Focus();
                return;
            }

            if (clave != repetida)
            {
                MostrarError("Las dos contraseñas no coinciden.");
                TxtRepetir.Focus();
                return;
            }

            // Se comprueba también aquí, aunque el servidor lo rechaza igual:
            // así el usuario se entera antes de gastar una petición.
            if (clave == actual)
            {
                MostrarError("La contraseña nueva no puede ser la provisional.");
                TxtClave.Focus();
                return;
            }

            using (Ocupado("Guardando…"))
            {
                ApiResult resultado = await _auth.CambiarContrasenaProvisionalAsync(actual, clave);

                if (!resultado.EsCorrecto)
                {
                    MostrarError(resultado.Status == ApiStatus.Offline
                        ? "Sin conexión con el servidor. Inténtalo de nuevo en un momento."
                        : resultado.MensajeParaUsuario());

                    TxtActual.Focus();
                    TxtActual.SelectAll();
                    return;
                }
            }

            ContrasenaCambiada = true;
            _puedeCerrarse = true;
            DialogResult = true;
        }

        private void AlCerrarSesion(object sender, RoutedEventArgs e)
        {
            _puedeCerrarse = true;
            DialogResult = false;
        }

        private IDisposable Ocupado(string texto)
        {
            _trabajando = true;

            string original = (string)BtnAccion.Content;

            BtnAccion.Content = texto;
            BtnAccion.IsEnabled = false;
            BtnSalir.IsEnabled = false;
            Cursor = System.Windows.Input.Cursors.Wait;

            OcultarError();

            return new AlSalir(() =>
            {
                _trabajando = false;
                BtnAccion.Content = original;
                BtnAccion.IsEnabled = true;
                BtnSalir.IsEnabled = true;
                Cursor = null;
            });
        }

        private void AlEscribir(object sender, RoutedEventArgs e) => OcultarError();

        private void MostrarError(string mensaje)
        {
            TxtError.Text = mensaje;
            PanelError.Visibility = Visibility.Visible;
        }

        private void OcultarError() => PanelError.Visibility = Visibility.Collapsed;

        private sealed class AlSalir : IDisposable
        {
            private readonly Action _accion;

            public AlSalir(Action accion) => _accion = accion;

            public void Dispose() => _accion();
        }
    }
}
