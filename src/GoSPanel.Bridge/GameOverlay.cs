using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using Windows.Storage;
using Windows.UI.ViewManagement;

namespace GoSPanel.Bridge;

/// <summary>
/// Overlay propio sobre el juego: solo FPS, FPS y batería, o completo (FPS, temperatura y batería). Es una ventana transparente,
/// siempre encima, que no recibe clics ni foco, así que no depende de poder fijar widgets en Game Bar.
/// Solo se muestra mientras el juego detectado está en primer plano (al abrir Game Bar se oculta).
/// </summary>
internal static class GameOverlay
{
    private static readonly string FilePath = Path.Combine(ApplicationData.Current.LocalFolder.Path, "overlay.json");
    private static readonly string[] Positions = { "tl", "tc", "tr", "bl", "br" };
    private static readonly string[] Contents = { "fps", "battery", "full" };
    private static readonly object Lock = new();

    private static bool _enabled;
    private static string _position = "tl";
    private static string _content = "full";
    private static bool _always;   // false = solo con un juego en primer plano
    private static IntPtr _hwnd;
    private static Timer? _timer;
    private static int _busy;
    private static bool _visible;
    private static WndProc? _wndProc;   // referencia viva para que el GC no recoja el delegado

    public static void Start()
    {
        Load();
        var thread = new Thread(RunWindow) { IsBackground = true, Name = "GameOverlay" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        _timer = new Timer(_ => Tick(), null, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(1));
    }

    public static JsonObject Get()
    {
        lock (Lock) return new JsonObject { ["enabled"] = _enabled, ["position"] = _position, ["content"] = _content, ["always"] = _always };
    }

    public static JsonObject Set(JsonObject args)
    {
        lock (Lock)
        {
            if (args["enabled"] is JsonValue e) _enabled = e.GetValue<bool>();
            if (args["position"] is JsonValue p && Positions.Contains(p.GetValue<string>())) _position = p.GetValue<string>();
            if (args["content"] is JsonValue c && Contents.Contains(c.GetValue<string>())) _content = c.GetValue<string>();
            if (args["always"] is JsonValue al) _always = al.GetValue<bool>();
            File.WriteAllText(FilePath, new JsonObject { ["enabled"] = _enabled, ["position"] = _position, ["content"] = _content, ["always"] = _always }.ToJsonString());
        }
        Tick();
        return Get();
    }

    private static void Load()
    {
        try
        {
            if (JsonNode.Parse(File.ReadAllText(FilePath)) is JsonObject o)
            {
                _enabled = o["enabled"]?.GetValue<bool>() ?? false;
                var p = o["position"]?.GetValue<string>();
                if (p != null && Positions.Contains(p)) _position = p;
                var c = o["content"]?.GetValue<string>();
                if (c != null && Contents.Contains(c)) _content = c;
                _always = o["always"]?.GetValue<bool>() ?? false;
            }
        }
        catch (Exception) { /* sin archivo: desactivado */ }
    }

    // ---------------------------------------------------------------------
    // Actualización (cada segundo, en el grupo de subprocesos)
    // ---------------------------------------------------------------------

    private static async void Tick()
    {
        if (_hwnd == IntPtr.Zero || Interlocked.Exchange(ref _busy, 1) == 1) return;
        try
        {
            bool enabled, always; string position, content;
            lock (Lock) { enabled = _enabled; position = _position; content = _content; always = _always; }

            var game = ForegroundTracker.Current;
            var foreground = GetForegroundWindow();
            GetWindowThreadProcessId(foreground, out var fgPid);
            var inGame = game is not null && fgPid == game.Value.Pid;
            if (!enabled || (!always && !inGame))
            {
                if (!ToastActive) Hide();
                return;
            }

            double? fps = null;
            if (inGame)
            {
                try { fps = Num(await Program.CallServiceAsync("fps.get", new JsonObject { ["pid"] = game!.Value.Pid }), "fps"); }
                catch (Exception) { }
            }
            var segments = new List<Segment> { new("FPS", fps.HasValue ? $"{fps:0}" : "—", true) };
            if (content == "fps")
            {
                if (!ToastActive) Render(foreground, position, segments.ToArray());
                return;
            }

            var status = await Program.CallServiceAsync("status");

            var battery = status["battery"] as JsonObject;
            var rate = Num(battery, "rateW");
            var percent = Num(battery, "percent");
            var temp = Num(status["cpu"] as JsonObject, "tempC");
            var ac = battery?["acOnline"] is JsonValue a && a.TryGetValue<bool>(out var on) && on;

            var batteryText = percent.HasValue ? $"{percent:0} %" : "—";
            var remaining = Num(battery, "remainingWh");
            if (!ac && rate > 0.5 && remaining.HasValue)
            {
                var minutes = (int)Math.Round(remaining.Value / rate!.Value * 60);
                batteryText += $" {minutes / 60}:{minutes % 60:00}";
            }

            if (content == "full") segments.Add(new(null, temp.HasValue ? $"{temp:0}°" : "—", false));
            segments.Add(new(ac ? "AC" : null, batteryText, false, !ac && percent < 20));
            if (!ToastActive) Render(foreground, position, segments.ToArray());
        }
        catch (Exception)
        {
            if (!ToastActive) Hide();
        }
        finally
        {
            Interlocked.Exchange(ref _busy, 0);
        }
    }

    // ---------------------------------------------------------------------
    // Aviso breve (p. ej. al cambiar de modo con Legion L + Y): grande en el centro, ~1,6 s, aunque el
    // overlay esté desactivado. Mientras dura, la actualización normal no lo pisa.
    // ---------------------------------------------------------------------

    private static readonly object RenderLock = new();
    private static DateTime _toastUntil;
    private static Timer? _toastTimer;
    private static bool ToastActive => DateTime.Now < _toastUntil;

    /// <summary>Aviso cuadrado en el centro de la pantalla: icono (Phosphor), título y detalle.</summary>
    public static void ShowToast(string title, string? detail = null, char glyph = '')
    {
        if (_hwnd == IntPtr.Zero) return;
        lock (RenderLock)
        {
            _toastUntil = DateTime.Now.AddMilliseconds(1600);
            RenderToast(GetForegroundWindow(), glyph, title, detail);
        }
        _toastTimer?.Dispose();
        _toastTimer = new Timer(_ =>
        {
            lock (RenderLock) { if (!ToastActive) Hide(); }
            Tick();   // vuelve el overlay normal si estaba activo
        }, null, 1700, Timeout.Infinite);
    }

    private static System.Drawing.Text.PrivateFontCollection? _iconFonts;

    private static FontFamily? IconFamily()
    {
        try
        {
            if (_iconFonts == null)
            {
                var path = Path.Combine(Windows.ApplicationModel.Package.Current.InstalledLocation.Path, "Assets", "Fonts", "Phosphor-Fill.ttf");
                _iconFonts = new System.Drawing.Text.PrivateFontCollection();
                _iconFonts.AddFontFile(path);
            }
            return _iconFonts.Families.FirstOrDefault();
        }
        catch (Exception) { return null; }
    }

    private static void RenderToast(IntPtr window, char glyph, string title, string? detail)
    {
        SetThreadDpiAwarenessContext(new IntPtr(-4));   // coordenadas de pantalla reales (150 %)
        var scale = GetDpiForWindow(_hwnd) / 96f;
        if (scale <= 0) scale = 1;
        var size = (int)(200 * scale);

        using var bmp = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.Clear(Color.Transparent);
            using (var path = RoundedRect(new RectangleF(0, 0, size, size), 28 * scale))
            using (var bg = new SolidBrush(Color.FromArgb(225, 23, 24, 29)))
            using (var border = new Pen(Color.FromArgb(50, 255, 255, 255), 1))
            {
                g.FillPath(bg, path);
                g.DrawPath(border, path);
            }

            var center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            var family = IconFamily();
            if (family != null)
            {
                using var iconFont = new Font(family, 64 * scale, FontStyle.Regular, GraphicsUnit.Pixel);
                using var accent = new SolidBrush(AccentColor());
                g.DrawString(glyph.ToString(), iconFont, accent, new RectangleF(0, 18 * scale, size, 90 * scale), center);
            }
            using var titleFont = new Font("Segoe UI Semibold", 24 * scale, FontStyle.Regular, GraphicsUnit.Pixel);
            using var detailFont = new Font("Segoe UI Semibold", 17 * scale, FontStyle.Regular, GraphicsUnit.Pixel);
            using var white = new SolidBrush(Color.FromArgb(244, 245, 248));
            using var muted = new SolidBrush(Color.FromArgb(160, 160, 172));
            g.DrawString(title, titleFont, white, new RectangleF(0, 112 * scale, size, 36 * scale), center);
            if (detail != null) g.DrawString(detail, detailFont, muted, new RectangleF(0, 148 * scale, size, 28 * scale), center);
        }

        var monitor = MonitorFromWindow(window, 2);
        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        GetMonitorInfo(monitor, ref info);
        var r = info.rcMonitor;
        Present(bmp, r.Left + (r.Right - r.Left - size) / 2, r.Top + (r.Bottom - r.Top - size) / 2);
    }

    private static double? Num(JsonObject? o, string key) =>
        o?[key] is JsonValue v && v.TryGetValue<double>(out var d) ? d : null;

    private sealed record Segment(string? Label, string Value, bool Accent, bool Warn = false);

    private static void Hide()
    {
        if (!_visible) return;
        _visible = false;
        ShowWindow(_hwnd, SwHide);
    }

    // ---------------------------------------------------------------------
    // Dibujo: una píldora oscura semitransparente con los valores separados por líneas finas
    // ---------------------------------------------------------------------

    private static void Render(IntPtr gameWindow, string position, Segment[] segments, bool large = false)
    {
        SetThreadDpiAwarenessContext(new IntPtr(-4));   // se llama desde el grupo de subprocesos
        var scale = GetDpiForWindow(_hwnd) / 96f;
        if (scale <= 0) scale = 1;
        if (large) scale *= 2f;   // aviso: grande y en el centro de la pantalla

        using var valueFont = new Font("Segoe UI Semibold", 15f * scale, FontStyle.Regular, GraphicsUnit.Pixel);
        using var labelFont = new Font("Segoe UI Semibold", 11f * scale, FontStyle.Regular, GraphicsUnit.Pixel);
        var format = StringFormat.GenericTypographic;
        format.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces;

        float padX = 12 * scale, gap = 10 * scale, labelGap = 4 * scale;
        var height = (int)Math.Ceiling(30 * scale);

        // Medir
        using var measureBmp = new Bitmap(1, 1);
        using var measure = Graphics.FromImage(measureBmp);
        var widths = segments.Select(s =>
            (s.Label is null ? 0 : measure.MeasureString(s.Label, labelFont, 1000, format).Width + labelGap)
            + measure.MeasureString(s.Value, valueFont, 1000, format).Width).ToArray();
        var width = (int)Math.Ceiling(padX * 2 + widths.Sum() + gap * 2 * (segments.Length - 1) + segments.Length - 1);

        using var bmp = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.Clear(Color.Transparent);

            using (var path = RoundedRect(new RectangleF(0, 0, width, height), height / 2f))
            using (var bg = new SolidBrush(Color.FromArgb(205, 23, 24, 29)))
                g.FillPath(bg, path);

            using var text = new SolidBrush(Color.FromArgb(244, 245, 248));
            using var muted = new SolidBrush(Color.FromArgb(154, 154, 166));
            using var accent = new SolidBrush(AccentColor());
            using var warn = new SolidBrush(Color.FromArgb(255, 176, 32));
            using var line = new Pen(Color.FromArgb(70, 255, 255, 255), 1);

            float x = padX;
            for (var i = 0; i < segments.Length; i++)
            {
                var s = segments[i];
                if (i > 0)
                {
                    x += gap;
                    g.DrawLine(line, x, height * 0.3f, x, height * 0.7f);
                    x += 1 + gap;
                }
                if (s.Label != null)
                {
                    var lh = labelFont.GetHeight(g);
                    g.DrawString(s.Label, labelFont, muted, x, (height - lh) / 2f + scale, format);
                    x += measure.MeasureString(s.Label, labelFont, 1000, format).Width + labelGap;
                }
                var vh = valueFont.GetHeight(g);
                g.DrawString(s.Value, valueFont, s.Warn ? warn : s.Accent ? accent : text, x, (height - vh) / 2f, format);
                x += measure.MeasureString(s.Value, valueFont, 1000, format).Width;
            }
        }

        // Colocar en la esquina elegida de la pantalla del juego
        var monitor = MonitorFromWindow(gameWindow, 2);
        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        GetMonitorInfo(monitor, ref info);
        var r = info.rcMonitor;
        var margin = (int)(12 * scale);
        var px = position[1] switch
        {
            'l' => r.Left + margin,
            'r' => r.Right - margin - width,
            _ => r.Left + (r.Right - r.Left - width) / 2,
        };
        var py = position[0] switch
        {
            't' => r.Top + margin,
            'b' => r.Bottom - margin - height,
            _ => r.Top + (r.Bottom - r.Top - height) / 2,
        };

        Present(bmp, px, py);
    }

    private static GraphicsPath RoundedRect(RectangleF r, float radius)
    {
        var d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(r.Left, r.Top, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static Color AccentColor()
    {
        try
        {
            var c = new UISettings().GetColorValue(UIColorType.AccentLight2);
            return Color.FromArgb(c.R, c.G, c.B);
        }
        catch (Exception)
        {
            return Color.FromArgb(52, 227, 154);
        }
    }

    private static void Present(Bitmap bmp, int x, int y)
    {
        var screenDc = GetDC(IntPtr.Zero);
        var memDc = CreateCompatibleDC(screenDc);
        var hBitmap = bmp.GetHbitmap(Color.FromArgb(0));
        var old = SelectObject(memDc, hBitmap);
        try
        {
            var size = new SIZE { cx = bmp.Width, cy = bmp.Height };
            var src = new POINT();
            var pos = new POINT { x = x, y = y };
            var blend = new BLENDFUNCTION { BlendOp = 0, BlendFlags = 0, SourceConstantAlpha = 255, AlphaFormat = 1 };
            UpdateLayeredWindow(_hwnd, screenDc, ref pos, ref size, memDc, ref src, 0, ref blend, 2);
            // Reafirmar «siempre encima»: Lossless Scaling y otros overlays también lo son.
            SetWindowPos(_hwnd, new IntPtr(-1), 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate | SwpShowWindow);
            _visible = true;
        }
        finally
        {
            SelectObject(memDc, old);
            DeleteObject(hBitmap);
            DeleteDC(memDc);
            ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    // ---------------------------------------------------------------------
    // Ventana: capa transparente, siempre encima, sin foco y que deja pasar los clics
    // ---------------------------------------------------------------------

    private static void RunWindow()
    {
        SetThreadDpiAwarenessContext(new IntPtr(-4));   // por monitor v2: texto nítido al 150 %
        _wndProc = (h, m, w, l) => DefWindowProc(h, m, w, l);
        var cls = new WNDCLASSEX
        {
            cbSize = Marshal.SizeOf<WNDCLASSEX>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
            hInstance = GetModuleHandle(null),
            lpszClassName = "GoPilotOverlay",
        };
        RegisterClassEx(ref cls);
        const int exStyle = 0x80000 /*LAYERED*/ | 0x20 /*TRANSPARENT*/ | 0x8 /*TOPMOST*/ | 0x80 /*TOOLWINDOW*/ | 0x8000000 /*NOACTIVATE*/;
        _hwnd = CreateWindowEx(exStyle, "GoPilotOverlay", "GoPilot Overlay", unchecked((int)0x80000000) /*POPUP*/,
            0, 0, 1, 1, IntPtr.Zero, IntPtr.Zero, cls.hInstance, IntPtr.Zero);

        while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }
    }

    private const int SwHide = 0;
    private const uint SwpNoSize = 0x1, SwpNoMove = 0x2, SwpNoActivate = 0x10, SwpShowWindow = 0x40;

    private delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEX
    {
        public int cbSize; public uint style; public IntPtr lpfnWndProc; public int cbClsExtra; public int cbWndExtra;
        public IntPtr hInstance; public IntPtr hIcon; public IntPtr hCursor; public IntPtr hbrBackground;
        public string? lpszMenuName; public string lpszClassName; public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)] private struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam, lParam; public uint time; public POINT pt; }
    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int x, y; }
    [StructLayout(LayoutKind.Sequential)] private struct SIZE { public int cx, cy; }
    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MONITORINFO { public int cbSize; public RECT rcMonitor, rcWork; public uint dwFlags; }
    [StructLayout(LayoutKind.Sequential, Pack = 1)] private struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern ushort RegisterClassEx(ref WNDCLASSEX cls);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowEx(int exStyle, string cls, string name, int style, int x, int y, int w, int h,
        IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);
    [DllImport("user32.dll")] private static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern int GetMessage(out MSG msg, IntPtr hWnd, uint min, uint max);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref MSG msg);
    [DllImport("user32.dll")] private static extern IntPtr DispatchMessage(ref MSG msg);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? name);
    [DllImport("user32.dll")] private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int cmd);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int w, int h, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out int pid);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hWnd, uint flags);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hWnd, IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("user32.dll")]
    private static extern bool UpdateLayeredWindow(IntPtr hWnd, IntPtr dst, ref POINT pos, ref SIZE size, IntPtr src,
        ref POINT srcPos, int key, ref BLENDFUNCTION blend, int flags);
}
