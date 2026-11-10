using System;
using System.Collections.Generic;
using System.Linq;
using Windows.System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;

namespace GoSPanel.Widget.Controls
{
    /// <summary>
    /// Fila del widget (estilo del menú de Legion L): icono, nombre, subtítulo y a la derecha un interruptor, un valor,
    /// un › (abre una subpágina) o unas opciones.
    /// Row = "switch" | "value" | "sub". Sin Row: baldosa (vertical u horizontal), solo para lo que aún no se ha pasado a filas.
    /// Opciones (Choices): pastillas, «‹ valor ›» con Compact, o cajas con Boxes (icono, valor y nombre; sin cabecera). Se cambian SOLO con ◀ ▶ (o tocando la opción);
    /// A no hace nada en ellas. Picked se lanza un momento después de dejar de pulsar (así cruzar varias opciones
    /// seguidas no aplica cada una por el camino).
    /// </summary>
    public sealed partial class Tile : UserControl
    {
        public event RoutedEventHandler Click;

        /// <summary>Opción elegida con ◀ ▶ o tocándola (índice en Choices).</summary>
        public event EventHandler<int> Picked;

        /// <summary>Se lanza al cambiar lo que se ve (icono, textos, estado).</summary>
        public event EventHandler VisualChanged;

        /// <summary>Ejecuta la acción de la baldosa como si se hubiera pulsado.</summary>
        public void PerformClick()
        {
            if (_available) Click?.Invoke(this, new RoutedEventArgs());
        }

        private void NotifyChanged() => VisualChanged?.Invoke(this, EventArgs.Empty);

        private readonly DispatcherTimer _pickTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };

        public Tile()
        {
            InitializeComponent();
            ApplyLayout();
            ApplyVisuals();
            Root.PreviewKeyDown += OnRootPreviewKeyDown;
            Root.GotFocus += (s, e) => ApplyVisuals();
            Root.LostFocus += (s, e) => ApplyVisuals();
            _pickTimer.Tick += (s, e) =>
            {
                _pickTimer.Stop();
                Picked?.Invoke(this, _selectedIndex);
            };
            // Solo mientras está en pantalla (Game Bar puede cerrar la ventana y crear otra en el mismo proceso)
            EventHandler onTheme = (s, e) => ApplyVisuals();
            Loaded += (s, e) => { Theme.Changed -= onTheme; Theme.Changed += onTheme; ApplyVisuals(); };
            Unloaded += (s, e) => { Theme.Changed -= onTheme; _pickTimer.Stop(); };
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

        /// <summary>Subtítulo bajo el nombre (solo en modo fila).</summary>
        public string Sub
        {
            get => _sub;
            set { _sub = value ?? ""; ApplyState(); }
        }
        private string _sub = "";

        // En las filas con interruptor el «Activado / Desactivado» sobra: ya lo dice el interruptor.
        private static readonly string[] OnOffWords =
            { "Activado", "Desactivado", "Activada", "Desactivada", "Activados", "Desactivados", "Activadas", "Desactivadas", "Encendido", "Apagado", "Encendidas", "Apagadas", "On", "Off" };

        private void ApplyState()
        {
            // Con opciones a la derecha, el valor ya se ve en ellas: el estado no se muestra
            var text = HasChoices || (_row == "switch" && Array.IndexOf(OnOffWords, _state) >= 0) ? "" : _state;
            StateText.Text = text;
            StateText.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
            SubText.Text = _sub;
            SubText.Visibility = _row != null && !_boxes && !string.IsNullOrEmpty(_sub) ? Visibility.Visible : Visibility.Collapsed;
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

        /// <summary>Si el mando puede llegar a la baldosa.</summary>
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

        // ---------------------------------------------------------------------
        // Opciones
        // ---------------------------------------------------------------------

        private List<string> _choices = new List<string>();
        private int _selectedIndex = -1;
        private bool _compact;

        private bool HasChoices => _choices.Count > 0;

        private bool _boxes;
        private string[] _boxGlyphs = new string[0];
        private string[] _boxValues = new string[0];

        /// <summary>
        /// Cada opción en su caja, a todo el ancho de la fila: icono arriba, un valor grande (SetBoxValues) y el nombre
        /// pequeño. La fila no muestra icono, nombre ni subtítulo: las cajas se explican solas. La elegida va con
        /// contorno y fondo suave del acento de Windows.
        /// </summary>
        public bool Boxes
        {
            get => _boxes;
            set { _boxes = value; ApplyLayout(); BuildChoices(); ApplyState(); }
        }

        /// <summary>Iconos de las cajas (Phosphor), separados por «|», en el orden de Choices.</summary>
        public string BoxGlyphs
        {
            get => string.Join("|", _boxGlyphs);
            set { _boxGlyphs = string.IsNullOrEmpty(value) ? new string[0] : value.Split('|'); BuildChoices(); }
        }

        /// <summary>Valor de cada caja (p. ej. «15 W»), en el orden de Choices.</summary>
        public void SetBoxValues(IEnumerable<string> values)
        {
            var list = values.ToArray();
            if (list.SequenceEqual(_boxValues)) return;
            _boxValues = list;
            PaintChoices();
        }

        /// <summary>Opciones separadas por «|» (p. ej. "No|Suave|Media|Fuerte").</summary>
        public string Choices
        {
            get => string.Join("|", _choices);
            set => SetChoices(string.IsNullOrEmpty(value) ? new string[0] : value.Split('|'));
        }

        /// <summary>«‹ valor ›» en lugar de todas las pastillas (para listas largas o textos largos).</summary>
        public bool Compact
        {
            get => _compact;
            set { _compact = value; BuildChoices(); }
        }

        public void SetChoices(IEnumerable<string> choices)
        {
            var list = choices.ToList();
            if (list.SequenceEqual(_choices)) return;
            _choices = list;
            BuildChoices();
            ApplyState();
        }

        /// <summary>Opción marcada. Ponerla desde el código no lanza Picked (salvo si se está eligiendo ahora mismo).</summary>
        public int SelectedIndex
        {
            get => _selectedIndex;
            set
            {
                if (_pickTimer.IsEnabled) return;   // el usuario está eligiendo: no pisarle con datos viejos
                _selectedIndex = value;
                PaintChoices();
            }
        }

        private void BuildChoices()
        {
            ChoicesPanel.Children.Clear();
            BoxesGrid.Children.Clear();
            BoxesGrid.ColumnDefinitions.Clear();
            ChoicesPanel.Visibility = HasChoices && !_boxes ? Visibility.Visible : Visibility.Collapsed;
            BoxesGrid.Visibility = HasChoices && _boxes ? Visibility.Visible : Visibility.Collapsed;
            if (!HasChoices) return;
            if (_boxes)
            {
                for (var i = 0; i < _choices.Count; i++)
                {
                    var index = i;
                    var panel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
                    panel.Children.Add(new TextBlock
                    {
                        FontFamily = (FontFamily)Application.Current.Resources["Phosphor"],
                        Text = i < _boxGlyphs.Length ? _boxGlyphs[i] : "", FontSize = 22, HorizontalAlignment = HorizontalAlignment.Center,
                    });
                    panel.Children.Add(new TextBlock
                    {
                        FontSize = 15, FontWeight = Windows.UI.Text.FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center,
                        Margin = new Thickness(0, 4, 0, 0), TextTrimming = TextTrimming.Clip,
                    });
                    panel.Children.Add(new TextBlock
                    {
                        Text = _choices[i], FontSize = 11, FontWeight = Windows.UI.Text.FontWeights.SemiBold,
                        HorizontalAlignment = HorizontalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 1, 0, 0),
                    });
                    var box = new Border
                    {
                        CornerRadius = new CornerRadius(11),
                        BorderThickness = new Thickness(2),
                        Padding = new Thickness(2, 8, 2, 7),
                        Child = panel,
                    };
                    box.AddHandler(TappedEvent, new TappedEventHandler((s, e) => { Pick(index); e.Handled = true; }), true);
                    BoxesGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    Grid.SetColumn(box, i);
                    BoxesGrid.Children.Add(box);
                }
            }
            else if (_compact)
            {
                ChoicesPanel.Spacing = 6;
                ChoicesPanel.Children.Add(Caret("", -1));
                ChoicesPanel.Children.Add(new TextBlock { FontSize = 13, FontWeight = Windows.UI.Text.FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
                ChoicesPanel.Children.Add(Caret("", 1));
            }
            else
            {
                ChoicesPanel.Spacing = 2;
                for (var i = 0; i < _choices.Count; i++)
                {
                    var index = i;
                    var pill = new Border
                    {
                        CornerRadius = new CornerRadius(11),
                        Padding = new Thickness(8, 3, 8, 4),
                        Child = new TextBlock { Text = _choices[i], FontSize = 11.5, FontWeight = Windows.UI.Text.FontWeights.SemiBold },
                    };
                    pill.AddHandler(TappedEvent, new TappedEventHandler((s, e) => { Pick(index); e.Handled = true; }), true);
                    ChoicesPanel.Children.Add(pill);
                }
            }
            PaintChoices();
        }

        private TextBlock Caret(string glyph, int direction)
        {
            var caret = new TextBlock
            {
                FontFamily = (FontFamily)Application.Current.Resources["Phosphor"],
                Text = glyph, FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Padding = new Thickness(2, 4, 2, 4),
            };
            caret.AddHandler(TappedEvent, new TappedEventHandler((s, e) => { Step(direction); e.Handled = true; }), true);
            return caret;
        }

        private void PaintChoices()
        {
            if (!HasChoices) return;
            var focused = Root.FocusState != FocusState.Unfocused;
            if (_boxes)
            {
                for (var i = 0; i < BoxesGrid.Children.Count; i++)
                {
                    var box = (Border)BoxesGrid.Children[i];
                    var texts = ((StackPanel)box.Child).Children.OfType<TextBlock>().ToArray();
                    var on = i == _selectedIndex;
                    box.Background = on ? Brush("AccentSoftBrush") : new SolidColorBrush(Windows.UI.Color.FromArgb(0x0F, 0xFF, 0xFF, 0xFF));
                    box.BorderBrush = on ? Brush("AccentBrush") : new SolidColorBrush(Windows.UI.Colors.Transparent);
                    texts[0].Foreground = on ? Brush("AccentLightBrush") : Brush("IconBrush");
                    texts[1].Text = i < _boxValues.Length ? _boxValues[i] : "";
                    texts[1].Visibility = string.IsNullOrEmpty(texts[1].Text) ? Visibility.Collapsed : Visibility.Visible;
                    texts[1].Foreground = Brush("TextBrush");
                    texts[2].Foreground = Brush(on ? "TextBrush" : "MutedBrush");
                }
                return;
            }
            if (_compact)
            {
                var value = (TextBlock)ChoicesPanel.Children[1];
                value.Text = _selectedIndex >= 0 && _selectedIndex < _choices.Count ? _choices[_selectedIndex] : "—";
                value.Foreground = Brush("TextBrush");
                ChoicesPanel.Children[0].Opacity = focused && _selectedIndex > 0 ? 1 : focused ? 0.3 : 0;
                ChoicesPanel.Children[2].Opacity = focused && _selectedIndex < _choices.Count - 1 ? 1 : focused ? 0.3 : 0;
                ((TextBlock)ChoicesPanel.Children[0]).Foreground = ((TextBlock)ChoicesPanel.Children[2]).Foreground = Brush("MutedBrush");
                return;
            }
            for (var i = 0; i < ChoicesPanel.Children.Count; i++)
            {
                var pill = (Border)ChoicesPanel.Children[i];
                var text = (TextBlock)pill.Child;
                var on = i == _selectedIndex;
                pill.Background = on ? new SolidColorBrush(focused ? Windows.UI.Colors.White : Windows.UI.Color.FromArgb(0xBF, 0xFF, 0xFF, 0xFF))
                                     : new SolidColorBrush(Windows.UI.Colors.Transparent);
                text.Foreground = on ? new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x11, 0x11, 0x11)) : Brush("MutedBrush");
            }
        }

        private void Step(int direction)
        {
            if (!HasChoices) return;
            Pick(Math.Max(0, Math.Min(_choices.Count - 1, _selectedIndex + direction)));
        }

        private void Pick(int index)
        {
            if (!_available || !HasChoices || index == _selectedIndex) return;
            _selectedIndex = index;
            PaintChoices();
            _pickTimer.Stop();
            _pickTimer.Start();
        }

        private void OnRootPreviewKeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (!HasChoices) return;
            var k = e.Key;
            int direction;
            if (k == VirtualKey.GamepadDPadLeft || k == VirtualKey.GamepadLeftThumbstickLeft || k == VirtualKey.Left) direction = -1;
            else if (k == VirtualKey.GamepadDPadRight || k == VirtualKey.GamepadLeftThumbstickRight || k == VirtualKey.Right) direction = 1;
            else return;
            Step(direction);
            e.Handled = true;   // ◀ ▶ son de las opciones: no mueven el foco
        }

        private void OnRootClick(object sender, RoutedEventArgs e)
        {
            if (HasChoices) return;   // las opciones se cambian con ◀ ▶ o tocándolas, no con A
            if (_available) Click?.Invoke(this, e);
        }

        // ---------------------------------------------------------------------
        // Aspecto
        // ---------------------------------------------------------------------

        private void ApplyLayout()
        {
            SwitchTrack.Visibility = _row == "switch" ? Visibility.Visible : Visibility.Collapsed;
            ChevronText.Visibility = _row == "sub" ? Visibility.Visible : Visibility.Collapsed;
            if (_row != null)
            {
                Root.Style = (Style)Application.Current.Resources["RowButton"];
                Place(IconText, 0, 0, 1);
                Grid.SetRowSpan(IconText, 2);
                Place(TitleText, 0, 1, 1);
                Place(SubText, 1, 1, 1);
                Place(StateText, 0, 2, 1);
                Grid.SetRowSpan(StateText, 2);
                Place(BadgeBorder, 0, 2, 1);
                Grid.SetRowSpan(ChoicesPanel, 2);
                Grid.SetRowSpan(SwitchTrack, 2);
                Grid.SetRowSpan(ChevronText, 2);
                IconText.HorizontalAlignment = HorizontalAlignment.Center;
                IconText.VerticalAlignment = VerticalAlignment.Center;
                IconText.Width = 22;
                TitleText.HorizontalAlignment = HorizontalAlignment.Left;
                StateText.HorizontalAlignment = HorizontalAlignment.Right;
                StateText.VerticalAlignment = VerticalAlignment.Center;
                StateText.MaxWidth = 160;
                IconText.FontSize = 18;
                IconText.Margin = new Thickness(0);
                TitleText.FontSize = 13.5;
                StateText.FontSize = 12;
                TitleText.FontWeight = Windows.UI.Text.FontWeights.SemiBold;
                Layout.VerticalAlignment = VerticalAlignment.Center;
                Layout.Margin = new Thickness(0, 6, 0, 6);
                Root.MinHeight = 46;
                Root.Padding = new Thickness(13, 0, 13, 0);
                // Con cajas, la fila es solo las cajas (sin icono ni nombre)
                Place(BoxesGrid, 0, 0, 4);
                Grid.SetRowSpan(BoxesGrid, 3);
                IconText.Visibility = TitleText.Visibility = _boxes ? Visibility.Collapsed : Visibility.Visible;
                if (_boxes)
                {
                    Layout.Margin = new Thickness(0, 11, 0, 11);
                    Root.Padding = new Thickness(11, 0, 11, 0);
                }
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
                // Fila (menú de Legion L): tarjeta gris; con el foco, más clara y con aro blanco (lo pone RowButton).
                var focused = Root.FocusState != FocusState.Unfocused;
                Root.Background = Brush(focused ? "RowFocusBrush" : "Panel2Brush");
                Root.BorderBrush = new SolidColorBrush(Windows.UI.Colors.Transparent);
                Root.BorderThickness = new Thickness(0);
                IconText.Foreground = _warning ? accent : Brush(focused ? "TextBrush" : "IconBrush");
                TitleText.Foreground = Brush(focused ? "TextBrush" : "RowTextBrush");
                SubText.Foreground = Brush(_warning ? "WarnBrush" : "MutedBrush");
                StateText.Foreground = _warning ? accent : Brush(focused ? "TextBrush" : "MutedBrush");
                // Interruptor blanco (como el menú de Legion L): encendido = pista blanca y bola oscura
                SwitchTrack.Background = _isOn ? Brush("TextBrush") : new SolidColorBrush(Windows.UI.Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF));
                SwitchKnob.Fill = _isOn ? new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x11, 0x11, 0x11)) : Brush("TextBrush");
                SwitchKnob.HorizontalAlignment = _isOn ? HorizontalAlignment.Right : HorizontalAlignment.Left;
                PaintChoices();
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
