using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using GoSPanel.Widget.Controls;
using Microsoft.Gaming.XboxGameBar;
using Windows.Data.Json;
using Windows.System;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Animation;
using Windows.UI.Xaml.Navigation;

namespace GoSPanel.Widget
{
    public sealed partial class MainWidget : Page
    {
        // SPL real de cada modo: lo apunta el servicio al leer el firmware (power.get › limits); hasta entonces, el de
        // una Legion Go S Z1 Extreme.
        private readonly Dictionary<string, int> _presetSpl = new Dictionary<string, int>
        {
            ["quiet"] = 10, ["balanced"] = 16, ["performance"] = 30,
        };
        private static readonly string[] ModeOrder = { "quiet", "balanced", "performance", "custom", "auto" };
        private static readonly Dictionary<string, string> ModeNames = new Dictionary<string, string>
        {
            ["quiet"] = "Silencio", ["balanced"] = "Equilibrado", ["performance"] = "Rendimiento", ["custom"] = "Manual", ["auto"] = "Auto",
        };
        private static readonly Dictionary<string, string> ModeGlyphs = new Dictionary<string, string>
        {
            ["quiet"] = "", ["balanced"] = "", ["performance"] = "", ["custom"] = "", ["auto"] = "",
        };

        private static readonly CultureInfo Es = new CultureInfo("es-ES");
        private const string SlideSpline = "0.2,0.8 0.2,1";
        private static readonly TimeSpan SlideDuration = TimeSpan.FromMilliseconds(260);

        private readonly DispatcherTimer _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        private readonly bool _systemAnimations = new UISettings().AnimationsEnabled;
        private bool _animate => _systemAnimations && Pref("animations", true);
        private XboxGameBarWidget _widget;

        private string _mode = "";
        private int _customSpl;
        private bool _polling, _loading, _connected, _initialized;
        private List<DisplayMode> _modes = new List<DisplayMode>();
        private int _currentWidth, _currentHeight, _currentHz;

        private readonly Throttle _brightness = new Throttle();
        private readonly Throttle _volume = new Throttle();

        public MainWidget()
        {
            Theme.Apply(Pref("theme", Theme.Simple));
            // Sonidos propios (más altos): los de XAML quedan apagados para no oírlos dos veces.
            ElementSoundPlayer.State = ElementSoundPlayerState.Off;
            Sounds.Enabled = Pref("sounds", true);
            _ = Sounds.InitAsync();
            InitializeComponent();
            AddHandler(PointerReleasedEvent, new PointerEventHandler(OnSoundPointerReleased), true);
            EventHandler onTheme = (s, e) => ApplyThemeShapes();
            Theme.Changed += onTheme;
            Unloaded += (s, e) => Theme.Changed -= onTheme;
            AccentColors.Attach(Dispatcher);
            ApplyInterfacePrefs();

            _timer.Tick += async (s, e) =>
            {
                try
                {
                    await RefreshStatusAsync();
                    await FollowPowerModeAsync();
                    if (_connected) await RefreshDesktopAsync();   // «activo» cambia con la app que hay delante
                    if (_connected) await RefreshAutoTdpAsync();   // vatios y FPS de AutoTDP en vivo
                }
                catch (Exception ex)
                {
                    LogError("tick", ex);
                }
            };
            Loaded += async (s, e) =>
            {
                ApplyThemeShapes();
                InitCards();
                InitUpdates();
                Window.Current.CoreWindow.KeyDown += OnCoreWindowKeyDown;
                FocusManager.GotFocus += OnAnyGotFocus;
                _timer.Start();
                try { await RefreshAllAsync(); }
                catch (Exception ex) { LogError("load", ex); }
                try { await RefreshUpdatesAsync(); }
                catch (Exception ex) { LogError("updates", ex); }
                await BridgeClient.EnsureStartupTaskAsync();
            };
            Unloaded += (s, e) =>
            {
                _timer.Stop();
                Window.Current.CoreWindow.KeyDown -= OnCoreWindowKeyDown;
                FocusManager.GotFocus -= OnAnyGotFocus;
            };

            // Cambio de filtro con el mando: se escucha en la página (incluidos eventos ya gestionados) y en la ventana.
            AddHandler(KeyDownEvent, new KeyEventHandler(OnPageKeyDown), true);
            // El ScrollViewer usa LT/RT/LB/RB para pasar página: se interceptan antes (fase de «preview») para que solo
            // cambien de filtro. La cruceta también se gestiona aquí: la navegación automática se equivoca en este widget.
            PreviewKeyDown += (s, e) =>
            {
                var k = e.Key;
                if (MoveFocus(k))
                {
                    e.Handled = true;
                    return;
                }
                if (k != VirtualKey.GamepadLeftTrigger && k != VirtualKey.GamepadRightTrigger
                    && k != VirtualKey.GamepadLeftShoulder && k != VirtualKey.GamepadRightShoulder) return;
                HandleTabKey(k);   // LB / RB no hacen nada en el widget
                e.Handled = true;
            };

            // Sliders: hay que «entrar» con A para moverlos (así no se mueven sin querer al bajar con el stick);
            // una vez dentro, el D-pad avanza de SmallChange en SmallChange (10 % brillo, 5 % volumen, 1 W TDP).
            BuildSwatches();
            BuildAutoTargets();
            foreach (var slider in new[] { SplSlider, SpptSlider, FpptSlider, LightBrightnessSlider, LightSpeedSlider, ChillMinSlider, ChillMaxSlider,
                                           LsCenterSlider, RsCenterSlider, LsEdgeSlider, RsEdgeSlider, LtLowSlider, RtLowSlider, LtHighSlider, RtHighSlider, GyroSensitivitySlider,
                                           DesktopPointerSlider, DesktopScrollSlider, AutoMinSlider, AutoMaxSlider })
            {
                slider.IsFocusEngagementEnabled = true;
                slider.PreviewKeyDown += OnSliderPreviewKeyDown;
            }
        }

        // =====================================================================
        // Escala fija: en la Game Bar de modo portátil el panel mide ~380 px de ancho fijo y el texto
        // a tamaño 1:1 queda pequeño frente a los paneles de Xbox; se amplía todo y el contenido se
        // reparte en el ancho restante.
        // =====================================================================

        private const double ContentScale = 1.2;

        private void OnScaleHostSizeChanged(object sender, SizeChangedEventArgs e)
        {
            RootScale.ScaleX = ContentScale;
            RootScale.ScaleY = ContentScale;
            LayoutRoot.Width = e.NewSize.Width / ContentScale;
            LayoutRoot.Height = e.NewSize.Height / ContentScale;
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            _widget = e.Parameter as XboxGameBarWidget;
        }

        private DateTime _lastTabKey;

        private void OnSliderPreviewKeyDown(object sender, KeyRoutedEventArgs e)
        {
            var slider = (Slider)sender;
            if (!slider.IsFocusEngaged) return;
            int direction;
            if (e.Key == VirtualKey.GamepadDPadLeft || e.Key == VirtualKey.GamepadLeftThumbstickLeft || e.Key == VirtualKey.Left) direction = -1;
            else if (e.Key == VirtualKey.GamepadDPadRight || e.Key == VirtualKey.GamepadLeftThumbstickRight || e.Key == VirtualKey.Right) direction = 1;
            else return;

            var step = slider.SmallChange > 0 ? slider.SmallChange : 1;
            var value = Math.Round((slider.Value + direction * step) / step) * step;
            slider.Value = Math.Max(slider.Minimum, Math.Min(slider.Maximum, value));
            e.Handled = true;
        }

        private void OnPageKeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (HandleTabKey(e.Key)) e.Handled = true;
        }

        private void OnCoreWindowKeyDown(Windows.UI.Core.CoreWindow sender, Windows.UI.Core.KeyEventArgs args)
        {
            HandleTabKey(args.VirtualKey);
            // Sonido de pulsar con A / Intro / espacio sobre un botón o un deslizador (también en las listas flotantes).
            var key = args.VirtualKey;
            if (!args.KeyStatus.WasKeyDown && (key == VirtualKey.GamepadA || key == VirtualKey.Enter || key == VirtualKey.Space))
            {
                var focused = FocusManager.GetFocusedElement();
                if (focused is ButtonBase || focused is Slider) Sounds.Play("Invoke");
            }
            else if (!args.KeyStatus.WasKeyDown && (key == VirtualKey.GamepadB || key == VirtualKey.Escape)) Sounds.Play("GoBack");
        }

        // Foco con el mando o el teclado (con el ratón o el dedo no suena, como en la app de Xbox).
        private void OnAnyGotFocus(object sender, FocusManagerGotFocusEventArgs e)
        {
            var control = e.NewFocusedElement as Control;
            if (control != null && control.FocusState == FocusState.Keyboard) Sounds.Focus();
            UpdateHints();
        }

        private void OnSoundPointerReleased(object sender, PointerRoutedEventArgs e)
        {
            for (var element = e.OriginalSource as DependencyObject; element != null && element != this; element = VisualTreeHelper.GetParent(element))
            {
                if (element is ButtonBase) { Sounds.Play("Invoke"); return; }
            }
        }

        /// <summary>LT / RT cambian de filtro (Game Bar se queda LB / RB para moverse entre widgets).</summary>
        private bool HandleTabKey(VirtualKey key)
        {
            if (_testing == "triggers" && (key == VirtualKey.GamepadLeftTrigger || key == VirtualKey.GamepadRightTrigger))
                return true;   // probando los gatillos: no cambian de pestaña
            int direction;
            // Solo LT / RT: LB / RB son de Game Bar (cambiar de widget).
            if (key == VirtualKey.GamepadLeftTrigger) direction = -1;
            else if (key == VirtualKey.GamepadRightTrigger) direction = 1;
            else return false;

            // La misma pulsación llega por la página y por la ventana: ignorar el duplicado.
            if ((DateTime.UtcNow - _lastTabKey).TotalMilliseconds < 150) return true;
            _lastTabKey = DateTime.UtcNow;
            ShowFilter((Array.IndexOf(Filters, _filter) + Filters.Length + direction) % Filters.Length);
            return true;
        }

        private static void AddSpline(Storyboard sb, DependencyObject target, string property, double from, double to)
        {
            var animation = new DoubleAnimationUsingKeyFrames();
            animation.KeyFrames.Add(new DiscreteDoubleKeyFrame { KeyTime = TimeSpan.Zero, Value = from });
            var spline = new SplineDoubleKeyFrame { KeyTime = SlideDuration, Value = to };
            spline.KeySpline = (KeySpline)Windows.UI.Xaml.Markup.XamlBindingHelper.ConvertValue(typeof(KeySpline), SlideSpline);
            animation.KeyFrames.Add(spline);
            Storyboard.SetTarget(animation, target);
            Storyboard.SetTargetProperty(animation, property);
            sb.Children.Add(animation);
        }

        // =====================================================================
        // Carga de datos
        // =====================================================================

        private async Task RefreshAllAsync()
        {
            await RefreshStatusAsync();
            _initialized = true;
            if (_connected) await RefreshAllButStatusAsync();
        }

        private async Task RefreshAllButStatusAsync()
        {
            await RefreshPowerModeAsync();
            await RefreshTdpAsync();
            await RefreshDisplayAsync();
            await RefreshQuickSlidersAsync();
            await RefreshTogglesAsync();
            await RefreshAmdAsync();
        }

        // =====================================================================
        // AMD Radeon (ADLX): AFMF, RSR, Chill, Nitidez, Anti-Lag, Vari-Bright
        // =====================================================================

        private readonly Dictionary<string, bool> _amd = new Dictionary<string, bool>();

        private static readonly Dictionary<string, string[]> AmdStateText = new Dictionary<string, string[]>
        {
            // [desactivado, activado]
            ["afmf"] = new[] { "Frames extra", "Generando frames" },
            ["rsr"] = new[] { "Super Resolution", "Activado" },
            ["chill"] = new[] { "Límite de FPS", "Activado" },
            ["sharpening"] = new[] { "Image Sharpening", "Activado" },
            ["antilag"] = new[] { "Menos latencia", "Activado" },
            ["varibright"] = new[] { "Ahorro de pantalla", "Activado" },
            ["freesync"] = new[] { "Desactivado", "Activado" },
        };

        private Card AmdCard(string feature)
        {
            switch (feature)
            {
                case "afmf": return AfmfCard;
                case "rsr": return RsrCard;
                case "chill": return ChillCard;
                case "sharpening": return SharpCard;
                case "antilag": return AntiLagCard;
                case "varibright": return VariBrightCard;
                case "freesync": return VrrCard;
                default: return null;
            }
        }

        private async Task RefreshAmdAsync()
        {
            var data = await CallAsync("amd.get", null, false);
            if (data == null) return;
            ApplyAmd(data);
        }

        private void ApplyAmd(JsonObject data)
        {
            var available = data.GetNamedBoolean("available", false);
            foreach (var feature in AmdStateText.Keys)
            {
                var info = Obj(data, feature);
                var supported = available && info != null && info.GetNamedBoolean("supported", false);
                var on = supported && info.ContainsKey("enabled") && info.GetNamedBoolean("enabled", false);
                _amd[feature] = on;

                var detail = AmdStateText[feature][on ? 1 : 0];
                if (feature == "chill" && on)
                {
                    var min = Num(info, "minFps");
                    var max = Num(info, "maxFps");
                    if (min.HasValue && max.HasValue) detail = min.Value.ToString("0") + "–" + max.Value.ToString("0") + " FPS";
                }
                if (feature == "sharpening" && on && Num(info, "sharpness").HasValue) detail = Num(info, "sharpness").Value.ToString("0") + " %";

                if (feature == "chill") ApplyChillRange(info, on);

                var card = AmdCard(feature);
                if (card == null) continue;
                card.Available = supported;
                card.IsOn = on;
                if (feature == "chill")
                {
                    // Chill: el rango en grande; apagado, «Apagado»
                    card.Value = !supported ? "—" : on ? detail.Replace(" FPS", "") : "Apagado";
                    card.State = !supported ? "No disponible" : on ? "FPS mínimos y máximos" : "Límite de FPS";
                }
                else card.State = !supported ? "No disponible" : on ? "Activado" + (detail != "Activado" ? " · " + detail : "") : "Desactivado";
            }
        }

        // ----- Radeon Chill: FPS mínimos y máximos -----

        private bool _applyingChill;
        private readonly Throttle _chillThrottle = new Throttle();

        private void ApplyChillRange(JsonObject info, bool on)
        {
            ChillPanel.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            ChillOffText.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
            if (!on || info == null) return;
            if (ChillMinSlider.IsFocusEngaged || ChillMaxSlider.IsFocusEngaged) return;   // no pisar al usuario

            _applyingChill = true;
            try
            {
                foreach (var slider in new[] { ChillMinSlider, ChillMaxSlider })
                {
                    slider.Minimum = Num(info, "rangeMin") ?? 30;
                    slider.Maximum = Num(info, "rangeMax") ?? 300;
                }
                ChillMinSlider.Value = Num(info, "minFps") ?? ChillMinSlider.Minimum;
                ChillMaxSlider.Value = Num(info, "maxFps") ?? ChillMaxSlider.Maximum;
            }
            finally
            {
                _applyingChill = false;
            }
            UpdateChillLabels();
        }

        private void UpdateChillLabels()
        {
            ChillMinValue.Text = ChillMinSlider.Value.ToString("0") + " FPS";
            ChillMaxValue.Text = ChillMaxSlider.Value.ToString("0") + " FPS";
        }

        private void OnChillSliderChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (ChillMinValue == null || ChillMaxValue == null) return;
            if (!_applyingChill)
            {
                // Mantener mínimo ≤ máximo empujando al otro slider.
                _applyingChill = true;
                try
                {
                    if (sender == ChillMinSlider && ChillMaxSlider.Value < ChillMinSlider.Value) ChillMaxSlider.Value = ChillMinSlider.Value;
                    if (sender == ChillMaxSlider && ChillMinSlider.Value > ChillMaxSlider.Value) ChillMinSlider.Value = ChillMaxSlider.Value;
                }
                finally
                {
                    _applyingChill = false;
                }
                var args = new JsonObject
                {
                    ["min"] = JsonValue.CreateNumberValue(ChillMinSlider.Value),
                    ["max"] = JsonValue.CreateNumberValue(ChillMaxSlider.Value),
                };
                _chillThrottle.Run(async () =>
                {
                    var data = await CallAsync("amd.chill", args);
                    if (data != null) ApplyAmd(data);
                });
            }
            UpdateChillLabels();
        }

        // ----- Giroscopio (IMU del mando) -----

        private bool _gyro = true;

        private async void OnGyroClick(object sender, RoutedEventArgs e)
        {
            var data = await CallAsync("gyro.set", new JsonObject { ["enabled"] = JsonValue.CreateBooleanValue(!_gyro) });
            if (data != null) ApplyLights(data);
        }

        private async void OnAmdClick(object sender, RoutedEventArgs e)
        {
            var feature = (string)((FrameworkElement)sender).Tag;
            bool current;
            _amd.TryGetValue(feature, out current);
            var data = await CallAsync("amd.set", new JsonObject
            {
                ["feature"] = JsonValue.CreateStringValue(feature),
                ["enabled"] = JsonValue.CreateBooleanValue(!current),
            });
            if (data != null) ApplyAmd(data);
        }

        // =====================================================================
        // Interruptores: CPU Boost, touchpad, reaplicar tras suspender
        // =====================================================================

        private bool _boost, _touchpad, _reapply;

        private async Task RefreshTogglesAsync()
        {
            var boost = await CallAsync("boost.get", null, false);
            if (boost != null) ApplyBoost(boost.GetNamedBoolean("enabled", true));
            var touchpad = await CallAsync("touchpad.get", null, false);
            if (touchpad != null) ApplyTouchpad(touchpad.GetNamedBoolean("enabled", true));
            var touchscreen = await CallAsync("touchscreen.get", null, false);
            if (touchscreen != null) ApplyTouchscreen(touchscreen);
            var settings = await CallAsync("settings.get", null, false);
            if (settings != null) ApplyReapply(settings.GetNamedBoolean("reapplyOnResume", true));
            var fan = await CallAsync("fan.get", null, false);
            if (fan != null) ApplyFan(fan.GetNamedBoolean("enabled", false));
            var charge = await CallAsync("charge.get", null, false);
            if (charge != null) ApplyCharge(charge.GetNamedBoolean("enabled", false));
            var lights = await CallAsync("lights.get", null, false);
            if (lights != null) ApplyLights(lights);
            var overlay = await CallAsync("overlay.get", null, false);
            if (overlay != null) ApplyGameOverlay(overlay);
            var buttons = await CallAsync("buttons.get", null, false);
            if (buttons != null) ApplyButtons(buttons);
            var legionSpace = await CallAsync("legionspace.get", null, false);
            if (legionSpace != null) ApplyLegionSpace(legionSpace);
            var controller = await CallAsync("controller.get", null, false);
            if (controller != null) ApplyController(controller);
            var advanced = await CallAsync("controller.advanced.get", null, false);
            if (advanced != null) ApplyAdvanced(advanced);
            var fanCurve = await CallAsync("fancurve.get", null, false);
            if (fanCurve != null) ApplyFanCurve(fanCurve);
            await RefreshDesktopAsync();
            await RefreshAutoTdpAsync();
        }

        // =====================================================================
        // Ajustes del mando (firmware): apagado automático, sondeo y cruceta
        // =====================================================================

        private static readonly int[] AutoSleepSteps = { 0, 5, 10, 15, 30, 60 };
        private static readonly int[] PollRateSteps = { 125, 250, 500, 1000 };
        private static readonly string[] VibrationNames = { "Apagada", "Suave", "Media", "Fuerte" };
        private int _vibration = -1, _touchVibration = -1, _autoSleep = -1, _pollRate = -1;
        private string _dpad;

        private void ApplyController(JsonObject data)
        {
            var available = data.GetNamedBoolean("available", false);
            foreach (var tile in new[] { VibrationTile, TouchVibrationTile, PollRateTile, DpadTile }) tile.Available = available;
            VibrationCard.Available = AutoSleepCard.Available = available;
            if (!available) return;
            IJsonValue v;
            _vibration = data.TryGetValue("vibration", out v) && v.ValueType == JsonValueType.Number ? (int)v.GetNumber() : -1;
            _touchVibration = data.TryGetValue("touchpadVibration", out v) && v.ValueType == JsonValueType.Number ? (int)v.GetNumber() : -1;
            _autoSleep = data.TryGetValue("autoSleep", out v) && v.ValueType == JsonValueType.Number ? (int)v.GetNumber() : -1;
            _pollRate = data.TryGetValue("pollRate", out v) && v.ValueType == JsonValueType.Number ? (int)v.GetNumber() : -1;
            _dpad = data.TryGetValue("dpad", out v) && v.ValueType == JsonValueType.String ? v.GetString() : null;

            VibrationTile.State = _vibration >= 0 ? VibrationNames[_vibration] : "—";
            VibrationTile.IsOn = _vibration > 0;
            TouchVibrationTile.State = _touchVibration >= 0 ? VibrationNames[_touchVibration] : "—";
            TouchVibrationTile.IsOn = _touchVibration > 0;
            // Baldosa: A sube la intensidad del mando (Apagada → Suave → Media → Fuerte); dentro, mando y touchpad
            VibrationCard.Value = _vibration >= 0 ? VibrationNames[_vibration] : "—";
            VibrationCard.State = "Touchpad: " + (_touchVibration >= 0 ? VibrationNames[_touchVibration].ToLowerInvariant() : "—");
            VibrationCard.IsOn = _vibration > 0;
            VibrationCard.ActionText = _vibration >= 0 ? "Vibración " + VibrationNames[(_vibration + 1) % 4].ToLowerInvariant() : null;
            AutoSleepCard.Value = _autoSleep < 0 ? "—" : _autoSleep == 0 ? "Nunca" : _autoSleep + " min";
            AutoSleepCard.IsOn = _autoSleep > 0;
            PollRateTile.State = _pollRate > 0 ? _pollRate + " Hz" : "—";
            DpadTile.State = _dpad == null ? "—" : _dpad + " direcciones";
        }

        private async Task SetControllerAsync(string key, IJsonValue value)
        {
            var data = await CallAsync("controller.set", new JsonObject { [key] = value });
            if (data != null) ApplyController(data);
        }

        private static int Next(int[] steps, int current)
        {
            var i = Array.IndexOf(steps, current);
            return steps[(i + 1) % steps.Length];
        }

        private async void OnVibrationClick(object sender, RoutedEventArgs e) =>
            await SetControllerAsync("vibration", JsonValue.CreateNumberValue((_vibration + 1) % 4));
        private async void OnTouchVibrationClick(object sender, RoutedEventArgs e) =>
            await SetControllerAsync("touchpadVibration", JsonValue.CreateNumberValue((_touchVibration + 1) % 4));
        private async void OnAutoSleepClick(object sender, RoutedEventArgs e) =>
            await SetControllerAsync("autoSleep", JsonValue.CreateNumberValue(Next(AutoSleepSteps, _autoSleep)));
        private async void OnPollRateClick(object sender, RoutedEventArgs e) =>
            await SetControllerAsync("pollRate", JsonValue.CreateNumberValue(Next(PollRateSteps, _pollRate)));
        private async void OnDpadClick(object sender, RoutedEventArgs e) =>
            await SetControllerAsync("dpad", JsonValue.CreateStringValue(_dpad == "8" ? "4" : "8"));

        // =====================================================================
        // Curva del ventilador (solo actúa en modo Manual)
        // =====================================================================

        private bool _fanCurveOn;
        private int[] _fanSpeeds = new int[10];
        private int[] _fanMin = new int[10];
        private readonly List<Slider> _fanSliders = new List<Slider>();
        private readonly List<TextBlock> _fanValues = new List<TextBlock>();
        private DispatcherTimer _fanCurveTimer;

        // Presets (velocidad % a 10…100 °C); nunca por debajo de la curva de fábrica de la Go S.
        private static readonly Dictionary<string, int[]> FanPresets = new Dictionary<string, int[]>
        {
            ["min"] = new[] { 25, 27, 32, 36, 41, 45, 51, 55, 59, 64 },
            ["balanced"] = new[] { 30, 33, 38, 44, 50, 57, 65, 72, 80, 88 },
            ["aggressive"] = new[] { 35, 40, 46, 54, 63, 72, 82, 90, 100, 100 },
        };

        private static readonly KeyValuePair<string, object>[] FanPresetOptions =
        {
            new KeyValuePair<string, object>("De fábrica (la más silenciosa)", "min"),
            new KeyValuePair<string, object>("Equilibrada", "balanced"),
            new KeyValuePair<string, object>("Agresiva", "aggressive"),
            new KeyValuePair<string, object>("Personalizada", "custom"),
        };

        private void ApplyFanCurve(JsonObject data)
        {
            _fanCurveOn = data.GetNamedBoolean("enabled", false);
            var active = data.GetNamedBoolean("active", false);
            _fanSpeeds = data.GetNamedArray("speeds").Select(v => (int)v.GetNumber()).ToArray();
            _fanMin = data.GetNamedArray("min").Select(v => (int)v.GetNumber()).ToArray();
            var temps = data.GetNamedArray("temps").Select(v => (int)v.GetNumber()).ToArray();

            FanCurveTile.Available = true;
            FanCurveTile.IsOn = _fanCurveOn;
            FanCurveTile.State = !_fanCurveOn ? "Desactivada" : active ? "Activa" : "En modo Manual";
            FanCurvePanel.Visibility = _fanCurveOn ? Visibility.Visible : Visibility.Collapsed;
            FanCurveNote.Text = active
                ? "Se aplica ahora (modo Manual). No baja de la curva de fábrica."
                : "Solo se aplica en modo Manual: elige Manual en «Modo de funcionamiento» para usarla.";
            var preset = FanPresets.FirstOrDefault(p => p.Value.SequenceEqual(_fanSpeeds)).Key ?? "custom";
            FanPresetSelector.SetOptions(FanPresetOptions, preset);

            if (_fanSliders.Count == 0)
            {
                for (var i = 0; i < 10; i++)
                {
                    var row = new Grid { ColumnSpacing = 8 };
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38) });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
                    var label = new TextBlock { Text = temps[i] + " °C", FontSize = 11.5, Foreground = Brush("MutedBrush"), VerticalAlignment = VerticalAlignment.Center };
                    var slider = new Slider { Minimum = 0, Maximum = 100, StepFrequency = 1, SmallChange = 5, LargeChange = 10, Tag = i, IsFocusEngagementEnabled = true };
                    Windows.UI.Xaml.Automation.AutomationProperties.SetName(slider, "Ventilador a " + temps[i] + " grados");
                    var value = new TextBlock { Style = (Style)Application.Current.Resources["ValueText"], FontSize = 12 };
                    Grid.SetColumn(slider, 1);
                    Grid.SetColumn(value, 2);
                    row.Children.Add(label);
                    row.Children.Add(slider);
                    row.Children.Add(value);
                    slider.ValueChanged += OnFanSliderChanged;
                    slider.PreviewKeyDown += OnSliderPreviewKeyDown;
                    _fanSliders.Add(slider);
                    _fanValues.Add(value);
                    FanCurveSliders.Children.Add(row);
                }
            }
            _loading = true;
            for (var i = 0; i < 10; i++)
            {
                _fanSliders[i].Minimum = _fanMin[i];
                _fanSliders[i].Value = _fanSpeeds[i];
                _fanValues[i].Text = _fanSpeeds[i] + " %";
            }
            _loading = false;
        }

        private async void OnFanCurveClick(object sender, RoutedEventArgs e)
        {
            var data = await CallAsync("fancurve.set", new JsonObject { ["enabled"] = JsonValue.CreateBooleanValue(!_fanCurveOn) });
            if (data != null) ApplyFanCurve(data);
        }

        private async void OnFanPresetSelected(object sender, object value)
        {
            int[] speeds;
            if (!FanPresets.TryGetValue((string)value, out speeds)) return;   // «Personalizada»: se edita con las barras
            await SendFanCurveAsync(speeds);
        }

        private void OnFanSliderChanged(object sender, Windows.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            var index = (int)((Slider)sender).Tag;
            if (index < _fanValues.Count) _fanValues[index].Text = (int)e.NewValue + " %";
            if (_loading || _fanSliders.Count < 10) return;
            // Se envía al dejar de mover (evita escribir en la BIOS en cada paso).
            if (_fanCurveTimer == null)
            {
                _fanCurveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
                _fanCurveTimer.Tick += async (s, a) =>
                {
                    _fanCurveTimer.Stop();
                    await SendFanCurveAsync(_fanSliders.Select(x => (int)x.Value).ToArray());
                };
            }
            _fanCurveTimer.Stop();
            _fanCurveTimer.Start();
        }

        private async Task SendFanCurveAsync(int[] speeds)
        {
            var array = new JsonArray();
            foreach (var s in speeds) array.Add(JsonValue.CreateNumberValue(s));
            var data = await CallAsync("fancurve.set", new JsonObject { ["speeds"] = array });
            if (data != null) ApplyFanCurve(data);
        }

        private bool _legionSpace;

        // =====================================================================
        // Tema (Sencillo / Contraste)
        // =====================================================================

        /// <summary>Textos de la baldosa Apariencia (tema y sonidos).</summary>
        private void ApplyThemeShapes()
        {
            var theme = Theme.IsContrast ? "Contraste" : "Sencillo";
            ThemeTile.State = theme;
            AppearanceCard.Value = theme;
            var sounds = Sounds.Enabled;
            SoundsTile.IsOn = sounds;
            SoundsTile.State = sounds ? "Activados" : "Desactivados";
            AppearanceCard.State = "Sonidos " + (sounds ? "activados" : "desactivados");
        }

        /// <summary>Recorre el árbol lógico (también lo que está oculto en otras pestañas).</summary>
        private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
        {
            var stack = new Stack<DependencyObject>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                var item = stack.Pop();
                yield return item;
                if (item is Panel panel) foreach (var child in panel.Children) stack.Push(child);
                else if (item is Border border && border.Child != null) stack.Push(border.Child);
                else if (item is ScrollViewer scroll && scroll.Content is DependencyObject sc) stack.Push(sc);
                else if (item is ContentControl content && content.Content is DependencyObject cc) stack.Push(cc);
            }
        }

        // Sonidos de navegación como en la app de Xbox (los del sistema para controles XAML)
        private void OnSoundsClick(object sender, RoutedEventArgs e)
        {
            var on = !Sounds.Enabled;
            SetPref("sounds", on);
            Sounds.Enabled = on;
            if (on) Sounds.Play("Invoke");
            ApplyThemeShapes();
        }

        private void OnThemeClick(object sender, RoutedEventArgs e)
        {
            var next = Theme.IsContrast ? Theme.Simple : Theme.Contrast;
            SetPref("theme", next);
            Theme.Apply(next);
        }

        private void ApplyLegionSpace(JsonObject data)
        {
            var installed = data.GetNamedBoolean("installed", false);
            _legionSpace = data.GetNamedBoolean("enabled", false);
            var running = data.GetNamedBoolean("running", false);
            LegionSpaceTile.Available = installed;
            LegionSpaceTile.IsOn = _legionSpace;
            LegionSpaceTile.State = !installed ? "No instalado" : _legionSpace ? "Activado" : running ? "Cerrándose…" : "Desactivado";
        }

        private async void OnLegionSpaceClick(object sender, RoutedEventArgs e)
        {
            LegionSpaceTile.State = _legionSpace ? "Cerrando…" : "Arrancando…";
            var enable = !_legionSpace;
            var data = await CallAsync("legionspace.set", new JsonObject { ["enabled"] = JsonValue.CreateBooleanValue(enable) });
            if (data != null) ApplyLegionSpace(data);
            // Su widget de Game Bar (el icono fijo de la izquierda) se quita y se vuelve a poner con Legion Space
            if (data != null) await CallAsync("legionwidget.set", new JsonObject { ["visible"] = JsonValue.CreateBooleanValue(enable) }, false);
            var buttons = await CallAsync("buttons.get", null, false);
            if (buttons != null) ApplyButtons(buttons);
        }

        // =====================================================================
        // Botones Legion L / R (los lee el agente; aquí solo se configuran)
        // =====================================================================

        private bool _buttonsEnabled;
        private string _buttonsSignature;

        // Atajos que se muestran: pulsación, pulsación larga y Legion + botón.
        private static readonly string[] ButtonKeys =
        {
            // L+Y, L+X, L+A, L+B y L+LB no aparecen: los hace el firmware del mando y no se pueden cambiar.
            "L", "R", "L.long", "R.long",
        };

        // Solo con «Atajos avanzados» (desactivado por defecto).
        private static readonly string[] AdvancedButtonKeys =
        {
            "R+A", "R+B", "R+X", "R+Y", "R+LB", "R+RB", "L+RB", "R+Start", "L+Start",
        };

        private static string ButtonLabel(string key)
        {
            var legion = key[0] == 'L' ? "Legion L" : "Legion R";
            if (key.Length == 1) return legion;
            if (key.EndsWith(".long")) return legion + " mantenido";
            return legion + " + " + key.Substring(2);
        }

        private void ApplyButtons(JsonObject data)
        {
            _buttonsEnabled = data.GetNamedBoolean("enabled", true);
            ButtonsTile.IsOn = _buttonsEnabled;
            ButtonsTile.State = _buttonsEnabled ? "Activado" : "Desactivado";
            ButtonsCard.Value = _buttonsEnabled ? "GoPilot" : "Apagados";
            ButtonsHint.Text = data.GetNamedBoolean("controller", false) ? "" : "Mando no encontrado";
            ButtonsHint.Visibility = string.IsNullOrEmpty(ButtonsHint.Text) ? Visibility.Collapsed : Visibility.Visible;
            ButtonsWarning.Visibility = _buttonsEnabled && data.GetNamedBoolean("legionSpace", false) ? Visibility.Visible : Visibility.Collapsed;
            ButtonsBody.Visibility = _buttonsEnabled ? Visibility.Visible : Visibility.Collapsed;

            // Reconstruir solo si cambian las asignaciones (para no perder el foco del mando).
            var bindings = data.GetNamedObject("bindings");
            var advanced = Pref("advancedButtons", false);
            AdvancedButtonsTile.IsOn = advanced;
            AdvancedButtonsTile.State = advanced ? "Legion L / R + otro botón" : "Ocultos";
            AdvancedButtonsCard.Visibility = _buttonsEnabled ? Visibility.Visible : Visibility.Collapsed;
            AdvancedButtonsList.Visibility = advanced ? Visibility.Visible : Visibility.Collapsed;
            var signature = bindings.Stringify() + advanced;
            if (signature == _buttonsSignature) return;
            _buttonsSignature = signature;

            var options = data.GetNamedArray("actions").Select(v => v.GetObject())
                .Select(a => new KeyValuePair<string, object>(a.GetNamedString("name"), a.GetNamedString("id"))).ToList();
            // Dos columnas: Legion L a la izquierda y Legion R a la derecha cuando hay pareja.
            // Legion L / R (y mantenidos) en su tarjeta; las combinaciones, en la de «Atajos avanzados».
            FillButtons(ButtonsList, ButtonKeys, options, bindings);
            FillButtons(AdvancedButtonsList, advanced ? AdvancedButtonKeys : new string[0], options, bindings);
        }

        private void FillButtons(Grid list, string[] keys, List<KeyValuePair<string, object>> options, JsonObject bindings)
        {
            list.Children.Clear();
            list.RowDefinitions.Clear();
            for (var i = 0; i < keys.Length; i++)
            {
                var key = keys[i];
                if (i % 2 == 0) list.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var selector = new Controls.Selector { Label = ButtonLabel(key), Tag = key };
                selector.SetOptions(options, bindings.ContainsKey(key) ? bindings.GetNamedString(key) : "none");
                selector.SelectionChanged += OnButtonBindingSelected;
                Grid.SetRow(selector, i / 2);
                Grid.SetColumn(selector, i % 2);
                list.Children.Add(selector);
            }
        }

        private async void OnAdvancedButtonsClick(object sender, RoutedEventArgs e)
        {
            SetPref("advancedButtons", !Pref("advancedButtons", false));
            var data = await CallAsync("buttons.get", null, false);
            if (data != null) ApplyButtons(data);
        }

        private async void OnButtonBindingSelected(object sender, object value)
        {
            var key = (string)((Controls.Selector)sender).Tag;
            var data = await CallAsync("buttons.set", new JsonObject
            {
                ["binding"] = JsonValue.CreateStringValue(key),
                ["action"] = JsonValue.CreateStringValue((string)value),
            });
            if (data != null) ApplyButtons(data);
        }

        private async void OnButtonsToggleClick(object sender, RoutedEventArgs e)
        {
            var data = await CallAsync("buttons.set", new JsonObject { ["enabled"] = JsonValue.CreateBooleanValue(!_buttonsEnabled) });
            if (data != null) ApplyButtons(data);
        }

        // =====================================================================
        // Overlay propio sobre el juego (lo dibuja el agente; no depende de fijar widgets en Game Bar)
        // =====================================================================

        private bool _gameOverlay;

        private static readonly KeyValuePair<string, object>[] OverlayPositions =
        {
            new KeyValuePair<string, object>("Arriba a la izquierda", "tl"), new KeyValuePair<string, object>("Arriba en el centro", "tc"),
            new KeyValuePair<string, object>("Arriba a la derecha", "tr"), new KeyValuePair<string, object>("Abajo a la izquierda", "bl"),
            new KeyValuePair<string, object>("Abajo a la derecha", "br"),
        };

        private static readonly KeyValuePair<string, object>[] OverlayContents =
        {
            new KeyValuePair<string, object>("Solo FPS", "fps"), new KeyValuePair<string, object>("FPS y batería", "battery"),
            new KeyValuePair<string, object>("FPS, temperatura y batería", "full"),
        };

        private void ApplyGameOverlay(JsonObject data)
        {
            _gameOverlay = data.GetNamedBoolean("enabled", false);
            _overlayAlways = data.GetNamedBoolean("always", false);
            GameOverlayTile.IsOn = _gameOverlay;
            GameOverlayTile.State = !_gameOverlay ? "Desactivado" : _overlayAlways ? "Siempre" : "En juegos";
            OverlaySettings.Visibility = _gameOverlay ? Visibility.Visible : Visibility.Collapsed;
            OverlayCard.IsOn = _gameOverlay;
            OverlayCard.Value = !_gameOverlay ? "Apagado" : _overlayAlways ? "Siempre" : "En juegos";
            OverlayContentSelector.SetOptions(OverlayContents, data.GetNamedString("content", "full"));
            OverlayPositionSelector.SetOptions(OverlayPositions, data.GetNamedString("position", "tl"));
        }

        private async void OnGameOverlayClick(object sender, RoutedEventArgs e)
        {
            var data = await CallAsync("overlay.set", new JsonObject { ["enabled"] = JsonValue.CreateBooleanValue(!_gameOverlay) });
            if (data != null) ApplyGameOverlay(data);
        }

        private bool _overlayAlways;

        // Ajustes › Overlay: Desactivado → Activado en juegos → Activado siempre → Desactivado
        private async void OnOverlayCycleClick(object sender, RoutedEventArgs e)
        {
            var args = !_gameOverlay
                ? new JsonObject { ["enabled"] = JsonValue.CreateBooleanValue(true), ["always"] = JsonValue.CreateBooleanValue(false) }
                : !_overlayAlways
                    ? new JsonObject { ["always"] = JsonValue.CreateBooleanValue(true) }
                    : new JsonObject { ["enabled"] = JsonValue.CreateBooleanValue(false) };
            var data = await CallAsync("overlay.set", args);
            if (data != null) ApplyGameOverlay(data);
        }

        private async void OnOverlayContentSelected(object sender, object value)
        {
            var data = await CallAsync("overlay.set", new JsonObject { ["content"] = JsonValue.CreateStringValue((string)value) });
            if (data != null) ApplyGameOverlay(data);
        }

        private async void OnOverlayPositionSelected(object sender, object value)
        {
            var data = await CallAsync("overlay.set", new JsonObject { ["position"] = JsonValue.CreateStringValue((string)value) });
            if (data != null) ApplyGameOverlay(data);
        }

        // =====================================================================
        // Ventilador al máximo y límite de carga
        // =====================================================================

        private bool _fullFan, _chargeLimit;

        private async void OnFanClick(object sender, RoutedEventArgs e)
        {
            var data = await CallAsync("fan.set", new JsonObject { ["enabled"] = JsonValue.CreateBooleanValue(!_fullFan) });
            if (data != null) ApplyFan(data.GetNamedBoolean("enabled", !_fullFan));
        }

        private void ApplyFan(bool on)
        {
            _fullFan = on;
            FanCard.IsOn = on;
            FanCard.Value = on ? "Máximo" : "Auto";
            FanCard.ActionText = on ? "Ventilador automático" : "Ventilador al máximo";
        }

        private async void OnChargeClick(object sender, RoutedEventArgs e)
        {
            var data = await CallAsync("charge.set", new JsonObject { ["enabled"] = JsonValue.CreateBooleanValue(!_chargeLimit) });
            if (data != null) ApplyCharge(data.GetNamedBoolean("enabled", !_chargeLimit));
        }

        private void ApplyCharge(bool on)
        {
            _chargeLimit = on;
            ChargeCard.IsOn = on;
            ChargeCard.State = on ? "Activado · cuida la batería" : "Carga completa";
        }

        // =====================================================================
        // Luces de los sticks (HID del mando)
        // =====================================================================

        private static readonly string[] SwatchColors =
        {
            "#FF2B45", "#FF7A00", "#FFD400", "#20E060", "#00D0FF", "#2050FF", "#242299", "#9B30FF", "#FF40B0", "#FFFFFF",
        };
        private static readonly KeyValuePair<string, object>[] EffectOptions =
        {
            new KeyValuePair<string, object>("Fijo", "solid"),
            new KeyValuePair<string, object>("Respiración", "breathe"),
            new KeyValuePair<string, object>("Croma", "chroma"),
            new KeyValuePair<string, object>("Arcoíris", "rainbow"),
            // Pulsos con los golpes del sonido del juego (lo dibuja GoPilot en tiempo real)
            new KeyValuePair<string, object>("Reactivo", "reactive"),
        };

        private JsonObject _lights;
        private bool _applyingLights;
        private readonly Throttle _lightsThrottle = new Throttle();

        private void BuildSwatches()
        {
            foreach (var hex in SwatchColors)
            {
                var color = ParseHex(hex);
                var button = new Button
                {
                    Style = (Style)Application.Current.Resources["FlatButton"],
                    Width = 28, Height = 28, Margin = new Thickness(0, 0, 2, 2), Tag = hex,
                    Content = new Windows.UI.Xaml.Shapes.Ellipse { Width = 20, Height = 20, Fill = new SolidColorBrush(color) },
                    HorizontalContentAlignment = HorizontalAlignment.Center,
                    VerticalContentAlignment = VerticalAlignment.Center,
                };
                ToolTipService.SetToolTip(button, hex);
                button.Click += async (s, e) => await SetLightsAsync(new JsonObject { ["color"] = JsonValue.CreateStringValue(hex), ["enabled"] = JsonValue.CreateBooleanValue(true) });
                ColorSwatches.Children.Add(button);
            }
        }

        private static Windows.UI.Color ParseHex(string hex)
        {
            hex = hex.TrimStart('#');
            return Windows.UI.Color.FromArgb(255, Convert.ToByte(hex.Substring(0, 2), 16), Convert.ToByte(hex.Substring(2, 2), 16), Convert.ToByte(hex.Substring(4, 2), 16));
        }

        private void ApplyLights(JsonObject state)
        {
            _lights = state;
            var available = state.GetNamedBoolean("available", false);
            var on = available && state.GetNamedBoolean("enabled", false);
            var effect = state.GetNamedString("effect", "solid");
            var color = state.GetNamedString("color", "#242299").ToUpperInvariant();

            // Baldosa: A recorre Apagadas → Bajo → Medio → Alto; dentro, efecto, color y velocidad
            var level = on ? LightLevel(state) : 0;
            LightsCard.Available = available;
            LightsCard.IsOn = on;
            LightsCard.Value = !available ? "—" : LightLevelNames[level];
            LightsCard.State = !available ? "Sin mando" : on ? EffectOptions.First(o => (string)o.Value == effect).Key : "Luces de los sticks";
            LightsCard.ActionText = "Luces: " + LightLevelNames[(level + 1) % LightLevelNames.Length].ToLowerInvariant();
            LightsOffText.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
            LightsSettings.Visibility = on ? Visibility.Visible : Visibility.Collapsed;

            _gyro = !state.ContainsKey("gyro") || state["gyro"].ValueType != JsonValueType.Boolean || state.GetNamedBoolean("gyro");
            GyroCard.Available = available;
            GyroCard.IsOn = available && _gyro;
            GyroCard.State = !available ? "Sin mando" : !_gyro ? "Desactivado" : "Activado · " + GyroModeNames[Math.Max(0, Math.Min(_gyroMode, GyroModeNames.Length - 1))].ToLowerInvariant();

            _applyingLights = true;
            try
            {
                EffectSelector.SetOptions(EffectOptions, effect);
                LightBrightnessSlider.Value = state.GetNamedNumber("brightness", 80);
                LightSpeedSlider.Value = state.GetNamedNumber("speed", 50);
                ReactiveLowSlider.Value = state.ContainsKey("reactiveLow") ? state.GetNamedNumber("reactiveLow") : 20;
                ReactiveHighSlider.Value = state.ContainsKey("reactiveHigh") ? state.GetNamedNumber("reactiveHigh") : 80;
            }
            finally
            {
                _applyingLights = false;
            }
            LightBrightnessValue.Text = LightBrightnessSlider.Value.ToString("0") + " %";
            LightSpeedValue.Text = LightSpeedSlider.Value.ToString("0") + " %";

            // Croma y arcoíris generan sus propios colores; el color elegido solo cuenta en fijo y respiración.
            var usesColor = effect == "solid" || effect == "breathe" || effect == "reactive";
            ColorSwatches.Opacity = usesColor ? 1 : 0.35;
            LightSpeedRow.Visibility = effect == "solid" || effect == "reactive" ? Visibility.Collapsed : Visibility.Visible;
            LightBrightnessRow.Visibility = effect == "reactive" ? Visibility.Collapsed : Visibility.Visible;   // Reactivo: reposo y pico
            ReactiveRows.Visibility = effect == "reactive" ? Visibility.Visible : Visibility.Collapsed;
            ReactiveLowValue.Text = ReactiveLowSlider.Value.ToString("0") + " %";
            ReactiveHighValue.Text = ReactiveHighSlider.Value.ToString("0") + " %";
            foreach (var swatch in ColorSwatches.Children.OfType<Button>())
            {
                var selected = (string)swatch.Tag == color;
                swatch.BorderBrush = selected ? Brush("TextBrush") : new SolidColorBrush(Windows.UI.Colors.Transparent);
                swatch.BorderThickness = new Thickness(selected ? 2 : 0);
            }
        }

        private async Task SetLightsAsync(JsonObject changes)
        {
            var data = await CallAsync("lights.set", changes);
            if (data != null) ApplyLights(data);
        }

        // Niveles de la baldosa: el brillo (o el pico, en Reactivo) de cada uno
        private static readonly string[] LightLevelNames = { "Apagadas", "Bajo", "Medio", "Alto" };
        private static readonly int[] LightLevelBrightness = { 0, 30, 60, 100 };

        private static int LightLevel(JsonObject state)
        {
            var reactive = state.GetNamedString("effect", "solid") == "reactive";
            var value = reactive && state.ContainsKey("reactiveHigh") ? state.GetNamedNumber("reactiveHigh") : state.GetNamedNumber("brightness", 80);
            return value <= 40 ? 1 : value <= 75 ? 2 : 3;
        }

        private async void OnLightsCardClick(object sender, RoutedEventArgs e)
        {
            if (_lights == null) return;
            var on = _lights.GetNamedBoolean("enabled", false);
            var next = ((on ? LightLevel(_lights) : 0) + 1) % LightLevelNames.Length;
            if (next == 0)
            {
                await SetLightsAsync(new JsonObject { ["enabled"] = JsonValue.CreateBooleanValue(false) });
                return;
            }
            var value = JsonValue.CreateNumberValue(LightLevelBrightness[next]);
            await SetLightsAsync(new JsonObject
            {
                ["enabled"] = JsonValue.CreateBooleanValue(true),
                ["brightness"] = value,
                ["reactiveHigh"] = JsonValue.CreateNumberValue(LightLevelBrightness[next]),
            });
        }

        private async void OnEffectSelected(object sender, object value)
        {
            await SetLightsAsync(new JsonObject { ["effect"] = JsonValue.CreateStringValue((string)value), ["enabled"] = JsonValue.CreateBooleanValue(true) });
        }

        private void OnLightSliderChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            // Al construir la página los deslizadores avisan antes de que existan los demás (y antes de leer el mando).
            if (LightBrightnessValue == null || LightSpeedValue == null || ReactiveHighValue == null || ReactiveHighSlider == null) return;
            LightBrightnessValue.Text = LightBrightnessSlider.Value.ToString("0") + " %";
            LightSpeedValue.Text = LightSpeedSlider.Value.ToString("0") + " %";
            ReactiveLowValue.Text = ReactiveLowSlider.Value.ToString("0") + " %";
            ReactiveHighValue.Text = ReactiveHighSlider.Value.ToString("0") + " %";
            if (_applyingLights || _lights == null) return;
            var changes = new JsonObject
            {
                ["brightness"] = JsonValue.CreateNumberValue(LightBrightnessSlider.Value),
                ["speed"] = JsonValue.CreateNumberValue(LightSpeedSlider.Value),
                ["reactiveLow"] = JsonValue.CreateNumberValue(ReactiveLowSlider.Value),
                ["reactiveHigh"] = JsonValue.CreateNumberValue(ReactiveHighSlider.Value),
            };
            _lightsThrottle.Run(() => SetLightsAsync(changes));
        }

        private async void OnBoostClick(object sender, RoutedEventArgs e)
        {
            var data = await CallAsync("boost.set", new JsonObject { ["enabled"] = JsonValue.CreateBooleanValue(!_boost) });
            if (data != null) ApplyBoost(data.GetNamedBoolean("enabled", !_boost));
        }

        private void ApplyBoost(bool on)
        {
            _boost = on;
            BoostCard.IsOn = on;
            BoostCard.State = on ? "Activado · hasta 5,1 GHz" : "Desactivado · ahorra batería";
        }

        private async void OnTouchpadClick(object sender, RoutedEventArgs e)
        {
            var data = await CallAsync("touchpad.set", new JsonObject { ["enabled"] = JsonValue.CreateBooleanValue(!_touchpad) });
            if (data != null) ApplyTouchpad(data.GetNamedBoolean("enabled", !_touchpad));
        }

        private void ApplyTouchpad(bool on)
        {
            _touchpad = on;
            TouchpadCard.IsOn = on;
            TouchpadCard.State = on ? "Activado" : "Desactivado";
        }

        private bool _touchscreen;

        private async void OnTouchscreenClick(object sender, RoutedEventArgs e)
        {
            var data = await CallAsync("touchscreen.set", new JsonObject { ["enabled"] = JsonValue.CreateBooleanValue(!_touchscreen) });
            if (data != null) ApplyTouchscreen(data);
        }

        private void ApplyTouchscreen(JsonObject data)
        {
            _touchscreen = data.GetNamedBoolean("enabled", true);
            TouchscreenCard.Available = data.GetNamedBoolean("supported", true);
            TouchscreenCard.IsOn = _touchscreen;
            TouchscreenCard.State = !TouchscreenCard.Available ? "No disponible" : _touchscreen ? "Activada" : "Desactivada";
        }

        private async void OnResumeClick(object sender, RoutedEventArgs e)
        {
            var data = await CallAsync("settings.set", new JsonObject { ["reapplyOnResume"] = JsonValue.CreateBooleanValue(!_reapply) });
            if (data != null) ApplyReapply(data.GetNamedBoolean("reapplyOnResume", !_reapply));
        }

        private void ApplyReapply(bool on)
        {
            _reapply = on;
            ResumeTile.IsOn = on;
            ResumeTile.State = on ? "Activado" : "Desactivado";
        }

        // =====================================================================
        // Ajustes de la interfaz (guardados en los ajustes locales del widget)
        // =====================================================================

        private static readonly KeyValuePair<string, object>[] RefreshRates =
        {
            new KeyValuePair<string, object>("1 segundo", 1), new KeyValuePair<string, object>("2 segundos", 2),
            new KeyValuePair<string, object>("5 segundos", 5),
        };

        private static T Pref<T>(string key, T fallback)
        {
            object value;
            return Windows.Storage.ApplicationData.Current.LocalSettings.Values.TryGetValue(key, out value) && value is T typed ? typed : fallback;
        }

        private static void SetPref(string key, object value) => Windows.Storage.ApplicationData.Current.LocalSettings.Values[key] = value;

        private void ApplyInterfacePrefs()
        {
            var animations = Pref("animations", true);
            var seconds = Pref("refreshSeconds", 2);
            _timer.Interval = TimeSpan.FromSeconds(seconds);
            Card.Animate = TileGrid.Animate = _animate;
            CardsGrid.RefreshAnimations();

            AnimationsTile.IsOn = animations && _systemAnimations;
            AnimationsTile.State = !_systemAnimations ? "Off en Windows" : animations ? "Activadas" : "Desactivadas";
            AnimationsTile.Available = _systemAnimations;
            RefreshRateSelector.SetOptions(RefreshRates, seconds);
            UpdateAbout();
        }

        private void OnAnimationsClick(object sender, RoutedEventArgs e)
        {
            SetPref("animations", !Pref("animations", true));
            ApplyInterfacePrefs();
        }

        private void OnRefreshRateSelected(object sender, object value)
        {
            SetPref("refreshSeconds", (int)value);
            ApplyInterfacePrefs();
        }

        private void UpdateAbout()
        {
            var v = Windows.ApplicationModel.Package.Current.Id.Version;
            AboutText.Text = "GoPilot " + v.Major + "." + v.Minor + "." + v.Build + "." + v.Revision
                             + " · licencia MIT · github.com/dlavia/GoPilot"
                             + "\nServicio: " + (_connected ? "conectado" : "sin conexión")
                             + " · Agente: " + (_connected ? "activo" : "—");
        }

        // =====================================================================
        // Juego en primer plano, teclado y cerrar juego
        // =====================================================================

        private string _gameName;
        private bool _killArmed;
        private DispatcherTimer _killTimer;

        private async Task RefreshForegroundAsync()
        {
            var data = await CallAsync("foreground.get", null, false);
            var active = data != null && data.GetNamedBoolean("active", false);
            _gameName = active ? data.GetNamedString("name", "") : null;
            _gamePath = active ? StringOrNull(data, "path") : null;


            KillCard.Available = active;
            if (!_killArmed) KillCard.State = active ? _gameName : "Sin juego";

            await RefreshProfilesAsync(active);
            await RefreshLosslessAsync(active);

            // FPS del juego en primer plano (contador ETW del servicio).
            double? fps = null;
            if (active)
            {
                var fpsData = await CallAsync("fps.get", new JsonObject { ["pid"] = JsonValue.CreateNumberValue(data.GetNamedNumber("pid", 0)) }, false);
                fps = Num(fpsData, "fps");
            }
            FpsValue.Text = fps.HasValue ? fps.Value.ToString("0") : "—";
        }

        // =====================================================================
        // Perfiles por juego
        // =====================================================================

        private static readonly Dictionary<string, string> ShortModeNames = new Dictionary<string, string>
        {
            ["quiet"] = "Silencio", ["balanced"] = "Equilibrado", ["performance"] = "Rendimiento", ["auto"] = "Auto",
        };

        private bool _autoProfiles = true;
        private string _profilesSignature;

        private async Task RefreshProfilesAsync(bool gameActive)
        {
            var data = await CallAsync("profiles.list", null, false);
            if (data == null) return;
            _profilesData = data;

            _autoProfiles = data.GetNamedBoolean("auto", true);
            _autoSave = data.GetNamedBoolean("autosave", true);
            AutoSaveTile.IsOn = _autoSave;
            AutoSaveTile.State = _autoSave ? "Activado" : "Solo con el botón Guardar";
            SaveProfileButton.Visibility = _autoSave ? Visibility.Collapsed : Visibility.Visible;
            AutoProfilesTile.IsOn = _autoProfiles;
            AutoProfilesTile.State = _autoProfiles ? "Activado" : "Desactivado";

            var currentExe = StringOrNull(data, "currentExe");
            var appliedExe = StringOrNull(data, "appliedExe");
            var profiles = data.GetNamedArray("profiles").Select(v => v.GetObject()).ToList();
            var current = profiles.FirstOrDefault(p => p.GetNamedString("exe", "") == currentExe);

            // Banner: perfil activo (Global o el del juego). Tocándolo se alterna entre ambos.
            _activeTarget = StringOrNull(data, "active") ?? "global";
            var onGlobal = _activeTarget == "global";
            var global = Obj(data, "global");
            // Baldosa Perfil: siempre el perfil APLICADO (no el juego abierto). Con juego, A alterna entre su perfil y Global.
            var activeProfile = profiles.FirstOrDefault(p => p.GetNamedString("exe", "") == _activeTarget);
            _currentExe = currentExe;
            ProfileCard.Value = !onGlobal && activeProfile != null ? activeProfile.GetNamedString("name", _activeTarget) : "Global";
            ProfileCard.Glyph = onGlobal ? "\uE28E" : "\uE26E";
            ProfileCard.IsOn = !onGlobal;
            ProfileCard.Quick = _gameName != null && currentExe != null ? "toggle" : "";
            ProfileCard.ActionText = !onGlobal ? "Usar Global" : current != null ? "Usar el perfil del juego" : "Crear perfil del juego";
            ProfileCard.State = !onGlobal ? "Perfil del juego" : _gameName != null ? "Global · " + _gameName + " abierto" : "Perfil Global";
            await ApplyProfileArtAsync(onGlobal ? null : activeProfile);

            // Reconstruir la lista solo si ha cambiado (para no perder el foco del mando). El arte que aún se estaba
            // buscando también cuenta: cuando llega, se rehace (salvo que el mando esté dentro de la lista).
            var signature = data.Stringify();
            var focusInList = IsInside(FocusManager.GetFocusedElement() as DependencyObject, ProfilesList);
            if (signature == _profilesSignature && !(_artPending && !focusInList)) return;
            _profilesSignature = signature;
            _artPending = false;
            var arts = new Dictionary<string, JsonObject>();
            foreach (var p in profiles)
            {
                var art = await GetArtAsync(p);
                if (art == null) continue;
                arts[p.GetNamedString("exe", "")] = art;
                if (art.GetNamedBoolean("pending", false)) _artPending = true;
            }

            ProfilesList.Children.Clear();
            ProfilesList.Children.Add(ProfileRow(global ?? new JsonObject(), "global", "Global", onGlobal, null));
            foreach (var p in profiles.OrderBy(p => p.GetNamedString("name", "")))
            {
                var exe = p.GetNamedString("exe", "");
                JsonObject art;
                arts.TryGetValue(exe, out art);
                ProfilesList.Children.Add(ProfileRow(p, exe, p.GetNamedString("name", exe), exe == _activeTarget, art));
            }
            ProfilesEmptyText.Visibility = profiles.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            ProfileCountText.Text = profiles.Count == 1 ? "1 perfil" : profiles.Count + " perfiles";
        }

        private static string ProfileSummary(JsonObject p)
        {
            var parts = new List<string>();
            var mode = p.GetNamedString("mode", "");
            var tdp = Obj(p, "tdp");
            parts.Add(mode == "custom" ? (Num(tdp, "spl") ?? 0).ToString("0") + " W"
                : ShortModeNames.ContainsKey(mode) ? ShortModeNames[mode] : mode);
            var w = Num(p, "width");
            var h = Num(p, "height");
            if (w.HasValue && h.HasValue) parts.Add(w.Value.ToString("0") + "×" + h.Value.ToString("0"));
            var hz = Num(p, "hz");
            if (hz.HasValue) parts.Add(hz.Value.ToString("0") + " Hz");
            if (p.ContainsKey("boost") && p["boost"].ValueType == JsonValueType.Boolean && !p.GetNamedBoolean("boost")) parts.Add("Sin Boost");
            if (p.ContainsKey("lossless") && p["lossless"].ValueType == JsonValueType.Boolean && p.GetNamedBoolean("lossless")) parts.Add("LS");
            return string.Join(" · ", parts);
        }

        /// <summary>Fila de la lista de perfiles: A lo aplica (también sin su juego abierto); la papelera lo borra.</summary>
        private UIElement ProfileRow(JsonObject p, string target, string name, bool active, JsonObject art)
        {
            var grid = new Grid { ColumnSpacing = 4 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var content = new Grid { ColumnSpacing = 10 };
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var cover = ArtImage(art, "capsule") ?? ArtImage(art, "hero");
            var icon = ArtImage(art, "icon");
            if (cover != null || icon != null)
            {
                // Carátula (o icono) del juego, como en la biblioteca de Steam
                content.Children.Add(new Border
                {
                    Width = cover != null ? 56 : 26, Height = 26, CornerRadius = new CornerRadius(5),
                    Background = new ImageBrush { ImageSource = cover ?? icon, Stretch = Stretch.UniformToFill },
                });
            }
            else
            {
                content.Children.Add(new TextBlock
                {
                    Style = (Style)Application.Current.Resources["IconText"],
                    Text = target == "global" ? "\uE28E" : "\uE26E", FontSize = 17, Width = 26, TextAlignment = TextAlignment.Center,
                    Foreground = active && Theme.IsContrast ? new SolidColorBrush(Theme.OnAccent) : Brush(active ? "TextBrush" : "MutedBrush"),
                });
            }
            var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            texts.Children.Add(new TextBlock { Text = name, FontSize = 12.5, FontWeight = Windows.UI.Text.FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
            texts.Children.Add(new TextBlock { Text = target == "global" ? "Sin juego" : ProfileSummary(p), FontSize = 11, Foreground = Brush(active ? "TextBrush" : "MutedBrush"), TextTrimming = TextTrimming.CharacterEllipsis });
            Grid.SetColumn(texts, 1);
            content.Children.Add(texts);

            var use = new Button
            {
                Style = (Style)Application.Current.Resources["FlatButton"],
                Background = active ? Brush("AccentBrush") : Brush("Panel3Brush"),
                Padding = new Thickness(10, 7, 10, 7),
                Content = content,
                Tag = target,
            };
            use.Click += async (s, e) => await UseProfileAsync(target);
            grid.Children.Add(use);

            if (target != "global")
            {
                var delete = new Button
                {
                    Style = (Style)Application.Current.Resources["FlatButton"],
                    Background = Brush("Panel3Brush"),
                    Width = 38,
                    HorizontalContentAlignment = HorizontalAlignment.Center,
                    VerticalContentAlignment = VerticalAlignment.Center,
                    Content = new TextBlock { Style = (Style)Application.Current.Resources["IconText"], Text = "\uE4A6", FontSize = 16 },
                };
                ToolTipService.SetToolTip(delete, "Borrar perfil");
                Windows.UI.Xaml.Automation.AutomationProperties.SetName(delete, "Borrar el perfil de " + name);
                delete.Click += async (s, e) =>
                {
                    await CallAsync("profiles.delete", new JsonObject { ["exe"] = JsonValue.CreateStringValue(target) });
                    _profilesSignature = null;
                    await RefreshProfilesAsync(_gameName != null);
                };
                Grid.SetColumn(delete, 1);
                grid.Children.Add(delete);
            }
            return grid;
        }

        private string _activeTarget = "global";
        private bool _autoSave = true;

        private async void OnAutoSaveClick(object sender, RoutedEventArgs e)
        {
            await CallAsync("profiles.autosave", new JsonObject { ["enabled"] = JsonValue.CreateBooleanValue(!_autoSave) });
            _profilesSignature = null;
            await RefreshProfilesAsync(_gameName != null);
        }

        private JsonObject _profilesData;

        private string _currentExe, _gamePath;
        private string _artShown;
        private bool _artPending;

        /// <summary>Arte de un perfil (lo busca el agente: Steam, app de Xbox o icono del .exe). Null si no hay conexión.</summary>
        private async Task<JsonObject> GetArtAsync(JsonObject profile)
        {
            var exe = profile.GetNamedString("exe", "");
            if (exe.Length == 0) return null;
            var args = new JsonObject { ["exe"] = JsonValue.CreateStringValue(exe) };
            // La ruta del juego: la guardada en el perfil o, si es el que está abierto, la de ahora
            var path = StringOrNull(profile, "path") ?? (exe == _currentExe ? _gamePath : null);
            if (path != null) args["path"] = JsonValue.CreateStringValue(path);
            return await CallAsync("art.get", args, false);
        }

        private static ImageSource ArtImage(JsonObject art, string key)
        {
            var relative = art != null ? StringOrNull(art, key) : null;
            return relative == null ? null : new Windows.UI.Xaml.Media.Imaging.BitmapImage(new Uri("ms-appdata:///local/" + relative));
        }

        /// <summary>Baldosa Perfil con el fondo y el icono del juego (Global: sin arte).</summary>
        private async Task ApplyProfileArtAsync(JsonObject profile)
        {
            var art = profile != null ? await GetArtAsync(profile) : null;
            var signature = profile == null ? null : profile.GetNamedString("exe", "") + "|" + (art?.Stringify() ?? "");
            if (signature == _artShown) return;
            _artShown = signature;
            ProfileCard.SetArt(ArtImage(art, "hero"), ArtImage(art, "icon"));
        }

        /// <summary>A en la baldosa Perfil (con juego): alterna entre el perfil del juego y Global.</summary>
        private async void OnProfileCardClick(object sender, RoutedEventArgs e)
        {
            if (_currentExe == null) return;
            await UseProfileAsync(_activeTarget == "global" ? "game" : "global");
        }

        /// <summary>Aplica un perfil: «global», «game» (el del juego abierto, se crea si no hay) o el exe de uno guardado.</summary>
        private async Task UseProfileAsync(string target)
        {
            if (target == _activeTarget) return;
            await CallAsync("profiles.use", new JsonObject { ["target"] = JsonValue.CreateStringValue(target) });
            _profilesSignature = null;
            await RefreshAllButStatusAsync();
            await RefreshProfilesAsync(_gameName != null);
        }

        private async void OnSaveProfileClick(object sender, RoutedEventArgs e)
        {
            if (await CallAsync("profiles.save") != null)
            {
                _profilesSignature = null;
                SaveProfileIcon.Foreground = Brush("AccentBrush");   // confirmación en el propio botón
                SaveProfileButton.BorderBrush = Brush("AccentBrush");
                await Task.Delay(1200);
                SaveProfileIcon.Foreground = Brush("MutedBrush");
                SaveProfileButton.BorderBrush = new SolidColorBrush(Windows.UI.Colors.Transparent);
            }
            await RefreshProfilesAsync(_gameName != null);
        }

        private async void OnAutoProfilesClick(object sender, RoutedEventArgs e)
        {
            await CallAsync("profiles.auto", new JsonObject { ["enabled"] = JsonValue.CreateBooleanValue(!_autoProfiles) });
            await RefreshProfilesAsync(_gameName != null);
        }

        // =====================================================================
        // Lossless Scaling
        // =====================================================================

        private async Task RefreshLosslessAsync(bool gameActive)
        {
            var data = await CallAsync("ls.get", null, false);
            if (data == null) return;
            var installed = data.GetNamedBoolean("installed", false);
            var on = data.GetNamedBoolean("active", false);
            var pending = data.ContainsKey("pending") && data["pending"].ValueType == JsonValueType.Boolean && data.GetNamedBoolean("pending");
            var state = !installed ? "No instalado" : !gameActive ? "Sin juego"
                : pending ? "Cierra Game Bar…" : on ? "Escalando" : "Desactivado";

            LosslessCard.Available = installed && gameActive;
            LosslessCard.IsOn = on;
            LosslessCard.State = state;
        }

        private async void OnLosslessClick(object sender, RoutedEventArgs e)
        {
            await CallAsync("ls.toggle");
            await RefreshLosslessAsync(_gameName != null);
        }

        private static string StringOrNull(JsonObject o, string key)
        {
            IJsonValue v;
            return o.TryGetValue(key, out v) && v.ValueType == JsonValueType.String ? v.GetString() : null;
        }

        private async void OnKeyboardClick(object sender, RoutedEventArgs e)
        {
            await CallAsync("keyboard.toggle");
        }

        private async void OnKillClick(object sender, RoutedEventArgs e)
        {
            if (_gameName == null) return;
            if (!_killArmed)
            {
                // Primera pulsación: pedir confirmación durante 3 s (evita cerrar el juego sin querer con el mando).
                _killArmed = true;
                KillCard.Warning = true;
                KillCard.State = "Pulsa A otra vez para cerrar";
                if (_killTimer == null)
                {
                    _killTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
                    _killTimer.Tick += (s, a) => { _killTimer.Stop(); DisarmKill(); };
                }
                _killTimer.Start();
                return;
            }
            _killTimer.Stop();
            DisarmKill();
            await CallAsync("foreground.kill");
            await RefreshForegroundAsync();
        }

        private void DisarmKill()
        {
            _killArmed = false;
            KillCard.Warning = false;
            KillCard.State = _gameName ?? "Sin juego";
        }

        private async Task RefreshStatusAsync()
        {
            if (_polling) return;
            _polling = true;
            try
            {
                var data = await CallAsync("status", null, false);
                // La etiqueta del banner la gestionan los perfiles (Global / Juego); aquí solo se toca si no hay
                // conexión o antes de la primera carga, para que no parpadee «Conectado» en cada lectura.
                // Sin conexión con el agente: se dice abajo a la derecha (en la barra de botones)
                ConnectionText.Text = data == null ? "Sin servicio" : "";
                var wasConnected = _connected;
                _connected = data != null;
                if (wasConnected != _connected) UpdateAbout();
                if (data == null) return;

                // Al recuperar la conexión, recargar todo lo que no se pudo leer.
                if (!wasConnected && _initialized)
                {
                    var _ = RefreshAllButStatusAsync();
                }

                var temp = Num(Obj(data, "cpu"), "tempC");
                var rpm = Num(Obj(data, "fan"), "rpm");
                // Consumo real de toda la APU (contador RAPL del servicio), enchufada o con batería
                var watts = Num(Obj(data, "apu"), "watts");
                TempValue.Text = temp.HasValue ? temp.Value.ToString("0") + "°" : "—";
                TempValue.Foreground = Brush(temp >= 85 ? "WarnBrush" : "TextBrush");
                PowerValue.Text = watts.HasValue ? watts.Value.ToString(watts.Value < 10 ? "0.0" : "0", Es) : "—";
                TdpNowText.Text = watts.HasValue ? "Ahora: " + watts.Value.ToString("0", Es) + " W de consumo real" : "";
                FanValue.Text = !rpm.HasValue ? "—" : rpm.Value < 100 ? "0" : (rpm.Value / 1000).ToString("0.0", Es) + "k";
                FanCard.State = !rpm.HasValue ? "—" : rpm.Value < 100 ? "Parado" : rpm.Value.ToString("#,0", Es) + " RPM";

                UpdateBattery(Obj(data, "battery"));
                await RefreshForegroundAsync();
                if (_initialized) await RefreshQuickSlidersAsync();
            }
            finally
            {
                _polling = false;
            }
        }

        private void UpdateBattery(JsonObject battery)
        {
            var percent = Num(battery, "percent");
            var rate = Num(battery, "rateW");
            var remaining = Num(battery, "remainingWh");
            var full = Num(battery, "fullWh");
            var health = Num(battery, "healthPct");
            var charging = battery != null && battery.GetNamedBoolean("charging", false);
            var acOnline = battery != null && battery.ContainsKey("acOnline")
                           && battery["acOnline"].ValueType == JsonValueType.Boolean && battery.GetNamedBoolean("acOnline");


            if (health.HasValue && full.HasValue)
                BatteryHealthText.Text = "Cargar solo hasta el 80 % alarga la vida de la batería si la consola pasa mucho tiempo enchufada."
                                         + "\nSalud de la batería: " + health.Value.ToString("0") + " % · capacidad actual " + full.Value.ToString("0.0", Es) + " Wh";

            // Consumo suavizado (la descarga instantánea salta mucho): media exponencial de unos 30 s
            if (rate.HasValue && rate.Value > 0.5)
                _rateAvg = _rateAvg <= 0 || charging != _rateCharging ? rate.Value : _rateAvg * 0.85 + rate.Value * 0.15;
            _rateCharging = charging;
            var pct = percent.HasValue ? percent.Value.ToString("0") + " %" : "";
            if (charging && _rateAvg > 0 && remaining.HasValue && full.HasValue)
            {
                BatteryIcon.Text = "\uE0BA";
                BatteryIcon.Foreground = Brush("OkBrush");
                BatteryValue.Text = Duration((full.Value * (Pref("chargeLimitPct", _chargeLimit ? 80 : 100) / 100.0) - remaining.Value) / _rateAvg);
                BatterySub.Text = "hasta llena · " + pct;
            }
            else if (acOnline)
            {
                BatteryIcon.Text = "\uE0BA";
                BatteryIcon.Foreground = Brush("OkBrush");
                BatteryValue.Text = pct;
                BatterySub.Text = "enchufada";
            }
            else if (_rateAvg > 0 && remaining.HasValue)
            {
                BatteryIcon.Text = percent < 20 ? "\uE0C8" : percent < 50 ? "\uE0C6" : "\uE0C2";
                BatteryIcon.Foreground = Brush(percent < 20 ? "WarnBrush" : "IconBrush");
                BatteryValue.Text = Duration(remaining.Value / _rateAvg);
                BatterySub.Text = "restante · " + pct;
            }
            else
            {
                BatteryValue.Text = pct.Length > 0 ? pct : "—";
                BatterySub.Text = "batería";
            }
        }

        private double _rateAvg;
        private bool _rateCharging;

        private static string Duration(double hours)
        {
            if (double.IsInfinity(hours) || double.IsNaN(hours) || hours < 0) return "—";
            var total = (int)Math.Round(hours * 60);
            if (total > 24 * 60) return "—";
            return total >= 60 ? (total / 60) + " h " + (total % 60).ToString("00") : total + " min";
        }

        /// <summary>El modo puede cambiar fuera del widget (Legion L + Y lo cambia el firmware): seguirlo.</summary>
        private async Task FollowPowerModeAsync()
        {
            if (!_connected) return;
            var data = await CallAsync("power.get", null, false);
            ApplyModeLimits(data);
            var mode = data?.GetNamedString("mode", "");
            if (string.IsNullOrEmpty(mode) || mode == _mode) return;
            ApplyMode(mode, false);
            if (mode == "custom") await RefreshTdpAsync();
        }

        private static void LogError(string where, Exception ex)
        {
            try
            {
                var path = System.IO.Path.Combine(Windows.Storage.ApplicationData.Current.LocalFolder.Path, "crash.log");
                System.IO.File.AppendAllText(path, DateTime.Now.ToString("s") + " [" + where + "] " + ex + "\r\n\r\n");
            }
            catch (Exception) { }
        }

        private async Task RefreshPowerModeAsync()
        {
            var data = await CallAsync("power.get");
            ApplyModeLimits(data);
            if (data != null) ApplyMode(data.GetNamedString("mode", ""), false);
        }

        /// <summary>Vatios (SPL) de las baldosas de modo: los que aplica de verdad el firmware.</summary>
        private void ApplyModeLimits(JsonObject power)
        {
            var limits = Obj(power, "limits");
            if (limits == null) return;
            var changed = false;
            foreach (var tile in new[] { ModeQuiet, ModeBalanced, ModePerformance })
            {
                var mode = (string)tile.Tag;
                var spl = Num(Obj(limits, mode), "spl");
                if (!spl.HasValue) continue;
                tile.State = spl.Value.ToString("0") + " W";
                changed |= _presetSpl[mode] != (int)spl.Value;
                _presetSpl[mode] = (int)spl.Value;
            }
            if (changed && _mode != "custom" && _presetSpl.ContainsKey(_mode)) UpdateModeSummary();
        }

        private async Task RefreshTdpAsync()
        {
            var data = await CallAsync("tdp.get");
            if (data == null) return;
            var limits = Obj(data, "limits");
            _loading = true;
            try
            {
                foreach (var pair in new[] { Tuple.Create("spl", SplSlider), Tuple.Create("sppt", SpptSlider), Tuple.Create("fppt", FpptSlider) })
                {
                    var range = Obj(limits, pair.Item1);
                    pair.Item2.Minimum = Num(range, "min") ?? pair.Item2.Minimum;
                    pair.Item2.Maximum = Num(range, "max") ?? pair.Item2.Maximum;
                    pair.Item2.Value = Num(data, pair.Item1) ?? pair.Item2.Value;
                }
            }
            finally
            {
                _loading = false;
            }
            _customSpl = (int)SplSlider.Value;
            ModeCustom.State = _customSpl + " W";
            _tdpLoaded = true;
            SplMinText.Text = SplSlider.Minimum.ToString("0") + " W";
            SplMaxText.Text = SplSlider.Maximum.ToString("0") + " W";
            ApplyTdpAutoPref();
            UpdateTdpLabels();
            UpdateModeSummary();
        }

        private DateTime _lastQuickInput;

        /// <summary>
        /// Lee brillo y volumen reales. Se llama también cada 2 s para seguir los cambios hechos fuera del widget
        /// (botones de la consola, Windows), salvo si el usuario está moviendo el slider.
        /// </summary>
        private async Task RefreshQuickSlidersAsync()
        {
            if ((DateTime.UtcNow - _lastQuickInput).TotalSeconds < 3) return;   // el usuario lo está cambiando

            var brightness = await CallAsync("brightness.get", null, false);
            var volume = await CallAsync("volume.get", null, false);
            var auto = await CallAsync("display.autobrightness.get", null, false);
            if ((DateTime.UtcNow - _lastQuickInput).TotalSeconds < 3) return;   // el usuario lo tocó mientras se leía

            if (brightness != null) BrightnessBar.Value = brightness.GetNamedNumber("value", 0);
            _autoBrightness = auto != null && auto.GetNamedBoolean("enabled", false);
            _lightSensor = auto != null && auto.GetNamedBoolean("sensor", false);
            BrightnessBar.Note = brightness == null ? "—" : _autoBrightness ? "Auto · " + BrightnessBar.Value.ToString("0") + " %" : null;
            if (volume != null) VolumeBar.Value = volume.GetNamedNumber("value", 0);
            _muted = volume != null && volume.GetNamedBoolean("muted", false);
            VolumeBar.Note = volume == null ? "—" : _muted ? "Silencio" : null;
            VolumeBar.Dimmed = _muted;
            UpdateHints();
        }
        // =====================================================================
        // Modos y TDP
        // =====================================================================

        private async void OnModeTileClick(object sender, RoutedEventArgs e)
        {
            await SetModeAsync((string)((Tile)sender).Tag);
        }

        /// <summary>A en la baldosa Modo: el siguiente (Silencio → Equilibrado → Rendimiento → Manual → Auto → …).</summary>
        private async void OnModeCardClick(object sender, RoutedEventArgs e)
        {
            var index = Array.IndexOf(ModeOrder, _autoTdp ? "auto" : _mode);
            await SetModeAsync(ModeOrder[(index + 1) % ModeOrder.Length]);
        }

        private async Task SetModeAsync(string mode)
        {
            // Auto lo lleva el agente (el firmware queda en Manual); cualquier otro modo lo apaga
            if (mode == "auto")
            {
                await SetAutoTdpAsync(new JsonObject { ["enabled"] = JsonValue.CreateBooleanValue(true) });
                await RefreshPowerModeAsync();
                return;
            }
            if (_autoTdp) await SetAutoTdpAsync(new JsonObject { ["enabled"] = JsonValue.CreateBooleanValue(false) });
            var data = await CallAsync("power.set", new JsonObject { ["mode"] = JsonValue.CreateStringValue(mode) });
            if (data != null) ApplyMode(data.GetNamedString("mode", ""), true);
        }

        private void ApplyMode(string mode, bool animate)
        {
            _mode = mode;
            // Con AutoTDP el firmware está en Manual, pero se muestra «Auto»
            var shown = _autoTdp ? "auto" : mode;
            ModeQuiet.IsOn = shown == "quiet";
            ModeBalanced.IsOn = shown == "balanced";
            ModePerformance.IsOn = shown == "performance";
            ModeCustom.IsOn = shown == "custom";
            ModeAuto.IsOn = shown == "auto";

            var isCustom = shown == "custom";
            if (TdpPanel.Visibility == Visibility.Visible != isCustom)
                ToggleTdpPanel(isCustom, animate && _animate);
            AutoPanel.Visibility = shown == "auto" ? Visibility.Visible : Visibility.Collapsed;
            UpdateModeSummary();
        }

        private void ToggleTdpPanel(bool show, bool animate)
        {
            if (!show)
            {
                TdpPanel.Visibility = Visibility.Collapsed;
                return;
            }
            TdpPanel.Visibility = Visibility.Visible;
            if (!animate) return;
            var sb = new Storyboard();
            AddSpline(sb, TdpPanel, "Opacity", 0, 1);
            AddSpline(sb, TdpPanelTransform, "Y", -10, 0);
            sb.Begin();
        }

        private void UpdateModeSummary()
        {
            var shown = _autoTdp ? "auto" : _mode;
            if (!ModeNames.ContainsKey(shown))
            {
                return;
            }
            ModeCard.Value = ModeNames[shown];
            ModeCard.Glyph = ModeGlyphs[shown];
            ModeCard.State = shown == "auto" ? _autoTarget + " FPS" + (_autoWatts.HasValue ? " · ahora " + _autoWatts.Value.ToString("0") + " W" : "")
                : (shown == "custom" ? _customSpl : _presetSpl[shown]) + " W";
            ModeAuto.State = _autoTarget + " FPS";
            ModeCard.ActionText = "Pasar a " + ModeNames[ModeOrder[(Array.IndexOf(ModeOrder, shown) + 1) % ModeOrder.Length]];
        }

        // ---------------------------------------------------------------------
        // Manual (como la Steam Deck): un límite de potencia (SPL); ráfagas y picos automáticos por encima en la
        // proporción de los modos de Lenovo, o a mano en «Avanzado». Se aplica al soltar el deslizador.
        // ---------------------------------------------------------------------

        private DispatcherTimer _tdpApplyTimer;
        // Los Slider con Minimum > 0 lanzan ValueChanged durante InitializeComponent, antes de que existan los demás
        // controles: hasta leer los valores reales (RefreshTdpAsync) no se reacciona ni, sobre todo, se aplica nada.
        private bool _tdpLoaded;

        private bool TdpAuto => Pref("tdpAuto", true);

        private void OnTdpSliderChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (!_tdpLoaded) return;
            if (!_loading)
            {
                string note = null;
                _loading = true;
                try
                {
                    if (TdpAuto)
                    {
                        if (sender == SplSlider) SetAutoBursts();
                    }
                    // A mano: mantener SPL <= sPPT <= fPPT empujando a los vecinos, y decir cuál se ha movido
                    else if (sender == SplSlider)
                    {
                        if (SpptSlider.Value < SplSlider.Value) { SpptSlider.Value = SplSlider.Value; note = "sPPT subido a " + Watts(SpptSlider) + " para no quedar por debajo del límite"; }
                        if (FpptSlider.Value < SplSlider.Value) { FpptSlider.Value = SplSlider.Value; note = "fPPT subido a " + Watts(FpptSlider) + " para no quedar por debajo del límite"; }
                    }
                    else if (sender == SpptSlider)
                    {
                        if (SplSlider.Value > SpptSlider.Value) { SplSlider.Value = SpptSlider.Value; note = "Límite bajado a " + Watts(SplSlider) + ": no puede superar las ráfagas"; }
                        if (FpptSlider.Value < SpptSlider.Value) { FpptSlider.Value = SpptSlider.Value; note = "fPPT subido a " + Watts(FpptSlider) + " para no quedar por debajo de las ráfagas"; }
                    }
                    else if (sender == FpptSlider)
                    {
                        if (SpptSlider.Value > FpptSlider.Value) { SpptSlider.Value = FpptSlider.Value; note = "sPPT bajado a " + Watts(SpptSlider) + ": no puede superar los picos"; }
                        if (SplSlider.Value > FpptSlider.Value) { SplSlider.Value = FpptSlider.Value; note = "Límite bajado a " + Watts(SplSlider) + ": no puede superar los picos"; }
                    }
                }
                finally
                {
                    _loading = false;
                }
                TdpNote.Text = note ?? "";
                TdpNote.Visibility = note != null ? Visibility.Visible : Visibility.Collapsed;
                ScheduleTdpApply();
            }
            UpdateTdpLabels();
        }

        private static string Watts(Slider slider) => slider.Value.ToString("0") + " W";

        /// <summary>Ráfagas = límite × 1,25 y picos = límite × 1,5 (sin pasar del máximo del firmware).</summary>
        private void SetAutoBursts()
        {
            var spl = SplSlider.Value;
            SpptSlider.Value = Math.Min(SpptSlider.Maximum, Math.Max(spl, Math.Round(spl * 1.25)));
            FpptSlider.Value = Math.Min(FpptSlider.Maximum, Math.Max(spl, Math.Round(spl * 1.5)));
        }

        /// <summary>
        /// Se aplica medio segundo después del último cambio y nunca con un deslizador «agarrado» con el mando: salir de
        /// él (A o B) es lo que lo confirma, así que no hace falta botón «Aplicar».
        /// </summary>
        private void ScheduleTdpApply()
        {
            if (_tdpApplyTimer == null)
            {
                _tdpApplyTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
                _tdpApplyTimer.Tick += async (s, e) =>
                {
                    if (SplSlider.IsFocusEngaged || SpptSlider.IsFocusEngaged || FpptSlider.IsFocusEngaged) return;
                    _tdpApplyTimer.Stop();
                    await ApplyTdpAsync();
                };
            }
            _tdpApplyTimer.Stop();
            _tdpApplyTimer.Start();
        }

        private async Task ApplyTdpAsync()
        {
            var args = new JsonObject
            {
                ["spl"] = JsonValue.CreateNumberValue(SplSlider.Value),
                ["sppt"] = JsonValue.CreateNumberValue(SpptSlider.Value),
                ["fppt"] = JsonValue.CreateNumberValue(FpptSlider.Value),
            };
            if (await CallAsync("tdp.set", args) == null) return;
            _customSpl = (int)SplSlider.Value;
            ModeCustom.State = _customSpl + " W";
            await RefreshPowerModeAsync();
            UpdateModeSummary();
        }

        private void UpdateTdpLabels()
        {
            if (!_tdpLoaded) return;
            SplValue.Text = SplSlider.Value.ToString("0");
            SpptValue.Text = Watts(SpptSlider);
            FpptValue.Text = Watts(FpptSlider);
            TdpAutoText.Text = "Ráfagas hasta " + Watts(SpptSlider) + " · picos hasta " + Watts(FpptSlider) + (TdpAuto ? " (automático)" : "");
        }

        private void ApplyTdpAutoPref()
        {
            var auto = TdpAuto;
            TdpAutoTile.IsOn = auto;
            TdpAutoTile.State = auto ? "Activado" : "Desactivado";
            // Automáticos: los deslizadores siguen al límite y no se tocan a mano
            SpptSlider.IsEnabled = !auto;
            FpptSlider.IsEnabled = !auto;
            if (auto) TdpNote.Visibility = Visibility.Collapsed;
        }

        private void OnTdpAdvancedClick(object sender, RoutedEventArgs e)
        {
            var open = TdpAdvancedBody.Visibility != Visibility.Visible;
            TdpAdvancedBody.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
            TdpAdvancedTile.State = open ? "" : "sPPT y fPPT";
        }

        private void OnTdpAutoClick(object sender, RoutedEventArgs e)
        {
            SetPref("tdpAuto", !TdpAuto);
            ApplyTdpAutoPref();
            if (TdpAuto)
            {
                _loading = true;
                try { SetAutoBursts(); } finally { _loading = false; }
                ScheduleTdpApply();
            }
            UpdateTdpLabels();
        }

        // =====================================================================
        // Pantalla
        // =====================================================================

        private async Task RefreshDisplayAsync()
        {
            var data = await CallAsync("display.get");
            if (data == null) return;

            var current = data.GetNamedObject("current");
            _currentWidth = (int)current.GetNamedNumber("width");
            _currentHeight = (int)current.GetNamedNumber("height");
            _currentHz = (int)current.GetNamedNumber("hz");

            _modes = data.GetNamedArray("modes").Select(v => v.GetObject())
                .Select(o => new DisplayMode((int)o.GetNamedNumber("width"), (int)o.GetNamedNumber("height"), (int)o.GetNamedNumber("hz")))
                .ToList();

            var resolutions = _modes.Select(m => new DisplayMode(m.Width, m.Height, 0)).Distinct()
                .OrderByDescending(m => m.Width * m.Height).ToList();
            ResolutionSelector.SetOptions(
                resolutions.Select(r => new KeyValuePair<string, object>(r.Width + " × " + r.Height, r)),
                new DisplayMode(_currentWidth, _currentHeight, 0));
            RefreshSelector.SetOptions(
                AvailableHz(_currentWidth, _currentHeight).OrderByDescending(h => h).Select(hz => new KeyValuePair<string, object>(hz + " Hz", hz)),
                _currentHz);

            RefreshCard.Value = _currentHz + " Hz";
            RefreshCard.IsOn = _currentHz >= AvailableHz(_currentWidth, _currentHeight).DefaultIfEmpty(_currentHz).Max();
            var hzOptions = AvailableHz(_currentWidth, _currentHeight).OrderBy(h => h).ToList();
            RefreshCard.ActionText = hzOptions.Count > 1 ? "Pasar a " + hzOptions[(hzOptions.IndexOf(_currentHz) + 1) % hzOptions.Count] + " Hz" : null;
            ResolutionCard.Value = _currentWidth + "×" + _currentHeight;
            ResolutionCard.State = _currentWidth == resolutions.FirstOrDefault()?.Width ? "Nativa" : "Escalada";
        }

        private IEnumerable<int> AvailableHz(int width, int height) =>
            _modes.Where(m => m.Width == width && m.Height == height).Select(m => m.Hz).Distinct();

        private async void OnResolutionSelected(object sender, object value)
        {
            var resolution = (DisplayMode)value;
            // Se conserva el refresco si la nueva resolución lo admite; si no, el más alto disponible.
            var available = AvailableHz(resolution.Width, resolution.Height).ToList();
            var hz = available.Contains(_currentHz) ? _currentHz : available.DefaultIfEmpty(_currentHz).Max();
            await SetDisplayAsync(resolution.Width, resolution.Height, hz);
        }

        private async void OnRefreshSelected(object sender, object value)
        {
            await SetDisplayAsync(_currentWidth, _currentHeight, (int)value);
        }

        private async void OnHomeHzClick(object sender, RoutedEventArgs e)
        {
            // De menos a más (60 → 90 → 120 → 60…)
            var options = AvailableHz(_currentWidth, _currentHeight).OrderBy(h => h).ToList();
            if (options.Count < 2) return;
            var next = options[(options.IndexOf(_currentHz) + 1) % options.Count];
            await SetDisplayAsync(_currentWidth, _currentHeight, next);
        }

        private async Task SetDisplayAsync(int width, int height, int hz)
        {
            var args = new JsonObject
            {
                ["width"] = JsonValue.CreateNumberValue(width),
                ["height"] = JsonValue.CreateNumberValue(height),
                ["hz"] = JsonValue.CreateNumberValue(hz),
            };
            await CallAsync("display.set", args);
            await RefreshDisplayAsync();   // también si falla, para mostrar el modo real
        }

        // =====================================================================
        // Brillo y volumen
        // =====================================================================

        private bool _autoBrightness, _lightSensor, _muted;

        private void OnBrightnessBarChanged(object sender, double value)
        {
            _lastQuickInput = DateTime.UtcNow;
            if (_autoBrightness) BrightnessBar.Note = "Auto · " + value.ToString("0") + " %";
            _brightness.Run(() => CallAsync("brightness.set", new JsonObject { ["value"] = JsonValue.CreateNumberValue(value) }));
        }

        private void OnVolumeBarChanged(object sender, double value)
        {
            _lastQuickInput = DateTime.UtcNow;
            // Subir el volumen quita el silencio (lo hace el agente)
            if (_muted && value > 0) { _muted = false; VolumeBar.Note = null; VolumeBar.Dimmed = false; }
            _volume.Run(() => CallAsync("volume.set", new JsonObject { ["value"] = JsonValue.CreateNumberValue(value) }));
        }

        /// <summary>A en el volumen: silenciar o volver a activar el sonido.</summary>
        private async void OnVolumeBarClick(object sender, RoutedEventArgs e)
        {
            if (VolumeBar.FromPointer) return;   // con el dedo, tocar la barra pone el nivel
            var data = await CallAsync("volume.mute", new JsonObject { ["muted"] = JsonValue.CreateBooleanValue(!_muted) });
            if (data == null) return;
            _muted = data.GetNamedBoolean("muted", false);
            VolumeBar.Note = _muted ? "Silencio" : null;
            VolumeBar.Dimmed = _muted;
            UpdateHints();
        }

        /// <summary>A en el brillo: brillo automático (solo si la consola tiene sensor de luz).</summary>
        private async void OnBrightnessBarClick(object sender, RoutedEventArgs e)
        {
            if (BrightnessBar.FromPointer || !_lightSensor) return;
            var data = await CallAsync("display.autobrightness.set", new JsonObject { ["enabled"] = JsonValue.CreateBooleanValue(!_autoBrightness) });
            if (data == null) return;
            _autoBrightness = data.GetNamedBoolean("enabled", false);
            BrightnessBar.Note = _autoBrightness ? "Auto · " + BrightnessBar.Value.ToString("0") + " %" : null;
            UpdateHints();
        }

        // ----- Barras de brillo y volumen: solo en «Todo», se pliegan con animación -----

        private Storyboard _barsAnimation;

        private void ShowQuickBars(bool show)
        {
            var target = show ? QuickBarsHeight : 0;
            if (Math.Abs(QuickBars.ActualHeight - target) < 0.5 && QuickBars.Opacity == (show ? 1 : 0)) return;
            // Si el foco estaba en una barra que se va, pasa a la primera baldosa
            if (!show && (FocusManager.GetFocusedElement() == BrightnessBar.FocusTarget || FocusManager.GetFocusedElement() == VolumeBar.FocusTarget))
                ProfileCard.FocusTarget.Focus(FocusState.Keyboard);
            BrightnessBar.FocusTarget.IsTabStop = VolumeBar.FocusTarget.IsTabStop = show;
            _barsAnimation?.Stop();
            if (!_animate)
            {
                QuickBars.Height = target;
                QuickBars.Opacity = show ? 1 : 0;
                return;
            }
            var sb = new Storyboard();
            var height = new DoubleAnimation
            {
                From = QuickBars.ActualHeight, To = target, Duration = TimeSpan.FromMilliseconds(260), EnableDependentAnimation = true,
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            };
            Storyboard.SetTarget(height, QuickBars);
            Storyboard.SetTargetProperty(height, "Height");
            var opacity = new DoubleAnimation { To = show ? 1 : 0, Duration = TimeSpan.FromMilliseconds(show ? 260 : 160) };
            Storyboard.SetTarget(opacity, QuickBars);
            Storyboard.SetTargetProperty(opacity, "Opacity");
            sb.Children.Add(height);
            sb.Children.Add(opacity);
            _barsAnimation = sb;
            sb.Begin();
        }

        private const double QuickBarsHeight = 76;   // 2 barras de 32 + 5 de separación + 7 hasta los filtros

        private void OnQuickBarsSizeChanged(object sender, SizeChangedEventArgs e)
        {
            // Que no se salgan mientras se pliegan
            QuickBars.Clip = new RectangleGeometry { Rect = new Windows.Foundation.Rect(0, 0, e.NewSize.Width, e.NewSize.Height) };
        }

        // =====================================================================
        // Utilidades
        // =====================================================================

        /// <summary>Envía un comando; devuelve "data" si fue bien o null (y opcionalmente muestra el error).</summary>
        private async Task<JsonObject> CallAsync(string cmd, JsonObject args = null, bool reportErrors = true)
        {
            var response = await BridgeClient.SendAsync(cmd, args);
            if (response.GetNamedBoolean("ok", false))
            {
                var data = response.GetNamedValue("data");
                return data.ValueType == JsonValueType.Object ? data.GetObject() : new JsonObject();
            }
            if (reportErrors) MessageText.Text = cmd + ": " + response.GetNamedString("error", "error desconocido");
            return null;
        }

        private void SetBar(Grid bar, double fraction, bool warn)
        {
            fraction = Math.Max(0, Math.Min(1, double.IsNaN(fraction) ? 0 : fraction));
            var fill = bar.Children.OfType<Border>().FirstOrDefault();
            if (fill == null)
            {
                fill = new Border { HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new CornerRadius(2) };
                bar.Children.Add(fill);
            }
            fill.Background = Brush(warn ? "WarnBrush" : "AccentBrush");
            fill.Width = bar.ActualWidth * fraction;
        }

        private static Brush Brush(string key) => (Brush)Application.Current.Resources[key];

        private static JsonObject Obj(JsonObject o, string key)
        {
            IJsonValue v;
            return o != null && o.TryGetValue(key, out v) && v.ValueType == JsonValueType.Object ? v.GetObject() : null;
        }

        private static double? Num(JsonObject o, string key)
        {
            IJsonValue v;
            return o != null && o.TryGetValue(key, out v) && v.ValueType == JsonValueType.Number ? v.GetNumber() : (double?)null;
        }

        private sealed class DisplayMode : IEquatable<DisplayMode>
        {
            public DisplayMode(int width, int height, int hz) { Width = width; Height = height; Hz = hz; }
            public int Width { get; }
            public int Height { get; }
            public int Hz { get; }
            public bool Equals(DisplayMode other) => other != null && Width == other.Width && Height == other.Height && Hz == other.Hz;
            public override bool Equals(object obj) => Equals(obj as DisplayMode);
            public override int GetHashCode() => (Width * 397 ^ Height) * 397 ^ Hz;
        }

        /// <summary>
        /// Para sliders que envían valores mientras se arrastran: como mucho una petición en vuelo
        /// y, al terminar, se envía solo el último valor pendiente.
        /// </summary>
        private sealed class Throttle
        {
            private Func<Task> _pending;
            private bool _busy;

            public async void Run(Func<Task> action)
            {
                _pending = action;
                if (_busy) return;
                _busy = true;
                try
                {
                    while (_pending != null)
                    {
                        var next = _pending;
                        _pending = null;
                        await next();
                    }
                }
                finally
                {
                    _busy = false;
                }
            }
        }
    }
}
