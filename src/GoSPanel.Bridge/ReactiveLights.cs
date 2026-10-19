using NAudio.CoreAudioApi;
using NAudio.Wave;
using Windows.Devices.Enumeration;
using Windows.Devices.Lights;
using Windows.Storage;
using Windows.UI;

namespace GoSPanel.Bridge;

/// <summary>
/// Efecto «Reactivo» de las luces de los sticks: pulsos con los golpes fuertes del sonido del juego (disparos,
/// explosiones). (Un destello con RT se probó y era demasiado brillante: quitado.) El mando se pone en modo «iluminación dinámica de Windows» (SET_RGB_CFG ·
/// modo 0): así su interfaz MI_04 es un LampArray estándar al que se envían colores en tiempo real sin escribir
/// nada en la memoria del mando (en el modo personalizado cada cambio se guarda en él y no conviene repetirlo).
/// Para controlarlo con el juego delante, GoPilot es la app de iluminación en segundo plano (extensión de paquete
/// com.microsoft.windows.lighting).
/// </summary>
internal static class ReactiveLights
{
    private const int FrameMs = 16;

    private static readonly object Lock = new();
    private static bool _running;
    private static (double R, double G, double B) _base;   // color elegido a pleno (0-255); el brillo lo pone el efecto
    private static DeviceWatcher? _watcher;
    private static LampArray? _lamps;
    private static Timer? _timer;
    private static WasapiLoopbackCapture? _capture;
    private static Timer? _audioRetry;

    private static double _pulse;              // 0-1, decae solo
    private static double _slowLevel = 0.01;   // nivel medio del sonido (para detectar golpes por encima de él)
    private static Color _last;

    public static bool Running { get { lock (Lock) return _running; } }

    private static double _low = 0.2, _high = 0.8;   // brillo en reposo y en los golpes (0-1), elegibles en el widget

    public static void Start(byte r, byte g, byte b, int lowPercent, int highPercent)
    {
        lock (Lock)
        {
            _base = (r, g, b);
            _low = Math.Clamp(lowPercent, 0, 100) / 100.0;
            _high = Math.Max(_low, Math.Clamp(highPercent, 0, 100) / 100.0);
            if (_running) return;
            _running = true;
            _pulse = 0;
            _last = default;

            _watcher = DeviceInformation.CreateWatcher(LampArray.GetDeviceSelector());
            _watcher.Added += OnLampAdded;
            _watcher.Removed += (s, info) => { lock (Lock) if (_lamps?.DeviceId == info.Id) _lamps = null; };
            _watcher.Start();

            StartAudio();
            WatchDefaultDevice(true);
            _timer = new Timer(_ => Render(), null, 0, FrameMs);
        }
        Log("iniciado");
    }

    public static void Stop()
    {
        DeviceWatcher? watcher;
        WasapiLoopbackCapture? capture;
        lock (Lock)
        {
            if (!_running) return;
            _running = false;
            _timer?.Dispose();
            _timer = null;
            _audioRetry?.Dispose();
            _audioRetry = null;
            watcher = _watcher;
            _watcher = null;
            _lamps = null;
            capture = _capture;
            _capture = null;
        }
        // Fuera del candado: al parar, la captura avisa (RecordingStopped) desde su hilo y ese aviso también lo
        // toma; con el candado cogido aquí, Dispose esperaba a ese hilo para siempre y se colgaba todo el mando.
        WatchDefaultDevice(false);
        try { watcher?.Stop(); } catch (Exception) { }
        try { capture?.StopRecording(); capture?.Dispose(); } catch (Exception) { }
        Log("parado");
    }

    private static async void OnLampAdded(DeviceWatcher sender, DeviceInformation info)
    {
        try
        {
            var lamps = await LampArray.FromIdAsync(info.Id);
            if (lamps == null || lamps.LampArrayKind != LampArrayKind.GameController) return;
            lamps.AvailabilityChanged += (s, e) => Log("disponible: " + s.IsAvailable);
            lock (Lock) { if (_running) _lamps = lamps; }
            Log($"luces: {info.Name}, {lamps.LampCount} LED, disponible: {lamps.IsAvailable}");
            if (!lamps.IsAvailable) await PrioritizeAsync();
        }
        catch (Exception ex) { Log("luces: " + ex.Message); }
    }

    /// <summary>
    /// Pide al servicio que ponga a GoPilot el primero en «Iluminación dinámica» (si no, Windows deja las luces a su
    /// propio efecto y GoPilot no las puede mover). Es lo que haría el usuario arrastrándolo arriba en Configuración.
    /// </summary>
    private static async Task PrioritizeAsync()
    {
        try
        {
            var sid = System.Security.Principal.WindowsIdentity.GetCurrent().User!.Value;
            var result = await Program.CallServiceAsync("lighting.prioritize", new System.Text.Json.Nodes.JsonObject { ["sid"] = sid });
            Log("prioridad: " + result.ToJsonString());
        }
        catch (Exception ex) { Log("prioridad: " + ex.Message); }
    }

    private static void Render()
    {
        LampArray? lamps;
        lock (Lock) lamps = _lamps;
        if (lamps == null) return;

        // Del brillo de reposo al de pico con los golpes (por defecto 20 → 80 %: con menos rango apenas se notaba).
        var scale = _low + (_high - _low) * _pulse;
        Color color = Color.FromArgb(255, Mix(_base.R * scale, 0), Mix(_base.G * scale, 0), Mix(_base.B * scale, 0));
        _pulse *= 0.9;   // ~250 ms en volver al reposo

        if (color.Equals(_last) || !lamps.IsAvailable) return;
        try
        {
            lamps.SetColor(color);
            _last = color;
        }
        catch (Exception) { }
    }

    private static byte Mix(double channel, double white) => (byte)Math.Clamp(channel + (255 - channel) * white, 0, 255);

    // ---------------------------------------------------------------------
    // Sonido del sistema (lo que suena por los altavoces, como el juego)
    // ---------------------------------------------------------------------

    // La captura en bucle se queda en la salida que había al empezar: al cambiar la predeterminada (auriculares,
    // Bluetooth…) se vuelve a abrir sobre la nueva, o las luces dejarían de reaccionar.
    private static MMDeviceEnumerator? _enumerator;
    private static readonly DefaultDeviceWatcher DeviceWatcherClient = new();

    private static void WatchDefaultDevice(bool on)
    {
        try
        {
            if (on && _enumerator == null)
            {
                _enumerator = new MMDeviceEnumerator();
                _enumerator.RegisterEndpointNotificationCallback(DeviceWatcherClient);
            }
            else if (!on && _enumerator != null)
            {
                _enumerator.UnregisterEndpointNotificationCallback(DeviceWatcherClient);
                _enumerator.Dispose();
                _enumerator = null;
            }
        }
        catch (Exception ex) { Log("audio: " + ex.Message); }
    }

    private static void RestartAudio()
    {
        WasapiLoopbackCapture? old;
        lock (Lock)
        {
            if (!_running) return;
            old = _capture;
            _capture = null;
        }
        try { old?.StopRecording(); old?.Dispose(); } catch (Exception) { }
        lock (Lock) if (_running && _capture == null) StartAudio();
        Log("audio: nueva salida predeterminada");
    }

    private sealed class DefaultDeviceWatcher : NAudio.CoreAudioApi.Interfaces.IMMNotificationClient
    {
        public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
        {
            // Se avisa desde un hilo de COM: el trabajo, aparte.
            if (flow == DataFlow.Render && role == Role.Multimedia) Task.Run(RestartAudio);
        }
        public void OnDeviceStateChanged(string deviceId, DeviceState newState) { }
        public void OnDeviceAdded(string pwstrDeviceId) { }
        public void OnDeviceRemoved(string deviceId) { }
        public void OnPropertyValueChanged(string pwstrDeviceId, NAudio.CoreAudioApi.PropertyKey key) { }
    }

    private static void StartAudio()
    {
        try
        {
            var capture = new WasapiLoopbackCapture();
            capture.DataAvailable += OnAudio;
            capture.RecordingStopped += (s, e) =>
            {
                // Cambio de salida (auriculares, Bluetooth…) o suspensión: se vuelve a abrir.
                lock (Lock)
                {
                    if (!_running || _capture != capture) return;
                    _capture = null;
                    _audioRetry = new Timer(_ => { lock (Lock) if (_running && _capture == null) StartAudio(); }, null, 2000, Timeout.Infinite);
                }
            };
            _capture = capture;
            capture.StartRecording();
        }
        catch (Exception ex)
        {
            Log("audio: " + ex.Message);
            _audioRetry = new Timer(_ => { lock (Lock) if (_running && _capture == null) StartAudio(); }, null, 5000, Timeout.Infinite);
        }
    }

    private static void OnAudio(object? sender, WaveInEventArgs e)
    {
        if (sender is not WasapiLoopbackCapture capture || e.BytesRecorded == 0) return;
        var format = capture.WaveFormat;
        if (format.Encoding != WaveFormatEncoding.IeeeFloat || format.BitsPerSample != 32) return;

        // Nivel RMS de este bloque (~10 ms) con todos los canales.
        double sum = 0;
        var samples = e.BytesRecorded / 4;
        for (var i = 0; i < samples; i++)
        {
            var v = BitConverter.ToSingle(e.Buffer, i * 4);
            sum += v * v;
        }
        var level = Math.Sqrt(sum / Math.Max(1, samples));

        // Golpe: bastante por encima del nivel medio reciente (la música o el ambiente constante no cuentan).
        // Un golpe del doble del nivel medio ya da casi el máximo.
        if (level > 0.03 && level > _slowLevel * 1.8)
            _pulse = Math.Max(_pulse, Math.Min(1, (level / _slowLevel - 1.3) / 1.2));
        _slowLevel = Math.Max(0.005, _slowLevel * 0.97 + level * 0.03);
    }

    private static void Log(string text)
    {
        try
        {
            var path = Path.Combine(ApplicationData.Current.LocalFolder.Path, "reactive.log");
            File.AppendAllText(path, $"{DateTime.Now:HH:mm:ss} {text}{Environment.NewLine}");
        }
        catch (Exception) { }
    }
}
