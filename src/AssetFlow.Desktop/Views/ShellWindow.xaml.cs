using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using AssetFlow.Core.Diagnostics;
using AssetFlow.Core.Dtos;
using AssetFlow.Core.Http;
using AssetFlow.Core.Security;
using AssetFlow.Core.Services;
using AssetFlow.Desktop.Dialogs;

namespace AssetFlow.Desktop.Views
{
    /// <summary>
    /// Ventana principal: navegación, cabecera y área de trabajo.
    /// </summary>
    public partial class ShellWindow : Window
    {
        private readonly DispatcherTimer _vigilanteConexion;
        private readonly SessionState _sesion = App.Obtener<SessionState>();
        private readonly AuthService _auth = App.Obtener<AuthService>();

        // Las paginas se conservan entre navegaciones: recrearlas en cada clic
        // obligaria a volver a consultar el servidor y perderia filtros y
        // posicion de desplazamiento.
        private InventarioPage _paginaInventario;
        private PrestamosPage _paginaPrestamos;
        private UsuariosPage _paginaUsuarios;
        private SolicitudesPage _paginaSolicitudes;

        private readonly RecuperacionesService _recuperaciones =
            App.Obtener<RecuperacionesService>();

        /// <summary>
        /// Sondea cada tanto si hay solicitudes de recuperación esperando.
        /// </summary>
        /// <remarks>
        /// Un minuto: quien ha perdido el acceso a su cuenta va a avisar por
        /// otra vía de todas formas, así que no hace falta más frecuencia, y
        /// consultar cada pocos segundos sería ruido contra el servidor. Sólo
        /// se activa para administradores, que son los únicos que pueden ver
        /// esas solicitudes.
        /// </remarks>
        private readonly DispatcherTimer _vigilanteSolicitudes;

        public ShellWindow()
        {
            InitializeComponent();

            TxtNombreUsuario.Text = _sesion.NombreCompleto;
            TxtIniciales.Text = _sesion.Iniciales();
            TxtRolUsuario.Text = _sesion.EsAdministrador ? "Administrador" : "Usuario";

            // Ocultar la seccion de administracion es solo comodidad visual:
            // quien manipule el cliente para mostrarla se encontrara con que
            // la API responde 403 a cada peticion.
            GrupoAdmin.Visibility = _sesion.EsAdministrador ? Visibility.Visible : Visibility.Collapsed;

            TxtVersion.Text = "v" + (System.Reflection.Assembly.GetExecutingAssembly()
                .GetName().Version?.ToString(3) ?? "1.0.0");

            // Estado de conexión visible de forma permanente: en una aplicación
            // que depende de un servidor, enterarse de que no hay red al fallar
            // una operación llega tarde.
            _vigilanteConexion = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
            _vigilanteConexion.Tick += async (s, e) => await ComprobarConexionAsync();

            _vigilanteSolicitudes = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
            _vigilanteSolicitudes.Tick += async (s, e) => await ActualizarPendientesAsync();

            Loaded += async (s, e) =>
            {
                Navegar("Inventario");
                NavInventario.IsChecked = true;
                await ComprobarConexionAsync();
                _vigilanteConexion.Start();

                if (_sesion.EsAdministrador)
                {
                    await ActualizarPendientesAsync();
                    _vigilanteSolicitudes.Start();
                }
            };

            Closed += (s, e) =>
            {
                _vigilanteConexion.Stop();
                _vigilanteSolicitudes.Stop();
            };

            PreviewKeyDown += AlPulsarTecla;
        }

        // ============================================================
        // NAVEGACION
        // ============================================================

        private void AlCambiarSeccion(object sender, RoutedEventArgs e)
        {
            if (!IsLoaded) return;

            var boton = sender as RadioButton;
            Navegar(boton?.Content?.ToString() ?? "Inventario");
        }

        private void Navegar(string seccion)
        {
            switch (seccion)
            {
                case "Inventario":
                    TxtTituloSeccion.Text = "Inventario";
                    TxtSubtituloSeccion.Text = "Artículos disponibles y control de stock";
                    Contenido.Content = ObtenerInventario();
                    break;

                case "Préstamos":
                    TxtTituloSeccion.Text = "Préstamos";
                    TxtSubtituloSeccion.Text = _sesion.EsAdministrador
                        ? "Material entregado y pendiente de devolución"
                        : "El material que tienes prestado";
                    Contenido.Content = ObtenerPrestamos();
                    break;

                case "Usuarios":
                    TxtTituloSeccion.Text = "Usuarios";
                    TxtSubtituloSeccion.Text = "Cuentas y permisos";
                    Contenido.Content = ObtenerUsuarios();
                    break;

                case "Solicitudes":
                    TxtTituloSeccion.Text = "Solicitudes";
                    TxtSubtituloSeccion.Text = "Recuperaciones de contraseña pendientes de autorizar";
                    Contenido.Content = ObtenerSolicitudes();
                    break;

                default:
                    TxtTituloSeccion.Text = "Inventario";
                    TxtSubtituloSeccion.Text = "Artículos disponibles y control de stock";
                    Contenido.Content = ObtenerInventario();
                    break;
            }
        }

        private PrestamosPage ObtenerPrestamos()
        {
            if (_paginaPrestamos == null)
            {
                _paginaPrestamos = new PrestamosPage();
                _paginaPrestamos.EstadoCambiado += m => TxtEstado.Text = m;
            }
            return _paginaPrestamos;
        }

        private UsuariosPage ObtenerUsuarios()
        {
            if (_paginaUsuarios == null)
            {
                _paginaUsuarios = new UsuariosPage();
                _paginaUsuarios.EstadoCambiado += m => TxtEstado.Text = m;
            }
            return _paginaUsuarios;
        }

        private SolicitudesPage ObtenerSolicitudes()
        {
            if (_paginaSolicitudes == null)
            {
                _paginaSolicitudes = new SolicitudesPage();

                // Al resolver una solicitud, el contador del menú tiene que
                // bajar sin esperar al siguiente sondeo.
                _paginaSolicitudes.PendientesCambiaron +=
                    async () => await ActualizarPendientesAsync();
            }
            return _paginaSolicitudes;
        }

        /// <summary>
        /// Refresca el aviso de solicitudes pendientes del menú.
        /// </summary>
        /// <remarks>
        /// Los fallos se ignoran a propósito: si el servidor no responde, ya lo
        /// dice el indicador de conexión, y un error por cada sondeo fallido
        /// llenaría la pantalla de avisos por algo que es sólo informativo.
        /// </remarks>
        private async Task ActualizarPendientesAsync()
        {
            if (!_sesion.EsAdministrador) return;

            ApiResult<PendingCountDto> resultado =
                await _recuperaciones.ContarPendientesAsync();

            if (!resultado.EsCorrecto || resultado.Valor is null) return;

            int pendientes = resultado.Valor.Pendientes;

            TxtPendientes.Text = pendientes > 99 ? "99+" : pendientes.ToString();

            AvisoPendientes.Visibility = pendientes > 0
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        /// <summary>
        /// La página de inventario se conserva entre navegaciones: recrearla en
        /// cada clic obligaría a volver a consultar el servidor y perdería el
        /// filtro y la posición de desplazamiento.
        /// </summary>
        private InventarioPage ObtenerInventario()
        {
            if (_paginaInventario == null)
            {
                _paginaInventario = new InventarioPage();
                _paginaInventario.EstadoCambiado += m => TxtEstado.Text = m;
            }
            return _paginaInventario;
        }

        // ============================================================
        // CONEXION
        // ============================================================

        private async System.Threading.Tasks.Task ComprobarConexionAsync()
        {
            bool ok;
            try
            {
                ok = await _auth.ComprobarConexionAsync();
            }
            catch (Exception ex)
            {
                Log.Error("Fallo al comprobar la conexión", ex);
                ok = false;
            }

            ChipConexion.Style = (Style)FindResource(ok ? "Badge.Success" : "Badge.Danger");
            IcoConexion.Text = (string)FindResource(ok ? "Ico.Online" : "Ico.Offline");
            TxtConexion.Text = ok ? "Conectado" : "Sin conexión";

            var color = (Brush)FindResource(ok ? "Success" : "Danger");
            IcoConexion.Foreground = color;
            TxtConexion.Foreground = color;
        }

        // ============================================================
        // TECLADO
        // ============================================================

        private void AlPulsarTecla(object sender, KeyEventArgs e)
        {
            bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;

            // Acceso directo a las secciones, como en cualquier herramienta de gestión
            if (ctrl && e.Key == Key.D1) { NavInventario.IsChecked = true; e.Handled = true; return; }
            if (ctrl && e.Key == Key.D2) { NavPrestamos.IsChecked = true; e.Handled = true; return; }
            if (ctrl && e.Key == Key.D3 && _sesion.EsAdministrador)
            {
                NavUsuarios.IsChecked = true; e.Handled = true; return;
            }
            if (ctrl && e.Key == Key.D4 && _sesion.EsAdministrador)
            {
                NavSolicitudes.IsChecked = true; e.Handled = true; return;
            }

            switch (Contenido.Content)
            {
                case InventarioPage inventario: inventario.ProcesarAtajo(e); break;
                case PrestamosPage prestamos: prestamos.ProcesarAtajo(e); break;
                case UsuariosPage usuarios: usuarios.ProcesarAtajo(e); break;
                case SolicitudesPage solicitudes: solicitudes.ProcesarAtajo(e); break;
            }
        }

        private async void AlCerrarSesion(object sender, RoutedEventArgs e)
        {
            if (!Aviso.Confirmar(this, "Cerrar sesión",
                    "Se cerrará la sesión actual y volverás a la pantalla de acceso.",
                    "Cerrar sesión"))
                return;

            // Se avisa al servidor para que revoque el token de refresco. Sin
            // esto, «cerrar sesión» solo borraría datos del cliente y la
            // credencial seguiría siendo válida durante días.
            await _auth.CerrarSesionAsync();

            var login = new LoginWindow();
            Application.Current.MainWindow = login;
            login.Show();
            Close();
        }
    }
}
