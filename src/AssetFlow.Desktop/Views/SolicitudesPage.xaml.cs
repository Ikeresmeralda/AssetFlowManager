using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AssetFlow.Core.Dtos;
using AssetFlow.Core.Http;
using AssetFlow.Core.Services;
using AssetFlow.Desktop.Dialogs;

namespace AssetFlow.Desktop.Views
{
    /// <summary>
    /// Bandeja de solicitudes de recuperación de contraseña. Sólo administradores.
    /// </summary>
    public partial class SolicitudesPage : UserControl
    {
        private readonly RecuperacionesService _servicio = App.Obtener<RecuperacionesService>();

        /// <summary>
        /// Evita que un doble clic lance dos decisiones sobre la misma
        /// solicitud. La segunda recibiría un 409 del servidor, así que no es
        /// un problema de integridad, pero sí un mensaje de error absurdo.
        /// </summary>
        private bool _operando;

        public SolicitudesPage()
        {
            InitializeComponent();

            Loaded += async (s, e) => await CargarAsync();
        }

        /// <summary>Se dispara al resolver una solicitud, para refrescar el aviso.</summary>
        public event Action PendientesCambiaron;

        public async Task CargarAsync()
        {
            bool soloPendientes = ChkSoloPendientes.IsChecked == true;

            ApiResult<List<PasswordResetRequestDto>> resultado =
                await _servicio.ListarAsync(soloPendientes);

            if (!resultado.EsCorrecto || resultado.Valor is null)
            {
                MostrarAviso(resultado.MensajeParaUsuario());
                return;
            }

            OcultarAviso();

            List<FilaSolicitud> filas = resultado.Valor.Select(s => new FilaSolicitud(s)).ToList();

            Tabla.ItemsSource = filas;

            TxtVacio.Text = soloPendientes
                ? "No hay ninguna solicitud pendiente."
                : "No hay ninguna solicitud registrada.";

            TxtVacio.Visibility = filas.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            Tabla.Visibility = filas.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        }

        private async void AlRecargar(object sender, RoutedEventArgs e) => await CargarAsync();

        private async void AlCambiarFiltro(object sender, RoutedEventArgs e)
        {
            // El evento salta al construir la ventana, antes de que exista la
            // tabla. Sin esta guarda, la página revienta al abrirse.
            if (!IsLoaded) return;

            await CargarAsync();
        }

        private async void AlAprobar(object sender, RoutedEventArgs e)
        {
            if (_operando) return;
            if (sender is not Button boton || boton.Tag is not int id) return;

            FilaSolicitud fila = Fila(id);
            if (fila is null) return;

            // Aprobar entrega el acceso a una cuenta. La comprobación de que
            // quien lo pide es quien dice ser no la puede hacer el sistema.
            bool seguir = Aviso.Confirmar(Window.GetWindow(this),
                "Aprobar la recuperación",
                $"Vas a dar acceso a la cuenta «{fila.Username}» ({fila.FullName}).\n\n" +
                "Asegúrate primero, por teléfono o en persona, de que quien lo ha " +
                "pedido es realmente esa persona: quien reciba la contraseña " +
                "provisional entrará en su cuenta.",
                "Aprobar");

            if (!seguir) return;

            _operando = true;

            try
            {
                ApiResult<PasswordResetApprovalDto> resultado = await _servicio.AprobarAsync(id);

                if (!resultado.EsCorrecto || resultado.Valor is null)
                {
                    MostrarAviso(resultado.MensajeParaUsuario());
                    return;
                }

                await CargarAsync();
                PendientesCambiaron?.Invoke();

                Aviso.Exito(Window.GetWindow(this),
                    "Contraseña provisional",
                    $"La contraseña provisional de «{resultado.Valor.Username}» es:\n\n" +
                    $"        {resultado.Valor.ContrasenaProvisional}\n\n" +
                    "Comunícasela a esa persona. Al entrar, la aplicación le pedirá " +
                    "que elija una contraseña propia, y esta provisional dejará de " +
                    "funcionar.");
            }
            finally
            {
                _operando = false;
            }
        }

        private async void AlDenegar(object sender, RoutedEventArgs e)
        {
            if (_operando) return;
            if (sender is not Button boton || boton.Tag is not int id) return;

            FilaSolicitud fila = Fila(id);
            if (fila is null) return;

            bool seguir = Aviso.Confirmar(Window.GetWindow(this),
                "Denegar la recuperación",
                $"La solicitud de «{fila.Username}» quedará denegada y su contraseña " +
                "no cambiará.",
                "Denegar");

            if (!seguir) return;

            _operando = true;

            try
            {
                ApiResult resultado = await _servicio.DenegarAsync(id);

                if (!resultado.EsCorrecto)
                {
                    MostrarAviso(resultado.MensajeParaUsuario());
                    return;
                }

                await CargarAsync();
                PendientesCambiaron?.Invoke();
            }
            finally
            {
                _operando = false;
            }
        }

        private FilaSolicitud Fila(int id) =>
            (Tabla.ItemsSource as IEnumerable<FilaSolicitud>)?.FirstOrDefault(f => f.Id == id);

        public void ProcesarAtajo(KeyEventArgs e)
        {
            if (e.Key == Key.F5)
            {
                _ = CargarAsync();
                e.Handled = true;
            }
        }

        private void MostrarAviso(string mensaje)
        {
            TxtAviso.Text = mensaje;
            PanelAviso.Visibility = Visibility.Visible;
        }

        private void OcultarAviso() => PanelAviso.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// Adaptador de una solicitud para la tabla.
    /// </summary>
    /// <remarks>
    /// Traduce a lo que la interfaz necesita pintar: la fecha en horario local
    /// y la visibilidad de los botones. Ocultarlos es comodidad, no seguridad:
    /// el servidor rechaza igualmente decidir sobre una solicitud ya resuelta.
    /// </remarks>
    public sealed class FilaSolicitud
    {
        private readonly PasswordResetRequestDto _origen;

        public FilaSolicitud(PasswordResetRequestDto origen) => _origen = origen;

        public int Id => _origen.Id;

        public string Username => _origen.Username;

        public string FullName => _origen.FullName;

        public string Estado => _origen.Estado;

        public string ResolvedByUsername => _origen.ResolvedByUsername ?? "—";

        /// <summary>
        /// La API devuelve UTC; aquí se muestra en la hora del equipo.
        /// </summary>
        public string SolicitadaTexto =>
            DateTime.SpecifyKind(_origen.RequestedAt, DateTimeKind.Utc)
                    .ToLocalTime()
                    .ToString("dd/MM/yyyy HH:mm");

        public Visibility VerAcciones =>
            _origen.EstaPendiente ? Visibility.Visible : Visibility.Collapsed;
    }
}
