using System;
using System.Linq;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;

namespace GoSPanel.Widget
{
    /// <summary>
    /// Potencia › Auto (AutoTDP): FPS objetivo, margen de vatios y Chill. El ajuste lo hace el agente (AutoTdp.cs);
    /// aquí se configura y se muestra lo que está haciendo.
    /// </summary>
    public sealed partial class MainWidget
    {
        private static readonly int[] AutoTargets = { 30, 40, 45, 60, 90, 120 };
        private bool _autoTdp;
        private int _autoTarget = 60;
        private bool _autoChill = true;
        // Los Slider con Minimum > 0 lanzan ValueChanged durante InitializeComponent: nada se envía hasta leerlos.
        private bool _autoLoaded;
        private DispatcherTimer _autoSaveTimer;

        private void BuildAutoTargets()
        {
            for (var i = 0; i < AutoTargets.Length; i++)
            {
                var target = AutoTargets[i];
                var button = new Button
                {
                    Style = (Style)Application.Current.Resources["FlatButton"],
                    Height = 32,
                    Tag = target,
                    HorizontalContentAlignment = HorizontalAlignment.Center,
                    VerticalContentAlignment = VerticalAlignment.Center,
                    Content = new TextBlock { Text = target.ToString(), FontSize = 13, FontWeight = Windows.UI.Text.FontWeights.SemiBold },
                };
                Grid.SetColumn(button, i);
                button.Click += OnAutoTargetClick;
                AutoTargetButtons.Children.Add(button);
            }
        }

        private async Task RefreshAutoTdpAsync()
        {
            var data = await CallAsync("autotdp.get", null, false);
            if (data != null) ApplyAutoTdp(data);
        }

        private void ApplyAutoTdp(JsonObject data)
        {
            var was = _autoTdp;
            _autoTdp = data.GetNamedBoolean("enabled", false);
            _autoTarget = (int)data.GetNamedNumber("target", 60);
            _autoChill = data.GetNamedBoolean("chill", true);

            AutoTargetValue.Text = _autoTarget.ToString();
            foreach (var button in AutoTargetButtons.Children.OfType<Button>())
            {
                var selected = (int)button.Tag == _autoTarget;
                button.Background = Brush(selected ? "AccentBrush" : "Panel3Brush");
                ((TextBlock)button.Content).Foreground = selected && Theme.IsContrast ? new Windows.UI.Xaml.Media.SolidColorBrush(Theme.OnAccent) : Brush("TextBrush");
            }

            if (!AutoMinSlider.IsFocusEngaged && !AutoMaxSlider.IsFocusEngaged && _autoSaveTimer?.IsEnabled != true)
            {
                _autoLoaded = false;
                AutoMinSlider.Value = data.GetNamedNumber("min", 8);
                AutoMaxSlider.Value = data.GetNamedNumber("max", 25);
                _autoLoaded = true;
            }
            AutoMinValue.Text = AutoMinSlider.Value.ToString("0") + " W";
            AutoMaxValue.Text = AutoMaxSlider.Value.ToString("0") + " W";

            AutoChillTile.IsOn = _autoChill;
            AutoChillTile.State = _autoChill ? "Activado" : "Desactivado";

            var watts = Num(data, "watts");
            var fps = Num(data, "fps");
            var status = StringOrNull(data, "status") ?? "";
            AutoLiveText.Text = !_autoTdp ? "Elige los FPS y GoPilot ajusta el consumo solo, gastando lo mínimo para llegar."
                : fps.HasValue && watts.HasValue ? "Ahora: " + watts.Value.ToString("0") + " W para " + fps.Value.ToString("0") + " FPS · " + status.ToLowerInvariant()
                : status;

            if (was != _autoTdp) ApplyMode(_mode, true);
            else if (_autoTdp) UpdateModeSummary();
        }

        private async Task SetAutoTdpAsync(JsonObject args)
        {
            var data = await CallAsync("autotdp.set", args);
            if (data != null) ApplyAutoTdp(data);
        }

        private async void OnAutoTargetClick(object sender, RoutedEventArgs e) =>
            await SetAutoTdpAsync(new JsonObject { ["target"] = JsonValue.CreateNumberValue((int)((Button)sender).Tag) });

        private async void OnAutoChillClick(object sender, RoutedEventArgs e) =>
            await SetAutoTdpAsync(new JsonObject { ["chill"] = JsonValue.CreateBooleanValue(!_autoChill) });

        private void OnAutoRangeChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (!_autoLoaded) return;
            // El mínimo no pasa del máximo (y al revés)
            _autoLoaded = false;
            if (sender == AutoMinSlider && AutoMinSlider.Value > AutoMaxSlider.Value) AutoMaxSlider.Value = AutoMinSlider.Value;
            if (sender == AutoMaxSlider && AutoMaxSlider.Value < AutoMinSlider.Value) AutoMinSlider.Value = AutoMaxSlider.Value;
            _autoLoaded = true;
            AutoMinValue.Text = AutoMinSlider.Value.ToString("0") + " W";
            AutoMaxValue.Text = AutoMaxSlider.Value.ToString("0") + " W";
            if (_autoSaveTimer == null)
            {
                _autoSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
                _autoSaveTimer.Tick += async (s, a) =>
                {
                    if (AutoMinSlider.IsFocusEngaged || AutoMaxSlider.IsFocusEngaged) return;   // al salir del deslizador
                    _autoSaveTimer.Stop();
                    await SetAutoTdpAsync(new JsonObject
                    {
                        ["min"] = JsonValue.CreateNumberValue(AutoMinSlider.Value),
                        ["max"] = JsonValue.CreateNumberValue(AutoMaxSlider.Value),
                    });
                };
            }
            _autoSaveTimer.Stop();
            _autoSaveTimer.Start();
        }
    }
}
