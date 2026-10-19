using System;
using System.Collections.Generic;
using System.Linq;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Media;

namespace GoSPanel.Widget.Controls
{
    /// <summary>
    /// Selector con el estilo del widget (oscuro, redondeado, borde fino) en lugar del ComboBox genérico.
    /// Al pulsarlo abre una lista flotante; la opción actual se marca con el color de acento.
    /// </summary>
    public sealed partial class Selector : UserControl
    {
        private List<KeyValuePair<string, object>> _options = new List<KeyValuePair<string, object>>();
        private object _selected;

        /// <summary>Se lanza cuando el usuario elige una opción distinta de la actual.</summary>
        public event EventHandler<object> SelectionChanged;

        public Selector()
        {
            InitializeComponent();
            OptionsFlyout.Opened += (s, e) => Sounds.Play("Show");
            OptionsFlyout.Closed += (s, e) => Sounds.Play("Hide");
        }

        public string Label
        {
            get => LabelText.Text;
            set
            {
                LabelText.Text = value ?? "";
                LabelText.Visibility = string.IsNullOrEmpty(value) ? Visibility.Collapsed : Visibility.Visible;
            }
        }

        public object SelectedValue => _selected;

        /// <summary>Sustituye las opciones (texto visible, valor) y marca la seleccionada sin lanzar el evento.</summary>
        public void SetOptions(IEnumerable<KeyValuePair<string, object>> options, object selected)
        {
            _options = options.ToList();
            _selected = selected;
            var current = _options.FirstOrDefault(o => Equals(o.Value, selected));
            ValueText.Text = current.Key ?? "—";
            Root.IsEnabled = _options.Count > 0;
        }

        private void OnRootClick(object sender, RoutedEventArgs e)
        {
            OptionsPanel.Children.Clear();
            Button selectedButton = null;
            foreach (var option in _options)
            {
                var isSelected = Equals(option.Value, _selected);
                var text = new TextBlock
                {
                    Text = option.Key,
                    FontSize = 15.5,
                    FontWeight = isSelected ? Windows.UI.Text.FontWeights.SemiBold : Windows.UI.Text.FontWeights.Normal,
                    Foreground = Brush(isSelected ? "AccentBrush" : "TextBrush"),
                    VerticalAlignment = VerticalAlignment.Center,
                };
                var button = new Button
                {
                    Style = (Style)Application.Current.Resources["FlatButton"],
                    Background = Brush(isSelected ? "AccentSoftBrush" : "Panel2Brush"),
                    // La lista flotante no hereda la escala del widget (×1,2): se compensa aquí.
                    MinWidth = (Root.ActualWidth - 8) * 1.2,
                    Height = 44,
                    Padding = new Thickness(12, 0, 12, 0),
                    Content = text,
                    Tag = option.Value,
                };
                button.Click += OnOptionClick;
                OptionsPanel.Children.Add(button);
                if (isSelected) selectedButton = button;
            }

            OptionsFlyout.Opened += FocusSelected;
            FlyoutBase.ShowAttachedFlyout(Root);

            void FocusSelected(object s, object a)
            {
                OptionsFlyout.Opened -= FocusSelected;
                (selectedButton ?? OptionsPanel.Children.OfType<Button>().FirstOrDefault())?.Focus(FocusState.Keyboard);
            }
        }

        private void OnOptionClick(object sender, RoutedEventArgs e)
        {
            OptionsFlyout.Hide();
            var value = ((Button)sender).Tag;
            if (Equals(value, _selected)) return;
            _selected = value;
            ValueText.Text = _options.First(o => Equals(o.Value, value)).Key;
            SelectionChanged?.Invoke(this, value);
        }

        private static Brush Brush(string key) => (Brush)Application.Current.Resources[key];
    }
}
