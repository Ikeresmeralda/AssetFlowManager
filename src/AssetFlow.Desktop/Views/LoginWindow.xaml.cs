using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using AssetFlow.Core.Configuration;
using AssetFlow.Core.Diagnostics;
using AssetFlow.Core.Http;
using AssetFlow.Core.Security;
using AssetFlow.Core.Services;
using AssetFlow.Desktop.Dialogs;

namespace AssetFlow.Desktop.Views
{
    public partial class LoginWindow : Window
    {
        private readonly AuthService _auth = App.Obtener<AuthService>();
        private readonly SessionState _sesion = App.Obtener<SessionState>();

        private bool _ocupado;

        public LoginWindow() : this(null)
        {
        }

        /// <param name="aviso">
        /// Motivo por el que se ha vuelto a esta pantalla, si procede. Volver
        /// al login sin explicar por que deja al usuario pensando que ha
        /// perdido su trabajo por un fallo.
        /// </param>
        public LoginWindow(string aviso)
        {
            InitializeComponent();

            ActualizarInfoServidor();

            TxtUsuario.Text = AppSettings.UltimoUsuario;
            ChkRecordar.IsChecked = AppSettings.RecordarSesion;

            if (!string.IsNullOrEmpty(aviso))
            {
                MostrarError(aviso);
            }

            Loaded += AlCargar;
        }

        private async void AlCargar(object sender, RoutedEventArgs e)
        {
            // El foco va al campo vacio: si ya hay usuario recordado, lo
            // logico es escribir directamente la contrasena.
            if (string.IsNullOrWhiteSpace(TxtUsuario.Text))
            {
                TxtUsuario.Focus();
            }
            else
            {
                TxtClave.Focus();
            }

            await IntentarReanudarAsync();
        }

        /// <summary>
        /// Reanuda la sesion guardada, si la hay, sin pedir credenciales.
        /// </summary>
        private async Task IntentarReanudarAsync()
        {
            if (!AppSettings.HayServidorConfigurado || !AppSettings.RecordarSesion)
            {
                return;
            }

            Ocupado(true, "Reanudando sesion…");

            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

                if (await _auth.ReanudarSesionAsync(cts.Token))
                {
                    AbrirAplicacion();
                    return;
                }
            }
            catch (Exception ex)
            {
                Log.Error("Fallo al reanudar la sesion", ex);
            }
            finally
            {
                Ocupado(false);
            }
        }

        private void ActualizarInfoServidor()
        {
            TxtServidorInfo.Text = AppSettings.HayServidorConfigurado
                ? "Servidor: " + AppSettings.ApiServer
                : "Servidor sin configurar";
        }

        private void AlEscribir(object sender, RoutedEventArgs e) => OcultarError();

        private void AlPulsarTecla(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                AlAcceder(null, null);
            }
        }

        private async void AlAcceder(object sender, RoutedEventArgs e)
        {
            if (_ocupado)
            {
                return;
            }

            string usuario = TxtUsuario.Text.Trim();
            string clave = TxtClave.Password;

            if (string.IsNullOrWhiteSpace(usuario))
            {
                MostrarError("Introduce tu nombre de usuario.");
                TxtUsuario.Focus();
                return;
            }

            if (string.IsNullOrEmpty(clave))
            {
                MostrarError("Introduce tu contraseña.");
                TxtClave.Focus();
                return;
            }

            if (!AppSettings.HayServidorConfigurado)
            {
                MostrarError("No hay ningún servidor configurado. Pulsa «Configurar servidor» para indicarlo.");
                return;
            }

            AppSettings.RecordarSesion = ChkRecordar.IsChecked == true;

            await ValidarAsync(usuario, clave);
        }

        private async Task ValidarAsync(string usuario, string clave)
        {
            Ocupado(true, "Comprobando…");
            OcultarError();

            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

                ApiResult resultado = await _auth.IniciarSesionAsync(usuario, clave, cts.Token);

                if (resultado.EsCorrecto)
                {
                    // La cuenta puede venir con una contrasena provisional. En
                    // ese caso la sesion no sirve para nada hasta cambiarla: el
                    // servidor rechaza cualquier otra peticion.
                    if (_sesion.Usuario?.MustChangePassword == true)
                    {
                        AbrirCambioObligatorio();
                        return;
                    }

                    AbrirAplicacion();
                    return;
                }

                // Cada motivo se explica de forma distinta. Antes todos los
                // fallos, incluida la caida del servidor, se mostraban como
                // "usuario o contrasena incorrectos", que es informacion falsa
                // y hace perder el tiempo al usuario buscando su contrasena.
                // El vaciado va ANTES de mostrar el error, no despues: Clear()
                // dispara PasswordChanged, que oculta el aviso. Al reves, el
                // mensaje se pintaba y se borraba en el mismo ciclo y el fallo
                // de credenciales quedaba mudo.
                if (resultado.Status == ApiStatus.Unauthenticated)
                {
                    TxtClave.Clear();
                    TxtClave.Focus();
                }

                MostrarError(resultado.Status switch
                {
                    ApiStatus.Unauthenticated => "Usuario o contraseña incorrectos.",
                    ApiStatus.TooManyRequests =>
                        "Demasiados intentos fallidos. Espera unos minutos antes de volver a probar.",
                    _ => resultado.MensajeParaUsuario()
                });
            }
            catch (Exception ex)
            {
                Log.Error("Error al iniciar sesion", ex);
                MostrarError("Se ha producido un error inesperado al iniciar sesión.");
            }
            finally
            {
                Ocupado(false);
            }
        }

        private void AbrirAplicacion()
        {
            var shell = new ShellWindow();
            Application.Current.MainWindow = shell;
            shell.Show();
            Close();
        }

        /// <summary>
        /// Obliga a cambiar la contraseña provisional antes de entrar.
        /// </summary>
        /// <remarks>
        /// Si la persona se echa atrás, se cierra la sesión y se vuelve al
        /// formulario: no se puede entrar arrastrando una contraseña que
        /// cualquiera puede deducir del nombre de usuario.
        /// </remarks>
        private async void AbrirCambioObligatorio()
        {
            string nombre = _sesion.Usuario?.FirstName ?? "";

            var dlg = new CambioObligatorioDialog(nombre) { Owner = this };

            if (dlg.ShowDialog() == true && dlg.ContrasenaCambiada)
            {
                AbrirAplicacion();
                return;
            }

            // Se echó atrás: la sesión no vale para nada, así que se cierra de
            // verdad (también en el servidor) en lugar de dejarla colgando.
            await _auth.CerrarSesionAsync();

            TxtClave.Clear();
            TxtClave.Focus();

            MostrarError("Tienes que elegir una contraseña nueva para poder entrar.");
        }

        private void Ocupado(bool ocupado, string texto = null)
        {
            _ocupado = ocupado;

            BtnAcceder.IsEnabled = !ocupado;
            TxtUsuario.IsEnabled = !ocupado;
            TxtClave.IsEnabled = !ocupado;
            BtnServidor.IsEnabled = !ocupado;
            ChkRecordar.IsEnabled = !ocupado;

            BtnAcceder.Content = ocupado ? (texto ?? "Comprobando…") : "Acceder";
        }

        private async void AlConfigurarServidor(object sender, RoutedEventArgs e)
        {
            var dlg = new ServidorDialog { Owner = this };

            if (dlg.ShowDialog() == true)
            {
                ActualizarInfoServidor();
                OcultarError();

                await IntentarReanudarAsync();
            }
        }

        /// <summary>
        /// Abre la solicitud de recuperación de contraseña.
        /// </summary>
        /// <remarks>
        /// La ventana sólo deja constancia de la solicitud: quien la resuelve
        /// es un administrador desde dentro de la aplicación. Aquí no hay nada
        /// que confirmar ni ninguna sesión que abrir.
        /// </remarks>
        private void AlRecuperar(object sender, RoutedEventArgs e)
        {
            var dlg = new RecuperacionDialog { Owner = this };

            dlg.ShowDialog();

            OcultarError();

            TxtClave.Clear();
        }

        private void MostrarError(string mensaje)
        {
            TxtError.Text = mensaje;
            PanelError.Visibility = Visibility.Visible;
        }

        private void OcultarError() => PanelError.Visibility = Visibility.Collapsed;
    }
}
