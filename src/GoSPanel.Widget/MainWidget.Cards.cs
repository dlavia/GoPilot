using System;
using System.Collections.Generic;
using System.Linq;
using GoSPanel.Widget.Controls;
using Windows.Foundation;
using Windows.System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;

namespace GoSPanel.Widget
{
    /// <summary>
    /// Página única de baldosas (maqueta design/perfil-modo-baldosas.html): filtros (LT / RT), una baldosa ampliada a la
    /// vez, «Todo» con las baldosas que elija el usuario (pref «todoCards»), la cruceta calculada a mano y la barra de
    /// botones de abajo. Perfil y Modo están siempre arriba.
    /// </summary>
    public sealed partial class MainWidget
    {
        private static readonly string[] Filters = { "Todo", "Rendimiento", "Pantalla", "AMD", "Mando", "Sistema" };

        private static readonly string[] DefaultTodo =
        {
            "AfmfCard", "VrrCard", "BoostCard", "LightsCard", "RefreshCard", "FanCard",
        };

        private string _filter = "Todo";
        private List<Card> _cards = new List<Card>();
        private readonly HashSet<string> _todo = new HashSet<string>();
        private bool _editingTodo;

        private void InitCards()
        {
            if (_cards.Count > 0) return;
            _cards = CardsGrid.Children.OfType<Card>().ToList();
            foreach (var card in _cards)
            {
                card.ExpandRequested += (s, e) => OnCardExpandRequested((Card)s);
                card.VisualChanged += (s, e) => UpdateHints();
            }

            var saved = Pref("todoCards", "");
            foreach (var id in string.IsNullOrEmpty(saved) ? DefaultTodo : saved.Split(','))
                if (_cards.Any(c => c.Name == id)) _todo.Add(id);

            for (var i = 0; i < Filters.Length; i++)
            {
                var index = i;
                var chip = new Button
                {
                    Style = (Style)Application.Current.Resources["FlatButton"],
                    IsTabStop = false,   // con el mando se cambia con LT / RT
                    Padding = new Thickness(12, 5, 12, 6),
                    Content = new TextBlock { Text = Filters[i], FontSize = 12, FontWeight = Windows.UI.Text.FontWeights.SemiBold },
                };
                chip.Click += (s, e) => ShowFilter(index);
                FiltersBar.Children.Add(chip);
            }
            ShowFilter(0, false);
            ProfileCard.FocusTarget.Focus(FocusState.Keyboard);
        }

        // ---------------------------------------------------------------------
        // Filtros
        // ---------------------------------------------------------------------

        private void ShowFilter(int index, bool focusFirst = true)
        {
            if (index < 0) return;
            var changed = _filter != Filters[index];
            _filter = Filters[index];
            if (changed && _editingTodo) EndEditTodo();
            foreach (var card in _cards) if (card.Expanded && changed) Collapse(card);
            ApplyFilterVisibility();
            ShowQuickBars(_filter == "Todo");

            for (var i = 0; i < FiltersBar.Children.Count; i++)
            {
                var chip = (Button)FiltersBar.Children[i];
                var selected = i == index;
                chip.Background = selected ? Brush("TextBrush") : Brush("Panel2Brush");
                ((TextBlock)chip.Content).Foreground = selected ? new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x11, 0x11, 0x14)) : Brush("TextBrush");
                if (selected) chip.StartBringIntoView();
            }
            if (!changed) return;
            ContentScroll.ChangeView(null, 0, null, !_animate);
            if (focusFirst) ProfileCard.FocusTarget.Focus(FocusState.Keyboard);
            UpdateHints();
        }

        private void ApplyFilterVisibility()
        {
            foreach (var card in _cards)
            {
                bool visible;
                if (_editingTodo) visible = !card.Pinned;
                else if (card.Pinned) visible = true;
                else if (_filter == "Todo") visible = _todo.Contains(card.Name);
                else visible = card.Filter.Split(' ').Contains(_filter);
                card.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            }
            EditTodoButton.Visibility = _filter == "Todo" ? Visibility.Visible : Visibility.Collapsed;
        }

        // ---------------------------------------------------------------------
        // Ampliar (una a la vez)
        // ---------------------------------------------------------------------

        private void OnCardExpandRequested(Card card)
        {
            if (_editingTodo)
            {
                if (card.Pinned) return;
                if (!_todo.Remove(card.Name)) _todo.Add(card.Name);
                card.Selection = _todo.Contains(card.Name);
                UpdateHints();
                return;
            }
            ExpandCard(card);
        }

        /// <summary>Abre la baldosa (cerrando la que estuviera abierta) o la cierra si ya lo estaba.</summary>
        private void ExpandCard(Card card)
        {
            var open = !card.Expanded;
            foreach (var other in _cards) if (other != card && other.Expanded) Collapse(other);
            if (open) card.Expanded = true;
            else Collapse(card);
            card.FocusTarget.Focus(FocusState.Keyboard);
            if (open)
            {
                // Cuando ya tiene su tamaño nuevo: que se vea entera (o lo más posible) en la pantalla
                var _ = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Low, () =>
                    card.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = _animate, VerticalAlignmentRatio = 0.0, VerticalOffset = -8 }));
            }
            UpdateHints();
        }

        private void Collapse(Card card)
        {
            card.Expanded = false;
            // Probar o calibrar el mando se termina al cerrar su baldosa
            if (card == SticksCard || card == GyroCard) StopTest();
        }

        // ---------------------------------------------------------------------
        // Elegir las baldosas de «Todo»
        // ---------------------------------------------------------------------

        private void OnEditTodoClick(object sender, RoutedEventArgs e)
        {
            if (_editingTodo)
            {
                EndEditTodo();
                ContentScroll.ChangeView(null, 0, null, !_animate);
                EditTodoButton.Focus(FocusState.Keyboard);
                return;
            }
            _editingTodo = true;
            foreach (var card in _cards)
            {
                if (card.Expanded) Collapse(card);
                if (!card.Pinned) card.Selection = _todo.Contains(card.Name);
            }
            EditTodoIcon.Text = "";
            EditTodoText.Text = "Listo";
            ApplyFilterVisibility();
            ContentScroll.ChangeView(null, 0, null, !_animate);
            var first = _cards.FirstOrDefault(c => !c.Pinned);
            first?.FocusTarget.Focus(FocusState.Keyboard);
        }

        private void EndEditTodo()
        {
            _editingTodo = false;
            foreach (var card in _cards) card.Selection = null;
            // En el orden de la página, para que «Todo» no dependa del orden en que se marcaron
            SetPref("todoCards", string.Join(",", _cards.Where(c => _todo.Contains(c.Name)).Select(c => c.Name)));
            EditTodoIcon.Text = "";
            EditTodoText.Text = "Elegir las baldosas de Todo";
            ApplyFilterVisibility();
        }

        // ---------------------------------------------------------------------
        // Cruceta: el control más cercano en esa dirección (la navegación automática se equivoca en este widget,
        // con el contenido escalado ×1,2: saltaba a sitios lejanos o no salía de una baldosa)
        // ---------------------------------------------------------------------

        private bool MoveFocus(VirtualKey key)
        {
            int dx = 0, dy = 0;
            if (key == VirtualKey.GamepadDPadUp || key == VirtualKey.GamepadLeftThumbstickUp || key == VirtualKey.Up) dy = -1;
            else if (key == VirtualKey.GamepadDPadDown || key == VirtualKey.GamepadLeftThumbstickDown || key == VirtualKey.Down) dy = 1;
            else if (key == VirtualKey.GamepadDPadLeft || key == VirtualKey.GamepadLeftThumbstickLeft || key == VirtualKey.Left) dx = -1;
            else if (key == VirtualKey.GamepadDPadRight || key == VirtualKey.GamepadLeftThumbstickRight || key == VirtualKey.Right) dx = 1;
            else return false;

            var focused = FocusManager.GetFocusedElement() as Control;
            if (focused == null || (focused as Slider)?.IsFocusEngaged == true || !IsInside(focused, LayoutRoot)) return false;
            if (focused is LevelButton && dx != 0) return false;   // ← → mueven la barra de brillo o volumen

            var from = Bounds(focused);
            Control best = null;
            var bestCost = double.MaxValue;
            foreach (var c in Focusables(LayoutRoot))
            {
                if (c == focused) continue;
                var r = Bounds(c);
                double cost;
                if (dy != 0)
                {
                    var gap = dy > 0 ? r.Top - from.Bottom : from.Top - r.Bottom;
                    if (gap < -2) continue;
                    // Mejor si se solapan en horizontal (la de debajo, no la de al lado)
                    var overlap = Math.Min(r.Right, from.Right) - Math.Max(r.Left, from.Left);
                    var side = overlap > 0 ? 0 : Math.Abs((r.Left + r.Right) / 2 - (from.Left + from.Right) / 2);
                    cost = gap + side * 0.6;
                }
                else
                {
                    var gap = dx > 0 ? r.Left - from.Right : from.Left - r.Right;
                    if (gap < -2) continue;
                    var overlap = Math.Min(r.Bottom, from.Bottom) - Math.Max(r.Top, from.Top);
                    if (overlap <= 0) continue;   // a los lados, solo en la misma fila
                    cost = gap + Math.Abs((r.Top + r.Bottom) / 2 - (from.Top + from.Bottom) / 2) * 0.5;
                }
                if (cost < bestCost) { bestCost = cost; best = c; }
            }
            if (best != null)
            {
                best.Focus(FocusState.Keyboard);
                best.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = _animate, VerticalOffset = 8 });
            }
            return true;   // también sin destino: que no se escape a otro sitio
        }

        private Rect Bounds(FrameworkElement element) =>
            element.TransformToVisual(LayoutRoot).TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));

        private static bool IsInside(DependencyObject element, DependencyObject root)
        {
            for (; element != null; element = VisualTreeHelper.GetParent(element))
                if (element == root) return true;
            return false;
        }

        /// <summary>Controles que pueden recibir el foco y se ven (lo oculto no se recorre).</summary>
        private static IEnumerable<Control> Focusables(DependencyObject root)
        {
            var stack = new Stack<DependencyObject>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                var item = stack.Pop();
                var element = item as UIElement;
                if (element != null && element.Visibility != Visibility.Visible) continue;
                var control = item as Control;
                if (control != null && item != root && control.IsTabStop && control.IsEnabled && control.ActualWidth > 0 && control.ActualHeight > 0)
                    yield return control;
                var count = VisualTreeHelper.GetChildrenCount(item);
                for (var i = 0; i < count; i++) stack.Push(VisualTreeHelper.GetChild(item, i));
            }
        }

        // ---------------------------------------------------------------------
        // Barra de botones: qué hacen A, Y y B donde está el foco
        // ---------------------------------------------------------------------

        private void UpdateHints()
        {
            if (HintAText == null) return;
            var focused = FocusManager.GetFocusedElement() as DependencyObject;
            Card card = null;
            for (var e = focused; e != null; e = VisualTreeHelper.GetParent(e))
                if ((card = e as Card) != null) break;
            var onHeader = card != null && focused == card.FocusTarget;
            var bar = focused == BrightnessBar.FocusTarget ? BrightnessBar : focused == VolumeBar.FocusTarget ? VolumeBar : null;
            HintPad.Visibility = bar != null ? Visibility.Visible : Visibility.Collapsed;
            HintA.Visibility = focused is Control && !(bar == BrightnessBar && !_lightSensor) ? Visibility.Visible : Visibility.Collapsed;
            HintAText.Text = bar == VolumeBar ? (_muted ? "Activar el sonido" : "Silenciar")
                : bar == BrightnessBar ? (_autoBrightness ? "Brillo manual" : "Brillo automático")
                : onHeader ? card.HintA : focused is Slider ? "Ajustar" : "Elegir";
            HintY.Visibility = onHeader && card.HintY && !card.Expanded ? Visibility.Visible : Visibility.Collapsed;
            HintB.Visibility = card != null && card.Expanded ? Visibility.Visible : Visibility.Collapsed;
        }
    }
}
