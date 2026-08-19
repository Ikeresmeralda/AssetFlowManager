using System;
using System.Windows;
using System.Windows.Threading;
using AssetFlow.Core;
using AssetFlow.Core.Configuration;
using AssetFlow.Core.Diagnostics;
using AssetFlow.Core.Security;
using AssetFlow.Core.Services;
using AssetFlow.Desktop.Views;
using Microsoft.Extensions.DependencyInjection;

namespace AssetFlow.Desktop
{
    public partial class App : Application
    {
        private ServiceProvider _servicios;

        /// <summary>
        /// Contenedor de servicios de la aplicacion.
        /// </summary>
        /// <remarks>
        /// WPF no admite inyeccion por constructor en las ventanas creadas
        /// desde XAML, asi que se expone un acceso estatico. Es un compromiso
        /// consciente: la alternativa (fabricas para cada ventana) anadiria
        /// mucho codigo para una aplicacion de este tamano. Lo importante es
        /// que los servicios se registran y se resuelven en un solo sitio, en
        /// lugar de instanciarse con new repartidos por las vistas como antes.
        /// </remarks>
        public static IServiceProvider Servicios { get; private set; }

        public static T Obtener<T>() where T : notnull =>
            Servicios.GetRequiredService<T>();

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Los manejadores se registran ANTES de abrir ninguna ventana: si se
            // hace despues, cualquier fallo durante el arranque cierra la
            // aplicacion sin dejar rastro en el registro.
            DispatcherUnhandledException += AlFallarEnLaInterfaz;
            AppDomain.CurrentDomain.UnhandledException += AlFallarFueraDeLaInterfaz;

            AppSettings.Cargar();

            var coleccion = new ServiceCollection();
            coleccion.AddAssetFlowCore();

            _servicios = coleccion.BuildServiceProvider();
            Servicios = _servicios;

            // Cuando la sesion se pierde en cualquier punto (token revocado,
            // cuenta desactivada), la aplicacion vuelve al login en lugar de
            // quedarse mostrando errores en cada pantalla.
            Obtener<SessionState>().SesionTerminada += AlTerminarLaSesion;

            Log.Info("Aplicacion iniciada");

            var login = new LoginWindow();
            MainWindow = login;
            login.Show();
        }

        private void AlTerminarLaSesion(string motivo)
        {
            Dispatcher.Invoke(() =>
            {
                if (MainWindow is LoginWindow)
                {
                    return;
                }

                Log.Info("Sesion terminada: " + motivo);

                var login = new LoginWindow(motivo);
                Window anterior = MainWindow;

                MainWindow = login;
                login.Show();
                anterior?.Close();
            });
        }

        private void AlFallarEnLaInterfaz(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            Log.Error("Error no controlado en la interfaz", e.Exception);

            MessageBox.Show(
                "Se ha producido un error inesperado.\n\n" +
                "La aplicacion seguira funcionando, pero conviene revisar la ultima accion.\n\n" +
                "Detalle registrado en:\n" + Log.RutaArchivo,
                "Error inesperado", MessageBoxButton.OK, MessageBoxImage.Warning);

            e.Handled = true;
        }

        private void AlFallarFueraDeLaInterfaz(object sender, UnhandledExceptionEventArgs e)
        {
            Log.Error("Error no controlado", e.ExceptionObject as Exception);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            AppSettings.Guardar();
            Log.Info("Aplicacion cerrada");

            _servicios?.Dispose();

            base.OnExit(e);
        }
    }
}
