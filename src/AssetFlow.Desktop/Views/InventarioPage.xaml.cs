using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using AssetFlow.Core.Diagnostics;
using AssetFlow.Core.Dtos;
using AssetFlow.Core.Http;
using AssetFlow.Core.Security;
using AssetFlow.Core.Services;
using AssetFlow.Desktop.Dialogs;

namespace AssetFlow.Desktop.Views
{
    public partial class InventarioPage : UserControl
    {
        private const int RetardoBusquedaMs = 280;

        private readonly MaterialsService _materiales = App.Obtener<MaterialsService>();
        private readonly SessionState _sesion = App.Obtener<SessionState>();
        private readonly DispatcherTimer _debounce;

        /// <summary>Conjunto completo recibido del servidor, sin filtrar.</summary>
        private List<MaterialDto> _todos = new List<MaterialDto>();

        private CancellationTokenSource _busquedaActual;
        private string _columnaOrden = "Name";
        private bool _ordenAscendente = true;

        public event Action<string> EstadoCambiado;

        public InventarioPage()
        {
            InitializeComponent();

            _debounce = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(RetardoBusquedaMs)
            };
            _debounce.Tick += async (s, e) => { _debounce.Stop(); await CargarAsync(); };

            // Las acciones de escritura solo se ofrecen a quien puede usarlas.
            // Es una decision de interfaz, no de seguridad: la API rechaza la
            // operacion con 403 aunque alguien logre pulsar el boton.
            AplicarPermisos();

            Loaded += AlCargarVista;
        }

        private void AplicarPermisos()
        {
            Visibility visible = _sesion.EsAdministrador
                ? Visibility.Visible : Visibility.Collapsed;

            BtnNuevo.Visibility = visible;
            ColAcciones.Visibility = visible;
        }

        private async void AlCargarVista(object sender, RoutedEventArgs e)
        {
            // El foco al buscador: en un inventario, nueve de cada diez veces
            // lo primero que se hace es buscar algo.
            TxtBuscar.Focus();
            await CargarAsync();
        }

        // ============================================================
        // CARGA Y FILTRADO
        // ============================================================

        private async Task CargarAsync()
        {
            _busquedaActual?.Cancel();
            _busquedaActual = new CancellationTokenSource();
            CancellationToken ct = _busquedaActual.Token;

            string termino = TxtBuscar.Text.Trim();
            BtnLimpiar.Visibility = termino.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

            Estado.MostrarCargando();
            Informar("Consultando…");

            // La busqueda se resuelve en el servidor: descargar el inventario
            // completo para filtrarlo aqui no escala.
            ApiResult<List<MaterialDto>> resultado =
                await _materiales.ListarAsync(termino, ct);

            if (ct.IsCancellationRequested || resultado.Status == ApiStatus.Cancelled)
            {
                return; // hay una consulta mas reciente en curso
            }

            if (!resultado.EsCorrecto)
            {
                _todos = new List<MaterialDto>();
                Tabla.ItemsSource = null;
                ActualizarResumen();

                // Cada motivo se explica de forma distinta y solo se ofrece
                // reintentar cuando reintentar puede servir de algo.
                Estado.MostrarError(
                    TituloDeError(resultado.Status),
                    resultado.MensajeParaUsuario(),
                    resultado.MereceReintento ? async () => await CargarAsync() : null);

                Informar(resultado.Status == ApiStatus.Offline ? "Sin conexión" : "Error");
                return;
            }

            _todos = resultado.Valor;
            AplicarFiltroYPintar();
            Informar(Texto.Contar(_todos.Count, "artículo", "artículos", "cargado", "cargados"));
        }

        private static string TituloDeError(ApiStatus status) => status switch
        {
            ApiStatus.Offline => "Sin conexión con el servidor",
            ApiStatus.Forbidden => "Sin permiso",
            ApiStatus.TooManyRequests => "Demasiadas peticiones",
            _ => "No se ha podido cargar el inventario"
        };

        /// <summary>
        /// Filtro por estado y ordenación, en memoria sobre lo ya recibido:
        /// no requiere otra ida y vuelta al servidor.
        /// </summary>
        private void AplicarFiltroYPintar()
        {
            IEnumerable<MaterialDto> vista = _todos;

            switch (CmbEstado.SelectedIndex)
            {
                case 1: vista = vista.Where(m => m.NecesitaReposicion && !m.EstaAgotado); break;
                case 2: vista = vista.Where(m => m.EstaAgotado); break;
                case 3: vista = vista.Where(m => !m.NecesitaReposicion); break;
            }

            vista = Ordenar(vista);

            List<MaterialDto> lista = vista.ToList();
            Tabla.ItemsSource = lista;

            ActualizarResumen();
            ActualizarRecuento(lista.Count);

            if (lista.Count == 0)
            {
                string termino = TxtBuscar.Text.Trim();

                if (_todos.Count == 0 && termino.Length == 0 && CmbEstado.SelectedIndex == 0)
                {
                    if (_sesion.EsAdministrador)
                    {
                        Estado.MostrarVacio(
                            "El inventario está vacío",
                            "Todavía no hay ningún artículo registrado. Crea el primero para empezar a gestionar el stock.",
                            "Crear el primer artículo",
                            () => AlCrear(null, null));
                    }
                    else
                    {
                        Estado.MostrarVacio(
                            "El inventario está vacío",
                            "Todavía no hay ningún artículo registrado. Pide a un administrador que dé de alta el material.",
                            null, null);
                    }
                }
                else
                {
                    Estado.MostrarSinResultados(termino, LimpiarTodosLosFiltros);
                }
                return;
            }

            Estado.Ocultar();

            if (Tabla.SelectedIndex < 0 && lista.Count > 0)
                Tabla.SelectedIndex = 0;
        }

        private IEnumerable<MaterialDto> Ordenar(IEnumerable<MaterialDto> datos)
        {
            Func<MaterialDto, object> clave = _columnaOrden switch
            {
                "Id" => m => m.Id,
                "Type" => m => m.Type ?? "",
                "Publisher" => m => m.Publisher ?? "",
                "AvailableQuantity" => m => m.AvailableQuantity,
                "TotalQuantity" => m => m.TotalQuantity,
                _ => m => m.Name ?? ""
            };

            return _ordenAscendente
                ? datos.OrderBy(clave)
                : datos.OrderByDescending(clave);
        }

        private void ActualizarResumen()
        {
            var cultura = CultureInfo.CurrentCulture;

            TxtTotalArticulos.Text = _todos.Count.ToString("N0", cultura);
            TxtTotalUnidades.Text = _todos.Sum(m => m.TotalQuantity).ToString("N0", cultura);
            TxtPrestadas.Text = _todos.Sum(m => m.OnLoanQuantity).ToString("N0", cultura);

            int reponer = _todos.Count(m => m.NecesitaReposicion);
            TxtStockBajo.Text = reponer.ToString("N0", cultura);
            TxtStockBajo.Foreground = reponer > 0
                ? (System.Windows.Media.Brush)FindResource("Warning")
                : (System.Windows.Media.Brush)FindResource("Text");

            BtnVerStockBajo.Visibility = reponer > 0 && CmbEstado.SelectedIndex == 0
                ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ActualizarRecuento(int mostrados)
        {
            TxtRecuento.Text = mostrados == _todos.Count
                ? Texto.Contar(mostrados, "artículo", "artículos")
                : $"{mostrados} de " + Texto.Contar(_todos.Count, "artículo", "artículos");
        }

        private void Informar(string mensaje) => EstadoCambiado?.Invoke(mensaje);

        private void LimpiarTodosLosFiltros()
        {
            TxtBuscar.Text = "";
            CmbEstado.SelectedIndex = 0;
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
            else if (e.Key == Key.Down || e.Key == Key.Enter)
            {
                // Pasar del buscador a la lista sin soltar el teclado
                if (Tabla.Items.Count > 0)
                {
                    Tabla.SelectedIndex = 0;
                    Tabla.Focus();
                    e.Handled = true;
                }
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

        private void AlVerStockBajo(object sender, RoutedEventArgs e)
        {
            CmbEstado.SelectedIndex = 1;
        }

        private async void AlRefrescar(object sender, RoutedEventArgs e)
        {
            _debounce.Stop();
            await CargarAsync();
        }

        /// <summary>
        /// Se ordena sobre la colección propia en lugar de dejar que el DataGrid
        /// ordene: así el criterio se conserva al recargar desde el servidor.
        /// </summary>
        private void AlOrdenar(object sender, DataGridSortingEventArgs e)
        {
            e.Handled = true;

            string columna = e.Column.SortMemberPath;
            if (string.IsNullOrEmpty(columna)) return;

            _ordenAscendente = columna != _columnaOrden || !_ordenAscendente;
            _columnaOrden = columna;

            foreach (var c in Tabla.Columns) c.SortDirection = null;
            e.Column.SortDirection = _ordenAscendente
                ? ListSortDirection.Ascending : ListSortDirection.Descending;

            AplicarFiltroYPintar();
        }

        private void AlSeleccionarFila(object sender, SelectionChangedEventArgs e)
        {
            // Mantiene la fila seleccionada a la vista al navegar con el teclado
            if (Tabla.SelectedItem != null)
                Tabla.ScrollIntoView(Tabla.SelectedItem);
        }

        private void AlDobleClic(object sender, MouseButtonEventArgs e)
        {
            if (_sesion.EsAdministrador && Tabla.SelectedItem is MaterialDto m) Editar(m);
        }

        private void AlEditarFila(object sender, RoutedEventArgs e)
        {
            if (((FrameworkElement)sender).DataContext is MaterialDto m) Editar(m);
        }

        private void AlEliminarFila(object sender, RoutedEventArgs e)
        {
            if (((FrameworkElement)sender).DataContext is MaterialDto m) Eliminar(m);
        }

        private void AlCrear(object sender, RoutedEventArgs e) => Editar(null);

        // ============================================================
        // ALTA, EDICION Y BAJA
        // ============================================================

        private async void Editar(MaterialDto material)
        {
            var dlg = new ArticuloDialog(material) { Owner = Window.GetWindow(this) };
            if (dlg.ShowDialog() != true) return;

            Informar(material == null ? "Creando artículo…" : "Guardando cambios…");

            ApiResult resultado = material == null
                ? await _materiales.CrearAsync(dlg.Resultado)
                : await _materiales.ActualizarAsync(material.Id, dlg.Resultado);

            if (!resultado.EsCorrecto)
            {
                // El conflicto de edición merece su propio mensaje: no es un
                // fallo del usuario ni del servidor, y la salida es recargar.
                if (resultado.Status == ApiStatus.Conflict)
                {
                    Aviso.Error(Window.GetWindow(this),
                        "El artículo ha cambiado",
                        resultado.MensajeParaUsuario());

                    await CargarAsync();
                    return;
                }

                Aviso.Error(Window.GetWindow(this),
                    "No se ha podido guardar",
                    resultado.MensajeParaUsuario());

                Informar("Error al guardar");
                return;
            }

            Informar(material == null ? "Artículo creado" : "Cambios guardados");
            await CargarAsync();
        }

        private async void Eliminar(MaterialDto material)
        {
            if (material == null) return;

            bool confirmado = Aviso.Confirmar(Window.GetWindow(this),
                "Eliminar artículo",
                $"Se eliminará «{material.Name}» (código {material.Id}) de forma permanente.\n\n" +
                "Esta acción no se puede deshacer.",
                "Eliminar", peligroso: true);

            if (!confirmado) return;

            Informar("Eliminando…");

            ApiResult resultado = await _materiales.EliminarAsync(material.Id);

            if (!resultado.EsCorrecto)
            {
                Aviso.Error(Window.GetWindow(this),
                    "No se ha podido eliminar",
                    resultado.MensajeParaUsuario());

                Informar("Error al eliminar");
                return;
            }

            Informar("Artículo eliminado");
            await CargarAsync();
        }

        // ============================================================
        // EXPORTACION
        // ============================================================

        private void AlExportar(object sender, RoutedEventArgs e)
        {
            var lista = Tabla.ItemsSource as List<MaterialDto>;
            if (lista == null || lista.Count == 0)
            {
                Aviso.Info(Window.GetWindow(this), "Nada que exportar",
                    "La vista actual no contiene ningún artículo.");
                return;
            }

            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Exportar inventario",
                Filter = "Archivo CSV (*.csv)|*.csv",
                FileName = $"inventario_{DateTime.Now:yyyy-MM-dd}.csv"
            };

            if (dlg.ShowDialog() != true) return;

            try
            {
                var sb = new StringBuilder();
                sb.AppendLine(
                    "Codigo;Articulo;Tipo;Proveedor;Total;Prestadas;Reservadas;Disponibles;Estado");

                foreach (var m in lista)
                {
                    sb.AppendLine(string.Join(";",
                        m.Id,
                        Csv(m.Name),
                        Csv(m.Type),
                        Csv(m.Publisher),
                        m.TotalQuantity,
                        m.OnLoanQuantity,
                        m.ReservedQuantity,
                        m.AvailableQuantity,
                        m.EstadoTexto));
                }

                // UTF-8 con BOM: sin él, Excel abre los acentos mal en Windows.
                File.WriteAllText(dlg.FileName, sb.ToString(), new UTF8Encoding(true));

                Informar("Inventario exportado");
                Aviso.Info(Window.GetWindow(this), "Exportación completada",
                    "Se han exportado " + Texto.Contar(lista.Count, "artículo", "artículos") +
                    $" a:\n{dlg.FileName}");
            }
            catch (Exception ex)
            {
                Log.Error("Error al exportar el inventario", ex);
                Aviso.Error(Window.GetWindow(this), "No se ha podido exportar", ex.Message);
            }
        }

        /// <summary>
        /// Escapa el separador y las comillas para que el CSV no se rompa, y
        /// neutraliza las fórmulas: una celda que empieza por = o + la ejecuta
        /// Excel al abrir el archivo, y el nombre del artículo lo escribe un
        /// usuario.
        /// </summary>
        private static string Csv(string valor)
        {
            valor = valor ?? "";

            if (valor.Length > 0 && "=+-@\t\r".IndexOf(valor[0]) >= 0)
            {
                valor = "'" + valor;
            }

            return valor.Contains(';') || valor.Contains('"') || valor.Contains('\n')
                ? "\"" + valor.Replace("\"", "\"\"") + "\""
                : valor;
        }

        // ============================================================
        // ATAJOS DE TECLADO
        // ============================================================

        public void ProcesarAtajo(KeyEventArgs e)
        {
            bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
            bool admin = _sesion.EsAdministrador;

            if (ctrl && e.Key == Key.N && admin) { AlCrear(null, null); e.Handled = true; }
            else if (ctrl && e.Key == Key.F) { TxtBuscar.Focus(); TxtBuscar.SelectAll(); e.Handled = true; }
            else if (e.Key == Key.F5) { AlRefrescar(null, null); e.Handled = true; }
            else if (e.Key == Key.F2 && admin && Tabla.SelectedItem is MaterialDto m1)
            {
                Editar(m1); e.Handled = true;
            }
            else if (e.Key == Key.Delete && admin && Tabla.IsKeyboardFocusWithin
                     && Tabla.SelectedItem is MaterialDto m2)
            {
                Eliminar(m2); e.Handled = true;
            }
        }
    }
}
