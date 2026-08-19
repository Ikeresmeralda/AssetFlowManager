using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using AssetFlow.Core.Dtos;
using AssetFlow.Core.Http;
using AssetFlow.Core.Security;
using AssetFlow.Core.Services;
using AssetFlow.Desktop.Dialogs;

namespace AssetFlow.Desktop.Views
{
    public partial class UsuariosPage : UserControl
    {
        private const int RetardoBusquedaMs = 220;

        private readonly UsersService _usuarios = App.Obtener<UsersService>();
        private readonly SessionState _sesion = App.Obtener<SessionState>();
        private readonly DispatcherTimer _debounce;

        private List<UserDto> _todos = new List<UserDto>();

        private CancellationTokenSource _consultaActual;

        public event Action<string> EstadoCambiado;

        public UsuariosPage()
        {
            InitializeComponent();

            _debounce = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(RetardoBusquedaMs)
            };
            _debounce.Tick += (s, e) => { _debounce.Stop(); AplicarFiltroYPintar(); };

            Loaded += AlCargarVista;
        }

        private async void AlCargarVista(object sender, RoutedEventArgs e)
        {
            TxtBuscar.Focus();
            await CargarAsync();
        }

        // ============================================================
        // CARGA
        // ============================================================

        private async Task CargarAsync()
        {
            _consultaActual?.Cancel();
            _consultaActual = new CancellationTokenSource();
            CancellationToken ct = _consultaActual.Token;

            Estado.MostrarCargando();
            Informar("Consultando…");

            ApiResult<List<UserDto>> resultado = await _usuarios.ListarAsync(ct);

            if (ct.IsCancellationRequested || resultado.Status == ApiStatus.Cancelled)
            {
                return;
            }

            if (!resultado.EsCorrecto)
            {
                _todos = new List<UserDto>();
                Tabla.ItemsSource = null;
                ActualizarResumen();

                // El 403 aquí solo puede darse si a alguien se le ha retirado
                // el rol mientras tenía la pantalla abierta. Merece un texto
                // propio, no "error del servidor".
                string titulo = resultado.Status switch
                {
                    ApiStatus.Offline => "Sin conexión con el servidor",
                    ApiStatus.Forbidden => "Ya no tienes permiso de administrador",
                    _ => "No se han podido cargar las cuentas"
                };

                Estado.MostrarError(titulo, resultado.MensajeParaUsuario(),
                    resultado.MereceReintento ? async () => await CargarAsync() : (Action)null);

                Informar(resultado.Status == ApiStatus.Offline ? "Sin conexión" : "Error");
                return;
            }

            _todos = resultado.Valor;
            AplicarFiltroYPintar();
            Informar(Texto.Contar(_todos.Count, "cuenta", "cuentas", "cargada", "cargadas"));
        }

        // ============================================================
        // FILTRADO
        // ============================================================

        private void AplicarFiltroYPintar()
        {
            IEnumerable<UserDto> vista = _todos;

            switch (CmbFiltro.SelectedIndex)
            {
                case 0: vista = vista.Where(u => u.IsActive); break;
                case 1: vista = vista.Where(u => u.EsAdministrador); break;
                case 2: vista = vista.Where(u => !u.IsActive); break;
                // case 3: todas
            }

            string termino = TxtBuscar.Text.Trim();
            BtnLimpiar.Visibility = termino.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

            if (termino.Length > 0)
            {
                const StringComparison Modo = StringComparison.CurrentCultureIgnoreCase;

                vista = vista.Where(u =>
                    (u.Username?.Contains(termino, Modo) ?? false) ||
                    (u.NombreCompleto?.Contains(termino, Modo) ?? false) ||
                    (u.Email?.Contains(termino, Modo) ?? false));
            }

            List<UserDto> lista = vista
                .OrderByDescending(u => u.EsAdministrador)
                .ThenBy(u => u.NombreCompleto)
                .ToList();

            Tabla.ItemsSource = lista;

            ActualizarResumen();
            ActualizarRecuento(lista.Count);

            if (lista.Count == 0)
            {
                Estado.MostrarSinResultados(termino, LimpiarTodosLosFiltros);
                return;
            }

            Estado.Ocultar();

            if (Tabla.SelectedIndex < 0)
            {
                Tabla.SelectedIndex = 0;
            }
        }

        private void ActualizarResumen()
        {
            var cultura = CultureInfo.CurrentCulture;

            TxtActivas.Text = _todos.Count(u => u.IsActive).ToString("N0", cultura);
            TxtAdmins.Text = _todos.Count(u => u.EsAdministrador && u.IsActive).ToString("N0", cultura);
            TxtConPrestamos.Text = _todos.Count(u => u.ActiveLoans > 0).ToString("N0", cultura);
        }

        private void ActualizarRecuento(int mostrados)
        {
            TxtRecuento.Text = mostrados == _todos.Count
                ? Texto.Contar(mostrados, "cuenta", "cuentas")
                : $"{mostrados} de " + Texto.Contar(_todos.Count, "cuenta", "cuentas");
        }

        private void Informar(string mensaje) => EstadoCambiado?.Invoke(mensaje);

        private void LimpiarTodosLosFiltros()
        {
            TxtBuscar.Text = "";
            CmbFiltro.SelectedIndex = 3;
        }

        // ============================================================
        // INTERACCION
        // ============================================================

        private void AlEscribirBusqueda(object sender, TextChangedEventArgs e)
        {
            _debounce.Stop();
            _debounce.Start();
        }

        private void AlPulsarTeclaEnBusqueda(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape && TxtBuscar.Text.Length > 0)
            {
                TxtBuscar.Text = "";
                e.Handled = true;
            }
            else if ((e.Key == Key.Down || e.Key == Key.Enter) && Tabla.Items.Count > 0)
            {
                Tabla.SelectedIndex = 0;
                Tabla.Focus();
                e.Handled = true;
            }
        }

        private void AlLimpiarBusqueda(object sender, RoutedEventArgs e)
        {
            TxtBuscar.Text = "";
            TxtBuscar.Focus();
        }

        private void AlCambiarFiltro(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded) return;
            AplicarFiltroYPintar();
        }

        private async void AlRefrescar(object sender, RoutedEventArgs e) => await CargarAsync();

        private void AlSeleccionarFila(object sender, SelectionChangedEventArgs e)
        {
            if (Tabla.SelectedItem != null)
            {
                Tabla.ScrollIntoView(Tabla.SelectedItem);
            }
        }

        private void AlDobleClic(object sender, MouseButtonEventArgs e)
        {
            if (Tabla.SelectedItem is UserDto u) Editar(u);
        }

        private void AlEditarFila(object sender, RoutedEventArgs e)
        {
            if (((FrameworkElement)sender).DataContext is UserDto u) Editar(u);
        }

        private void AlCambiarAccesoFila(object sender, RoutedEventArgs e)
        {
            if (((FrameworkElement)sender).DataContext is UserDto u) CambiarAcceso(u);
        }

        private void AlEliminarFila(object sender, RoutedEventArgs e)
        {
            if (((FrameworkElement)sender).DataContext is UserDto u) Eliminar(u);
        }

        private void AlCrear(object sender, RoutedEventArgs e) => Editar(null);

        // ============================================================
        // OPERACIONES
        // ============================================================

        private async void Editar(UserDto usuario)
        {
            var dlg = new UsuarioDialog(usuario) { Owner = Window.GetWindow(this) };

            if (dlg.ShowDialog() != true) return;

            ApiResult resultado;

            if (usuario == null)
            {
                Informar("Creando cuenta…");
                resultado = await _usuarios.CrearAsync(dlg.PeticionAlta);
            }
            else
            {
                Informar("Guardando cambios…");
                resultado = await _usuarios.ActualizarAsync(usuario.Id, dlg.PeticionEdicion);
            }

            if (!resultado.EsCorrecto)
            {
                Aviso.Error(Window.GetWindow(this),
                    usuario == null ? "No se ha podido crear la cuenta" : "No se ha podido guardar",
                    resultado.MensajeParaUsuario());

                Informar("Error al guardar");
                return;
            }

            Informar(usuario == null ? "Cuenta creada" : "Cambios guardados");
            await CargarAsync();
        }

        private async void CambiarAcceso(UserDto usuario)
        {
            if (usuario == null) return;

            var dlg = new AccesoDialog(usuario) { Owner = Window.GetWindow(this) };

            if (dlg.ShowDialog() != true) return;

            if (dlg.ReiniciarContrasena)
            {
                ApiResult<PasswordResetApprovalDto> reinicio =
                    await _usuarios.ReiniciarContrasenaAsync(usuario.Id);

                if (!reinicio.EsCorrecto || reinicio.Valor is null)
                {
                    Aviso.Error(Window.GetWindow(this),
                        "No se ha podido reiniciar la contraseña",
                        reinicio.MensajeParaUsuario());
                    return;
                }

                Aviso.Info(Window.GetWindow(this), "Contraseña provisional",
                    $"La contraseña provisional de «{reinicio.Valor.Username}» es:\n\n" +
                    $"        {reinicio.Valor.ContrasenaProvisional}\n\n" +
                    "Comunícasela a esa persona. Al entrar, la aplicación le pedirá que " +
                    "elija una contraseña propia, y esta provisional dejará de funcionar.\n\n" +
                    "Se han cerrado todas sus sesiones abiertas.");
            }

            if (dlg.CambiaAcceso)
            {
                ApiResult acceso = await _usuarios.CambiarAccesoAsync(
                    usuario.Id, new UpdateUserAccessRequest(dlg.Rol, dlg.Activa));

                if (!acceso.EsCorrecto)
                {
                    Aviso.Error(Window.GetWindow(this),
                        "No se ha podido cambiar el acceso",
                        acceso.MensajeParaUsuario());
                    return;
                }

                Informar("Acceso actualizado");
            }

            await CargarAsync();
        }

        private async void Eliminar(UserDto usuario)
        {
            if (usuario == null) return;

            if (usuario.Id == _sesion.IdUsuario)
            {
                Aviso.Info(Window.GetWindow(this), "No es posible",
                    "No puedes eliminar tu propia cuenta.");
                return;
            }

            // El mensaje explica lo que va a pasar de verdad: si hay historial
            // la cuenta se desactiva en lugar de borrarse, y prometer un
            // borrado que no ocurre sería mentir al usuario.
            string detalle = usuario.ActiveLoans > 0
                ? $"«{usuario.NombreCompleto}» tiene {usuario.ActiveLoans} préstamos sin devolver.\n\n" +
                  "No se puede eliminar hasta que devuelva el material."
                : $"Se eliminará la cuenta de «{usuario.NombreCompleto}».\n\n" +
                  "Si tiene préstamos en el historial, la cuenta se desactivará en lugar " +
                  "de borrarse, para no dejar el histórico incompleto.";

            if (usuario.ActiveLoans > 0)
            {
                Aviso.Info(Window.GetWindow(this), "No se puede eliminar", detalle);
                return;
            }

            if (!Aviso.Confirmar(Window.GetWindow(this), "Eliminar cuenta", detalle,
                    "Eliminar", peligroso: true))
            {
                return;
            }

            ApiResult resultado = await _usuarios.EliminarAsync(usuario.Id);

            if (!resultado.EsCorrecto)
            {
                Aviso.Error(Window.GetWindow(this),
                    "No se ha podido eliminar",
                    resultado.MensajeParaUsuario());
                return;
            }

            Informar("Cuenta eliminada");
            await CargarAsync();
        }

        // ============================================================
        // ATAJOS
        // ============================================================

        public void ProcesarAtajo(KeyEventArgs e)
        {
            bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;

            if (ctrl && e.Key == Key.N) { AlCrear(null, null); e.Handled = true; }
            else if (ctrl && e.Key == Key.F) { TxtBuscar.Focus(); TxtBuscar.SelectAll(); e.Handled = true; }
            else if (e.Key == Key.F5) { AlRefrescar(null, null); e.Handled = true; }
            else if (e.Key == Key.F2 && Tabla.SelectedItem is UserDto u)
            {
                Editar(u); e.Handled = true;
            }
        }
    }
}
