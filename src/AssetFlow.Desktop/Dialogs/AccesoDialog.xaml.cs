using System;
using System.Security.Cryptography;
using System.Windows;
using AssetFlow.Core.Dtos;
using AssetFlow.Core.Security;

namespace AssetFlow.Desktop.Dialogs
{
    /// <summary>
    /// Cambio de rol, activación de la cuenta y reinicio de contraseña.
    /// </summary>
    public partial class AccesoDialog : Window
    {
        private readonly UserDto _usuario;
        private readonly bool _esUnoMismo;

        /// <summary>Rol elegido: "Admin" o "User".</summary>
        public string Rol { get; private set; }

        /// <summary>Si la cuenta queda habilitada para iniciar sesión.</summary>
        public bool Activa { get; private set; }

        /// <summary>Indica si el rol o el estado han cambiado respecto al original.</summary>
        public bool CambiaAcceso { get; private set; }

        /// <summary>
        /// Si se ha pedido reiniciar la contraseña de esta cuenta.
        /// </summary>
        /// <remarks>
        /// No hay ninguna contraseña que devolver: la elige el servidor
        /// (<c>usuario + "123@"</c>) y la persona queda obligada a cambiarla al
        /// entrar. Que el administrador no la escriba es deliberado: una
        /// contraseña que conocen dos personas ya no identifica a ninguna.
        /// </remarks>
        public bool ReiniciarContrasena { get; private set; }

        public AccesoDialog(UserDto usuario)
        {
            InitializeComponent();

            _usuario = usuario;
            _esUnoMismo = usuario.Id == App.Obtener<SessionState>().IdUsuario;

            Subtitulo.Text = $"{usuario.NombreCompleto} · {usuario.Username}";

            CmbRol.SelectedIndex = usuario.EsAdministrador ? 1 : 0;
            ChkActiva.IsChecked = usuario.IsActive;

            // Un administrador no puede retirarse a sí mismo el acceso: es la
            // forma más fácil de dejar el sistema sin nadie que lo administre.
            // La API también lo rechaza; aquí se evita que llegue a intentarlo.
            if (_esUnoMismo)
            {
                CmbRol.IsEnabled = false;
                ChkActiva.IsEnabled = false;

                MostrarAviso("Esta es tu propia cuenta: no puedes cambiarte el rol " +
                             "ni desactivarte. Pídeselo a otro administrador.");
            }

            Loaded += (s, e) => CmbRol.Focus();
        }

        private void AlGuardar(object sender, RoutedEventArgs e)
        {
            ReiniciarContrasena = ChkCambiarClave.IsChecked == true;

            Rol = CmbRol.SelectedIndex == 1 ? "Admin" : "User";
            Activa = ChkActiva.IsChecked == true;

            CambiaAcceso = !_esUnoMismo &&
                           (Rol != _usuario.Role || Activa != _usuario.IsActive);

            if (!CambiaAcceso && !ReiniciarContrasena)
            {
                // Nada que aplicar: se cierra como una cancelación en lugar de
                // lanzar peticiones que no cambian nada.
                DialogResult = false;
                Close();
                return;
            }

            // Desactivar a alguien con material sin devolver es legítimo (puede
            // haberse ido de la asociación), pero conviene que quien lo hace
            // sepa que el material sigue fuera.
            if (CambiaAcceso && !Activa && _usuario.ActiveLoans > 0)
            {
                bool seguir = Aviso.Confirmar(this,
                    "La cuenta tiene material prestado",
                    $"«{_usuario.NombreCompleto}» tiene {_usuario.ActiveLoans} préstamos sin " +
                    "devolver.\n\nDesactivar la cuenta no devuelve el material: seguirá " +
                    "contando como prestado en el inventario.",
                    "Desactivar de todos modos", peligroso: true);

                if (!seguir) return;
            }

            DialogResult = true;
            Close();
        }

        private void AlCancelar(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void MostrarAviso(string mensaje)
        {
            TxtAviso.Text = mensaje;
            PanelAviso.Visibility = Visibility.Visible;
        }
    }
}
