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
    public partial class PrestamosPage : UserControl
    {
        private const int RetardoBusquedaMs = 220;

        /// <summary>
        /// Filtros de la lista. El valor es la clave de estado del servidor,
        /// o uno de los pseudoestados que no le corresponden a una sola clave.
        /// </summary>
        private enum Filtro
        {
            Todos,
            Pendientes,
            EnCurso,
            Vencidos,
            DevolucionesPendientes,
            Cerrados
        }

        private readonly LoansService _prestamos = App.Obtener<LoansService>();
        private readonly SessionState _sesion = App.Obtener<SessionState>();
        private readonly DispatcherTimer _debounce;

        /// <summary>Filtros disponibles, en el orden del desplegable.</summary>
        private readonly List<Filtro> _filtros = new List<Filtro>();

        /// <summary>Todo lo recibido del servidor, sin filtrar.</summary>
        private List<LoanDto> _todos = new List<LoanDto>();

        private CancellationTokenSource _consultaActual;

        /// <summary>Evita lanzar dos operaciones a la vez sobre la misma fila.</summary>
        private bool _operando;

        public event Action<string> EstadoCambiado;

        public PrestamosPage()
        {
            InitializeComponent();

            _debounce = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(RetardoBusquedaMs)
            };
            _debounce.Tick += (s, e) => { _debounce.Stop(); AplicarFiltroYPintar(); };

            PrepararSegunRol();

            Loaded += AlCargarVista;
        }

        /// <summary>
        /// Ajusta la pantalla al rol.
        /// </summary>
        /// <remarks>
        /// Esto es presentación, no control de acceso: no ofrece acciones que
        /// el servidor iba a rechazar de todas formas. Que un usuario normal no
        /// vea el botón «Aprobar» no es lo que le impide aprobar; lo que se lo
        /// impide es que <c>POST /api/loans/{id}/approve</c> exige el rol de
        /// administrador.
        /// </remarks>
        private void PrepararSegunRol()
        {
            bool admin = _sesion.EsAdministrador;

            // A un usuario normal la columna de persona no le dice nada: todos
            // los préstamos que ve son suyos.
            if (!admin)
            {
                ColPersona.Visibility = Visibility.Collapsed;
            }

            _filtros.Clear();
            CmbEstado.Items.Clear();

            Anadir(Filtro.EnCurso, "En curso");

            if (admin)
            {
                Anadir(Filtro.Pendientes, "Pendientes de aprobar");
                Anadir(Filtro.DevolucionesPendientes, "Devoluciones pendientes");
            }
            else
            {
                Anadir(Filtro.Pendientes, "Mis solicitudes");
            }

            Anadir(Filtro.Vencidos, "Vencidos");
            Anadir(Filtro.Cerrados, "Cerrados");
            Anadir(Filtro.Todos, "Todos");

            // Un administrador entra viendo lo que exige su atención; un
            // usuario, lo que tiene en la mano.
            SeleccionarFiltro(admin ? Filtro.Pendientes : Filtro.EnCurso);

            BtnResumenPendientes.Visibility = Visibility.Visible;
            SepPendientes.Visibility = Visibility.Visible;
            TxtEtiquetaPendientes.Text = admin ? "POR APROBAR" : "MIS SOLICITUDES";

            BtnResumenDevoluciones.Visibility = admin ? Visibility.Visible : Visibility.Collapsed;
            SepDevoluciones.Visibility = admin ? Visibility.Visible : Visibility.Collapsed;

            TxtAyuda.Text = admin
                ? "Doble clic para ver el detalle  ·  Ctrl+N nuevo préstamo"
                : "Doble clic para ver el detalle  ·  Ctrl+N solicitar material";

            BtnNuevo.ToolTip = admin
                ? "Registrar un préstamo (Ctrl+N)"
                : "Solicitar material (Ctrl+N)";

            ((TextBlock)((StackPanel)BtnNuevo.Content).Children[1]).Text = admin
                ? "Nuevo préstamo"
                : "Solicitar material";

            void Anadir(Filtro filtro, string etiqueta)
            {
                _filtros.Add(filtro);
                CmbEstado.Items.Add(new ComboBoxItem { Content = etiqueta });
            }
        }

        private void SeleccionarFiltro(Filtro filtro)
        {
            int indice = _filtros.IndexOf(filtro);

            if (indice >= 0)
            {
                CmbEstado.SelectedIndex = indice;
            }
        }

        private Filtro FiltroActual =>
            CmbEstado.SelectedIndex >= 0 && CmbEstado.SelectedIndex < _filtros.Count
                ? _filtros[CmbEstado.SelectedIndex]
                : Filtro.Todos;

        private async void AlCargarVista(object sender, RoutedEventArgs e)
        {
            TxtBuscar.Focus();
            await CargarAsync();
        }

        // ============================================================
        // CARGA
        // ============================================================

        /// <summary>
        /// Trae el historial completo una sola vez y filtra en memoria.
        /// </summary>
        /// <remarks>
        /// A diferencia del inventario, aquí no se busca en el servidor: el
        /// volumen de préstamos de una asociación es pequeño y el filtro por
        /// estado se usa constantemente. Ir al servidor en cada cambio de
        /// desplegable sería peor experiencia sin ganar nada.
        ///
        /// Lo que llega sigue estando acotado por el servidor: un usuario
        /// normal recibe únicamente los suyos, pida lo que pida.
        /// </remarks>
        private async Task CargarAsync()
        {
            _consultaActual?.Cancel();
            _consultaActual = new CancellationTokenSource();
            CancellationToken ct = _consultaActual.Token;

            Estado.MostrarCargando();
            Informar("Consultando…");

            ApiResult<List<LoanDto>> resultado = await _prestamos.ListarAsync(ct: ct);

            if (ct.IsCancellationRequested || resultado.Status == ApiStatus.Cancelled)
            {
                return;
            }

            if (!resultado.EsCorrecto)
            {
                _todos = new List<LoanDto>();
                Tabla.ItemsSource = null;
                ActualizarResumen();

                Estado.MostrarError(
                    resultado.Status == ApiStatus.Offline
                        ? "Sin conexión con el servidor"
                        : "No se han podido cargar los préstamos",
                    resultado.MensajeParaUsuario(),
                    resultado.MereceReintento ? async () => await CargarAsync() : (Action)null);

                Informar(resultado.Status == ApiStatus.Offline ? "Sin conexión" : "Error");
                return;
            }

            _todos = resultado.Valor;
            AplicarFiltroYPintar();
            Informar(Texto.Contar(_todos.Count, "préstamo", "préstamos", "cargado", "cargados"));
        }

        // ============================================================
        // FILTRADO
        // ============================================================

        private void AplicarFiltroYPintar()
        {
            IEnumerable<LoanDto> vista = _todos;

            switch (FiltroActual)
            {
                case Filtro.Pendientes:
                    vista = vista.Where(p => p.EstaPendiente);
                    break;
                case Filtro.EnCurso:
                    // La devolución pedida y aún sin confirmar sigue siendo
                    // material fuera del almacén: pertenece a «en curso».
                    vista = vista.Where(p => p.EstaActivo || p.TieneDevolucionSolicitada);
                    break;
                case Filtro.Vencidos:
                    vista = vista.Where(p => p.IsOverdue);
                    break;
                case Filtro.DevolucionesPendientes:
                    vista = vista.Where(p => p.TieneDevolucionSolicitada);
                    break;
                case Filtro.Cerrados:
                    vista = vista.Where(p => p.EstaCerrado);
                    break;
                case Filtro.Todos:
                default:
                    break;
            }

            string termino = TxtBuscar.Text.Trim();
            BtnLimpiar.Visibility = termino.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

            if (termino.Length > 0)
            {
                vista = vista.Where(p => Coincide(p, termino));
            }

            // Orden por urgencia: primero lo vencido, después lo que espera una
            // decisión, y sólo entonces el resto por fecha. Es el mismo criterio
            // con el que alguien miraría la lista para decidir qué hacer.
            List<FilaPrestamo> lista = vista
                .OrderByDescending(p => p.IsOverdue)
                .ThenByDescending(p => p.EstaPendiente || p.TieneDevolucionSolicitada)
                .ThenByDescending(p => p.FechaRelevante)
                .ThenByDescending(p => p.Id)
                .Select(p => new FilaPrestamo(p, _sesion.EsAdministrador))
                .ToList();

            Tabla.ItemsSource = lista;

            ActualizarResumen();
            ActualizarRecuento(lista.Count);

            if (lista.Count == 0)
            {
                MostrarVacio(termino);
                return;
            }

            Estado.Ocultar();

            if (Tabla.SelectedIndex < 0)
            {
                Tabla.SelectedIndex = 0;
            }
        }

        /// <summary>
        /// Estado vacío con el texto del caso concreto.
        /// </summary>
        /// <remarks>
        /// «No hay resultados» a secas obliga a adivinar si no hay datos, si el
        /// filtro los esconde o si algo ha fallado. Cada situación dice qué
        /// pasa y qué se puede hacer.
        /// </remarks>
        private void MostrarVacio(string termino)
        {
            bool admin = _sesion.EsAdministrador;

            if (_todos.Count > 0)
            {
                if (termino.Length > 0)
                {
                    Estado.MostrarSinResultados(termino, LimpiarTodosLosFiltros);
                    return;
                }

                switch (FiltroActual)
                {
                    case Filtro.Pendientes:
                        Estado.MostrarVacio(
                            admin ? "No hay solicitudes pendientes" : "No tienes solicitudes pendientes",
                            admin
                                ? "Cuando alguien pida material aparecerá aquí para que lo apruebes o lo rechaces."
                                : "Cuando pidas material, la solicitud aparecerá aquí hasta que la resuelvan.",
                            "Ver todos", () => SeleccionarFiltro(Filtro.Todos));
                        return;

                    case Filtro.DevolucionesPendientes:
                        Estado.MostrarVacio(
                            "No hay devoluciones por confirmar",
                            "Aquí aparecerá el material que alguien haya dado por devuelto y falte comprobar.",
                            "Ver todos", () => SeleccionarFiltro(Filtro.Todos));
                        return;

                    case Filtro.Vencidos:
                        Estado.MostrarVacio(
                            "Nada vencido",
                            "Todo el material prestado está dentro de plazo.",
                            "Ver todos", () => SeleccionarFiltro(Filtro.Todos));
                        return;

                    default:
                        Estado.MostrarSinResultados(termino, LimpiarTodosLosFiltros);
                        return;
                }
            }

            Estado.MostrarVacio(
                admin ? "Todavía no hay préstamos" : "Todavía no tienes material prestado",
                admin
                    ? "Cuando se preste material aparecerá aquí. Registra el primero para empezar."
                    : "Solicita el material que necesites y lo verás aquí mientras se resuelve.",
                admin ? "Registrar un préstamo" : "Solicitar material",
                () => AlCrear(null, null));
        }

        private static bool Coincide(LoanDto prestamo, string termino)
        {
            const StringComparison Modo = StringComparison.CurrentCultureIgnoreCase;

            if (prestamo.UserFullName?.Contains(termino, Modo) == true) return true;
            if (prestamo.Reason?.Contains(termino, Modo) == true) return true;
            if (prestamo.Id.ToString().Contains(termino, Modo)) return true;

            return prestamo.Lines.Any(l => l.MaterialName?.Contains(termino, Modo) == true);
        }

        private void ActualizarResumen()
        {
            var cultura = CultureInfo.CurrentCulture;

            int pendientes = _todos.Count(p => p.EstaPendiente);
            int devoluciones = _todos.Count(p => p.TieneDevolucionSolicitada);
            int enCurso = _todos.Count(p => p.EstaActivo || p.TieneDevolucionSolicitada);
            int vencidos = _todos.Count(p => p.IsOverdue);

            int unidades = _todos
                .Where(p => p.EstaActivo || p.TieneDevolucionSolicitada)
                .Sum(p => p.UnidadesTotales);

            TxtPendientes.Text = pendientes.ToString("N0", cultura);
            TxtDevoluciones.Text = devoluciones.ToString("N0", cultura);
            TxtEnCurso.Text = enCurso.ToString("N0", cultura);
            TxtUnidades.Text = unidades.ToString("N0", cultura);
            TxtVencidos.Text = vencidos.ToString("N0", cultura);

            // El color resalta lo que pide atención, pero la cifra ya está
            // escrita: nadie depende del tono para saber cuántos hay.
            TxtVencidos.Foreground = Tono(vencidos > 0, "Danger");
            TxtPendientes.Foreground = Tono(pendientes > 0, "Warning");
            TxtDevoluciones.Foreground = Tono(devoluciones > 0, "Accent");

            System.Windows.Media.Brush Tono(bool destacar, string recurso) =>
                (System.Windows.Media.Brush)FindResource(destacar ? recurso : "Text");
        }

        private void ActualizarRecuento(int mostrados)
        {
            TxtRecuento.Text = mostrados == _todos.Count
                ? Texto.Contar(mostrados, "préstamo", "préstamos")
                : $"{mostrados} de " + Texto.Contar(_todos.Count, "préstamo", "préstamos");
        }

        private void Informar(string mensaje) => EstadoCambiado?.Invoke(mensaje);

        private void LimpiarTodosLosFiltros()
        {
            TxtBuscar.Text = "";
            SeleccionarFiltro(Filtro.Todos);
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

        private void AlVerVencidos(object sender, RoutedEventArgs e) =>
            SeleccionarFiltro(Filtro.Vencidos);

        private void AlFiltrarPendientes(object sender, RoutedEventArgs e) =>
            SeleccionarFiltro(Filtro.Pendientes);

        private void AlFiltrarEnCurso(object sender, RoutedEventArgs e) =>
            SeleccionarFiltro(Filtro.EnCurso);

        private void AlFiltrarDevoluciones(object sender, RoutedEventArgs e) =>
            SeleccionarFiltro(Filtro.DevolucionesPendientes);

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
            if (Tabla.SelectedItem is FilaPrestamo f) MostrarDetalle(f.Prestamo);
        }

        private static LoanDto DeLaFila(object sender) =>
            ((FrameworkElement)sender).DataContext is FilaPrestamo f ? f.Prestamo : null;

        private async void AlDevolverFila(object sender, RoutedEventArgs e) =>
            await DevolverAsync(DeLaFila(sender));

        private async void AlAprobarFila(object sender, RoutedEventArgs e) =>
            await AprobarAsync(DeLaFila(sender));

        private async void AlRechazarFila(object sender, RoutedEventArgs e) =>
            await RechazarAsync(DeLaFila(sender));

        private async void AlConfirmarDevolucionFila(object sender, RoutedEventArgs e) =>
            await ConfirmarDevolucionAsync(DeLaFila(sender));

        private async void AlRechazarDevolucionFila(object sender, RoutedEventArgs e) =>
            await RechazarDevolucionAsync(DeLaFila(sender));

        private async void AlEliminarFila(object sender, RoutedEventArgs e) =>
            await EliminarAsync(DeLaFila(sender));

        // ============================================================
        // OPERACIONES
        // ============================================================

        private async void AlCrear(object sender, RoutedEventArgs e)
        {
            var dlg = new PrestamoDialog { Owner = Window.GetWindow(this) };

            if (dlg.ShowDialog() != true) return;

            bool admin = _sesion.EsAdministrador;

            Informar(admin ? "Registrando préstamo…" : "Enviando solicitud…");

            ApiResult<LoanDto> resultado = await _prestamos.CrearAsync(dlg.Resultado);

            if (!resultado.EsCorrecto)
            {
                Aviso.Error(Window.GetWindow(this),
                    admin ? "No se ha podido registrar el préstamo" : "No se ha podido enviar la solicitud",
                    resultado.MensajeParaUsuario());

                Informar("Error al registrar");
                return;
            }

            if (!admin)
            {
                Aviso.Info(Window.GetWindow(this),
                    "Solicitud enviada",
                    "Un administrador tiene que aprobarla antes de que puedas recoger el material.\n\n" +
                    "La verás en «Mis solicitudes» hasta que se resuelva.");
            }

            Informar(admin ? "Préstamo registrado" : "Solicitud enviada");
            await CargarAsync();
        }

        /// <summary>
        /// Ejecuta una operación sobre un préstamo con el guion común: confirmar,
        /// bloquear la pantalla, llamar, tratar el conflicto y recargar.
        /// </summary>
        /// <remarks>
        /// Las seis acciones de esta pantalla se diferencian en el texto y en la
        /// llamada; todo lo demás era idéntico. Un 409 significa siempre lo
        /// mismo: alguien se ha adelantado desde otra sesión, así que no es un
        /// error del usuario y basta con recargar para que vea el estado real.
        /// </remarks>
        private async Task EjecutarAsync(
            LoanDto prestamo,
            string tituloConfirmacion,
            string cuerpoConfirmacion,
            string textoBoton,
            Func<Task<ApiResult<LoanDto>>> operacion,
            string enCurso,
            string tituloError,
            bool peligroso = false)
        {
            if (prestamo == null || _operando) return;

            bool confirmado = Aviso.Confirmar(Window.GetWindow(this),
                tituloConfirmacion, cuerpoConfirmacion, textoBoton, peligroso);

            if (!confirmado) return;

            await LanzarAsync(operacion, enCurso, tituloError);
        }

        /// <summary>
        /// Ejecuta la llamada, ya confirmada, y deja la pantalla coherente.
        /// </summary>
        /// <remarks>
        /// Un 409 significa siempre lo mismo: alguien se ha adelantado desde
        /// otra sesión. No es un error del usuario, así que se le explica y se
        /// recarga para que vea el estado real en lugar de dejarle una fila
        /// que ya no existe.
        /// </remarks>
        private async Task LanzarAsync(
            Func<Task<ApiResult<LoanDto>>> operacion, string enCurso, string tituloError)
        {
            // Sin este cierre, un doble clic nervioso manda dos veces la misma
            // aprobación y la segunda vuelve como un 409 desconcertante.
            _operando = true;
            IsEnabled = false;
            Informar(enCurso);

            try
            {
                ApiResult<LoanDto> resultado = await operacion();

                if (!resultado.EsCorrecto)
                {
                    if (resultado.Status == ApiStatus.Conflict)
                    {
                        Aviso.Info(Window.GetWindow(this),
                            "El préstamo ha cambiado",
                            resultado.MensajeParaUsuario());
                    }
                    else
                    {
                        Aviso.Error(Window.GetWindow(this), tituloError,
                            resultado.MensajeParaUsuario());

                        Informar("Error");
                        return;
                    }
                }
            }
            finally
            {
                _operando = false;
                IsEnabled = true;
            }

            await CargarAsync();
        }

        private Task AprobarAsync(LoanDto prestamo)
        {
            if (prestamo == null) return Task.CompletedTask;

            return EjecutarAsync(prestamo,
                "Aprobar la solicitud",
                $"Se aprobará la solicitud {prestamo.Id} de {prestamo.UserFullName} " +
                $"({prestamo.ResumenArticulos}).\n\n" +
                "El material pasará a contar como entregado y dejará de estar disponible.",
                "Aprobar",
                () => _prestamos.AprobarAsync(prestamo.Id),
                "Aprobando…",
                "No se ha podido aprobar la solicitud");
        }

        private async Task RechazarAsync(LoanDto prestamo)
        {
            if (prestamo == null || _operando) return;

            // El motivo se recoge en el propio diálogo de confirmación: quien
            // recibe el rechazo merece saber por qué, y un rechazo sin
            // explicación obliga a preguntar por otro canal.
            string motivo = NotaDialog.Pedir(Window.GetWindow(this),
                "Rechazar la solicitud",
                $"Se rechazará la solicitud {prestamo.Id} de {prestamo.UserFullName}. " +
                "Las unidades reservadas volverán a estar disponibles.\n\n" +
                "Esta decisión no se puede deshacer: haría falta una solicitud nueva.",
                "Motivo del rechazo (opcional)",
                "Rechazar", peligroso: true);

            if (motivo == null) return;

            await LanzarAsync(
                () => _prestamos.RechazarAsync(prestamo.Id, motivo),
                "Rechazando…",
                "No se ha podido rechazar la solicitud");
        }

        private Task DevolverAsync(LoanDto prestamo)
        {
            if (prestamo == null || !prestamo.EstaActivo) return Task.CompletedTask;

            if (_sesion.EsAdministrador)
            {
                return EjecutarAsync(prestamo,
                    "Registrar devolución",
                    $"Se marcará como devuelto el préstamo {prestamo.Id} " +
                    $"({prestamo.ResumenArticulos}).\n\n" +
                    "Las unidades volverán a estar disponibles en el inventario.",
                    "Registrar devolución",
                    () => _prestamos.DevolverAsync(prestamo.Id),
                    "Registrando devolución…",
                    "No se ha podido registrar la devolución");
            }

            return EjecutarAsync(prestamo,
                "Pedir la devolución",
                $"Se avisará de que devuelves el préstamo {prestamo.Id} " +
                $"({prestamo.ResumenArticulos}).\n\n" +
                "El préstamo seguirá a tu nombre hasta que un administrador " +
                "compruebe que el material ha vuelto.",
                "Pedir devolución",
                () => _prestamos.SolicitarDevolucionAsync(prestamo.Id),
                "Enviando la solicitud…",
                "No se ha podido pedir la devolución");
        }

        private Task ConfirmarDevolucionAsync(LoanDto prestamo)
        {
            if (prestamo == null) return Task.CompletedTask;

            return EjecutarAsync(prestamo,
                "Confirmar la devolución",
                $"Confirmas que ha vuelto el material del préstamo {prestamo.Id} " +
                $"de {prestamo.UserFullName} ({prestamo.ResumenArticulos}).\n\n" +
                "Las unidades volverán a estar disponibles en el inventario.",
                "Confirmar",
                () => _prestamos.ConfirmarDevolucionAsync(prestamo.Id),
                "Confirmando…",
                "No se ha podido confirmar la devolución");
        }

        private async Task RechazarDevolucionAsync(LoanDto prestamo)
        {
            if (prestamo == null || _operando) return;

            string motivo = NotaDialog.Pedir(Window.GetWindow(this),
                "El material no ha vuelto",
                $"El préstamo {prestamo.Id} volverá a contar como activo y seguirá " +
                $"a nombre de {prestamo.UserFullName}.",
                "Qué falta o en qué estado ha vuelto (opcional)",
                "Marcar como no devuelto", peligroso: true);

            if (motivo == null) return;

            await LanzarAsync(
                () => _prestamos.RechazarDevolucionAsync(prestamo.Id, motivo),
                "Actualizando…",
                "No se ha podido actualizar el préstamo");
        }

        private async Task EliminarAsync(LoanDto prestamo)
        {
            if (prestamo == null || _operando) return;

            bool confirmado = Aviso.Confirmar(Window.GetWindow(this),
                "Eliminar del historial",
                $"Se eliminará el préstamo {prestamo.Id} de forma permanente.\n\n" +
                "El historial dejará de reflejar que este material se prestó. " +
                "Esta acción no se puede deshacer.",
                "Eliminar", peligroso: true);

            if (!confirmado) return;

            _operando = true;
            IsEnabled = false;

            try
            {
                ApiResult resultado = await _prestamos.EliminarAsync(prestamo.Id);

                if (!resultado.EsCorrecto)
                {
                    Aviso.Error(Window.GetWindow(this),
                        "No se ha podido eliminar",
                        resultado.MensajeParaUsuario());
                    return;
                }
            }
            finally
            {
                _operando = false;
                IsEnabled = true;
            }

            Informar("Préstamo eliminado");
            await CargarAsync();
        }

        private async void MostrarDetalle(LoanDto prestamo)
        {
            var detalle = new System.Text.StringBuilder();

            detalle.AppendLine($"Estado:      {prestamo.EstadoTexto} — {prestamo.EstadoDetalle}");

            if (_sesion.EsAdministrador)
            {
                detalle.AppendLine($"Persona:     {prestamo.UserFullName}");
            }

            detalle.AppendLine($"Solicitado:  {prestamo.RequestedAt.ToLocalTime():dd/MM/yyyy HH:mm}");

            if (prestamo.LoanDate is not null)
            {
                detalle.AppendLine($"Prestado:    {prestamo.LoanDate:dd/MM/yyyy}");
            }

            detalle.AppendLine($"Devolución:  {prestamo.EstimatedReturnDate:dd/MM/yyyy} (prevista)");

            if (prestamo.ReturnDate is not null)
            {
                detalle.AppendLine($"Devuelto:    {prestamo.ReturnDate:dd/MM/yyyy}");
            }

            if (prestamo.DecidedByName is not null)
            {
                detalle.AppendLine($"Resuelto por: {prestamo.DecidedByName}");
            }

            if (!string.IsNullOrWhiteSpace(prestamo.DecisionNote))
            {
                detalle.AppendLine($"Nota:        {prestamo.DecisionNote}");
            }

            if (!string.IsNullOrWhiteSpace(prestamo.ReturnDecisionNote))
            {
                detalle.AppendLine($"Devolución:  {prestamo.ReturnDecisionNote}");
            }

            if (!string.IsNullOrWhiteSpace(prestamo.Reason))
            {
                detalle.AppendLine($"Motivo:      {prestamo.Reason}");
            }

            detalle.AppendLine();
            detalle.AppendLine("Material:");

            foreach (LoanLineDto linea in prestamo.Lines)
            {
                detalle.AppendLine($"   {linea.Quantity} x {linea.MaterialName}");
            }

            // El historial es una consulta aparte y puede fallar sin que eso
            // deba impedir ver el detalle, que ya está cargado.
            ApiResult<List<LoanHistoryEntryDto>> historial =
                await _prestamos.HistorialAsync(prestamo.Id);

            if (historial.EsCorrecto && historial.Valor.Count > 0)
            {
                detalle.AppendLine();
                detalle.AppendLine("Historial:");

                foreach (LoanHistoryEntryDto entrada in historial.Valor)
                {
                    detalle.AppendLine(
                        $"   {entrada.OccurredAt.ToLocalTime():dd/MM/yyyy HH:mm}  " +
                        $"{entrada.AccionTexto} · {entrada.ActorName}");

                    if (!string.IsNullOrWhiteSpace(entrada.Details))
                    {
                        detalle.AppendLine($"      {entrada.Details}");
                    }
                }
            }

            Aviso.Info(Window.GetWindow(this),
                $"Préstamo {prestamo.Id} · {prestamo.EstadoTexto}",
                detalle.ToString().TrimEnd());
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
            else if (e.Key == Key.F2 && Tabla.SelectedItem is FilaPrestamo f)
            {
                _ = DevolverAsync(f.Prestamo);
                e.Handled = true;
            }
        }
    }
}
