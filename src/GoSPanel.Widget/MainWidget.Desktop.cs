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
            DesktopTile.SelectedIndex = index;
            DesktopTile.Sub = _desktopMode == "off" ? "El mando como ratón sin juego delante" : active ? "Activo ahora" : string.IsNullOrEmpty(reason) ? "En espera" : reason;
            DesktopTile.State = DesktopModeNames[index] + (_desktopMode == "off" ? "" : active ? " · activo" : string.IsNullOrEmpty(reason) ? "" : " · " + reason.ToLowerInvariant());

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

        /// <summary>Apagado / Auto / Siempre con ◀ ▶.</summary>
        private async void OnDesktopPicked(object sender, int index)
        {
            if (index >= 0 && index < DesktopModes.Length) await SetDesktopModeAsync(DesktopModes[index]);
        }

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
