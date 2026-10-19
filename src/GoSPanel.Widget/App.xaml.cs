using Microsoft.Gaming.XboxGameBar;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.ApplicationModel.AppService;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace GoSPanel.Widget
{
    sealed partial class App : Application
    {
        // Cada ventana de widget se guarda para que no se libere.
        private readonly System.Collections.Generic.List<XboxGameBarWidget> _widgets = new System.Collections.Generic.List<XboxGameBarWidget>();

        public App()
        {
            InitializeComponent();
            Suspending += OnSuspending;
            // Un error no previsto no debe tumbar el widget: se anota en LocalState\crash.log y se sigue.
            UnhandledException += (s, e) =>
            {
                e.Handled = true;
                try
                {
                    var path = System.IO.Path.Combine(Windows.Storage.ApplicationData.Current.LocalFolder.Path, "crash.log");
                    System.IO.File.AppendAllText(path, System.DateTime.Now.ToString("s") + " " + e.Exception + "\r\n\r\n");
                }
                catch (System.Exception) { }
            };
        }

        // Game Bar abre el widget mediante el protocolo ms-gamebarwidget.
        protected override void OnActivated(IActivatedEventArgs args)
        {
            if (args.Kind != ActivationKind.Protocol) return;
            var protocolArgs = args as IProtocolActivatedEventArgs;
            if (protocolArgs == null || protocolArgs.Uri.Scheme != "ms-gamebarwidget") return;

            var widgetArgs = args as XboxGameBarWidgetActivatedEventArgs;
            if (widgetArgs == null || !widgetArgs.IsLaunchActivation) return;

            var rootFrame = new Frame();
            Window.Current.Content = rootFrame;
            var widget = new XboxGameBarWidget(widgetArgs, Window.Current.CoreWindow, rootFrame);
            _widgets.Add(widget);
            rootFrame.Navigate(typeof(MainWidget), widget);
            Window.Current.Closed += (s, e) => _widgets.Remove(widget);
            Window.Current.Activate();
        }

        // Lanzado como app normal (fuera de Game Bar): útil para depurar la interfaz.
        protected override void OnLaunched(LaunchActivatedEventArgs e)
        {
            // GoPilot es un widget: abierto como aplicación (menú Inicio, o Windows al reiniciarlo tras actualizar)
            // se abre en Game Bar y esta ventana se cierra. Solo los scripts de prueba lo abren como app («standalone»).
            if (e.Arguments != "standalone")
            {
                var open = Windows.System.Launcher.LaunchUriAsync(new System.Uri("ms-gamebar://toggleForeground/activate/GoSPanel_3ncfs0ed3kzx8_App_GoSPanel"));
                // Si este proceso ya tiene el widget abierto en Game Bar, no se cierra
                open.Completed = (op, status) => { if (_widgets.Count == 0) Exit(); };
                return;
            }
            if (Window.Current.Content == null)
            {
                // Fuera de Game Bar no hay fondo del host detrás del contenido translúcido.
                var rootFrame = new Frame { Background = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 32, 32, 32)) };
                Window.Current.Content = rootFrame;
                rootFrame.Navigate(typeof(MainWidget), null);
            }
            Window.Current.Activate();
        }

        // El puente full trust se conecta aquí mediante el AppService del paquete.
        protected override void OnBackgroundActivated(BackgroundActivatedEventArgs args)
        {
            base.OnBackgroundActivated(args);
            var details = args.TaskInstance.TriggerDetails as AppServiceTriggerDetails;
            if (details != null)
                BridgeClient.Attach(args.TaskInstance, details.AppServiceConnection);
        }

        private void OnSuspending(object sender, SuspendingEventArgs e)
        {
            e.SuspendingOperation.GetDeferral().Complete();
        }
    }
}
