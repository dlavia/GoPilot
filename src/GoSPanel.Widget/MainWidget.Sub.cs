using System;
using System.Collections.Generic;
using System.Linq;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media.Animation;

namespace GoSPanel.Widget
{
    /// <summary>
    /// Subpáginas (diseño del menú de Legion L): lo que antes se desplegaba debajo de una fila (Avanzado de potencia,
    /// sticks, gatillos, giroscopio, luces) se abre a pantalla completa con un título «‹ Nombre · Pestaña».
    /// A en la fila entra; B (o tocar el título) vuelve a la fila. LT / RT cierran la subpágina y cambian de pestaña.
    /// Los controles no se duplican: se mueven a SubHost al abrir y vuelven a su sitio al cerrar, así que el resto del
    /// código los sigue usando por su nombre como siempre.
    /// </summary>
    public sealed partial class MainWidget
    {
        private sealed class SubPart
        {
            public FrameworkElement Element;
            public Panel Parent;
            public int Index;
            public Visibility Visibility;
        }

        private readonly List<SubPart> _subParts = new List<SubPart>();
        private Action _subClosed;
        private Control _subOpener;

        private bool SubOpen => _subParts.Count > 0;

        /// <summary>Lo que se ve ahora: la subpágina abierta o el panel de la pestaña.</summary>
        private ScrollViewer CurrentPanel => SubOpen ? SubPanel : _panels[_tab];

        /// <param name="main">Contenido principal (se hace visible al abrir).</param>
        /// <param name="extras">Otros elementos que acompañan (conservan su visibilidad, p. ej. las instrucciones de calibrar).</param>
        private void OpenSub(string title, string from, Control opener, Action onClosed, FrameworkElement main, params FrameworkElement[] extras)
        {
            if (SubOpen) CloseSub(false);
            var parts = new[] { main }.Concat(extras ?? new FrameworkElement[0]).Where(p => p != null).ToList();
            foreach (var element in parts)
            {
                var parent = element.Parent as Panel;
                if (parent == null) continue;
                _subParts.Add(new SubPart { Element = element, Parent = parent, Index = parent.Children.IndexOf(element), Visibility = element.Visibility });
            }
            if (!SubOpen) return;
            foreach (var part in _subParts) part.Parent.Children.Remove(part.Element);
            foreach (var part in _subParts) SubHost.Children.Add(part.Element);
            main.Visibility = Visibility.Visible;

            _subClosed = onClosed;
            _subOpener = opener;
            SubTitleText.Text = title;
            SubFromText.Text = string.IsNullOrEmpty(from) ? "" : "· " + from;
            HintTabs.Visibility = Visibility.Collapsed;
            HintBack.Visibility = Visibility.Visible;

            _panels[_tab].Visibility = Visibility.Collapsed;
            SubPanel.Visibility = Visibility.Visible;
            SlideIn(SubPanel, 1);   // entra desde la derecha
            SubPanel.ChangeView(null, 0, null, true);
            SubPanel.UpdateLayout();
            _lastContentFocus = null;
            (FocusManager.FindFirstFocusableElement(SubHost) as Control)?.Focus(FocusState.Keyboard);
        }

        /// <param name="animate">false al cambiar de pestaña (ya se anima el cambio de pestaña).</param>
        private void CloseSub(bool focusOpener, bool animate = true)
        {
            if (!SubOpen) return;
            SubHost.Children.Clear();
            // Por orden de posición: si dos piezas venían del mismo panel, cada una vuelve a su hueco
            foreach (var part in _subParts.OrderBy(p => p.Index))
            {
                part.Parent.Children.Insert(Math.Min(part.Index, part.Parent.Children.Count), part.Element);
                part.Element.Visibility = part.Visibility;
            }
            _subParts.Clear();

            SubPanel.Visibility = Visibility.Collapsed;
            _panels[_tab].Visibility = Visibility.Visible;
            if (animate) SlideIn(_panels[_tab], -1);   // la pestaña vuelve desde la izquierda
            else StopSubSlide();
            HintTabs.Visibility = Visibility.Visible;
            HintBack.Visibility = Visibility.Collapsed;

            var closed = _subClosed;
            _subClosed = null;
            closed?.Invoke();

            var opener = _subOpener;
            _subOpener = null;
            _lastContentFocus = null;
            if (focusOpener && opener != null)
            {
                _panels[_tab].UpdateLayout();
                opener.Focus(FocusState.Keyboard);
            }
        }

        private void OnSubBackClick(object sender, RoutedEventArgs e) => CloseSub(true);

        // ---------------------------------------------------------------------
        // Transición: lo que entra se desliza un poco y aparece (como al cambiar de pestaña); lo que sale se va al momento
        // ---------------------------------------------------------------------

        private Storyboard _subSlide;
        private UIElement _subSlideTarget;

        private void SlideIn(UIElement element, int direction)
        {
            StopSubSlide();
            if (!_animate) return;
            var sb = new Storyboard();
            AddSpline(sb, element.RenderTransform, "X", direction * PanelHost.ActualWidth * 0.18, 0);
            AddSpline(sb, element, "Opacity", 0, 1);
            sb.Completed += (s, e) => { if (_subSlide == sb) StopSubSlide(); };
            _subSlide = sb;
            _subSlideTarget = element;
            sb.Begin();
        }

        /// <summary>Para la transición en curso y deja el elemento en su sitio y visible del todo.</summary>
        private void StopSubSlide()
        {
            if (_subSlide == null) return;
            _subSlide.Stop();
            if (_subSlideTarget != null) ResetSlide(_subSlideTarget);
            _subSlide = null;
            _subSlideTarget = null;
        }
    }
}
