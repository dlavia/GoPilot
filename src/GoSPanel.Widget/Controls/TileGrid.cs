using System;
using System.Collections.Generic;
using System.Numerics;
using Windows.Foundation;
using Windows.UI.Composition;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Hosting;

namespace GoSPanel.Widget.Controls
{
    /// <summary>
    /// Cuadrícula de baldosas en dos columnas, todas del mismo tamaño. La baldosa ampliada (y cualquier elemento que
    /// no sea una baldosa, como el botón «Editar Todo») ocupa la fila entera; la siguiente sube a llenar el hueco
    /// que deja, así nunca queda una baldosa sola. Al cambiar de sitio, las baldosas se deslizan (animación implícita
    /// de Composition sobre el desplazamiento que pone la disposición).
    /// </summary>
    public sealed class TileGrid : Panel
    {
        public double Gap { get; set; } = 6;
        public double TileHeight { get; set; } = 78;

        private readonly List<Rect> _rects = new List<Rect>();
        private ImplicitAnimationCollection _implicit;
        private bool _animationsReady;

        public static bool Animate { get; set; } = true;

        private static bool IsFull(UIElement child)
        {
            var card = child as Card;
            return card == null || card.Expanded;
        }

        /// <summary>Coloca las baldosas (relleno denso: una de media anchura ocupa el primer hueco libre).</summary>
        private Size Place(double width, bool measure)
        {
            _rects.Clear();
            var half = Math.Max(0, (width - Gap) / 2);
            double y = 0;               // donde empieza la siguiente fila nueva
            double holeY = -1;          // hueco libre a la derecha de una fila (o -1)
            foreach (var child in Children)
            {
                if (child.Visibility != Visibility.Visible) { _rects.Add(Rect.Empty); continue; }
                if (IsFull(child))
                {
                    if (measure) child.Measure(new Size(width, double.PositiveInfinity));
                    var h = child.DesiredSize.Height;
                    _rects.Add(new Rect(0, y, width, h));
                    y += h + Gap;
                }
                else if (holeY >= 0)
                {
                    if (measure) child.Measure(new Size(half, TileHeight));
                    _rects.Add(new Rect(half + Gap, holeY, half, TileHeight));
                    holeY = -1;
                }
                else
                {
                    if (measure) child.Measure(new Size(half, TileHeight));
                    _rects.Add(new Rect(0, y, half, TileHeight));
                    holeY = y;
                    y += TileHeight + Gap;
                }
            }
            return new Size(width, Math.Max(0, y - Gap));
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            var width = double.IsInfinity(availableSize.Width) ? 320 : availableSize.Width;
            return Place(width, true);
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            Place(finalSize.Width, false);
            for (var i = 0; i < Children.Count; i++)
            {
                var rect = _rects[i];
                Children[i].Arrange(rect.IsEmpty ? new Rect(0, 0, 0, 0) : rect);
            }
            if (!_animationsReady)
            {
                // Después de la primera disposición: si no, todas entrarían deslizándose desde la esquina.
                _animationsReady = true;
                var _ = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Low, EnableAnimations);
            }
            return finalSize;
        }

        private void EnableAnimations()
        {
            foreach (var child in Children)
            {
                var visual = ElementCompositionPreview.GetElementVisual(child);
                if (_implicit == null)
                {
                    var compositor = visual.Compositor;
                    var offset = compositor.CreateVector3KeyFrameAnimation();
                    offset.Target = "Offset";
                    offset.InsertExpressionKeyFrame(1f, "this.FinalValue",
                        compositor.CreateCubicBezierEasingFunction(new Vector2(0.2f, 0.8f), new Vector2(0.2f, 1f)));
                    offset.Duration = TimeSpan.FromMilliseconds(280);
                    _implicit = compositor.CreateImplicitAnimationCollection();
                    _implicit["Offset"] = offset;
                }
                visual.ImplicitAnimations = Animate ? _implicit : null;
            }
        }

        /// <summary>Activa o quita el deslizamiento (ajuste «Animaciones»).</summary>
        public void RefreshAnimations()
        {
            if (_animationsReady) EnableAnimations();
        }
    }
}
