using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using GoSPanel.Widget.Controls;
using Windows.Data.Json;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;

namespace GoSPanel.Widget
{
    /// <summary>
    /// Equipo › Ajustes avanzados: Sticks, Gatillos y Giroscopio se despliegan debajo (uno a la vez), cada uno
    /// con sus ajustes y los botones Calibrar, Probar (valores en directo del mando) y Por defecto.
    /// Los ajustes se guardan en el propio mando (los escribe el agente con el protocolo de Lenovo).
    /// </summary>
    public sealed partial class MainWidget
    {
        private static readonly string[] GyroModeNames = { "Desactivado", "Ratón", "Stick izquierdo", "Stick derecho" };

        private int _gyroMode;
        private DispatcherTimer _deadzoneTimer, _gyroSensTimer, _testTimer;
        // Empieza en true: al construir la página los deslizadores avisan de su valor inicial (el mínimo) y eso
        // no debe enviarse al mando. Solo se envía lo que mueva el usuario después de leer los valores reales.
        private bool _loadingAdvanced = true;
        private string _advancedOpen, _testing, _calibrating;

        private void ApplyAdvanced(JsonObject data)
        {
            var available = data.GetNamedBoolean("available", false);
            AdvancedSection.Visibility = available ? Visibility.Visible : Visibility.Collapsed;
            if (!available) return;

            _loadingAdvanced = true;
            void Pair(string key, string a, string b, Slider sa, TextBlock ta, Slider sb, TextBlock tb)
            {
                var o = Obj(data, key);
                if (o == null) return;
                sa.Value = o.GetNamedNumber(a, sa.Value);
                sb.Value = o.GetNamedNumber(b, sb.Value);
                ta.Text = (int)sa.Value + " %";
                tb.Text = (int)sb.Value + " %";
            }
            Pair("leftStick", "center", "edge", LsCenterSlider, LsCenterValue, LsEdgeSlider, LsEdgeValue);
            Pair("rightStick", "center", "edge", RsCenterSlider, RsCenterValue, RsEdgeSlider, RsEdgeValue);
            Pair("leftTrigger", "low", "high", LtLowSlider, LtLowValue, LtHighSlider, LtHighValue);
            Pair("rightTrigger", "low", "high", RtLowSlider, RtLowValue, RtHighSlider, RtHighValue);

            IJsonValue v;
            _gyroMode = data.TryGetValue("gyroMode", out v) && v.ValueType == JsonValueType.Number ? (int)v.GetNumber() : 0;
            var modeName = _gyroMode >= 0 && _gyroMode < GyroModeNames.Length ? GyroModeNames[_gyroMode] : "—";
            GyroModeTile.State = modeName;
            GyroModeTile.IsOn = _gyroMode > 0;
            AdvGyroTile.State = modeName;
            GyroSensitivityRow.Visibility = _gyroMode > 0 ? Visibility.Visible : Visibility.Collapsed;
            if (data.TryGetValue("gyroSensitivity", out v) && v.ValueType == JsonValueType.Number)
            {
                GyroSensitivitySlider.Value = v.GetNumber();
                GyroSensitivityValue.Text = ((int)v.GetNumber()).ToString();
            }
            _loadingAdvanced = false;
        }

        private async Task SetAdvancedAsync(JsonObject args)
        {
            var data = await CallAsync("controller.advanced.set", args);
            if (data != null) ApplyAdvanced(data);
        }

        // ---------------------------------------------------------------------
        // Desplegables
        // ---------------------------------------------------------------------

        private void OnAdvancedTabClick(object sender, RoutedEventArgs e)
        {
            var kind = (string)((Tile)sender).Tag;
            _advancedOpen = _advancedOpen == kind ? null : kind;   // tocar la abierta la cierra
            StopTest();
            CalibrationPanel.Visibility = Visibility.Collapsed;
            _calibrating = null;
            SticksPanel.Visibility = _advancedOpen == "sticks" ? Visibility.Visible : Visibility.Collapsed;
            TriggersPanel.Visibility = _advancedOpen == "triggers" ? Visibility.Visible : Visibility.Collapsed;
            GyroPanel.Visibility = _advancedOpen == "gyro" ? Visibility.Visible : Visibility.Collapsed;
            LightsPanel.Visibility = _advancedOpen == "lights" ? Visibility.Visible : Visibility.Collapsed;
            AdvLightsTile.IsOn = _advancedOpen == "lights";
            AdvSticksTile.IsOn = _advancedOpen == "sticks";
            AdvTriggersTile.IsOn = _advancedOpen == "triggers";
            AdvGyroTile.IsOn = _advancedOpen == "gyro" || _gyroMode > 0;
        }

        // ---------------------------------------------------------------------
        // Zonas muertas (se envían al dejar de mover, para no escribir en el mando a cada paso)
        // ---------------------------------------------------------------------

        private void OnDeadzoneChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            // Al construir la página, los deslizadores avisan de su valor inicial antes de que existan los demás.
            if (RtHighValue == null || RtHighSlider == null || LsCenterSlider == null) return;
            var map = new Dictionary<Slider, TextBlock>
            {
                [LsCenterSlider] = LsCenterValue, [RsCenterSlider] = RsCenterValue, [LsEdgeSlider] = LsEdgeValue, [RsEdgeSlider] = RsEdgeValue,
                [LtLowSlider] = LtLowValue, [RtLowSlider] = RtLowValue, [LtHighSlider] = LtHighValue, [RtHighSlider] = RtHighValue,
            };
            TextBlock label;
            if (map.TryGetValue((Slider)sender, out label)) label.Text = (int)e.NewValue + " %";
            if (_loadingAdvanced) return;
            if (_deadzoneTimer == null)
            {
                _deadzoneTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
                _deadzoneTimer.Tick += async (s, a) =>
                {
                    _deadzoneTimer.Stop();
                    await SetAdvancedAsync(DeadzoneArgs());
                };
            }
            _deadzoneTimer.Stop();
            _deadzoneTimer.Start();
        }

        private JsonObject DeadzoneArgs()
        {
            JsonObject Pair(string a1, Slider s1, string a2, Slider s2) => new JsonObject
            {
                [a1] = JsonValue.CreateNumberValue((int)s1.Value), [a2] = JsonValue.CreateNumberValue((int)s2.Value),
            };
            return new JsonObject
            {
                ["leftStick"] = Pair("center", LsCenterSlider, "edge", LsEdgeSlider),
                ["rightStick"] = Pair("center", RsCenterSlider, "edge", RsEdgeSlider),
                ["leftTrigger"] = Pair("low", LtLowSlider, "high", LtHighSlider),
                ["rightTrigger"] = Pair("low", RtLowSlider, "high", RtHighSlider),
            };
        }

        // ---------------------------------------------------------------------
        // Giroscopio
        // ---------------------------------------------------------------------

        private async void OnGyroModeClick(object sender, RoutedEventArgs e)
        {
            await SetAdvancedAsync(new JsonObject { ["gyroMode"] = JsonValue.CreateNumberValue((_gyroMode + 1) % GyroModeNames.Length) });
            var lights = await CallAsync("lights.get", null, false);   // el sensor se enciende solo al elegir un modo
            if (lights != null) ApplyLights(lights);
        }

        private void OnGyroSensitivityChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (GyroSensitivityValue == null) return;   // aún construyendo la página
            GyroSensitivityValue.Text = ((int)e.NewValue).ToString();
            if (_loadingAdvanced) return;
            if (_gyroSensTimer == null)
            {
                _gyroSensTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
                _gyroSensTimer.Tick += async (s, a) =>
                {
                    _gyroSensTimer.Stop();
                    await SetAdvancedAsync(new JsonObject { ["gyroSensitivity"] = JsonValue.CreateNumberValue((int)GyroSensitivitySlider.Value) });
                };
            }
            _gyroSensTimer.Stop();
            _gyroSensTimer.Start();
        }

        // ---------------------------------------------------------------------
        // Por defecto
        // ---------------------------------------------------------------------

        private async void OnDefaultsClick(object sender, RoutedEventArgs e)
        {
            var kind = (string)((Button)sender).Tag;
            JsonObject Pair(string a1, int v1, string a2, int v2) => new JsonObject
            {
                [a1] = JsonValue.CreateNumberValue(v1), [a2] = JsonValue.CreateNumberValue(v2),
            };
            if (kind == "sticks")
                await SetAdvancedAsync(new JsonObject { ["leftStick"] = Pair("center", 5, "edge", 100), ["rightStick"] = Pair("center", 5, "edge", 100) });
            else if (kind == "triggers")
                await SetAdvancedAsync(new JsonObject { ["leftTrigger"] = Pair("low", 5, "high", 95), ["rightTrigger"] = Pair("low", 5, "high", 95) });
            else
                await SetAdvancedAsync(new JsonObject { ["gyroMode"] = JsonValue.CreateNumberValue(0), ["gyroSensitivity"] = JsonValue.CreateNumberValue(50) });
        }

        // ---------------------------------------------------------------------
        // Probar: lee los valores del mando en directo mientras el panel está abierto
        // ---------------------------------------------------------------------

        private void OnTestClick(object sender, RoutedEventArgs e)
        {
            var kind = (string)((Button)sender).Tag;
            if (_testing == kind) { StopTest(); return; }
            StopTest();
            _testing = kind;
            if (kind == "gyro" && !_gyro)   // la prueba necesita el sensor encendido
            {
                var _ = CallAsync("gyro.set", new JsonObject { ["enabled"] = JsonValue.CreateBooleanValue(true) }, false)
                    .ContinueWith(t => { if (t.Result != null) { var __ = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () => ApplyLights(t.Result)); } });
            }
            (kind == "sticks" ? (UIElement)SticksTest : kind == "triggers" ? (UIElement)TriggersTest : GyroTest).Visibility = Visibility.Visible;
            ((Button)sender).Content = new TextBlock { Text = "Parar", FontSize = 12.5, FontWeight = Windows.UI.Text.FontWeights.SemiBold };
            if (_testTimer == null)
            {
                _testTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(70) };
                _testTimer.Tick += async (s, a) =>
                {
                    if (_testing == null || _tab != 3) { StopTest(); return; }
                    var d = await CallAsync("controller.input", null, false);
                    if (d != null) ShowInput(d);
                };
            }
            _testTimer.Start();
        }

        private void StopTest()
        {
            _testTimer?.Stop();
            _testing = null;
            SticksTest.Visibility = TriggersTest.Visibility = GyroTest.Visibility = Visibility.Collapsed;
            foreach (var b in new[] { TestSticksButton, TestTriggersButton, TestGyroButton })
                b.Content = new TextBlock { Text = "Probar", FontSize = 12.5, FontWeight = Windows.UI.Text.FontWeights.SemiBold };
        }

        private void ShowInput(JsonObject d)
        {
            double N(string k) => d.GetNamedNumber(k, 0);
            const double radius = 46;   // círculo de 110 con un punto de 18
            LeftStickMove.X = N("lx") / 128 * radius;
            LeftStickMove.Y = -N("ly") / 128 * radius;
            RightStickMove.X = N("rx") / 128 * radius;
            RightStickMove.Y = -N("ry") / 128 * radius;

            var width = Math.Max(0, ((FrameworkElement)LeftTriggerFill.Parent).ActualWidth);
            LeftTriggerFill.Width = N("lt") / 255 * width;
            RightTriggerFill.Width = N("rt") / 255 * width;
            LeftTriggerText.Text = Math.Round(N("lt") / 2.55) + " %";
            RightTriggerText.Text = Math.Round(N("rt") / 2.55) + " %";

            var w = GyroTest.ActualWidth / 2 - 12;
            var h = GyroTest.ActualHeight / 2 - 12;
            GyroMove.X = Math.Max(-w, Math.Min(w, N("gz") / 128 * w * 4));
            GyroMove.Y = Math.Max(-h, Math.Min(h, N("gx") / 128 * h * 4));
        }

        // ---------------------------------------------------------------------
        // Calibración: Calibrar → instrucciones → Terminar
        // ---------------------------------------------------------------------

        private static string[] CalibrationTargets(string kind) =>
            kind == "sticks" ? new[] { "lstick", "rstick" } : kind == "triggers" ? new[] { "ltrigger", "rtrigger" } : new[] { "gyro" };

        private async void OnCalibrateClick(object sender, RoutedEventArgs e)
        {
            var kind = (string)((Button)sender).Tag;
            StopTest();
            foreach (var target in CalibrationTargets(kind))
                if (await CallAsync("controller.calibrate", new JsonObject
                    {
                        ["target"] = JsonValue.CreateStringValue(target), ["action"] = JsonValue.CreateStringValue("start"),
                    }) == null) return;
            _calibrating = kind;
            CalibrationText.Text = kind == "sticks"
                ? "Gira los dos sticks despacio hasta el tope, dando varias vueltas completas. Luego suéltalos y pulsa Terminar."
                : kind == "triggers"
                    ? "Aprieta los dos gatillos hasta el fondo y suéltalos del todo, dos o tres veces. Luego pulsa Terminar."
                    : "Deja la consola quieta sobre una superficie plana unos segundos. Luego pulsa Terminar.";
            CalibrationPanel.Visibility = Visibility.Visible;
            CalibrationDoneButton.Focus(FocusState.Keyboard);
        }

        private async void OnCalibrationDoneClick(object sender, RoutedEventArgs e)
        {
            if (_calibrating == null) return;
            var ok = true;
            foreach (var target in CalibrationTargets(_calibrating))
            {
                var r = await CallAsync("controller.calibrate", new JsonObject
                {
                    ["target"] = JsonValue.CreateStringValue(target), ["action"] = JsonValue.CreateStringValue("stop"),
                });
                ok &= r != null && r.GetNamedBoolean("ok", false);
            }
            CalibrationText.Text = ok ? "Calibración correcta." : "No se pudo calibrar. Prueba otra vez.";
            var button = _calibrating == "sticks" ? CalSticksButton : _calibrating == "triggers" ? CalTriggersButton : CalGyroButton;
            _calibrating = null;
            await Task.Delay(2000);
            if (_calibrating == null) CalibrationPanel.Visibility = Visibility.Collapsed;
            button.Focus(FocusState.Keyboard);
        }

        // ---------------------------------------------------------------------
        // Restablecer el mando de fábrica (pulsar dos veces)
        // ---------------------------------------------------------------------

        private DateTime _factoryArmedUntil;

        private async void OnFactoryResetClick(object sender, RoutedEventArgs e)
        {
            if (DateTime.UtcNow > _factoryArmedUntil)
            {
                _factoryArmedUntil = DateTime.UtcNow.AddSeconds(4);
                FactoryResetText.Text = "Pulsa otra vez para restablecer todo el mando";
                await Task.Delay(4000);
                if (DateTime.UtcNow > _factoryArmedUntil) FactoryResetText.Text = "Restablecer el mando de fábrica";
                return;
            }
            _factoryArmedUntil = DateTime.MinValue;
            FactoryResetText.Text = "Restableciendo…";
            var data = await CallAsync("controller.factory");
            if (data != null) ApplyAdvanced(data);
            FactoryResetText.Text = data != null ? "Mando restablecido" : "Restablecer el mando de fábrica";
            var controller = await CallAsync("controller.get", null, false);
            if (controller != null) ApplyController(controller);
            var lights = await CallAsync("lights.get", null, false);
            if (lights != null) ApplyLights(lights);
            await Task.Delay(2500);
            FactoryResetText.Text = "Restablecer el mando de fábrica";
        }
    }
}
