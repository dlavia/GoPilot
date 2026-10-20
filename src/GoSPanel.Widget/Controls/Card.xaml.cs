using System;
using Windows.System;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Markup;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Animation;

namespace GoSPanel.Widget.Controls
{
    /// <summary>
    /// Baldosa de la página única (maqueta design/perfil-modo-baldosas.html). Mitad de ancho y altura fija; ampliada
    /// ocupa la fila entera y enseña <see cref="Body"/> debajo.
    ///   A (toque)        · la acción rápida (<see cref="Quick"/>): «toggle» enciende o apaga, «cycle» pasa al siguiente
    ///                      valor, «action» ejecuta algo; sin acción rápida, abre las opciones.
    ///   Mantener A o Y   · abre o cierra las opciones (si tiene). B las cierra.
    /// Con <see cref="Value"/> vacío se ve como interruptor: icono y punto arriba, el nombre abajo en grande.
    /// </summary>
    [ContentProperty(Name = nameof(Body))]
    public sealed partial class Card : UserControl
    {
        /// <summary>A (toque) con acción rápida.</summary>
        public event RoutedEventHandler Click;

        /// <summary>Pide abrir o cerrar las opciones (mantener A, Y, B o A sin acción rápida). Lo decide la página.</summary>
        public event EventHandler ExpandRequested;

        /// <summary>Cambió algo visible (estado, textos): la página repinta la barra de botones.</summary>
        public event EventHandler VisualChanged;

        private static readonly TimeSpan HoldTime = TimeSpan.FromMilliseconds(450);
        private readonly DispatcherTimer _holdTimer = new DispatcherTimer { Interval = HoldTime };
        private Storyboard _holdAnimation;

        public Card()
        {
            InitializeComponent();
            Root.Tap += OnTap;
            Root.HoldStarted += StartHold;
            Root.HoldEnded += StopHold;
            _holdTimer.Tick += (s, e) =>
            {
                StopHold();
                Root.SuppressNextTap();   // el clic al soltar no cuenta
                ExpandRequested?.Invoke(this, EventArgs.Empty);
            };
            AddHandler(KeyDownEvent, new KeyEventHandler(OnCardKeyDown), false);
            ApplyLayout();
            ApplyVisuals();
            EventHandler onTheme = (s, e) => ApplyVisuals();
            Loaded += (s, e) => { Theme.Changed -= onTheme; Theme.Changed += onTheme; ApplyVisuals(); };
            Unloaded += (s, e) => Theme.Changed -= onTheme;
        }

        // ---------------------------------------------------------------------
        // Contenido
        // ---------------------------------------------------------------------

        public string Glyph
        {
            get => IconText.Text;
            set { IconText.Text = value ?? ""; }
        }

        public string Title
        {
            get => _title;
            set { _title = value ?? ""; ApplyLayout(); Changed(); }
        }
        private string _title = "";

        /// <summary>Dato grande («71 %», «Auto»). Vacío: baldosa de interruptor (el nombre va abajo en grande).</summary>
        public string Value
        {
            get => _value;
            set { _value = value ?? ""; ApplyLayout(); Changed(); }
        }
        private string _value = "";

        /// <summary>Línea pequeña de abajo («Activado · FreeSync», «Toca para cambiar»).</summary>
        public string State
        {
            get => StateText.Text;
            set { StateText.Text = value ?? ""; if (_expanded) ApplyLayout(); Changed(); }
        }

        /// <summary>Opciones que se ven al ampliar (contenido XAML de la baldosa).</summary>
        public UIElement Body
        {
            get => BodyHost.Content as UIElement;
            set { BodyHost.Content = value; ApplyLayout(); }
        }

        public bool HasBody => BodyHost.Content != null;

        /// <summary>Perfil y Modo: icono grande dentro de un cuadrado de color.</summary>
        public bool Key
        {
            get => _key;
            set { _key = value; ApplyLayout(); ApplyVisuals(); }
        }
        private bool _key;

        /// <summary>Qué hace A: "" (abrir opciones), "toggle", "cycle" o "action".</summary>
        public string Quick
        {
            get => _quick;
            set { _quick = value ?? ""; ApplyLayout(); Changed(); }
        }
        private string _quick = "";

        /// <summary>Texto de A en la barra de botones (por defecto Encender / Apagar, Cambiar u Opciones).</summary>
        public string ActionText
        {
            get => _actionText;
            set { _actionText = value; Changed(); }
        }
        private string _actionText;

        /// <summary>Filtros en los que sale, separados por espacios (Rendimiento, Pantalla, AMD, Mando, Sistema).</summary>
        public string Filter { get; set; } = "";

        /// <summary>Siempre arriba, con cualquier filtro (Perfil y Modo).</summary>
        public bool Pinned { get; set; }

        // ---------------------------------------------------------------------
        // Estado
        // ---------------------------------------------------------------------

        public bool IsOn
        {
            get => _isOn;
            set { if (_isOn == value) return; _isOn = value; ApplyVisuals(); Changed(); }
        }
        private bool _isOn;

        /// <summary>Aviso en naranja (p. ej. «pulsa otra vez para cerrar el juego»).</summary>
        public bool Warning
        {
            get => _warning;
            set { _warning = value; ApplyVisuals(); Changed(); }
        }
        private bool _warning;

        private bool _available = true;
        /// <summary>No disponible: apagada y sin acción, pero el mando puede pasar por encima.</summary>
        public bool Available
        {
            get => _available;
            set { _available = value; Layout.Opacity = value ? 1 : 0.4; Changed(); }
        }

        private bool _expanded;
        public bool Expanded
        {
            get => _expanded;
            set
            {
                if (_expanded == value) return;
                _expanded = value;
                BodyHost.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
                ChevronRotation.Angle = value ? 180 : 0;
                ApplyLayout();
                if (value && Animate) FadeInBody();
                (Parent as FrameworkElement)?.InvalidateMeasure();
                Changed();
            }
        }

        /// <summary>Editando «Todo»: true = incluida (✓), false = fuera (apagada), null = uso normal.</summary>
        public bool? Selection
        {
            get => _selection;
            set { _selection = value; ApplyLayout(); ApplyVisuals(); }
        }
        private bool? _selection;

        public static bool Animate { get; set; } = true;

        private ImageSource _art, _iconImage;

        /// <summary>Arte del juego: fondo detrás de la cabecera e icono en el cuadrado (null = sin arte).</summary>
        public void SetArt(ImageSource art, ImageSource icon)
        {
            _art = art;
            _iconImage = icon;
            ApplyVisuals();
        }

        public Control FocusTarget => Root;

        /// <summary>Lo que dice la barra de botones para A.</summary>
        public string HintA
        {
            get
            {
                if (_selection.HasValue) return _selection.Value ? "Quitar de Todo" : "Añadir a Todo";
                if (!string.IsNullOrEmpty(_actionText) && _quick != "") return _actionText;
                switch (_quick)
                {
                    case "toggle": return _isOn ? "Apagar" : "Encender";
                    case "cycle": return "Cambiar";
                    case "action": return "Activar";
                    default: return _expanded ? "Cerrar" : "Opciones";
                }
            }
        }

        /// <summary>Si A no abre las opciones pero las hay, se abren con Y (o manteniendo A).</summary>
        public bool HintY => HasBody && _quick != "" && !_selection.HasValue;

        // ---------------------------------------------------------------------
        // Entrada
        // ---------------------------------------------------------------------

        private void OnTap(object sender, EventArgs e)
        {
            if (_selection.HasValue || _quick == "")
            {
                ExpandRequested?.Invoke(this, EventArgs.Empty);
                return;
            }
            if (_available) Click?.Invoke(this, new RoutedEventArgs());
        }

        /// <summary>Ejecuta la acción rápida como si se hubiera pulsado A.</summary>
        public void PerformClick()
        {
            if (_available && _quick != "") Click?.Invoke(this, new RoutedEventArgs());
        }

        private void StartHold(object sender, EventArgs e)
        {
            if (!HasBody || _selection.HasValue || _quick == "") return;   // sin opciones aparte, A ya las abre
            _holdTimer.Start();
            HoldBar.Opacity = 1;
            var accent = ((SolidColorBrush)Brush("AccentBrush")).Color;
            HoldFill.Background = new SolidColorBrush(Color.FromArgb(0x55, accent.R, accent.G, accent.B));
            HoldFill.Opacity = 1;
            _holdAnimation?.Stop();
            var sb = new Storyboard();
            foreach (var target in new DependencyObject[] { HoldScale, HoldFillScale })
            {
                var anim = new DoubleAnimation { From = 0, To = 1, Duration = HoldTime };
                Storyboard.SetTarget(anim, target);
                Storyboard.SetTargetProperty(anim, "ScaleX");
                sb.Children.Add(anim);
            }
            _holdAnimation = sb;
            sb.Begin();
        }

        private void StopHold(object sender = null, EventArgs e = null)
        {
            _holdTimer.Stop();
            _holdAnimation?.Stop();
            HoldScale.ScaleX = HoldFillScale.ScaleX = 0;
            HoldBar.Opacity = HoldFill.Opacity = 0;
        }

        private void OnCardKeyDown(object sender, KeyRoutedEventArgs e)
        {
            // Y abre o cierra; B cierra (desde la cabecera o desde dentro de las opciones)
            if (e.Key == VirtualKey.GamepadY && HasBody && !_selection.HasValue)
            {
                e.Handled = true;
                ExpandRequested?.Invoke(this, EventArgs.Empty);
            }
            else if ((e.Key == VirtualKey.GamepadB || e.Key == VirtualKey.Escape) && _expanded)
            {
                e.Handled = true;
                ExpandRequested?.Invoke(this, EventArgs.Empty);
            }
        }

        private void FadeInBody()
        {
            var sb = new Storyboard();
            var opacity = new DoubleAnimation { From = 0, To = 1, Duration = TimeSpan.FromMilliseconds(220), BeginTime = TimeSpan.FromMilliseconds(60) };
            Storyboard.SetTarget(opacity, BodyHost);
            Storyboard.SetTargetProperty(opacity, "Opacity");
            sb.Children.Add(opacity);
            BodyHost.Opacity = 0;
            sb.Begin();
        }

        private void Changed() => VisualChanged?.Invoke(this, EventArgs.Empty);

        // ---------------------------------------------------------------------
        // Aspecto
        // ---------------------------------------------------------------------

        private void ApplyLayout()
        {
            // Ampliada: cabecera de una línea (icono, «Brillo · 71 %» y ⌃); ya se sabe dónde se está y deja sitio a las opciones
            Root.Height = _expanded ? 46 : 92;
            HoldFill.Height = ArtLayer.Height = Root.Height;
            if (_expanded)
            {
                TitleText.Visibility = Visibility.Visible;
                TitleText.Text = string.IsNullOrEmpty(_value) ? _title + (string.IsNullOrEmpty(StateText.Text) ? "" : " · " + StateText.Text)
                    : _key ? _value : _title + " · " + _value;
                TitleText.FontSize = 13.5;
                TitleText.FontWeight = Windows.UI.Text.FontWeights.SemiBold;
                ValueText.Visibility = StateText.Visibility = Visibility.Collapsed;
                Dot.Visibility = Visibility.Collapsed;
                ChevronText.Visibility = Visibility.Visible;
                ChevronText.Text = "";
                ChevronText.FontSize = 11;
                IconBox.Width = IconBox.Height = _key ? 26 : double.NaN;
                IconText.FontSize = _key ? 15 : 16;
                IconBox.VerticalAlignment = VerticalAlignment.Center;
                return;
            }
            TitleText.FontSize = 12;
            TitleText.FontWeight = Windows.UI.Text.FontWeights.Normal;
            ValueText.Visibility = StateText.Visibility = Visibility.Visible;
            IconBox.VerticalAlignment = VerticalAlignment.Top;
            var toggleLook = string.IsNullOrEmpty(_value);
            // Interruptor: icono y punto arriba, el nombre abajo en grande. Valor: icono + nombre arriba y el dato grande.
            TitleText.Visibility = toggleLook || _key ? Visibility.Collapsed : Visibility.Visible;
            ValueText.Text = toggleLook ? _title : _value;
            ValueText.FontSize = toggleLook ? 14 : _key ? 16 : 21;
            ValueText.Margin = new Thickness(0, 0, 0, toggleLook ? 0 : 1);
            TitleText.Text = _title;
            Dot.Visibility = (_quick == "toggle" || _quick == "cycle") && !_selection.HasValue ? Visibility.Visible : Visibility.Collapsed;
            ChevronText.Visibility = HasBody && !_selection.HasValue ? Visibility.Visible : Visibility.Collapsed;
            if (_selection.HasValue)
            {
                ChevronText.Visibility = Visibility.Visible;
                ChevronText.Text = _selection.Value ? "" : "";   // ✓ / círculo vacío
                ChevronText.FontSize = 15;
                ChevronRotation.Angle = 0;
            }
            else
            {
                ChevronText.Text = "";
                ChevronText.FontSize = 11;
            }
            IconBox.Width = IconBox.Height = _key ? 34 : double.NaN;
            IconText.FontSize = _key ? 18 : 16;
        }

        private void ApplyVisuals()
        {
            var on = _isOn || _warning;
            var accent = ((SolidColorBrush)Brush(_warning ? "WarnBrush" : "AccentBrush")).Color;
            var tinted = on && _quick != "" && _quick != "action" || _warning;
            if (Theme.IsContrast && tinted)
            {
                Frame.Background = new SolidColorBrush(accent);
                Frame.BorderBrush = new SolidColorBrush(Colors.Transparent);
                var dark = new SolidColorBrush(Theme.OnAccent);
                IconText.Foreground = TitleText.Foreground = ValueText.Foreground = StateText.Foreground = ChevronText.Foreground = dark;
                Dot.Fill = dark;
            }
            else
            {
                Frame.Background = tinted ? new SolidColorBrush(Color.FromArgb(0x30, accent.R, accent.G, accent.B)) : Brush("Panel2Brush");
                Frame.BorderBrush = tinted ? new SolidColorBrush(Color.FromArgb(0x8C, accent.R, accent.G, accent.B)) : new SolidColorBrush(Colors.Transparent);
                var soft = new SolidColorBrush(Lighten(accent));
                IconText.Foreground = tinted || _key ? soft : Brush("IconBrush");
                TitleText.Foreground = Brush("TextBrush");
                ValueText.Foreground = Brush("TextBrush");
                StateText.Foreground = tinted ? soft : Brush("MutedBrush");
                ChevronText.Foreground = _selection == true ? Brush("AccentBrush") : Brush("MutedBrush");
                Dot.Fill = on ? new SolidColorBrush(accent) : new SolidColorBrush(Color.FromArgb(0xFF, 0x5A, 0x5B, 0x62));
            }
            IconBox.Background = _key ? new SolidColorBrush(Color.FromArgb(0x30, accent.R, accent.G, accent.B)) : new SolidColorBrush(Colors.Transparent);
            // Con arte: el icono del juego ocupa el cuadrado y su fondo va detrás de la cabecera
            if (_iconImage != null) IconBox.Background = new ImageBrush { ImageSource = _iconImage, Stretch = Stretch.UniformToFill };
            IconText.Visibility = _iconImage != null ? Visibility.Collapsed : Visibility.Visible;
            ArtLayer.Visibility = _art != null ? Visibility.Visible : Visibility.Collapsed;
            if (_art != null)
            {
                ArtLayer.Background = new ImageBrush { ImageSource = _art, Stretch = Stretch.UniformToFill, AlignmentY = AlignmentY.Center };
                ValueText.Foreground = TitleText.Foreground = new SolidColorBrush(Colors.White);
            }
            Frame.Opacity = _selection == false ? 0.45 : 1;
        }

        /// <summary>Acento aclarado para iconos y textos sobre el tinte (se lee mejor que el acento puro).</summary>
        private static Color Lighten(Color c) =>
            Color.FromArgb(0xFF, (byte)(c.R + (255 - c.R) * 0.45), (byte)(c.G + (255 - c.G) * 0.45), (byte)(c.B + (255 - c.B) * 0.45));

        private static Brush Brush(string key) => (Brush)Application.Current.Resources[key];
    }

    /// <summary>
    /// Botón de la cabecera de la baldosa: A, Intro y espacio se gestionan aquí (al soltar) para poder distinguir
    /// un toque de «mantener». El ratón y el dedo igual: mantener pulsado abre las opciones.
    /// </summary>
    public sealed class CardButton : Button
    {
        public event EventHandler Tap, HoldStarted, HoldEnded;
        private bool _keyDown, _suppress;

        public CardButton()
        {
            Click += (s, e) =>
            {
                if (_suppress) { _suppress = false; return; }
                Tap?.Invoke(this, EventArgs.Empty);
            };
            AddHandler(PointerPressedEvent, new PointerEventHandler((s, e) => { _suppress = false; HoldStarted?.Invoke(this, EventArgs.Empty); }), true);
            AddHandler(PointerReleasedEvent, new PointerEventHandler((s, e) => HoldEnded?.Invoke(this, EventArgs.Empty)), true);
            PointerExited += (s, e) => HoldEnded?.Invoke(this, EventArgs.Empty);
            PointerCaptureLost += (s, e) => HoldEnded?.Invoke(this, EventArgs.Empty);
            LostFocus += (s, e) => { _keyDown = false; HoldEnded?.Invoke(this, EventArgs.Empty); };
        }

        /// <summary>El clic que llega al soltar después de mantener no cuenta.</summary>
        public void SuppressNextTap() => _suppress = true;

        private static bool IsAccept(VirtualKey key) => key == VirtualKey.GamepadA || key == VirtualKey.Enter || key == VirtualKey.Space;

        protected override void OnKeyDown(KeyRoutedEventArgs e)
        {
            if (!IsAccept(e.Key)) { base.OnKeyDown(e); return; }
            e.Handled = true;
            if (_keyDown) return;   // repetición de la tecla mantenida
            _keyDown = true;
            _suppress = false;
            HoldStarted?.Invoke(this, EventArgs.Empty);
        }

        protected override void OnKeyUp(KeyRoutedEventArgs e)
        {
            if (!IsAccept(e.Key)) { base.OnKeyUp(e); return; }
            e.Handled = true;
            if (!_keyDown) return;
            _keyDown = false;
            HoldEnded?.Invoke(this, EventArgs.Empty);
            if (_suppress) { _suppress = false; return; }
            Tap?.Invoke(this, EventArgs.Empty);
        }
    }
}
