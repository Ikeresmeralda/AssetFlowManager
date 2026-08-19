using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AssetFlow.Core.Dtos;
using AssetFlow.Core.Http;
using AssetFlow.Core.Security;
using AssetFlow.Core.Services;

namespace AssetFlow.Desktop.Dialogs
{
    /// <summary>
    /// Línea del préstamo que se está montando.
    /// </summary>
    /// <remarks>
    /// Implementa notificación de cambios para que la cantidad se refresque en
    /// la lista al pulsar los botones, sin tener que repintarla entera.
    /// </remarks>
    public sealed class LineaPrestamo : INotifyPropertyChanged
    {
        private int _cantidad;

        public LineaPrestamo(MaterialDto material)
        {
            MaterialId = material.Id;
            Nombre = material.Name;
            Disponibles = material.AvailableQuantity;
            _cantidad = 1;
        }

        public int MaterialId { get; }

        public string Nombre { get; }

        /// <summary>Tope que impone el inventario en el momento de abrir el diálogo.</summary>
        public int Disponibles { get; }

        public int Cantidad
        {
            get => _cantidad;
            set
            {
                // La cantidad se acota aquí y no en quien llama: así ningún
                // camino puede dejarla fuera de rango.
                int acotada = Math.Clamp(value, 1, Math.Max(1, Disponibles));

                if (_cantidad == acotada) return;

                _cantidad = acotada;
                Notificar();
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void Notificar([CallerMemberName] string propiedad = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propiedad));
    }

    public partial class PrestamoDialog : Window
    {
        private readonly MaterialsService _materiales = App.Obtener<MaterialsService>();
        private readonly UsersService _usuarios = App.Obtener<UsersService>();
        private readonly SessionState _sesion = App.Obtener<SessionState>();

        private readonly ObservableCollection<LineaPrestamo> _seleccion = new();

        private List<MaterialDto> _catalogo = new List<MaterialDto>();

        /// <summary>Petición lista para enviar. Válida si ShowDialog devuelve true.</summary>
        public CreateLoanRequest Resultado { get; private set; }

        public PrestamoDialog()
        {
            InitializeComponent();

            ListaSeleccion.ItemsSource = _seleccion;
            _seleccion.CollectionChanged += (s, e) => ActualizarResumenSeleccion();

            // Por defecto, dos semanas: es el plazo habitual de un préstamo de
            // material para una actividad, y evita teclear la fecha en el caso
            // más común.
            FechaDevolucion.SelectedDate = DateTime.Today.AddDays(14);
            FechaDevolucion.DisplayDateStart = DateTime.Today;
            FechaDevolucion.DisplayDateEnd = DateTime.Today.AddYears(1);

            // Solo un administrador presta en nombre de otro. Para el resto el
            // selector sobra: el servidor ignoraría cualquier otro valor.
            if (!_sesion.EsAdministrador)
            {
                LblPersona.Visibility = Visibility.Collapsed;
                CmbUsuario.Visibility = Visibility.Collapsed;
            }

            Loaded += AlCargar;
        }

        private async void AlCargar(object sender, RoutedEventArgs e)
        {
            ActualizarResumenSeleccion();
            BtnGuardar.IsEnabled = false;

            await CargarCatalogoAsync();

            if (_sesion.EsAdministrador)
            {
                await CargarUsuariosAsync();
            }

            BtnGuardar.IsEnabled = true;
            TxtBuscar.Focus();
        }

        // ============================================================
        // CARGA
        // ============================================================

        private async System.Threading.Tasks.Task CargarCatalogoAsync()
        {
            ApiResult<List<MaterialDto>> resultado = await _materiales.ListarAsync();

            if (!resultado.EsCorrecto)
            {
                MostrarError("No se ha podido cargar el inventario. " +
                             resultado.MensajeParaUsuario());
                return;
            }

            // Solo se ofrece lo que se puede prestar de verdad. Mostrar
            // artículos agotados obligaría al usuario a descubrir el problema
            // al intentar guardar.
            _catalogo = resultado.Valor
                .Where(m => m.AvailableQuantity > 0)
                .OrderBy(m => m.Name)
                .ToList();

            PintarCatalogo();
        }

        private async System.Threading.Tasks.Task CargarUsuariosAsync()
        {
            ApiResult<List<UserSummaryDto>> resultado = await _usuarios.ListarResumenAsync();

            if (!resultado.EsCorrecto)
            {
                MostrarError("No se ha podido cargar la lista de personas. " +
                             resultado.MensajeParaUsuario());
                return;
            }

            CmbUsuario.ItemsSource = resultado.Valor;

            // Preseleccionado uno mismo: es el caso más frecuente incluso
            // siendo administrador.
            CmbUsuario.SelectedItem = resultado.Valor
                .FirstOrDefault(u => u.Id == _sesion.IdUsuario);

            if (CmbUsuario.SelectedItem is null && resultado.Valor.Count > 0)
            {
                CmbUsuario.SelectedIndex = 0;
            }
        }

        private void PintarCatalogo()
        {
            string termino = TxtBuscar.Text.Trim();

            IEnumerable<MaterialDto> vista = _catalogo;

            if (termino.Length > 0)
            {
                vista = vista.Where(m =>
                    (m.Name?.Contains(termino, StringComparison.CurrentCultureIgnoreCase) ?? false) ||
                    (m.Type?.Contains(termino, StringComparison.CurrentCultureIgnoreCase) ?? false));
            }

            // Lo ya añadido desaparece del catálogo: así no se puede añadir dos
            // veces el mismo artículo y quedan dos líneas del mismo material.
            var yaElegidos = _seleccion.Select(l => l.MaterialId).ToHashSet();

            ListaCatalogo.ItemsSource = vista
                .Where(m => !yaElegidos.Contains(m.Id))
                .ToList();
        }

        // ============================================================
        // SELECCION
        // ============================================================

        private void AlBuscar(object sender, TextChangedEventArgs e)
        {
            if (!IsLoaded) return;
            PintarCatalogo();
        }

        private void AlAnadirDesdeCatalogo(object sender, MouseButtonEventArgs e)
        {
            if (ListaCatalogo.SelectedItem is MaterialDto material)
            {
                Anadir(material);
            }
        }

        private void Anadir(MaterialDto material)
        {
            if (_seleccion.Any(l => l.MaterialId == material.Id)) return;

            _seleccion.Add(new LineaPrestamo(material));
            PintarCatalogo();
            OcultarError();
        }

        private void AlSumarUnidad(object sender, RoutedEventArgs e)
        {
            if (((FrameworkElement)sender).DataContext is LineaPrestamo linea)
            {
                linea.Cantidad++;
                ActualizarResumenSeleccion();
            }
        }

        private void AlRestarUnidad(object sender, RoutedEventArgs e)
        {
            if (((FrameworkElement)sender).DataContext is LineaPrestamo linea)
            {
                linea.Cantidad--;
                ActualizarResumenSeleccion();
            }
        }

        private void AlQuitarLinea(object sender, RoutedEventArgs e)
        {
            if (((FrameworkElement)sender).DataContext is LineaPrestamo linea)
            {
                _seleccion.Remove(linea);
                PintarCatalogo();
            }
        }

        private void ActualizarResumenSeleccion()
        {
            int unidades = _seleccion.Sum(l => l.Cantidad);

            TxtTotalUnidades.Text = _seleccion.Count == 0
                ? "0 unidades"
                : $"{_seleccion.Count} artículos · {unidades} unidades";

            PanelSeleccionVacia.Visibility = _seleccion.Count == 0
                ? Visibility.Visible : Visibility.Collapsed;
        }

        // ============================================================
        // VALIDACION Y GUARDADO
        // ============================================================

        private void AlCambiarFecha(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded) return;
            ValidarFecha();
        }

        private bool ValidarFecha()
        {
            DateTime? fecha = FechaDevolucion.SelectedDate;

            string error = fecha is null
                ? "Indica la fecha prevista de devolución."
                : fecha.Value.Date < DateTime.Today
                    ? "La fecha no puede ser anterior a hoy."
                    : fecha.Value.Date > DateTime.Today.AddYears(1)
                        ? "La fecha no puede superar un año."
                        : null;

            ErrFecha.Text = error ?? "";
            ErrFecha.Visibility = error is null ? Visibility.Collapsed : Visibility.Visible;

            FechaDevolucion.BorderBrush = error is null
                ? (System.Windows.Media.Brush)FindResource("BorderStrong")
                : (System.Windows.Media.Brush)FindResource("Danger");

            return error is null;
        }

        private void AlGuardar(object sender, RoutedEventArgs e)
        {
            OcultarError();

            bool valido = ValidarFecha();

            if (_seleccion.Count == 0)
            {
                MostrarError("Añade al menos un artículo al préstamo.");
                valido = false;
            }

            if (_sesion.EsAdministrador && CmbUsuario.SelectedItem is null)
            {
                MostrarError("Indica a quién se presta el material.");
                valido = false;
            }

            if (!valido)
            {
                if (ErrFecha.Visibility == Visibility.Visible)
                {
                    FechaDevolucion.Focus();
                }
                return;
            }

            // El destinatario solo se envía si es administrador. Para el resto
            // se manda null y el servidor usa el usuario del token, que es la
            // única fuente de identidad en la que se confía.
            int? destinatario = _sesion.EsAdministrador && CmbUsuario.SelectedItem is UserSummaryDto u
                ? u.Id
                : (int?)null;

            Resultado = new CreateLoanRequest(
                destinatario,
                DateOnly.FromDateTime(FechaDevolucion.SelectedDate.Value),
                string.IsNullOrWhiteSpace(TxtMotivo.Text) ? null : TxtMotivo.Text.Trim(),
                _seleccion.Select(l => new CreateLoanLineRequest(l.MaterialId, l.Cantidad)).ToList());

            DialogResult = true;
            Close();
        }

        private void AlCancelar(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void MostrarError(string mensaje)
        {
            TxtError.Text = mensaje;
            PanelError.Visibility = Visibility.Visible;
        }

        private void OcultarError() => PanelError.Visibility = Visibility.Collapsed;
    }
}
