using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Windows.Storage;

namespace GoSPanel.Bridge;

/// <summary>
/// Modo escritorio (como la Steam Deck): el mando hace de ratón y teclado. Stick derecho = ratón, stick izquierdo =
/// rueda, RT/LT = clic izquierdo/derecho, R3 = clic central, A/B = Intro/Esc, X = teclado en pantalla, Y = vista de
/// tareas, LB/RB = pestaña anterior/siguiente, cruceta = flechas.
/// Modos: «off», «auto» (solo sin juego delante) y «always». Nunca actúa con Game Bar abierta ni en apps que ya se
/// manejan con el mando (app Xbox, Steam…), porque esas recibirían cada pulsación dos veces. GoPilot no puede ocultar
/// el mando a los juegos (haría falta un controlador como HidHide): por eso en «auto» se apaga al tener un juego delante.
/// Lee los informes que ya recoge LegionButtons (MI_06) y genera la entrada con SendInput.
/// </summary>
internal static class DesktopMode
{
    private static string FilePath => Path.Combine(ApplicationData.Current.LocalFolder.Path, "desktop.json");
    private static readonly object Lock = new();
    private static string _mode = "auto";
    private static int _pointerSpeed = 55, _scrollSpeed = 40;
    private static bool? _override;            // la acción de los botones Legion lo fuerza hasta que cambie la app delante
    private static int _overridePid;
    private static bool _active;
    private static string _reason = "";

    // Apps que ya se manejan con el mando: el modo escritorio duplicaría cada pulsación.
    private static readonly HashSet<string> ControllerApps = new(StringComparer.OrdinalIgnoreCase)
    {
        "XboxPcApp", "GameBar", "steam", "steamwebhelper", "Playnite.FullscreenApp", "GoSPanel.Widget",
    };

    private const int BtnA = 0x000100, BtnB = 0x000200, BtnX = 0x000400, BtnY = 0x000800, BtnLb = 0x001000, BtnRb = 0x004000;
    private const int Up = 0x000010, Down = 0x000020, Left = 0x000040, Right = 0x000080, R3 = 0x000008, BtnStart = 0x400000;
    private const int LegionL = 0x000001, LegionR = 0x000002;

    public static void Start()
    {
        Load();
        new Thread(Loop) { IsBackground = true, Name = "DesktopMode", Priority = ThreadPriority.AboveNormal }.Start();
    }

    // ---------------------------------------------------------------------
    // Configuración
    // ---------------------------------------------------------------------

    public static JsonObject Get()
    {
        lock (Lock)
        {
            return new JsonObject
            {
                ["mode"] = _mode, ["pointerSpeed"] = _pointerSpeed, ["scrollSpeed"] = _scrollSpeed,
                ["active"] = _active, ["reason"] = _reason,
            };
        }
    }

    public static JsonObject Set(JsonObject args)
    {
        lock (Lock)
        {
            if (args["mode"]?.GetValue<string>() is string m && m is "off" or "auto" or "always") { _mode = m; _override = null; }
            if (args["pointerSpeed"] is JsonNode p) _pointerSpeed = Math.Clamp((int)p.GetValue<double>(), 1, 100);
            if (args["scrollSpeed"] is JsonNode s) _scrollSpeed = Math.Clamp((int)s.GetValue<double>(), 1, 100);
            Save();
        }
        return Get();
    }

    /// <summary>Acción de los botones Legion: lo enciende o lo apaga hasta que cambie la app que hay delante.</summary>
    public static bool Toggle()
    {
        lock (Lock)
        {
            _override = !_active;
            _overridePid = ForegroundPid();
            return _override.Value;
        }
    }

    private static void Load()
    {
        try
        {
            if (JsonNode.Parse(File.ReadAllText(FilePath)) is JsonObject o)
            {
                _mode = o["mode"]?.GetValue<string>() ?? _mode;
                _pointerSpeed = (int?)o["pointerSpeed"]?.GetValue<double>() ?? _pointerSpeed;
                _scrollSpeed = (int?)o["scrollSpeed"]?.GetValue<double>() ?? _scrollSpeed;
            }
        }
        catch (Exception) { /* sin archivo: valores por defecto */ }
    }

    private static void Save()
    {
        try
        {
            File.WriteAllText(FilePath, new JsonObject { ["mode"] = _mode, ["pointerSpeed"] = _pointerSpeed, ["scrollSpeed"] = _scrollSpeed }
                .ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception) { }
    }

    // ---------------------------------------------------------------------
    // Bucle: decidir si está activo y traducir el mando a ratón y teclado (≈125 veces por segundo)
    // ---------------------------------------------------------------------

    private static void Loop()
    {
        var input = new byte[33];
        int previous = 0;
        double restX = 0, restY = 0, restWheel = 0, restHWheel = 0;
        bool leftDown = false, rightDown = false;
        var nextCheck = DateTime.MinValue;
        var active = false;
        var keyboardOpen = false;
        while (true)
        {
            try
            {
                Thread.Sleep(8);
                if (DateTime.UtcNow >= nextCheck)
                {
                    active = Decide();
                    keyboardOpen = active && TouchKeyboard.IsVisible();
                    nextCheck = DateTime.UtcNow.AddMilliseconds(250);
                }
                if (!LegionButtons.Snapshot(input)) { active = false; }
                var buttons = input[1] | input[2] << 8 | input[3] << 16;

                // Con un botón Legion pulsado manda su atajo, no el modo escritorio.
                if (!active || (buttons & (LegionL | LegionR)) != 0)
                {
                    if (leftDown) { Mouse(0x0004); leftDown = false; }
                    if (rightDown) { Mouse(0x0010); rightDown = false; }
                    previous = buttons;
                    restX = restY = restWheel = restHWheel = 0;
                    continue;
                }

                int speed, scroll;
                lock (Lock) { speed = _pointerSpeed; scroll = _scrollSpeed; }

                // Ratón: stick derecho con zona muerta y curva (fino cerca del centro, rápido al fondo)
                var (mx, my) = Curve((sbyte)input[7], (sbyte)input[8]);
                var max = 4 + speed * 0.3;                       // píxeles por ciclo a fondo (≈ 0,9–4,2 mil por segundo)
                restX += mx * max;
                restY -= my * max;                               // el stick da + hacia arriba; la pantalla, + hacia abajo
                var dx = (int)restX; var dy = (int)restY;
                restX -= dx; restY -= dy;
                if (dx != 0 || dy != 0) MouseMove(dx, dy);

                // Rueda: stick izquierdo (vertical y horizontal)
                var (wx, wy) = Curve((sbyte)input[5], (sbyte)input[6]);
                restWheel += wy * scroll * 0.6;
                restHWheel += wx * scroll * 0.6;
                var wheel = (int)restWheel; var hwheel = (int)restHWheel;
                restWheel -= wheel; restHWheel -= hwheel;
                if (wheel != 0) Mouse(0x0800, wheel);
                if (hwheel != 0) Mouse(0x1000, hwheel);

                // Gatillos analógicos como clics (con histéresis para que no repiquen)
                var rt = input[14]; var lt = input[13];
                if (!leftDown && rt > 150) { Mouse(0x0002); leftDown = true; }
                else if (leftDown && rt < 90) { Mouse(0x0004); leftDown = false; }
                if (!rightDown && lt > 150) { Mouse(0x0008); rightDown = true; }
                else if (rightDown && lt < 90) { Mouse(0x0010); rightDown = false; }

                // Botones (al pulsar). Con el teclado en pantalla abierto se los queda él: ya se maneja con el mando y
                // cada pulsación llegaría dos veces (A escribía la tecla y además Intro).
                var pressed = buttons & ~previous;
                previous = buttons;
                if (pressed == 0) continue;
                if (keyboardOpen)
                {
                    if ((pressed & BtnStart) != 0) TouchKeyboard.Toggle();   // Start lo cierra (X puede ser «borrar» en él)
                    continue;
                }
                if ((pressed & BtnA) != 0) Keys(0x0D);                    // Intro
                if ((pressed & BtnB) != 0) Keys(0x1B);                    // Esc
                if ((pressed & BtnX) != 0) TouchKeyboard.Toggle();
                if ((pressed & BtnY) != 0) Keys(0x5B, 0x09);              // Win+Tab
                if ((pressed & BtnLb) != 0) Keys(0x11, 0x10, 0x09);       // Ctrl+Mayús+Tab
                if ((pressed & BtnRb) != 0) Keys(0x11, 0x09);             // Ctrl+Tab
                if ((pressed & Up) != 0) Keys(0x26);
                if ((pressed & Down) != 0) Keys(0x28);
                if ((pressed & Left) != 0) Keys(0x25);
                if ((pressed & Right) != 0) Keys(0x27);
                if ((pressed & R3) != 0) { Mouse(0x0020); Mouse(0x0040); } // clic central
            }
            catch (Exception)
            {
                Thread.Sleep(500);
            }
        }
    }

    /// <summary>Stick a [-1, 1] por eje con zona muerta (8 %) y curva cuadrática.</summary>
    private static (double X, double Y) Curve(sbyte x, sbyte y)
    {
        double nx = x / 127.0, ny = y / 127.0;
        var len = Math.Sqrt(nx * nx + ny * ny);
        if (len < 0.08) return (0, 0);
        var scaled = Math.Min(1, (len - 0.08) / 0.92);
        var factor = scaled * scaled / len;
        return (nx * factor, ny * factor);
    }

    /// <summary>¿Debe actuar ahora? Guarda también el motivo para mostrarlo en el widget.</summary>
    private static bool Decide()
    {
        string mode; bool? manual; int manualPid;
        lock (Lock) { mode = _mode; manual = _override; manualPid = _overridePid; }

        var pid = ForegroundPid();
        if (manual != null && pid != manualPid) { lock (Lock) _override = null; manual = null; }   // otra app delante: vuelve al modo

        bool active; string reason;
        if (IsGameBarOpen()) { active = false; reason = "Game Bar abierta"; }
        else if (manual == false) { active = false; reason = "Desactivado con el botón"; }
        else if (manual == true) { active = true; reason = "Activado con el botón"; }
        else if (mode == "off") { active = false; reason = ""; }
        else if (IsControllerApp(pid)) { active = false; reason = "App que ya usa el mando"; }
        else if (mode == "always") { active = true; reason = ""; }
        else if (pid != 0 && pid == ForegroundTracker.CurrentPid) { active = false; reason = "Juego delante"; }
        else { active = true; reason = "Sin juego delante"; }

        lock (Lock) { _active = active; _reason = reason; }
        return active;
    }

    private static bool IsControllerApp(int pid)
    {
        try { using var p = Process.GetProcessById(pid); return ControllerApps.Contains(p.ProcessName); }
        catch (Exception) { return false; }
    }

    private static bool IsGameBarOpen()
    {
        try { return Windows.Gaming.UI.GameBar.IsInputRedirected; }
        catch (Exception) { return false; }
    }

    private static int ForegroundPid()
    {
        GetWindowThreadProcessId(GetForegroundWindow(), out var pid);
        return (int)pid;
    }

    // ---------------------------------------------------------------------
    // SendInput
    // ---------------------------------------------------------------------

    private static void MouseMove(int dx, int dy) => Send(new INPUT { type = 0, u = new InputUnion { mi = new MOUSEINPUT { dx = dx, dy = dy, dwFlags = 0x0001 } } });

    private static void Mouse(uint flags, int data = 0) =>
        Send(new INPUT { type = 0, u = new InputUnion { mi = new MOUSEINPUT { dwFlags = flags, mouseData = unchecked((uint)data) } } });

    private static void Keys(params ushort[] keys)
    {
        var inputs = new List<INPUT>();
        foreach (var k in keys) inputs.Add(new INPUT { type = 1, u = new InputUnion { ki = new KEYBDINPUT { wVk = k } } });
        foreach (var k in keys.Reverse()) inputs.Add(new INPUT { type = 1, u = new InputUnion { ki = new KEYBDINPUT { wVk = k, dwFlags = 2 } } });
        SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<INPUT>());
    }

    private static void Send(INPUT input) => SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());

    [StructLayout(LayoutKind.Sequential)] private struct INPUT { public uint type; public InputUnion u; }
    [StructLayout(LayoutKind.Explicit)] private struct InputUnion { [FieldOffset(0)] public KEYBDINPUT ki; [FieldOffset(0)] public MOUSEINPUT mi; }
    [StructLayout(LayoutKind.Sequential)] private struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] private struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }
    [DllImport("user32.dll")] private static extern uint SendInput(uint count, INPUT[] inputs, int size);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
}
