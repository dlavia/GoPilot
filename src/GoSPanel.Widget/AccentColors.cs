using System;
using Windows.UI;
using Windows.UI.Core;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Media;

namespace GoSPanel.Widget
{
    /// <summary>
    /// Usa el color de acento de Windows en todo el widget y lo sigue si el usuario lo cambia.
    /// Los pinceles de App.xaml (AccentBrush, AccentSoftBrush, sliders...) son instancias compartidas, así que
    /// basta con cambiar su color; cada ventana (panel y overlay) lo aplica en su propio hilo.
    /// </summary>
    internal static class AccentColors
    {
        private static readonly UISettings Settings = new UISettings();

        private static readonly string[] SolidKeys =
        {
            "AccentBrush", "SystemControlHighlightAccentBrush",
            "SliderTrackValueFill", "SliderTrackValueFillPointerOver", "SliderTrackValueFillPressed",
        };

        /// <summary>Aplica el acento ahora y lo mantiene actualizado en la ventana de este hilo.</summary>
        public static void Attach(CoreDispatcher dispatcher)
        {
            Apply();
            Settings.ColorValuesChanged += async (s, e) =>
                await dispatcher.RunAsync(CoreDispatcherPriority.Normal, Apply);
        }

        private static void Apply()
        {
            var accent = Settings.GetColorValue(UIColorType.Accent);
            var resources = Application.Current.Resources;
            foreach (var key in SolidKeys)
                if (resources.TryGetValue(key, out var brush) && brush is SolidColorBrush solid)
                    solid.Color = accent;

            // Versión translúcida para el fondo de las baldosas activas (~22 % de opacidad).
            if (resources.TryGetValue("AccentSoftBrush", out var soft) && soft is SolidColorBrush softBrush)
                softBrush.Color = Color.FromArgb(0x38, accent.R, accent.G, accent.B);
            if (resources.TryGetValue("AccentLightBrush", out var light) && light is SolidColorBrush lightBrush)
                lightBrush.Color = Settings.GetColorValue(UIColorType.AccentLight2);
        }
    }
}
