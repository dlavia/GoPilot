using System;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Media;

namespace GoSPanel.Widget
{
    /// <summary>
    /// Temas del widget (Ajustes › Tema). Como en AccentColors, se cambian los colores de los pinceles compartidos
    /// de App.xaml, así que todo lo que los usa se repinta solo; lo que cambia de forma (baldosas activas,
    /// pestañas, etiqueta del perfil) escucha <see cref="Changed"/>.
    ///   simple   · «Sencillo» (por defecto): sin cajas de sección, activo con aro de color, pestañas subrayadas.
    ///   contrast · «Contraste»: activo relleno de color con texto oscuro, sin bordes.
    /// Los dos usan el fondo de Game Bar (colores translúcidos).
    /// </summary>
    internal static class Theme
    {
        public const string Simple = "simple", Contrast = "contrast";

        public static string Current { get; private set; } = Simple;
        public static bool IsContrast => Current == Contrast;

        /// <summary>Texto e iconos sobre el relleno de color (tema Contraste).</summary>
        public static readonly Color OnAccent = Color.FromArgb(0xFF, 0x06, 0x12, 0x0B);

        public static event EventHandler Changed;

        public static void Apply(string name)
        {
            Current = name == Contrast ? Contrast : Simple;
            // Fondo negro (como el menú de Legion L) en los dos temas; Contraste lleva filas más claras.
            Set("PageBrush", 0xFF, 0x00, 0x00, 0x00);
            if (IsContrast)
            {
                Set("PanelBrush", 0x00, 0x00, 0x00, 0x00);
                Set("Panel2Brush", 0xFF, 0x2A, 0x2B, 0x33);
                Set("RowFocusBrush", 0xFF, 0x4A, 0x4B, 0x58);
                Set("Panel3Brush", 0x2E, 0xFF, 0xFF, 0xFF);
                Set("DockBrush", 0x8C, 0x00, 0x00, 0x00);
                Set("LineBrush", 0x00, 0x00, 0x00, 0x00);
                Set("TrackBrush", 0x40, 0xFF, 0xFF, 0xFF);
                Set("TextBrush", 0xFF, 0xFF, 0xFF, 0xFF);
                Set("MutedBrush", 0xFF, 0xB5, 0xB9, 0xC3);
                Set("IconBrush", 0xFF, 0xFF, 0xFF, 0xFF);
            }
            else
            {
                Set("PanelBrush", 0x00, 0x00, 0x00, 0x00);
                Set("Panel2Brush", 0xC7, 0x1C, 0x1D, 0x24);
                Set("RowFocusBrush", 0xE6, 0x3C, 0x3D, 0x48);
                Set("Panel3Brush", 0x1F, 0xFF, 0xFF, 0xFF);
                Set("DockBrush", 0x73, 0x00, 0x00, 0x00);
                Set("LineBrush", 0x26, 0xFF, 0xFF, 0xFF);
                Set("TrackBrush", 0x33, 0xFF, 0xFF, 0xFF);
                Set("TextBrush", 0xFF, 0xF5, 0xF5, 0xF7);
                Set("MutedBrush", 0xFF, 0x9B, 0x9B, 0xA5);
                Set("IconBrush", 0xFF, 0xC9, 0xC9, 0xD1);
            }
            // Si Game Bar cerró una ventana anterior del widget, sus controles ya no existen: se quitan sus
            // suscripciones en vez de dejar que el error tumbe la ventana nueva.
            foreach (var handler in Changed?.GetInvocationList() ?? new Delegate[0])
            {
                try { ((EventHandler)handler)(null, EventArgs.Empty); }
                catch (Exception) { Changed -= (EventHandler)handler; }
            }
        }

        private static void Set(string key, byte a, byte r, byte g, byte b)
        {
            object brush;
            if (Application.Current.Resources.TryGetValue(key, out brush) && brush is SolidColorBrush solid)
                solid.Color = Color.FromArgb(a, r, g, b);
        }
    }
}
