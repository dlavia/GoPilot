using System;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.ApplicationModel.AppService;
using Windows.ApplicationModel.Background;
using Windows.Data.Json;
using Windows.Foundation.Collections;

namespace GoSPanel.Widget
{
    /// <summary>
    /// Canal con el puente full trust. El widget lanza el puente y este se conecta de vuelta
    /// por el AppService "GoSPanel.Bridge"; a partir de ahí las peticiones van por esa conexión.
    /// </summary>
    internal static class BridgeClient
    {
        private static readonly object Sync = new object();
        private static AppServiceConnection _connection;
        private static BackgroundTaskDeferral _deferral;
        private static TaskCompletionSource<bool> _ready = new TaskCompletionSource<bool>();
        private static Task _launch;

        public static void Attach(IBackgroundTaskInstance task, AppServiceConnection connection)
        {
            lock (Sync)
            {
                _deferral = task.GetDeferral();
                task.Canceled += (s, reason) => Detach();
                connection.ServiceClosed += (s, e) => Detach();
                _connection = connection;
                _ready.TrySetResult(true);
            }
        }

        private static void Detach()
        {
            lock (Sync)
            {
                _connection = null;
                if (_deferral != null) _deferral.Complete();
                _deferral = null;
                _ready = new TaskCompletionSource<bool>();
                _launch = null;
            }
        }

        public static async Task<JsonObject> SendAsync(string cmd, JsonObject args = null)
        {
            var connection = await EnsureConnectedAsync();
            if (connection == null) return Error("No se pudo iniciar el puente");

            var request = new JsonObject { ["cmd"] = JsonValue.CreateStringValue(cmd) };
            if (args != null) request["args"] = args;

            var response = await connection.SendMessageAsync(new ValueSet { ["req"] = request.Stringify() });
            if (response.Status != AppServiceResponseStatus.Success)
                return Error("Puente no disponible (" + response.Status + ")");

            object raw;
            JsonObject result;
            if (response.Message.TryGetValue("resp", out raw) && JsonObject.TryParse(raw as string, out result))
                return result;
            return Error("Respuesta no válida del puente");
        }

        private static async Task<AppServiceConnection> EnsureConnectedAsync()
        {
            Task<bool> ready;
            lock (Sync)
            {
                if (_connection != null) return _connection;
                if (_launch == null)
                    _launch = FullTrustProcessLauncher.LaunchFullTrustProcessForCurrentAppAsync("Relay").AsTask();
                ready = _ready.Task;
            }
            // El puente (.NET autocontenido) puede tardar ~8 s en arrancar en frío.
            await Task.WhenAny(ready, Task.Delay(15000));
            lock (Sync) return _connection;
        }

        /// <summary>Asegura que el agente arranque con Windows (los perfiles funcionan sin abrir el widget).</summary>
        public static async Task EnsureStartupTaskAsync()
        {
            try
            {
                var task = await StartupTask.GetAsync("GoSPanelAgent");
                if (task.State == StartupTaskState.Disabled) await task.RequestEnableAsync();
            }
            catch (Exception)
            {
                // Si el sistema no lo permite, el agente seguirá arrancando al abrir el widget.
            }
        }

        private static JsonObject Error(string message)
        {
            return new JsonObject
            {
                ["ok"] = JsonValue.CreateBooleanValue(false),
                ["error"] = JsonValue.CreateStringValue(message),
            };
        }
    }
}
