using System;
using System.Linq;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;

namespace GoSPanel.Widget
{
    /// <summary>
    /// Ajustes › Actualizaciones: versión nueva publicada en GitHub (la buscan, descargan e instalan el servicio y
    /// apply-update.ps1), aviso en Inicio, punto en la pestaña Ajustes y desinstalación.
    /// </summary>
    public sealed partial class MainWidget
    {
        private const string GlyphUpToDate = "", GlyphDownload = "", GlyphWarning = "";

        private string _latestVersion;      // versión publicada más nueva que la instalada (null = al día)
        private string _latestNotes = "";
        private string _updateState = "idle";
        private bool _updateBodyOpen = true;
        private DispatcherTimer _updateTimer;

        private static Version InstalledVersion
        {
            get
            {
                var v = Windows.ApplicationModel.Package.Current.Id.Version;
                return new Version(v.Major, v.Minor, v.Build, v.Revision);
            }
        }

        /// <summary>Lo primero de Inicio: el aviso de versión nueva si se ve, o el brillo.</summary>
        private Control HomeFirstControl => UpdateNotice.Visibility == Visibility.Visible ? (Control)UpdateNoticeButton : BrightnessSlider;

        private void InitUpdates()
        {
            AutoUpdateTile.IsOn = Pref("autoUpdate", true);
            // Con el mando, X sobre el aviso lo oculta (la ✕ es para el táctil).
            UpdateNoticeButton.AddHandler(KeyDownEvent, new KeyEventHandler((s, e) =>
            {
                if (e.Key != VirtualKey.GamepadX) return;
                e.Handled = true;
                DismissUpdateNotice();
            }), true);
        }

        private async Task CheckUpdatesAsync(bool force)
        {
            if (force)
            {
                UpdateTile.State = "Buscando…";
                UpdateHint.Text = "";
            }
            var data = await CallAsync("update.check", new JsonObject { ["force"] = JsonValue.CreateBooleanValue(force) }, false);
            if (data != null) ApplyUpdate(data);
            else if (force)
            {
                UpdateTile.State = "Sin conexión";
                UpdateHint.Text = "no se pudo comprobar";
            }
        }

        private void ApplyUpdate(JsonObject data)
        {
            _updateState = StringOrNull(data, "state") ?? "idle";
            _latestVersion = null;
            var latest = Obj(data, "latest");
            Version published;
            if (latest != null && Version.TryParse(StringOrNull(latest, "version") ?? "", out published) && published > InstalledVersion)
            {
                _latestVersion = StringOrNull(latest, "version");
                _latestNotes = StringOrNull(latest, "notes") ?? "";
            }

            // Si la última instalación de esta versión falló, se muestra como error (lo escribe apply-update.ps1).
            var error = StringOrNull(data, "error");
            var last = Obj(data, "last");
            if (_updateState == "idle" && _latestVersion != null && last != null
                && StringOrNull(last, "state") == "error" && StringOrNull(last, "version") == _latestVersion)
            {
                _updateState = "error";
                error = StringOrNull(last, "message");
            }

            var v = InstalledVersion;
            var busy = _updateState == "downloading" || _updateState == "installing";
            UpdateTile.IsOn = _latestVersion != null && !busy;
            UpdateNotesBox.Visibility = Visibility.Collapsed;
            UpdateProgress.Visibility = Visibility.Collapsed;
            UpdateMessage.Visibility = Visibility.Collapsed;
            UpdateButtons.Visibility = Visibility.Collapsed;

            if (busy)
            {
                var downloading = _updateState == "downloading";
                var progress = (int)data.GetNamedNumber("progress", 0);
                UpdateTile.Glyph = GlyphDownload;
                UpdateTile.Title = (downloading ? "Descargando " : "Instalando ") + (_latestVersion ?? "") + "…";
                UpdateTile.State = downloading ? progress + " %" : "";
                UpdateProgress.IsIndeterminate = !downloading;
                UpdateProgress.Value = progress;
                UpdateProgress.Visibility = Visibility.Visible;
                ShowUpdateMessage("Al terminar, Game Bar se cerrará unos segundos y GoPilot volverá con la versión nueva. No apagues la consola.", false);
                UpdateBody.Visibility = Visibility.Visible;
                UpdateHint.Text = "";
                StartUpdatePolling();
            }
            else if (_updateState == "error")
            {
                UpdateTile.Glyph = GlyphWarning;
                UpdateTile.Title = "No se pudo actualizar";
                UpdateTile.State = _latestVersion ?? "";
                ShowUpdateMessage((error ?? "Error desconocido") + " Sigues con la " + v.ToString() + ".", true);
                UpdateNowButton.Content = "Reintentar";
                UpdateButtons.Visibility = Visibility.Visible;
                UpdateBody.Visibility = _updateBodyOpen ? Visibility.Visible : Visibility.Collapsed;
                UpdateHint.Text = "";
            }
            else if (_latestVersion != null)
            {
                UpdateTile.Glyph = GlyphDownload;
                UpdateTile.Title = "Versión nueva · " + _latestVersion;
                UpdateTile.State = "Disponible";
                UpdateNotes.Text = FormatNotes(_latestNotes);
                UpdateNotesBox.Visibility = string.IsNullOrWhiteSpace(_latestNotes) ? Visibility.Collapsed : Visibility.Visible;
                UpdateNowButton.Content = "Actualizar";
                UpdateButtons.Visibility = Visibility.Visible;
                UpdateBody.Visibility = _updateBodyOpen ? Visibility.Visible : Visibility.Collapsed;
                UpdateHint.Text = "tienes la " + v.ToString();
            }
            else
            {
                UpdateTile.Glyph = GlyphUpToDate;
                UpdateTile.Title = "GoPilot " + v.ToString();
                UpdateTile.State = "Al día";
                UpdateBody.Visibility = Visibility.Collapsed;
                DateTimeOffset checkedAt;
                UpdateHint.Text = DateTimeOffset.TryParse(StringOrNull(data, "checkedAt") ?? "", out checkedAt)
                    ? "comprobado a las " + checkedAt.ToLocalTime().ToString("HH:mm") : "";
            }

            UpdateDot.Visibility = _latestVersion != null ? Visibility.Visible : Visibility.Collapsed;
            var notice = _latestVersion != null && !busy && Pref("updateDismissed", "") != _latestVersion;
            UpdateNoticeTitle.Text = "GoPilot " + _latestVersion + " disponible";
            SetUpdateNotice(notice);
        }

        private void ShowUpdateMessage(string text, bool error)
        {
            UpdateMessage.Text = text;
            UpdateMessage.Foreground = Brush(error ? "WarnBrush" : "MutedBrush");
            UpdateMessage.Visibility = Visibility.Visible;
        }

        /// <summary>Las notas de la versión (Markdown de GitHub) como texto: «- algo» pasa a «• algo».</summary>
        private static string FormatNotes(string markdown)
        {
            var lines = markdown.Replace("\r", "").Split('\n')
                .Select(l => l.Trim())
                .Where(l => l.Length > 0)
                .Select(l => l.StartsWith("- ") || l.StartsWith("* ") ? "• " + l.Substring(2)
                    : l.StartsWith("#") ? l.TrimStart('#', ' ') : l);
            return "Novedades\n" + string.Join("\n", lines);
        }

        private void StartUpdatePolling()
        {
            if (_updateTimer != null) return;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            var lastAnswer = DateTime.UtcNow;
            var asking = false;
            timer.Tick += async (s, e) =>
            {
                if (asking) return;   // una consulta cada vez, aunque tarde
                asking = true;
                try
                {
                    var data = await CallAsync("update.status", null, false);
                    if (data != null)
                    {
                        lastAnswer = DateTime.UtcNow;
                        ApplyUpdate(data);
                    }
                    else if (DateTime.UtcNow - lastAnswer > TimeSpan.FromMinutes(2))
                    {
                        // Sin respuesta tanto tiempo: no dejar el widget atascado en «Instalando…»
                        StopUpdatePolling();
                        _updateState = "idle";
                        await CheckUpdatesAsync(true);
                        return;
                    }
                    if (_updateState == "downloading" || _updateState == "installing") return;
                    StopUpdatePolling();
                    // El servicio se ha reiniciado (actualizado) y ha olvidado la versión: se vuelve a preguntar
                    // para mostrar «Al día» o el error de la última instalación.
                    if (data != null && Obj(data, "latest") == null) await CheckUpdatesAsync(false);
                }
                finally
                {
                    asking = false;
                }
            };
            _updateTimer = timer;
            timer.Start();
        }

        private void StopUpdatePolling()
        {
            if (_updateTimer == null) return;
            _updateTimer.Stop();
            _updateTimer = null;
        }

        // ---------------------------------------------------------------------
        // Aviso en Inicio
        // ---------------------------------------------------------------------

        private void SetUpdateNotice(bool visible)
        {
            var target = visible ? Visibility.Visible : Visibility.Collapsed;
            if (UpdateNotice.Visibility == target) return;
            var hadFocus = FocusManager.GetFocusedElement() == UpdateNoticeButton;
            UpdateNotice.Visibility = target;
            // El StackPanel cuenta el hueco del aviso aunque esté oculto: el margen negativo lo compensa.
            QuickSlidersSection.Margin = new Thickness(0, visible ? 0 : -10, 0, -8);
            LinkHomeTop();
            if (hadFocus) BrightnessSlider.Focus(FocusState.Keyboard);
        }

        /// <summary>Perfil → aviso (si se ve) → brillo, con la cruceta.</summary>
        private void LinkHomeTop()
        {
            if (_tab == 0) LinkProfileTo(HomeFirstControl);
            if (UpdateNotice.Visibility != Visibility.Visible) return;
            UpdateNoticeButton.XYFocusDown = BrightnessSlider;
            BrightnessSlider.XYFocusUp = UpdateNoticeButton;
        }

        private void DismissUpdateNotice()
        {
            if (_latestVersion != null) SetPref("updateDismissed", _latestVersion);
            SetUpdateNotice(false);
        }

        private void OnUpdateNoticeDismiss(object sender, RoutedEventArgs e) => DismissUpdateNotice();

        private async void OnUpdateNoticeClick(object sender, RoutedEventArgs e)
        {
            _updateBodyOpen = true;
            ShowTab(4);
            UpdateBody.Visibility = Visibility.Visible;
            await Task.Delay(50);
            UpdatesSection.StartBringIntoView();
            UpdateTile.FocusTarget.Focus(FocusState.Keyboard);
        }

        // ---------------------------------------------------------------------
        // Ajustes › Actualizaciones
        // ---------------------------------------------------------------------

        private async void OnUpdateTileClick(object sender, RoutedEventArgs e)
        {
            if (_updateState == "downloading" || _updateState == "installing") return;
            if (_latestVersion == null && _updateState != "error")
            {
                await CheckUpdatesAsync(true);
                return;
            }
            _updateBodyOpen = UpdateBody.Visibility != Visibility.Visible;
            UpdateBody.Visibility = _updateBodyOpen ? Visibility.Visible : Visibility.Collapsed;
        }

        private async void OnUpdateNowClick(object sender, RoutedEventArgs e)
        {
            // Primero se refresca: la versión que guarda el servicio puede tener una hora.
            await CheckUpdatesAsync(true);
            if (_latestVersion == null) return;
            var data = await CallAsync("update.install");
            if (data != null) ApplyUpdate(data);
        }

        private void OnUpdateLaterClick(object sender, RoutedEventArgs e)
        {
            _updateBodyOpen = false;
            UpdateBody.Visibility = Visibility.Collapsed;
            DismissUpdateNotice();
            UpdateTile.FocusTarget.Focus(FocusState.Keyboard);
        }

        private async void OnAutoUpdateClick(object sender, RoutedEventArgs e)
        {
            var enabled = !Pref("autoUpdate", true);
            SetPref("autoUpdate", enabled);
            AutoUpdateTile.IsOn = enabled;
            if (enabled) await CheckUpdatesAsync(false);
        }

        /// <summary>Al abrir el widget: busca (si está activado; el servicio guarda la respuesta una hora) o solo lee el estado.</summary>
        private async Task RefreshUpdatesAsync()
        {
            if (Pref("autoUpdate", true)) await CheckUpdatesAsync(false);
            else
            {
                var data = await CallAsync("update.status", null, false);
                if (data != null) ApplyUpdate(data);
            }
        }

        // ---------------------------------------------------------------------
        // Desinstalar (pulsar dos veces)
        // ---------------------------------------------------------------------

        private DateTime _uninstallArmedUntil;

        private async void OnUninstallClick(object sender, RoutedEventArgs e)
        {
            if (DateTime.UtcNow > _uninstallArmedUntil)
            {
                _uninstallArmedUntil = DateTime.UtcNow.AddSeconds(4);
                UninstallTile.Title = "Pulsa otra vez para desinstalar";
                await Task.Delay(4000);
                if (DateTime.UtcNow > _uninstallArmedUntil) UninstallTile.Title = "Desinstalar GoPilot";
                return;
            }
            _uninstallArmedUntil = DateTime.MaxValue;   // no se puede repetir
            UninstallTile.Title = "Desinstalando…";
            // El servicio lo hace todo (y deja Legion Space activado); Game Bar se cerrará al quitar el widget.
            if (await CallAsync("uninstall") != null)
            {
                UninstallTile.Title = "Desinstalando… Game Bar se cerrará";
                MessageText.Text = "GoPilot se está desinstalando y Legion Space vuelve a quedar activado. Cuando Game Bar se cierre, habrá terminado.";
            }
            else
            {
                _uninstallArmedUntil = DateTime.MinValue;
                UninstallTile.Title = "Desinstalar GoPilot";
            }
        }
    }
}
