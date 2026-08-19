using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace AssetFlow.Desktop.Controls
{
    /// <summary>
    /// Estados de una vista: cargando, vacío, sin resultados y error.
    /// </summary>
    public partial class EstadoPanel : UserControl
    {
        private Action _accion;
        private Storyboard _animacion;

        public EstadoPanel()
        {
            InitializeComponent();
            PrepararAnimacion();
        }

        /// <summary>
        /// Tres puntos que laten con desfase. Se detiene al ocultar el panel:
        /// una animación corriendo de fondo consume ciclos de composición
        /// aunque no se vea.
        /// </summary>
        private void PrepararAnimacion()
        {
            _animacion = new Storyboard { RepeatBehavior = RepeatBehavior.Forever };

            var puntos = new[] { P1, P2, P3 };
            for (int i = 0; i < puntos.Length; i++)
            {
                var anim = new DoubleAnimationUsingKeyFrames
                {
                    BeginTime = TimeSpan.FromMilliseconds(i * 140),
                    Duration = new Duration(TimeSpan.FromMilliseconds(900))
                };
                anim.KeyFrames.Add(new LinearDoubleKeyFrame(0.28, KeyTime.FromPercent(0)));
                anim.KeyFrames.Add(new LinearDoubleKeyFrame(1.0, KeyTime.FromPercent(0.35)));
                anim.KeyFrames.Add(new LinearDoubleKeyFrame(0.28, KeyTime.FromPercent(0.7)));

                Storyboard.SetTarget(anim, puntos[i]);
                Storyboard.SetTargetProperty(anim, new PropertyPath("Opacity"));
                _animacion.Children.Add(anim);
            }
        }

        public void MostrarCargando(string texto = "Cargando…")
        {
            TxtCargando.Text = texto;
            PanelMensaje.Visibility = Visibility.Collapsed;
            PanelCargando.Visibility = Visibility.Visible;
            Visibility = Visibility.Visible;
            _animacion.Begin();
        }

        /// <summary>Estado vacío: no hay datos todavía. Ofrece la acción de crear.</summary>
        public void MostrarVacio(string titulo, string detalle, string textoAccion = null, Action accion = null)
        {
            Configurar(titulo, detalle, textoAccion, accion,
                (string)FindResource("Ico.Empty"),
                (Brush)FindResource("TextSubtle"),
                (Brush)FindResource("SurfaceSunken"));
        }

        /// <summary>Sin resultados: hay datos, pero el filtro no encuentra ninguno.</summary>
        public void MostrarSinResultados(string termino, Action limpiarFiltro)
        {
            Configurar(
                "Sin resultados",
                string.IsNullOrEmpty(termino)
                    ? "Ningún elemento coincide con los filtros aplicados."
                    : $"Ningún elemento coincide con «{termino}».",
                "Quitar filtros", limpiarFiltro,
                (string)FindResource("Ico.Search"),
                (Brush)FindResource("TextSubtle"),
                (Brush)FindResource("SurfaceSunken"));
        }

        /// <summary>Error: algo ha fallado y se puede reintentar.</summary>
        public void MostrarError(string titulo, string detalle, Action reintentar = null)
        {
            Configurar(titulo, detalle,
                reintentar != null ? "Reintentar" : null, reintentar,
                (string)FindResource("Ico.Offline"),
                (Brush)FindResource("Danger"),
                (Brush)FindResource("DangerSoft"));
        }

        private void Configurar(string titulo, string detalle, string textoAccion, Action accion,
                                string icono, Brush colorIcono, Brush fondoIcono)
        {
            Titulo.Text = titulo;
            Detalle.Text = detalle;
            Icono.Text = icono;
            Icono.Foreground = colorIcono;
            IconoFondo.Background = fondoIcono;

            _accion = accion;
            BtnAccion.Content = textoAccion ?? "";
            BtnAccion.Visibility = textoAccion != null && accion != null
                ? Visibility.Visible : Visibility.Collapsed;

            _animacion.Stop();
            PanelCargando.Visibility = Visibility.Collapsed;
            PanelMensaje.Visibility = Visibility.Visible;
            Visibility = Visibility.Visible;
        }

        public void Ocultar()
        {
            _animacion.Stop();
            Visibility = Visibility.Collapsed;
        }

        private void AlPulsarAccion(object sender, RoutedEventArgs e) => _accion?.Invoke();
    }
}
