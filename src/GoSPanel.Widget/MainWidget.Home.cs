using System;
using System.Collections.Generic;
using System.Linq;
using GoSPanel.Widget.Controls;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;

namespace GoSPanel.Widget
{
    /// <summary>
    /// Baldosas de Inicio configurables (Inicio › «Editar baldosas»): qué baldosas salen y en qué orden.
    /// Se guarda en los ajustes locales («homeTiles», ids separados por comas). Las baldosas extra son copias de
    /// las de otras pestañas: muestran lo mismo y, al pulsarlas, hacen lo mismo que la original.
    /// </summary>
    public sealed partial class MainWidget
    {
        private static readonly string[] DefaultHomeTiles =
        {
            "mode", "hz", "boost", "lossless", "afmf", "rsr", "chill", "fan", "overlay",
            "lights", "touchpad", "touchscreen", "keyboard", "kill", "sharp",
        };

        private readonly Dictionary<string, Tile> _homeTiles = new Dictionary<string, Tile>();
        private readonly List<string> _homeOrder = new List<string>();
        private bool _editingHome;
        private string _pickedTile;
        private readonly Dictionary<string, Button> _covers = new Dictionary<string, Button>();

        private void InitHomeTiles()
        {
            if (_homeTiles.Count > 0) return;
            _homeTiles["mode"] = HomeModeTile;
            _homeTiles["hz"] = HomeHzTile;
            _homeTiles["boost"] = HomeBoostTile;
            _homeTiles["lossless"] = HomeLosslessTile;
            _homeTiles["afmf"] = HomeAfmfTile;
            _homeTiles["rsr"] = HomeRsrTile;
            _homeTiles["chill"] = HomeChillTile;
            _homeTiles["fan"] = HomeFanTile;
            _homeTiles["overlay"] = HomeOverlayTile;
            _homeTiles["lights"] = HomeLightsTile;
            _homeTiles["touchpad"] = HomeTouchpadTile;
            _homeTiles["touchscreen"] = HomeTouchscreenTile;
            _homeTiles["keyboard"] = HomeKeyboardTile;
            _homeTiles["kill"] = HomeKillTile;
            _homeTiles["sharp"] = HomeSharpTile;
            // Copias de baldosas de otras pestañas
            _homeTiles["charge"] = Mirror(ChargeTile, "Carga 80 %");
            _homeTiles["vibration"] = Mirror(VibrationTile, "Vibración");
            _homeTiles["gyro"] = Mirror(GyroTile, "Giroscopio");
            _homeTiles["antilag"] = Mirror(AmdAntiLagTile, "Anti-Lag");
            _homeTiles["varibright"] = Mirror(AmdVariBrightTile, "Vari-Bright");
            _homeTiles["fancurve"] = Mirror(FanCurveTile, "Curva ventilador");
            _homeTiles["legionspace"] = Mirror(LegionSpaceTile, "Legion Space");
            _homeTiles["theme"] = Mirror(ThemeTile, "Tema");
            _homeTiles["freesync"] = Mirror(AmdFreeSyncTile, "VRR");
            _homeTiles["touchvibration"] = Mirror(TouchVibrationTile, "Vibr. touchpad");
            _homeTiles["autosleep"] = Mirror(AutoSleepTile, "Apagado mando");
            _homeTiles["pollrate"] = Mirror(PollRateTile, "Sondeo");
            _homeTiles["dpad"] = Mirror(DpadTile, "Cruceta");
            _homeTiles["monitor"] = Mirror(ShowMonitorTile, "Monitor");
            _homeTiles["animations"] = Mirror(AnimationsTile, "Animaciones");
            _homeTiles["sounds"] = Mirror(SoundsTile, "Sonidos");
            _homeTiles["buttons"] = Mirror(ButtonsTile, "Botones Legion");
            _homeTiles["autoprofiles"] = Mirror(AutoProfilesTile, "Perfiles auto.");
            _homeTiles["autosave"] = Mirror(AutoSaveTile, "Autoguardado");
            _homeTiles["resume"] = Mirror(ResumeTile, "Tras suspender");

            foreach (var tile in _homeTiles.Values)
            {
                var wasAvailable = tile.Available;
                tile.VisualChanged += (s, e) =>
                {
                    if (tile.Available == wasAvailable) return;
                    wasAvailable = tile.Available;
                    if (!_editingHome) UpdateHomeNavigation();
                };
            }

            var saved = Pref("homeTiles", "");
            var ids = string.IsNullOrEmpty(saved) ? DefaultHomeTiles : saved.Split(',');
            _homeOrder.AddRange(ids.Where(id => _homeTiles.ContainsKey(id)).Distinct());
            LayoutHome();
        }

        /// <summary>Baldosa de Inicio que copia a otra (icono, textos, estado) y ejecuta su acción.</summary>
        private static Tile Mirror(Tile source, string title)
        {
            var tile = new Tile { Vertical = true };
            void Sync()
            {
                tile.Glyph = source.Glyph;
                tile.Title = title;
                tile.State = source.State;
                tile.IsOn = source.IsOn;
                tile.Warning = source.Warning;
                tile.Available = source.Available;
            }
            source.VisualChanged += (s, e) => Sync();
            tile.Click += (s, e) => source.PerformClick();
            Sync();
            return tile;
        }

        private void SaveHomeOrder() => SetPref("homeTiles", string.Join(",", _homeOrder));

        // ---------------------------------------------------------------------
        // Disposición
        // ---------------------------------------------------------------------

        private void LayoutHome()
        {
            // Sacar las baldosas de donde estén (la cuadrícula o los marcos de edición)
            foreach (var tile in _homeTiles.Values)
                if (tile.Parent is Panel parent) parent.Children.Remove(tile);
            HomeGrid.Children.Clear();
            AvailableGrid.Children.Clear();
            _covers.Clear();

            Place(HomeGrid, _homeOrder.Select(id => _editingHome ? EditFrame(id, true) : (FrameworkElement)_homeTiles[id]).ToList());

            var _nav = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Low, UpdateHomeNavigation);
            EditHomeButton.Visibility = _editingHome ? Visibility.Collapsed : Visibility.Visible;
            EditHomeHint.Visibility = _editingHome ? Visibility.Visible : Visibility.Collapsed;
            EditHomePanel.Visibility = _editingHome ? Visibility.Visible : Visibility.Collapsed;
            if (!_editingHome) return;

            var available = _homeTiles.Keys.Where(id => !_homeOrder.Contains(id)).ToList();
            Place(AvailableGrid, available.Select(id => (FrameworkElement)EditFrame(id, false)).ToList());
            AvailableEmpty.Visibility = available.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private static void Place(Grid grid, IList<FrameworkElement> items)
        {
            grid.RowDefinitions.Clear();
            for (var i = 0; i < items.Count; i++)
            {
                if (i % 3 == 0) grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(72) });
                Grid.SetRow(items[i], i / 3);
                Grid.SetColumn(items[i], i % 3);
                grid.Children.Add(items[i]);
            }
        }

        /// <summary>
        /// Marco de edición: la baldosa debajo (sin poder pulsarla) y encima un botón transparente
        /// (marcar / intercambiar o añadir) y una insignia ✕ para quitarla.
        /// </summary>
        private Grid EditFrame(string id, bool inHome)
        {
            var tile = _homeTiles[id];
            tile.IsHitTestVisible = false;
            tile.Focusable = false;   // solo la tapa de edición recibe el foco del mando
            var frame = new Grid { RenderTransform = new CompositeTransform(), RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5) };
            frame.Children.Add(tile);

            var picked = inHome && id == _pickedTile;
            var cover = new Button
            {
                Style = (Style)Application.Current.Resources["FlatButton"],
                Background = new SolidColorBrush(inHome ? Colors.Transparent : Color.FromArgb(0x66, 0, 0, 0)),
                BorderBrush = picked ? Brush("TextBrush") : new SolidColorBrush(Colors.Transparent),
                BorderThickness = new Thickness(picked ? 2 : 1),
                Tag = id,
            };
            Windows.UI.Xaml.Automation.AutomationProperties.SetName(cover, (inHome ? "Mover " : "Añadir ") + tile.Title);
            cover.Click += inHome ? (RoutedEventHandler)OnHomeTilePicked : OnHomeTileAdded;
            if (inHome)
            {
                _covers[id] = cover;
                // Con el mando: mantener A (o pulsar X) quita la baldosa; la ✕ pequeña es para el táctil.
                cover.AddHandler(KeyDownEvent, new Windows.UI.Xaml.Input.KeyEventHandler((s, e) =>
                {
                    if (e.Key == Windows.System.VirtualKey.GamepadX)
                    {
                        e.Handled = true;
                        RemoveHomeTile(id);
                    }
                    else if (e.Key == Windows.System.VirtualKey.GamepadA && !e.KeyStatus.WasKeyDown)
                        StartHold(id);
                }), true);
                cover.AddHandler(KeyUpEvent, new Windows.UI.Xaml.Input.KeyEventHandler((s, e) =>
                {
                    if (e.Key == Windows.System.VirtualKey.GamepadA) _holdTimer?.Stop();
                }), true);
            }
            frame.Children.Add(cover);

            var badge = new Button
            {
                Style = (Style)Application.Current.Resources["FlatButton"],
                Width = 24, Height = 24,
                CornerRadius = new CornerRadius(12),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, -6, -6, 0),
                Background = new SolidColorBrush(inHome ? Color.FromArgb(0xFF, 0xE5, 0x48, 0x4D) : ((SolidColorBrush)Brush("AccentBrush")).Color),
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                Content = new TextBlock
                {
                    FontFamily = new FontFamily("Segoe MDL2 Assets"),
                    Text = inHome ? "" : "",   // ✕ / +
                    FontSize = 10,
                    Foreground = new SolidColorBrush(inHome ? Colors.White : Theme.OnAccent),
                },
                Tag = id,
                IsTabStop = false,   // que el mando no caiga en la insignia al moverse entre baldosas
            };
            Windows.UI.Xaml.Automation.AutomationProperties.SetName(badge, (inHome ? "Quitar " : "Añadir ") + tile.Title);
            badge.Click += inHome ? (RoutedEventHandler)OnHomeTileRemoved : OnHomeTileAdded;
            frame.Children.Add(badge);
            return frame;
        }

        private void FocusTile(string id)
        {
            // Los botones recién creados aún no están en pantalla: se enfoca cuando termine la disposición.
            var _ = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Low, () =>
            {
                var target = HomeGrid.Children.Concat(AvailableGrid.Children).OfType<Grid>()
                    .Select(g => g.Children.OfType<Button>().FirstOrDefault())
                    .FirstOrDefault(b => b != null && (string)b.Tag == id);
                target?.Focus(FocusState.Keyboard);
            });
        }

        // ---------------------------------------------------------------------
        // Edición
        // ---------------------------------------------------------------------

        private void OnEditHomeClick(object sender, RoutedEventArgs e)
        {
            _editingHome = true;
            _pickedTile = null;
            LayoutHome();
            if (_homeOrder.Count > 0) FocusTile(_homeOrder[0]);
        }

        private void OnEditHomeDoneClick(object sender, RoutedEventArgs e)
        {
            _editingHome = false;
            _pickedTile = null;
            foreach (var tile in _homeTiles.Values)
            {
                tile.IsHitTestVisible = true;
                tile.Focusable = true;
            }
            SaveHomeOrder();
            LayoutHome();
            EditHomeButton.Focus(FocusState.Keyboard);
        }

        private void OnResetHomeClick(object sender, RoutedEventArgs e)
        {
            _homeOrder.Clear();
            _homeOrder.AddRange(DefaultHomeTiles);
            _pickedTile = null;
            LayoutHome();
        }

        private DispatcherTimer _holdTimer;
        private string _holdTile;
        private DateTime _suppressPickUntil;

        /// <summary>A mantenido 0,6 s: se quita la baldosa y se ignora el clic que llega al soltar.</summary>
        private void StartHold(string id)
        {
            if (_holdTimer == null)
            {
                _holdTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
                _holdTimer.Tick += (s, e) =>
                {
                    _holdTimer.Stop();
                    _suppressPickUntil = DateTime.UtcNow.AddMilliseconds(800);   // el clic de soltar A no cuenta
                    RemoveHomeTile(_holdTile);
                };
            }
            _holdTile = id;
            _holdTimer.Stop();
            _holdTimer.Start();
        }

        /// <summary>
        /// Primera pulsación: marca la baldosa. Segunda (en otra): las intercambia moviendo sus marcos, sin
        /// reconstruir la cuadrícula (así el mando no pierde el foco). En la misma: desmarca.
        /// </summary>
        private void OnHomeTilePicked(object sender, RoutedEventArgs e)
        {
            _holdTimer?.Stop();
            if (DateTime.UtcNow < _suppressPickUntil) return;
            var id = (string)((Button)sender).Tag;
            if (_pickedTile == null)
            {
                _pickedTile = id;
                MarkPicked(id, true);
                return;
            }
            var picked = _pickedTile;
            _pickedTile = null;
            MarkPicked(picked, false);
            if (picked == id) return;

            int a = _homeOrder.IndexOf(picked), b = _homeOrder.IndexOf(id);
            _homeOrder[a] = id;
            _homeOrder[b] = picked;
            var frameA = (FrameworkElement)_covers[picked].Parent;
            var frameB = (FrameworkElement)_covers[id].Parent;
            var oldA = frameA.TransformToVisual(HomeGrid).TransformPoint(new Windows.Foundation.Point(0, 0));
            var oldB = frameB.TransformToVisual(HomeGrid).TransformPoint(new Windows.Foundation.Point(0, 0));
            int rowA = Grid.GetRow(frameA), colA = Grid.GetColumn(frameA);
            Grid.SetRow(frameA, Grid.GetRow(frameB));
            Grid.SetColumn(frameA, Grid.GetColumn(frameB));
            Grid.SetRow(frameB, rowA);
            Grid.SetColumn(frameB, colA);
            UpdateHomeNavigation();
            if (!_animate) return;
            // Cada una sale desde donde estaba y se desliza a su sitio nuevo
            HomeGrid.UpdateLayout();
            SlideFrom(frameA, oldA);
            SlideFrom(frameB, oldB);
        }

        private void SlideFrom(FrameworkElement frame, Windows.Foundation.Point oldPosition)
        {
            var now = frame.TransformToVisual(HomeGrid).TransformPoint(new Windows.Foundation.Point(0, 0));
            var t = (CompositeTransform)frame.RenderTransform;
            var sb = new Windows.UI.Xaml.Media.Animation.Storyboard();
            AddSpline(sb, t, "TranslateX", oldPosition.X - now.X, 0);
            AddSpline(sb, t, "TranslateY", oldPosition.Y - now.Y, 0);
            sb.Begin();
        }

        private void AnimateScale(FrameworkElement frame, double to)
        {
            var t = frame.RenderTransform as CompositeTransform;
            if (t == null) return;
            if (!_animate) { t.ScaleX = t.ScaleY = to; return; }
            var sb = new Windows.UI.Xaml.Media.Animation.Storyboard();
            AddSpline(sb, t, "ScaleX", t.ScaleX, to);
            AddSpline(sb, t, "ScaleY", t.ScaleY, to);
            sb.Begin();
        }

        private void MarkPicked(string id, bool on)
        {
            Button cover;
            if (!_covers.TryGetValue(id, out cover)) return;
            cover.BorderBrush = on ? Brush("TextBrush") : new SolidColorBrush(Colors.Transparent);
            cover.BorderThickness = new Thickness(on ? 2 : 1);
            cover.Background = new SolidColorBrush(on ? Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF) : Colors.Transparent);
            var frame = (FrameworkElement)cover.Parent;
            Canvas.SetZIndex(frame, on ? 1 : 0);
            AnimateScale(frame, on ? 1.06 : 1.0);
        }

        // ---------------------------------------------------------------------
        // Navegación con la cruceta
        // ---------------------------------------------------------------------

        /// <summary>
        /// La navegación automática (XY) se equivoca en este widget (el contenido va escalado ×1,2): desde una
        /// baldosa «derecha» podía saltar al perfil. Se fija a mano: cuadrícula de 3 columnas, arriba a los
        /// deslizadores y abajo al botón de editar (o a «Disponibles» y a los botones en modo edición).
        /// </summary>
        private void UpdateHomeNavigation()
        {
            // Las baldosas no disponibles también reciben el foco (se ven apagadas y no hacen nada al pulsarlas).
            var home = _homeOrder.Select(id => _editingHome
                    ? (Control)(_covers.TryGetValue(id, out var c) ? c : null)
                    : _homeTiles[id].FocusTarget)
                .ToList();
            var available = _editingHome
                ? AvailableGrid.Children.OfType<Grid>().Select(g => (Control)g.Children.OfType<Button>().First()).ToList()
                : new List<Control>();
            Control afterHome = _editingHome ? (available.FirstOrDefault() ?? ResetHomeButton) : EditHomeButton;

            var homeLast = home.LastOrDefault(c => c != null);
            LinkGrid(home, VolumeSlider, afterHome);
            if (_editingHome) LinkGrid(available, homeLast ?? (Control)VolumeSlider, ResetHomeButton);

            // Perfil ↔ deslizadores ↔ baldosas
            if (_tab == 0) LinkProfileTo(BrightnessSlider);
            BrightnessSlider.XYFocusDown = VolumeSlider;
            VolumeSlider.XYFocusUp = BrightnessSlider;
            VolumeSlider.XYFocusDown = home.FirstOrDefault(c => c != null) ?? afterHome;
            EditHomeButton.XYFocusUp = homeLast ?? VolumeSlider;
            ResetHomeButton.XYFocusUp = available.LastOrDefault() ?? homeLast ?? (Control)VolumeSlider;
            DoneHomeButton.XYFocusUp = ResetHomeButton.XYFocusUp;
            ResetHomeButton.XYFocusRight = DoneHomeButton;
            DoneHomeButton.XYFocusLeft = ResetHomeButton;
        }

        /// <summary>
        /// Enlaza una cuadrícula de 3 columnas. Los huecos (null) se saltan: a los lados se busca la siguiente de la
        /// fila; arriba/abajo, la más cercana en la fila siguiente que tenga alguna disponible.
        /// </summary>
        private static void LinkGrid(IList<Control> items, Control above, Control below)
        {
            var n = items.Count;
            var rows = (n + 2) / 3;
            Control At(int row, int col) => row >= 0 && row < rows && col >= 0 && col < 3 && row * 3 + col < n ? items[row * 3 + col] : null;
            Control Nearest(int row, int col)
            {
                foreach (var d in new[] { 0, -1, 1, -2, 2 })
                {
                    var c = At(row, col + d);
                    if (c != null) return c;
                }
                return null;
            }
            for (var i = 0; i < n; i++)
            {
                var c = items[i];
                if (c == null) continue;
                int row = i / 3, col = i % 3;
                Control left = null, right = null, up = null, down = null;
                for (var k = col - 1; k >= 0 && left == null; k--) left = At(row, k);
                for (var k = col + 1; k < 3 && right == null; k++) right = At(row, k);
                for (var r = row - 1; r >= 0 && up == null; r--) up = Nearest(r, col);
                for (var r = row + 1; r < rows && down == null; r++) down = Nearest(r, col);
                c.XYFocusLeft = left ?? c;
                c.XYFocusRight = right ?? c;
                c.XYFocusUp = up ?? above;
                c.XYFocusDown = down ?? below;
            }
        }

        private void OnHomeTileRemoved(object sender, RoutedEventArgs e) => RemoveHomeTile((string)((Button)sender).Tag);

        private void RemoveHomeTile(string id)
        {
            var index = _homeOrder.IndexOf(id);
            _homeOrder.Remove(id);
            if (_pickedTile == id) _pickedTile = null;
            LayoutHome();
            if (_homeOrder.Count > 0) FocusTile(_homeOrder[Math.Min(index, _homeOrder.Count - 1)]);
        }

        private void OnHomeTileAdded(object sender, RoutedEventArgs e)
        {
            var id = (string)((Button)sender).Tag;
            if (!_homeOrder.Contains(id)) _homeOrder.Add(id);
            LayoutHome();
            FocusTile(id);
        }
    }
}
