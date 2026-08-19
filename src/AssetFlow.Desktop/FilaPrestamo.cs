using System;
using System.Windows;
using AssetFlow.Core.Dtos;

namespace AssetFlow.Desktop
{
    /// <summary>
    /// Un préstamo tal y como lo ve la fila de la tabla.
    /// </summary>
    /// <remarks>
    /// Existe por una razón concreta: qué botones tienen sentido en una fila
    /// depende del estado del préstamo <b>y</b> del rol de quien mira. Un
    /// administrador ante una solicitud pendiente ve «Aprobar» y «Rechazar»;
    /// el usuario que la pidió no ve ninguno de los dos, sólo espera.
    ///
    /// Resolverlo con <c>DataTrigger</c> en XAML exigía una docena de reglas
    /// cruzadas, ilegibles y fáciles de romper. Aquí se decide una vez, en
    /// C#, donde se puede leer y probar.
    ///
    /// Aviso importante: esto decide qué se <b>muestra</b>, no qué se
    /// <b>permite</b>. Ocultar un botón no es una medida de seguridad; quien
    /// llame a la API directamente se salta esta clase por completo. Los
    /// permisos reales los comprueba el servidor en cada endpoint, y esta
    /// clase se limita a no ofrecer acciones que iban a acabar en un 403.
    /// </remarks>
    public sealed class FilaPrestamo
    {
        private readonly bool _esAdministrador;

        public FilaPrestamo(LoanDto prestamo, bool esAdministrador)
        {
            Prestamo = prestamo ?? throw new ArgumentNullException(nameof(prestamo));
            _esAdministrador = esAdministrador;
        }

        public LoanDto Prestamo { get; }

        // --------------------------------------------------------------
        // Datos que pinta la tabla. Se reexponen con el mismo nombre para
        // que las columnas del XAML no tengan que saber que existe esta
        // envoltura.
        // --------------------------------------------------------------

        public int Id => Prestamo.Id;

        public string UserFullName => Prestamo.UserFullName;

        public string ResumenArticulos => Prestamo.ResumenArticulos;

        public string Reason => Prestamo.Reason;

        public DateOnly? LoanDate => Prestamo.LoanDate;

        public DateOnly EstimatedReturnDate => Prestamo.EstimatedReturnDate;

        public string EstadoTexto => Prestamo.EstadoTexto;

        public string EstadoDetalle => Prestamo.EstadoDetalle;

        // --------------------------------------------------------------
        // Acciones disponibles en la fila
        // --------------------------------------------------------------

        /// <summary>Un administrador resuelve una solicitud pendiente.</summary>
        public Visibility VerDecision => Mostrar(_esAdministrador && Prestamo.EstaPendiente);

        /// <summary>
        /// El usuario pide devolver lo que tiene. Para un administrador el
        /// mismo botón da el préstamo por devuelto directamente, porque es
        /// quien recibe el material.
        /// </summary>
        public Visibility VerDevolver => Mostrar(Prestamo.EstaActivo);

        public string TextoDevolver => _esAdministrador ? "Devolver" : "Pedir devolución";

        /// <summary>Un administrador confirma o rechaza una devolución pedida.</summary>
        public Visibility VerDecisionDevolucion =>
            Mostrar(_esAdministrador && Prestamo.TieneDevolucionSolicitada);

        /// <summary>
        /// Sólo un administrador borra del historial, y sólo lo ya cerrado:
        /// borrar un préstamo vivo dejaría material fuera sin registro de
        /// quién lo tiene.
        /// </summary>
        public Visibility VerEliminar => Mostrar(_esAdministrador && Prestamo.EstaCerrado);

        private static Visibility Mostrar(bool condicion) =>
            condicion ? Visibility.Visible : Visibility.Collapsed;
    }
}
