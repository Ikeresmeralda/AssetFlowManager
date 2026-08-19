using System;
using System.Threading.Tasks;
using System.Windows;
using AssetFlow.Core.Http;
using AssetFlow.Core.Services;

namespace AssetFlow.Desktop.Dialogs
{
    /// <summary>
    /// Solicitud de recuperación de contraseña, dirigida a un administrador.
    /// </summary>
    public partial class RecuperacionDialog : Window
    {
        private readonly AuthService _auth = App.Obtener<AuthService>();

        private bool _trabajando;

        public RecuperacionDialog()
        {
            InitializeComponent();
            Loaded += (s, e) => TxtCorreo.Focus();
        }

        /// <summary>Cierto si la solicitud llegó a registrarse.</summary>
        public bool SolicitudEnviada { get; private set; }

        private async void AlAceptar(object sender, RoutedEventArgs e)
        {
            if (_trabajando) return;

            await SolicitarAsync();
        }

        /// <summary>
        /// Registra la solicitud.
        /// </summary>
        /// <remarks>
        /// El mensaje que se muestra al terminar es siempre el mismo, y se
        /// muestra incluso cuando el correo no corresponde a ninguna cuenta:
        /// es el propio servidor el que responde igual en los dos casos.
        /// Cambiar el texto según lo que devolviera convertiría esta ventana en
        /// un comprobador de qué correos están dados de alta.
        /// </remarks>
        private async Task SolicitarAsync()
        {
            string correo = TxtCorreo.Text.Trim();

            // Validación de forma, no de existencia. Sirve para no gastar una
            // petición en algo que no es un correo; nunca para decidir si la
            // cuenta existe, que es cosa del servidor.
            if (correo.Length == 0)
            {
                MostrarError("Escribe el correo de tu cuenta.");
                TxtCorreo.Focus();
                return;
            }

            if (!PareceCorreo(correo))
            {
                MostrarError("Ese correo no tiene un formato válido.");
                TxtCorreo.Focus();
                return;
            }

            using (Ocupado("Enviando…"))
            {
                ApiResult resultado = await _auth.SolicitarRecuperacionAsync(correo);

                // Sólo se distinguen los fallos que no dicen nada de la cuenta:
                // sin red, o demasiadas peticiones.
                if (!resultado.EsCorrecto)
                {
                    MostrarError(resultado.Status == ApiStatus.Offline
                        ? "Sin conexión con el servidor. Inténtalo de nuevo en un momento."
                        : resultado.MensajeParaUsuario());
                    return;
                }
            }

            SolicitudEnviada = true;

            TxtCorreo.IsEnabled = false;
            BtnAccion.IsEnabled = false;
            BtnCancelar.Content = "Cerrar";

            MostrarInfo("Si existe una cuenta asociada a ese correo, un administrador " +
                        "recibirá tu solicitud. Ponte en contacto con esa persona para " +
                        "que te facilite la contraseña provisional.");
        }

        // ============================================================
        // ESTADO DE LA VENTANA
        // ============================================================

        /// <summary>
        /// Bloquea la ventana mientras hay una petición en curso y la
        /// desbloquea al salir del <c>using</c>, pase lo que pase.
        /// </summary>
        /// <remarks>
        /// Sin esto, pulsar dos veces «Enviar solicitud» manda dos peticiones
        /// seguidas y gasta el presupuesto del limitador sin motivo.
        /// </remarks>
        private IDisposable Ocupado(string texto)
        {
            _trabajando = true;

            string original = (string)BtnAccion.Content;

            BtnAccion.Content = texto;
            BtnAccion.IsEnabled = false;
            TxtCorreo.IsEnabled = false;
            Cursor = System.Windows.Input.Cursors.Wait;

            OcultarError();

            return new AlSalir(() =>
            {
                _trabajando = false;
                BtnAccion.Content = original;
                BtnAccion.IsEnabled = true;
                TxtCorreo.IsEnabled = true;
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

        private void MostrarInfo(string mensaje)
        {
            TxtInfo.Text = mensaje;
            PanelInfo.Visibility = Visibility.Visible;
        }

        private void AlCancelar(object sender, RoutedEventArgs e) => DialogResult = false;

        /// <summary>
        /// Comprobación de forma mínima. No pretende validar un correo según el
        /// RFC: eso lo hace el servidor, y aquí sólo evita gastar una petición
        /// en un texto que a simple vista no es una dirección.
        /// </summary>
        private static bool PareceCorreo(string valor)
        {
            int arroba = valor.IndexOf('@');

            return arroba > 0
                && arroba < valor.Length - 1
                && valor.IndexOf('@', arroba + 1) < 0
                && valor.IndexOf(' ') < 0
                && valor.LastIndexOf('.') > arroba + 1;
        }

        private sealed class AlSalir : IDisposable
        {
            private readonly Action _accion;

            public AlSalir(Action accion) => _accion = accion;

            public void Dispose() => _accion();
        }
    }
}
