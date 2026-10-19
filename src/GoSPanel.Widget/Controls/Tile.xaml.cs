using System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;

namespace GoSPanel.Widget.Controls
{
    /// <summary>
    /// Baldosa del diseño (modos, cuadrícula de Inicio, interruptores). Vertical: icono arriba y texto centrado;
    /// horizontal: icono a la izquierda y título + estado a la derecha. IsOn la resalta con el color de acento.
    /// Row = "switch" | "value" | "sub": fila de lista como la app de Xbox (icono, nombre, valor a la derecha y un
    /// interruptor o ›), para las pestañas que no son Inicio.
    /// </summary>
    public sealed partial class Tile : UserControl
    {
        public event RoutedEventHandler Click;

        /// <summary>Se lanza al cambiar lo que se ve (icono, textos, estado): lo usan las copias de Inicio.</summary>
        public event EventHandler VisualChanged;

        /// <summary>Ejecuta la acción de la baldosa como si se hubiera pulsado.</summary>
        public void PerformClick()
        {
            if (_available) Click?.Invoke(this, new RoutedEventArgs());
        }

        private void NotifyChanged() => VisualChanged?.Invoke(this, EventArgs.Empty);

        public Tile()
        {
            InitializeComponent();
            ApplyLayout();
            ApplyVisuals();
            // Solo mientras está en pantalla (Game Bar puede cerrar la ventana y crear otra en el mismo proceso)
            EventHandler onTheme = (s, e) => ApplyVisuals();
            Loaded += (s, e) => { Theme.Changed -= onTheme; Theme.Changed += onTheme; ApplyVisuals(); };
            Unloaded += (s, e) => Theme.Changed -= onTheme;
        }

        public string Glyph
        {
            get => IconText.Text;
            set { IconText.Text = value ?? ""; NotifyChanged(); }
        }

        public string Title
        {
            get => TitleText.Text;
            set { TitleText.Text = value ?? ""; NotifyChanged(); }
        }

        public string State
        {
            get => _state;
            set
            {
                _state = value ?? "";
                ApplyState();
                NotifyChanged();
            }
        }
        private string _state = "";

        // En las filas con interruptor el «Activado / Desactivado» sobra: ya lo dice el interruptor.
        private static readonly string[] OnOffWords =
            { "Activado", "Desactivado", "Activada", "Desactivada", "Activados", "Desactivados", "Activadas", "Desactivadas", "Encendido", "Apagado", "Encendidas", "Apagadas", "On", "Off" };

        private void ApplyState()
        {
            var text = _row == "switch" && Array.IndexOf(OnOffWords, _state) >= 0 ? "" : _state;
            StateText.Text = text;
            StateText.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
        }

        private string _row;
        public string Row
        {
            get => _row;
            set { _row = string.IsNullOrEmpty(value) ? null : value; ApplyLayout(); ApplyState(); ApplyVisuals(); }
        }

        /// <summary>Texto pequeño a la derecha (por ejemplo un atajo de teclado). Solo en horizontal.</summary>
        public string Badge
        {
            get => BadgeText.Text;
            set
            {
                BadgeText.Text = value ?? "";
                BadgeBorder.Visibility = string.IsNullOrEmpty(value) ? Visibility.Collapsed : Visibility.Visible;
            }
        }

        private bool _vertical;
        public bool Vertical
        {
            get => _vertical;
            set { _vertical = value; ApplyLayout(); }
        }

        private bool _large;
        /// <summary>Baldosa grande (icono y título mayores), para acciones destacadas.</summary>
        public bool Large
        {
            get => _large;
            set { _large = value; ApplyLayout(); }
        }

        private bool _isOn;
        public bool IsOn
        {
            get => _isOn;
            set { _isOn = value; ApplyVisuals(); NotifyChanged(); }
        }

        private bool _warning;
        /// <summary>Estado de aviso (naranja), p. ej. «pulsa otra vez para confirmar».</summary>
        public bool Warning
        {
            get => _warning;
            set { _warning = value; ApplyVisuals(); NotifyChanged(); }
        }

        /// <summary>El control que recibe el foco (para fijar a mano la navegación con la cruceta).</summary>
        public Control FocusTarget => Root;

        /// <summary>Si el mando puede llegar a la baldosa (en la edición de Inicio se desactiva).</summary>
        public bool Focusable
        {
            get => Root.IsTabStop;
            set => Root.IsTabStop = value;
        }

        private bool _available = true;
        /// <summary>
        /// No disponible: se ve apagada y no hace nada al pulsarla, pero el mando puede pasar por encima
        /// (si se deshabilitara el botón, la cruceta no podría cruzar una fila de baldosas apagadas).
        /// </summary>
        public bool Available
        {
            get => _available;
            set
            {
                _available = value;
                Layout.Opacity = value ? 1 : 0.4;
                NotifyChanged();
            }
        }

        private void OnRootClick(object sender, RoutedEventArgs e)
        {
            if (_available) Click?.Invoke(this, e);
        }

        private void ApplyLayout()
        {
            SwitchTrack.Visibility = _row == "switch" ? Visibility.Visible : Visibility.Collapsed;
            ChevronText.Visibility = _row == "sub" ? Visibility.Visible : Visibility.Collapsed;
            if (_row != null)
            {
                Place(IconText, 0, 0, 1);
                Place(TitleText, 0, 1, 1);
                Place(StateText, 0, 2, 1);
                Place(BadgeBorder, 0, 2, 1);
                IconText.HorizontalAlignment = HorizontalAlignment.Center;
                IconText.Width = 20;
                TitleText.HorizontalAlignment = HorizontalAlignment.Left;
                StateText.HorizontalAlignment = HorizontalAlignment.Right;
                StateText.MaxWidth = 150;
                IconText.FontSize = 17;
                IconText.Margin = new Thickness(0);
                TitleText.FontSize = 13;
                StateText.FontSize = 12;
                TitleText.FontWeight = Windows.UI.Text.FontWeights.Normal;
                Layout.VerticalAlignment = VerticalAlignment.Center;
                Root.MinHeight = 42;
                Root.Padding = new Thickness(10, 0, 12, 0);
                return;
            }
            if (_vertical)
            {
                Place(IconText, 0, 0, 4);
                Place(TitleText, 1, 0, 4);
                Place(StateText, 2, 0, 4);
                foreach (var t in new[] { IconText, TitleText, StateText }) t.HorizontalAlignment = HorizontalAlignment.Center;
                IconText.FontSize = 22;
                IconText.Margin = new Thickness(0, 0, 0, 2);
                TitleText.FontSize = 11.5;
                Layout.VerticalAlignment = VerticalAlignment.Center;
                Root.Padding = new Thickness(4, 9, 4, 8);
            }
            else
            {
                Place(IconText, 0, 0, 1);
                Grid.SetRowSpan(IconText, 2);
                Place(TitleText, 0, 1, 1);
                Place(StateText, 1, 1, 1);
                Place(BadgeBorder, 0, 2, 1);
                Grid.SetRowSpan(BadgeBorder, 2);
                foreach (var t in new[] { TitleText, StateText }) t.HorizontalAlignment = HorizontalAlignment.Left;
                IconText.FontSize = _large ? 26 : 20;
                IconText.Margin = new Thickness(0);
                TitleText.FontSize = _large ? 13.5 : 12;
                Layout.VerticalAlignment = VerticalAlignment.Center;
                Root.Padding = _large ? new Thickness(12) : new Thickness(10, 9, 10, 9);
            }
        }

        private static void Place(FrameworkElement element, int row, int column, int columnSpan)
        {
            Grid.SetRow(element, row);
            Grid.SetColumn(element, column);
            Grid.SetColumnSpan(element, columnSpan);
            Grid.SetRowSpan(element, 1);
        }

        private void ApplyVisuals()
        {
            var accent = Brush(_warning ? "WarnBrush" : "AccentBrush");
            var highlighted = _isOn || _warning;
            if (_row != null)
            {
                // Fila: sin aro; lo activo se ve en el interruptor (o en el valor, en color de acento).
                Root.Background = Brush("Panel2Brush");
                Root.BorderBrush = new SolidColorBrush(Windows.UI.Colors.Transparent);
                Root.BorderThickness = new Thickness(0);
                IconText.Foreground = highlighted ? accent : Brush("IconBrush");
                TitleText.Foreground = Brush("TextBrush");
                StateText.Foreground = highlighted && _row != "switch" ? accent : Brush("MutedBrush");
                SwitchTrack.Background = highlighted ? accent : new SolidColorBrush(Windows.UI.Color.FromArgb(0x66, 0x80, 0x84, 0x8C));
                SwitchKnob.Fill = highlighted ? new SolidColorBrush(Theme.OnAccent) : Brush("IconBrush");
                SwitchKnob.HorizontalAlignment = highlighted ? HorizontalAlignment.Right : HorizontalAlignment.Left;
                return;
            }
            if (Theme.IsContrast && highlighted)
            {
                // Contraste: activo relleno de color con texto e icono oscuros
                var dark = new SolidColorBrush(Theme.OnAccent);
                Root.Background = accent;
                Root.BorderBrush = new SolidColorBrush(Windows.UI.Colors.Transparent);
                Root.BorderThickness = new Thickness(1);
                IconText.Foreground = TitleText.Foreground = StateText.Foreground = dark;
                return;
            }
            // Sencillo: activo con aro de color; Contraste: inactivo gris sin borde
            Root.Background = Brush("Panel2Brush");
            Root.BorderBrush = highlighted ? accent : Brush("LineBrush");
            Root.BorderThickness = new Thickness(highlighted ? 2 : 1);
            IconText.Foreground = highlighted ? accent : Brush("IconBrush");
            TitleText.Foreground = Brush("TextBrush");
            StateText.Foreground = _warning ? accent : Brush(_isOn ? "TextBrush" : "MutedBrush");
        }

        private static Brush Brush(string key) => (Brush)Application.Current.Resources[key];
    }
}
