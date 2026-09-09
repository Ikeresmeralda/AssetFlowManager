using System.Windows;
using System.Windows.Media;

namespace AssetFlow.Desktop.Dialogs;

public partial class AvisoWindow : Window
{
    private string _credencial;

    public AvisoWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Contraseña que el diálogo debe mostrar aparte, en un cuadro copiable.
    /// </summary>
    /// <remarks>
    /// Se fija antes de <see cref="Configurar"/>. No se mete dentro del texto
    /// del mensaje a propósito: sólo se enseña una vez y quien la recibe tiene
    /// que poder copiarla sin transcribirla.
    /// </remarks>
    internal void ConCredencial(string credencial) => _credencial = credencial;

    internal void Configurar(string titulo, string mensaje, string icono,
                             Brush colorIcono, Brush fondoIcono,
                             string textoConfirmar, string textoCancelar,
                             bool peligroso)
    {
        Titulo.Text = titulo;
        Mensaje.Text = mensaje;
        Icono.Text = icono;
        Icono.Foreground = colorIcono;
        IconoFondo.Background = fondoIcono;

        BtnConfirmar.Content = textoConfirmar;

        // Sin credencial, el cuadro de abajo no existe para el usuario.
        if (!string.IsNullOrEmpty(_credencial))
        {
            TxtCredencial.Text = _credencial;
            PanelCredencial.Visibility = Visibility.Visible;
        }

        if (textoCancelar != null)
        {
            BtnCancelar.Content = textoCancelar;
            BtnCancelar.Visibility = Visibility.Visible;
        }

        if (peligroso)
        {
            BtnConfirmar.Style = (Style)FindResource("Btn.Danger");
            // En una acción destructiva el foco arranca en Cancelar: pulsar
            // Intro por inercia no debe borrar nada.
            BtnCancelar.IsDefault = true;
            Loaded += (s, e) => BtnCancelar.Focus();
        }
        else
        {
            BtnConfirmar.IsDefault = true;
            Loaded += (s, e) => BtnConfirmar.Focus();
        }
    }

    private void AlConfirmar(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void AlCancelar(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void AlCopiar(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(TxtCredencial.Text);

            BtnCopiar.Content = "Copiado";
            TxtCredencial.SelectAll();
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            // El portapapeles lo puede tener bloqueado otro proceso. No es
            // motivo para tirar la ventana abajo: la contraseña sigue a la
            // vista y se puede seleccionar a mano, que es justo el caso que
            // este cuadro existe para cubrir.
            BtnCopiar.Content = "No se pudo";
        }
    }
}
