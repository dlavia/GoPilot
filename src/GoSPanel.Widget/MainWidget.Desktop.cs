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
    /// Equipo › Modo escritorio: el mando como ratón (lo hace el agente, DesktopMode.cs). Aquí solo se elige el modo
    /// (Apagado / Automático / Siempre) y las velocidades, y se muestra si está activo ahora.
    /// </summary>
    public sealed partial class MainWidget
    {
        private static readonly string[] DesktopModes = { "off", "auto", "always" };
        private static readonly string[] DesktopModeNames = { "Apagado", "Automático", "Siempre" };
        private string _desktopMode = "off";
        // Los Slider con Minimum > 0 lanzan ValueChanged durante InitializeComponent: no se envía nada hasta leerlos.
        private bool _desktopLoaded;
        private DispatcherTimer _desktopSaveTimer;

        private async Task RefreshDesktopAsync()
        {
            var data = await CallAsync("desktop.get", null, false);
            if (data != null) ApplyDesktop(data);
        }

        private void ApplyDesktop(JsonObject data)
        {
            _desktopMode = StringOrNull(data, "mode") ?? "off";
            var active = data.GetNamedBoolean("active", false);
            var reason = StringOrNull(data, "reason");
            var index = Math.Max(0, Array.IndexOf(DesktopModes, _desktopMode));
            DesktopTile.IsOn = active;
            DesktopTile.State = DesktopModeNames[index] + (_desktopMode == "off" ? "" : active ? " · activo" : string.IsNullOrEmpty(reason) ? "" : " · " + reason.ToLowerInvariant());

            foreach (var button in DesktopModeButtons.Children.OfType<Button>())
            {
                var selected = (string)button.Tag == _desktopMode;
                button.Background = Brush(selected ? "AccentBrush" : "Panel3Brush");
                ((TextBlock)button.Content).Foreground = selected && Theme.IsContrast ? new Windows.UI.Xaml.Media.SolidColorBrush(Theme.OnAccent) : Brush("TextBrush");
            }

            // No pisar un deslizador que se está moviendo
            if (!DesktopPointerSlider.IsFocusEngaged && !DesktopScrollSlider.IsFocusEngaged && _desktopSaveTimer?.IsEnabled != true)
            {
                _desktopLoaded = false;
                DesktopPointerSlider.Value = data.GetNamedNumber("pointerSpeed", 55);
                DesktopScrollSlider.Value = data.GetNamedNumber("scrollSpeed", 40);
                _desktopLoaded = true;
            }
            DesktopPointerValue.Text = DesktopPointerSlider.Value.ToString("0");
            DesktopScrollValue.Text = DesktopScrollSlider.Value.ToString("0");
        }

        private async Task SetDesktopModeAsync(string mode)
        {
            var data = await CallAsync("desktop.set", new JsonObject { ["mode"] = JsonValue.CreateStringValue(mode) });
            if (data != null) ApplyDesktop(data);
        }

        /// <summary>La fila (y su copia en Inicio) pasa al siguiente modo: Apagado → Automático → Siempre.</summary>
        private async void OnDesktopTileClick(object sender, RoutedEventArgs e)
        {
            var next = DesktopModes[(Array.IndexOf(DesktopModes, _desktopMode) + 1) % DesktopModes.Length];
            await SetDesktopModeAsync(next);
        }

        private async void OnDesktopModeClick(object sender, RoutedEventArgs e) => await SetDesktopModeAsync((string)((Button)sender).Tag);

        private void OnDesktopSliderChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (!_desktopLoaded) return;
            DesktopPointerValue.Text = DesktopPointerSlider.Value.ToString("0");
            DesktopScrollValue.Text = DesktopScrollSlider.Value.ToString("0");
            // Se guarda un momento después de dejar de moverlo
            if (_desktopSaveTimer == null)
            {
                _desktopSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
                _desktopSaveTimer.Tick += async (s, a) =>
                {
                    _desktopSaveTimer.Stop();
                    await CallAsync("desktop.set", new JsonObject
                    {
                        ["pointerSpeed"] = JsonValue.CreateNumberValue(DesktopPointerSlider.Value),
                        ["scrollSpeed"] = JsonValue.CreateNumberValue(DesktopScrollSlider.Value),
                    });
                };
            }
            _desktopSaveTimer.Stop();
            _desktopSaveTimer.Start();
        }
    }
}
