using System;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using AssetFlow.Core.Dtos;

namespace AssetFlow.Desktop.Dialogs
{
    /// <summary>
    /// Alta y edición de una cuenta.
    /// </summary>
    /// <remarks>
    /// El alta y la edición comparten formulario pero no campos: al editar
    /// desaparecen contraseña y rol. Cambiar el rol o reiniciar la contraseña
    /// de alguien tiene consecuencias distintas de corregirle un teléfono, y
    /// mezclarlo en el mismo botón «Guardar» invita a hacerlo sin querer.
    /// </remarks>
    public partial class UsuarioDialog : Window
    {
        private readonly UserDto _original;
        private bool _validacionActiva;

        /// <summary>Petición de alta. Válida si es una cuenta nueva.</summary>
        public CreateUserRequest PeticionAlta { get; private set; }

        /// <summary>Petición de edición. Válida si se estaba editando.</summary>
        public UpdateUserRequest PeticionEdicion { get; private set; }

        public UsuarioDialog(UserDto usuario)
        {
            InitializeComponent();

            _original = usuario;

            if (usuario is null)
            {
                Titulo.Text = "Nueva cuenta";
                Subtitulo.Text = "Da de alta a una persona en el sistema.";
                BtnGuardar.Content = "Crear cuenta";
            }
            else
            {
                Titulo.Text = "Editar cuenta";
                Subtitulo.Text = usuario.Username;
                BtnGuardar.Content = "Guardar cambios";

                TxtUsuario.Text = usuario.Username;
                TxtUsuario.IsEnabled = false;
                TxtUsuario.ToolTip = "El nombre de usuario no se puede cambiar: es la credencial de acceso.";

                TxtNombre.Text = usuario.FirstName ?? "";
                TxtApellidos.Text = usuario.LastName ?? "";
                TxtCorreo.Text = usuario.Email ?? "";
                TxtTelefono.Text = usuario.PhoneNumber ?? "";

                PanelAlta.Visibility = Visibility.Collapsed;
            }

            Loaded += (s, e) =>
            {
                if (usuario is null)
                {
                    TxtUsuario.Focus();
                }
                else
                {
                    TxtNombre.Focus();
                    TxtNombre.SelectAll();
                }
            };
        }

        private bool EsAlta => _original is null;

        // ============================================================
        // ENTRADA
        // ============================================================

        private void AlEditarCampo(object sender, TextChangedEventArgs e)
        {
            if (!IsInitialized) return;

            // La validación no salta mientras se escribe por primera vez: solo
            // tras un intento de guardar. Marcar en rojo un campo a medio
            // rellenar es hostil.
            if (_validacionActiva) Validar();

            OcultarError();
        }

        private void AlEditarClave(object sender, RoutedEventArgs e)
        {
            if (!IsInitialized) return;

            if (_validacionActiva) Validar();

            OcultarError();
        }

        /// <summary>
        /// Genera una contraseña aleatoria y la deja en el portapapeles.
        /// </summary>
        /// <remarks>
        /// Se genera con RandomNumberGenerator, no con Random: una contraseña
        /// predecible no es una contraseña. Se excluyen los caracteres
        /// ambiguos porque va a dictarse o copiarse a mano.
        /// </remarks>
        private void AlGenerarClave(object sender, RoutedEventArgs e)
        {
            const string Alfabeto =
                "abcdefghijkmnopqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789";

            string clave = string.Concat(RandomNumberGenerator.GetItems<char>(Alfabeto, 14));

            TxtClave.Password = clave;

            try
            {
                Clipboard.SetText(clave);

                Aviso.Info(this, "Contraseña generada",
                    "Se ha copiado al portapapeles:\n\n" + clave +
                    "\n\nComunícasela a la persona por un canal seguro. " +
                    "No se guarda en ningún sitio recuperable.");
            }
            catch (Exception)
            {
                // El portapapeles puede estar bloqueado por otra aplicación.
                // No es motivo para perder la contraseña ya generada.
                Aviso.Info(this, "Contraseña generada",
                    "No se ha podido copiar al portapapeles. Anótala:\n\n" + clave);
            }
        }

        // ============================================================
        // VALIDACION
        // ============================================================

        private static readonly Regex PatronUsuario = new("^[a-zA-Z0-9._-]+$", RegexOptions.Compiled);

        private bool Validar()
        {
            bool valido = true;

            if (EsAlta)
            {
                string usuario = TxtUsuario.Text.Trim();

                valido &= Comprobar(TxtUsuario, ErrUsuario,
                    string.IsNullOrWhiteSpace(usuario)
                        ? "Indica el nombre de usuario."
                        : usuario.Length < 3
                            ? "Debe tener al menos 3 caracteres."
                            : !PatronUsuario.IsMatch(usuario)
                                ? "Solo se admiten letras sin acentos, números, punto, guion y guion bajo."
                                : null);
            }

            valido &= Comprobar(TxtNombre, ErrNombre,
                string.IsNullOrWhiteSpace(TxtNombre.Text) ? "Indica el nombre." : null);

            valido &= Comprobar(TxtApellidos, ErrApellidos,
                string.IsNullOrWhiteSpace(TxtApellidos.Text) ? "Indica los apellidos." : null);

            valido &= Comprobar(TxtCorreo, ErrCorreo,
                string.IsNullOrWhiteSpace(TxtCorreo.Text)
                    ? "Indica el correo electrónico."
                    : !EsCorreoValido(TxtCorreo.Text.Trim())
                        ? "El correo no tiene un formato válido."
                        : null);

            if (EsAlta)
            {
                valido &= Comprobar(TxtClave, ErrClave,
                    TxtClave.Password.Length == 0
                        ? "Indica una contraseña inicial."
                        : TxtClave.Password.Length < 10
                            ? "Debe tener al menos 10 caracteres."
                            : null);
            }

            return valido;
        }

        /// <summary>
        /// Comprobación de forma, no de existencia. Se usa MailAddress en
        /// lugar de una expresión regular propia porque las expresiones
        /// regulares de correo o rechazan direcciones válidas o aceptan
        /// basura, y casi siempre las dos cosas.
        /// </summary>
        private static bool EsCorreoValido(string valor)
        {
            try
            {
                var direccion = new System.Net.Mail.MailAddress(valor);
                return direccion.Address == valor && valor.Contains('.');
            }
            catch (FormatException)
            {
                return false;
            }
        }

        private bool Comprobar(Control campo, TextBlock destino, string error)
        {
            if (error is null)
            {
                destino.Visibility = Visibility.Collapsed;
                campo.BorderBrush = (System.Windows.Media.Brush)FindResource("BorderStrong");
                return true;
            }

            destino.Text = error;
            destino.Visibility = Visibility.Visible;
            campo.BorderBrush = (System.Windows.Media.Brush)FindResource("Danger");
            return false;
        }

        // ============================================================
        // GUARDAR
        // ============================================================

        private void AlGuardar(object sender, RoutedEventArgs e)
        {
            _validacionActiva = true;

            if (!Validar())
            {
                // El foco al primer campo con error, para corregirlo sin
                // buscarlo con el ratón.
                if (ErrUsuario.Visibility == Visibility.Visible) TxtUsuario.Focus();
                else if (ErrNombre.Visibility == Visibility.Visible) TxtNombre.Focus();
                else if (ErrApellidos.Visibility == Visibility.Visible) TxtApellidos.Focus();
                else if (ErrCorreo.Visibility == Visibility.Visible) TxtCorreo.Focus();
                else if (ErrClave.Visibility == Visibility.Visible) TxtClave.Focus();
                return;
            }

            string telefono = string.IsNullOrWhiteSpace(TxtTelefono.Text)
                ? null : TxtTelefono.Text.Trim();

            if (EsAlta)
            {
                PeticionAlta = new CreateUserRequest(
                    TxtUsuario.Text.Trim(),
                    TxtNombre.Text.Trim(),
                    TxtApellidos.Text.Trim(),
                    TxtCorreo.Text.Trim(),
                    telefono,
                    TxtClave.Password,
                    CmbRol.SelectedIndex == 1 ? "Admin" : "User");
            }
            else
            {
                PeticionEdicion = new UpdateUserRequest(
                    TxtNombre.Text.Trim(),
                    TxtApellidos.Text.Trim(),
                    TxtCorreo.Text.Trim(),
                    telefono);
            }

            DialogResult = true;
            Close();
        }

        private void AlCancelar(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void OcultarError() => PanelError.Visibility = Visibility.Collapsed;
    }
}
