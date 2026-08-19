using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AssetFlow.Core.Dtos;

namespace AssetFlow.Desktop.Dialogs
{
    /// <summary>
    /// Alta y edición de un artículo.
    /// </summary>
    public partial class ArticuloDialog : Window
    {
        private readonly MaterialDto _original;
        private bool _validacionActiva;
        private readonly int _umbral;
        private readonly System.Guid? _version;

        /// <summary>Artículo con los datos introducidos. Válido si ShowDialog devuelve true.</summary>
        public SaveMaterialRequest Resultado { get; private set; }

        public ArticuloDialog(MaterialDto material)
        {
            InitializeComponent();

            _original = material;
            _umbral = material?.LowStockThreshold ?? 5;
            _version = material?.Version;

            if (material != null)
            {
                Titulo.Text = "Editar artículo";
                Subtitulo.Text = material.Name;
                TxtCodigo.Text = "Código " + material.Id;

                TxtNombre.Text = material.Name ?? "";
                TxtTipo.Text = material.Type ?? "";
                TxtProveedor.Text = material.Publisher ?? "";
                TxtStock.Text = material.TotalQuantity.ToString();

                BtnGuardar.Content = "Guardar cambios";
            }
            else
            {
                // El valor del XAML se pierde con el TextChanged inicial:
                // se fija aquí para que el campo no quede en blanco.
                TxtStock.Text = "0";
            }

            Loaded += (s, e) =>
            {
                TxtNombre.Focus();
                TxtNombre.SelectAll();
                RevisarAvisoStock();
            };
        }

        // ============================================================
        // ENTRADA
        // ============================================================

        /// <summary>Bloquea cualquier carácter que no sea un dígito.</summary>
        private void SoloNumeros(object sender, TextCompositionEventArgs e)
        {
            e.Handled = !e.Text.All(char.IsDigit);
        }

        private void AlSumarUnidad(object sender, RoutedEventArgs e) => AjustarStock(+1);

        private void AlRestarUnidad(object sender, RoutedEventArgs e) => AjustarStock(-1);

        private void AjustarStock(int delta)
        {
            int actual = LeerStock();
            // El stock no baja de cero: una existencia negativa no significa nada.
            TxtStock.Text = System.Math.Max(0, actual + delta).ToString();
            TxtStock.CaretIndex = TxtStock.Text.Length;
        }

        private int LeerStock()
        {
            return int.TryParse(TxtStock.Text, out int v) ? v : 0;
        }

        private void AlEditarCampo(object sender, TextChangedEventArgs e)
        {
            // TextChanged se dispara durante InitializeComponent, cuando los
            // controles declarados más abajo en el XAML todavía no existen.
            if (!IsInitialized) return;

            // La validación no salta mientras se escribe por primera vez: solo
            // después de un intento de guardar. Marcar en rojo un campo que el
            // usuario aún no ha terminado de rellenar es hostil.
            if (_validacionActiva) Validar();

            RevisarAvisoStock();
        }

        /// <summary>
        /// Avisa si el artículo queda por debajo del umbral de reposición.
        /// Es información, no un error: guardar sigue estando permitido.
        /// </summary>
        private void RevisarAvisoStock()
        {
            int stock = LeerStock();

            if (stock == 0)
            {
                TxtAvisoStock.Text = "Este artículo quedará marcado como agotado y no podrá prestarse.";
                AvisoStock.Visibility = Visibility.Visible;
            }
            else if (stock <= _umbral)
            {
                TxtAvisoStock.Text = $"Por debajo de {_umbral + 1} unidades " +
                                     "se marcará como stock bajo en el inventario.";
                AvisoStock.Visibility = Visibility.Visible;
            }
            else
            {
                AvisoStock.Visibility = Visibility.Collapsed;
            }
        }

        // ============================================================
        // VALIDACION
        // ============================================================

        private bool Validar()
        {
            bool valido = true;

            valido &= Comprobar(TxtNombre, ErrNombre,
                string.IsNullOrWhiteSpace(TxtNombre.Text)
                    ? "Indica el nombre del artículo."
                    : null);

            valido &= Comprobar(TxtTipo, ErrTipo,
                string.IsNullOrWhiteSpace(TxtTipo.Text)
                    ? "Indica el tipo o la categoría."
                    : null);

            valido &= Comprobar(TxtStock, ErrStock,
                !int.TryParse(TxtStock.Text, out int stock)
                    ? "Introduce un número de unidades."
                    : stock < 0 ? "El stock no puede ser negativo." : null);

            return valido;
        }

        private bool Comprobar(Control campo, TextBlock destino, string error)
        {
            if (error == null)
            {
                destino.Visibility = Visibility.Collapsed;
                Validation.ClearInvalid(campo.GetBindingExpression(TextBox.TextProperty)
                                        ?? (System.Windows.Data.BindingExpressionBase)null);
                campo.ClearValue(Validation.HasErrorProperty);
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
                // El foco va al primer campo con error, para poder corregirlo
                // sin buscarlo con el ratón.
                if (ErrNombre.Visibility == Visibility.Visible) TxtNombre.Focus();
                else if (ErrTipo.Visibility == Visibility.Visible) TxtTipo.Focus();
                else TxtStock.Focus();
                return;
            }

            // Se construye una peticion nueva en lugar de modificar el objeto
            // original. Mutarlo aqui dejaba la fila de la tabla ya cambiada
            // aunque el guardado fallara despues, mostrando datos que el
            // servidor nunca llego a aceptar.
            //
            // Version viaja de vuelta al servidor para detectar ediciones
            // simultaneas: si otro usuario ha tocado el articulo mientras este
            // dialogo estaba abierto, la API responde 409 en lugar de pisar
            // sus cambios.
            Resultado = new SaveMaterialRequest(
                TxtNombre.Text.Trim(),
                TxtTipo.Text.Trim(),
                string.IsNullOrWhiteSpace(TxtProveedor.Text) ? null : TxtProveedor.Text.Trim(),
                LeerStock(),
                _umbral,
                _version);
            DialogResult = true;
            Close();
        }

        private void AlCancelar(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
