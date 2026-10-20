using System;
using Windows.System;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;

namespace GoSPanel.Widget.Controls
{
    /// <summary>
    /// Barra de nivel (brillo, volumen): se rellena hasta el valor. Con el foco, ← → la mueven de 10 en 10; con el dedo
    /// o el ratón, tocar o arrastrar. A lanza <see cref="Click"/> (silenciar, brillo automático).
    /// </summary>
    public sealed partial class LevelBar : UserControl
    {
        /// <summary>El usuario ha cambiado el valor (no se lanza al ponerlo desde el código).</summary>
        public event EventHandler<double> ValueChanged;
        public event RoutedEventHandler Click;

        public LevelBar()
        {
            InitializeComponent();
            Root.Step += (s, direction) => SetByUser(Math.Round((_value + direction * StepSize) / StepSize) * StepSize);
            Root.Click += (s, e) => Click?.Invoke(this, e);
            Root.DragTo += (s, fraction) => SetByUser(Math.Round(fraction * 100 / 5) * 5);
            Root.GotFocus += (s, e) => ApplyVisuals();
            Root.LostFocus += (s, e) => ApplyVisuals();
            SizeChanged += (s, e) => ApplyFill();
            EventHandler onTheme = (s, e) => ApplyVisuals();
            Loaded += (s, e) => { Theme.Changed -= onTheme; Theme.Changed += onTheme; ApplyVisuals(); };
            Unloaded += (s, e) => Theme.Changed -= onTheme;
            ApplyVisuals();
        }

        public double StepSize { get; set; } = 10;

        public string Glyph { get => IconText.Text; set => IconText.Text = value ?? ""; }
        public string Label { get => LabelText.Text; set => LabelText.Text = value ?? ""; }

        private double _value;
        public double Value
        {
            get => _value;
            set { _value = Math.Max(0, Math.Min(100, value)); ApplyFill(); ApplyText(); }
        }

        private string _note;
        /// <summary>Texto en lugar del número (p. ej. «Silencio»); null = el valor.</summary>
        public string Note
        {
            get => _note;
            set { _note = value; ApplyText(); }
        }

        private bool _dimmed;
        /// <summary>Relleno apagado (silenciado o brillo automático).</summary>
        public bool Dimmed
        {
            get => _dimmed;
            set { _dimmed = value; ApplyVisuals(); }
        }

        public Control FocusTarget => Root;

        /// <summary>El último clic vino del dedo o el ratón (tocar la barra pone el nivel, no silencia).</summary>
        public bool FromPointer => Root.LastWasPointer;

        private void SetByUser(double value)
        {
            value = Math.Max(0, Math.Min(100, value));
            if (value == _value) return;
            Value = value;
            ValueChanged?.Invoke(this, value);
        }

        private void ApplyFill() => Fill.Width = Math.Max(0, Root.ActualWidth * _value / 100);

        private void ApplyText() => ValueText.Text = _note ?? _value.ToString("0") + " %";

        private void ApplyVisuals()
        {
            var focused = Root.FocusState != FocusState.Unfocused;
            var accent = ((SolidColorBrush)Application.Current.Resources["AccentBrush"]).Color;
            // Con el foco, el relleno en color de acento (se ve que ← → la mueven); sin él, gris claro
            Fill.Background = new SolidColorBrush(_dimmed ? Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF)
                : focused ? Color.FromArgb(0x70, accent.R, accent.G, accent.B) : Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF));
            var text = (Brush)Application.Current.Resources[_dimmed ? "MutedBrush" : "TextBrush"];
            IconText.Foreground = LabelText.Foreground = ValueText.Foreground = text;
        }
    }

    /// <summary>Botón de la barra: ← → (cruceta, stick o teclado) mueven el valor en vez de cambiar el foco.</summary>
    public sealed class LevelButton : Button
    {
        public event EventHandler<int> Step;
        public event EventHandler<double> DragTo;
        private bool _dragging;

        public bool LastWasPointer { get; private set; }

        public LevelButton()
        {
            AddHandler(PointerPressedEvent, new PointerEventHandler((s, e) => { LastWasPointer = true; _dragging = true; CapturePointer(e.Pointer); Drag(e); }), true);
            AddHandler(PointerMovedEvent, new PointerEventHandler((s, e) => { if (_dragging) Drag(e); }), true);
            AddHandler(PointerReleasedEvent, new PointerEventHandler((s, e) => { _dragging = false; ReleasePointerCapture(e.Pointer); }), true);
            PointerCaptureLost += (s, e) => _dragging = false;
        }

        /// <summary>Con el dedo o el ratón, tocar o arrastrar pone el valor; el clic (A) no se lanza al soltar.</summary>
        public bool IsDragging => _dragging;

        private void Drag(PointerRoutedEventArgs e)
        {
            if (e.Pointer.PointerDeviceType == Windows.Devices.Input.PointerDeviceType.Mouse && !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            var x = e.GetCurrentPoint(this).Position.X;
            DragTo?.Invoke(this, ActualWidth > 0 ? Math.Max(0, Math.Min(1, x / ActualWidth)) : 0);
        }

        public static bool IsLeft(VirtualKey k) => k == VirtualKey.GamepadDPadLeft || k == VirtualKey.GamepadLeftThumbstickLeft || k == VirtualKey.Left;
        public static bool IsRight(VirtualKey k) => k == VirtualKey.GamepadDPadRight || k == VirtualKey.GamepadLeftThumbstickRight || k == VirtualKey.Right;

        protected override void OnKeyDown(KeyRoutedEventArgs e)
        {
            LastWasPointer = false;
            if (IsLeft(e.Key) || IsRight(e.Key))
            {
                e.Handled = true;
                Step?.Invoke(this, IsRight(e.Key) ? 1 : -1);
                return;
            }
            base.OnKeyDown(e);
        }
    }
}
