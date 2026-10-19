using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Win32.SafeHandles;
using Windows.Storage;

namespace GoSPanel.Bridge;

/// <summary>
/// Botones Legion L / Legion R y sus combinaciones. El mando de la Go S (1A86:E310) los envía solo por su
/// interfaz HID de Lenovo MI_06 (página 0xFFA0, informe de 33 bytes), que trae además el resto de botones:
///   byte 1: 01 Legion L · 02 Legion R · 04 L3 · 08 R3 · 10 arriba · 20 abajo · 40 izquierda · 80 derecha
///   byte 2: 01 A · 02 B · 04 X · 08 Y · 10 LB · 20 LT · 40 RB · 80 RT
///   byte 3: 01 trasero izquierdo · 02 trasero derecho · 40 Start
/// Windows no hace nada con Legion L/R; aquí se convierten en acciones configurables (buttons.json).
/// Pulsación corta, pulsación larga (≥ 0,6 s) y Legion + otro botón.
/// </summary>
internal static class LegionButtons
{
    private const int LegionL = 0x000001, LegionR = 0x000002;
    private static readonly TimeSpan LongPress = TimeSpan.FromMilliseconds(600);

    /// <summary>Botones que se pueden combinar con Legion L/R (clave en la configuración, máscara).</summary>
    public static readonly (string Key, string Name, int Mask)[] ComboButtons =
    {
        ("A", "A", 0x000100), ("B", "B", 0x000200), ("X", "X", 0x000400), ("Y", "Y", 0x000800),
        ("LB", "LB", 0x001000), ("RB", "RB", 0x004000), ("LT", "LT", 0x002000), ("RT", "RT", 0x008000),
        ("Up", "Arriba", 0x000010), ("Down", "Abajo", 0x000020), ("Left", "Izquierda", 0x000040), ("Right", "Derecha", 0x000080),
        ("Start", "Start", 0x400000), ("L3", "L3", 0x000004), ("R3", "R3", 0x000008),
    };

    /// <summary>Acciones disponibles (id, nombre visible).</summary>
    public static readonly (string Id, string Name)[] Actions =
    {
        ("none", "Nada"),
        ("gamebar", "Game Bar"),
        ("gopilot", "GoPilot"),
        ("xboxhome", "Inicio de Xbox"),
        ("taskview", "Vista de tareas"),
        ("desktop", "Escritorio"),
        ("keyboard", "Teclado"),
        ("snip", "Recortes"),
        ("screenshot", "Captura"),
        ("record", "Grabar"),
        ("thermal", "Modo térmico"),
        ("sas", "Ctrl+Alt+Supr"),
        ("taskmgr", "Admin. de tareas"),
        ("lossless", "Lossless"),
        ("overlay", "Overlay"),
        ("killgame", "Cerrar juego"),
    };

    // Valores por defecto: Legion L abre la app Xbox (mantenido, Game Bar), Legion R abre GoPilot y Legion R
    // mantenido, la vista de tareas. (Probado: Game Bar en su Inicio necesitaba simular ↑ en la cruceta y a veces
    // bloqueaba la navegación; descartado.)
    private static readonly Dictionary<string, string> Defaults = new()
    {
        ["L"] = "xboxhome", ["L.long"] = "gamebar", ["R"] = "gopilot", ["R.long"] = "taskview",
    };

    /// <summary>
    /// Atajos que resuelve el propio firmware del mando aunque Legion Space esté desactivado (L+Y modo térmico,
    /// L+X recortes, L+A Ctrl+Alt+Supr, L+B teclado, L+LB): no se pueden cambiar, así que no se ofrecen y GoPilot
    /// no hace nada con ellos (salvo mostrar el aviso del modo con L+Y).
    /// </summary>
    public static readonly HashSet<string> FirmwareCombos = new() { "L+Y", "L+X", "L+A", "L+B", "L+LB" };

    private static string FilePath => Path.Combine(ApplicationData.Current.LocalFolder.Path, "buttons.json");
    private static readonly object Lock = new();
    private static bool _enabled = true;
    private static Dictionary<string, string> _bindings = new(Defaults);
    private static DateTime _lastReport;

    public static void Start()
    {
        Load();
        new Thread(ReadLoop) { IsBackground = true, Name = "LegionButtons" }.Start();
    }

    // ---------------------------------------------------------------------
    // Configuración
    // ---------------------------------------------------------------------

    public static JsonObject Get()
    {
        lock (Lock)
        {
            var bindings = new JsonObject();
            foreach (var (k, v) in _bindings) bindings[k] = v;
            var actions = new JsonArray();
            foreach (var (id, name) in Actions) actions.Add(new JsonObject { ["id"] = id, ["name"] = name });
            var combos = new JsonArray();
            foreach (var (key, name, _) in ComboButtons) combos.Add(new JsonObject { ["key"] = key, ["name"] = name });
            return new JsonObject
            {
                ["enabled"] = _enabled,
                ["bindings"] = bindings,
                ["actions"] = actions,
                ["combos"] = combos,
                ["controller"] = (DateTime.Now - _lastReport).TotalSeconds < 10 || FindDevice() != null,
                ["legionSpace"] = Process.GetProcessesByName("LegionSpace").Length > 0 || Process.GetProcessesByName("LSDaemon").Length > 0,
            };
        }
    }

    /// <summary>args: enabled (bool) y/o binding ("L", "R", "R.long", "L+A"...) con action (id o "none").</summary>
    public static JsonObject Set(JsonObject args)
    {
        lock (Lock)
        {
            if (args["enabled"] is JsonValue e) _enabled = e.GetValue<bool>();
            if (args["binding"]?.GetValue<string>() is { } key && args["action"]?.GetValue<string>() is { } action)
            {
                if (FirmwareCombos.Contains(key)) throw new ArgumentException("Ese atajo lo controla el firmware del mando");
                if (!Actions.Any(a => a.Id == action)) throw new ArgumentException("Acción desconocida");
                if (action == "none") _bindings.Remove(key); else _bindings[key] = action;
            }
            if (args["reset"] is JsonValue r && r.GetValue<bool>()) _bindings = new(Defaults);
            Save();
        }
        return Get();
    }

    private static void Load()
    {
        try
        {
            if (JsonNode.Parse(File.ReadAllText(FilePath)) is JsonObject o)
            {
                _enabled = o["enabled"]?.GetValue<bool>() ?? true;
                if (o["bindings"] is JsonObject b)
                    _bindings = b.Where(kv => kv.Value != null && !FirmwareCombos.Contains(kv.Key))
                                 .ToDictionary(kv => kv.Key, kv => kv.Value!.GetValue<string>());
            }
        }
        catch (Exception) { /* sin archivo: atajos por defecto */ }
    }

    private static void Save()
    {
        var bindings = new JsonObject();
        foreach (var (k, v) in _bindings) bindings[k] = v;
        File.WriteAllText(FilePath, new JsonObject { ["enabled"] = _enabled, ["bindings"] = bindings }
            .ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    // ---------------------------------------------------------------------
    // Lectura del mando
    // ---------------------------------------------------------------------

    private static void ReadLoop()
    {
        while (true)
        {
            try
            {
                var path = FindDevice();
                if (path == null) { Thread.Sleep(3000); continue; }
                using var handle = CreateFile(path, 0x80000000 /*GENERIC_READ*/, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
                if (handle.IsInvalid) { Thread.Sleep(3000); continue; }
                using var stream = new FileStream(handle, FileAccess.Read, 0, false);
                var buffer = new byte[64];
                var tracker = new PressTracker();
                while (true)
                {
                    var n = stream.Read(buffer, 0, buffer.Length);
                    if (n < 4) continue;
                    _lastReport = DateTime.Now;
                    lock (InputLock) Array.Copy(buffer, LastInput, Math.Min(n, LastInput.Length));
                    tracker.Update(buffer[1] | buffer[2] << 8 | buffer[3] << 16);
                }
            }
            catch (Exception)
            {
                Thread.Sleep(2000);   // mando desconectado o suspensión: se vuelve a buscar
            }
        }
    }

    /// <summary>Sigue un botón Legion desde que se pulsa hasta que se suelta.</summary>
    private sealed class PressTracker
    {
        private int _previous;
        private bool _primed;
        private readonly DateTime _created = DateTime.Now;
        private string? _held;         // "L" o "R"
        private DateTime _since;
        private bool _comboFired, _longFired;
        private Timer? _longTimer;

        public void Update(int buttons)
        {
            // El primer informe solo fija el estado (al arrancar el mando puede llegar con bits marcados) y durante
            // los primeros segundos tras abrir el mando no se dispara nada: evita «pulsaciones» fantasma al encender.
            if (!_primed || (DateTime.Now - _created).TotalSeconds < 5)
            {
                _primed = true;
                _previous = buttons;
                return;
            }
            var pressed = buttons & ~_previous;
            _previous = buttons;

            if (_held == null)
            {
                var legion = (pressed & LegionL) != 0 ? "L" : (pressed & LegionR) != 0 ? "R" : null;
                if (legion == null) return;
                _held = legion;
                _since = DateTime.Now;
                _comboFired = _longFired = false;
                // La pulsación larga se dispara al cumplirse el tiempo, sin esperar a soltar.
                var key = legion + ".long";
                _longTimer = new Timer(_ =>
                {
                    if (_held == legion && !_comboFired && Binding(key) != null) { _longFired = true; Fire(key); }
                }, null, LongPress, Timeout.InfiniteTimeSpan);
                return;
            }

            var mask = _held == "L" ? LegionL : LegionR;
            foreach (var (key, _, bit) in ComboButtons)
                if ((pressed & bit) != 0)
                {
                    _comboFired = true;
                    Fire(_held + "+" + key);
                }

            if ((buttons & mask) == 0)   // soltado
            {
                _longTimer?.Dispose();
                if (!_comboFired && !_longFired) Fire(_held);
                _held = null;
            }
        }
    }

    // Último informe del mando, para el modo de prueba del widget:
    //   5-6 stick izquierdo X/Y · 7-8 stick derecho X/Y (con signo) · 13-14 gatillos (0-255) · 16/18/20 giroscopio
    private static readonly object InputLock = new();
    private static readonly byte[] LastInput = new byte[33];

    public static JsonObject Input()
    {
        lock (InputLock)
        {
            return new JsonObject
            {
                ["fresh"] = (DateTime.Now - _lastReport).TotalSeconds < 1,
                ["lx"] = (sbyte)LastInput[5], ["ly"] = (sbyte)LastInput[6],
                ["rx"] = (sbyte)LastInput[7], ["ry"] = (sbyte)LastInput[8],
                ["lt"] = LastInput[13], ["rt"] = LastInput[14],
                ["gx"] = (sbyte)LastInput[16], ["gy"] = (sbyte)LastInput[18], ["gz"] = (sbyte)LastInput[20],
            };
        }
    }

    private static string? Binding(string key)
    {
        lock (Lock) return _enabled && _bindings.TryGetValue(key, out var a) ? a : null;
    }

    private static void Fire(string key)
    {
        if (key == "L+Y")   // el firmware ya ha cambiado el modo: solo se enseña cuál ha quedado
        {
            _ = Task.Run(async () =>
            {
                try { await Task.Delay(400); await ShowModeToastAsync(); }
                catch (Exception) { }
            });
            return;
        }
        if (FirmwareCombos.Contains(key)) return;
        var action = Binding(key);
        if (action == null) return;
        _ = Task.Run(async () =>
        {
            try { await RunAsync(action, key); }
            catch (Exception) { /* una acción fallida no debe parar la lectura del mando */ }
        });
    }

    // ---------------------------------------------------------------------
    // Acciones
    // ---------------------------------------------------------------------

    private const string GoPilotWidgetId = "GoSPanel_3ncfs0ed3kzx8_App_GoSPanel";

    public static async Task RunAsync(string action, string? key = null)
    {
        switch (action)
        {
            case "gamebar": Launch("ms-gamebar://toggleForeground"); break;
            case "gopilot": Launch("ms-gamebar://toggleForeground/activate/" + GoPilotWidgetId); break;
            // App Xbox (la pantalla principal en modo pantalla completa). Se abre por su AUMID y no con «msxbox://»:
            // un enlace sin destino hace que la app muestre «Actualizar aplicación».
            case "xboxhome": Process.Start("explorer.exe", @"shell:AppsFolder\Microsoft.GamingApp_8wekyb3d8bbwe!Microsoft.Xbox.App"); break;
            case "taskview": Keys(VkLWin, VkTab); break;
            case "desktop": Keys(VkLWin, 0x44 /*D*/); break;
            case "keyboard": TouchKeyboard.Toggle(); break;
            case "snip": Launch("ms-screenclip:"); break;
            case "screenshot": Keys(VkLWin, VkMenu, 0x2C /*Impr Pant*/); break;
            case "record": Keys(VkLWin, VkMenu, 0x52 /*R*/); break;
            case "taskmgr": Process.Start(new ProcessStartInfo("taskmgr.exe") { UseShellExecute = true }); break;
            case "sas": await Program.CallServiceAsync("sas"); break;
            case "lossless":
                await LosslessScaling.ToggleAsync();
                GameOverlay.ShowToast("Lossless", "Escalado", '');
                break;
            case "overlay":
                var on = !(GameOverlay.Get()["enabled"]?.GetValue<bool>() ?? false);
                GameOverlay.Set(new JsonObject { ["enabled"] = on });
                GameOverlay.ShowToast("Overlay", on ? "Activado" : "Desactivado", '');
                break;
            case "killgame": ForegroundTracker.Kill(); break;
            case "thermal":
                // Legion L + Y ya lo atiende el firmware (cambia de modo él solo): aquí solo se muestra el aviso.
                // En cualquier otra combinación lo cambia GoPilot.
                if (key != "L+Y")
                {
                    var current = (await Program.CallServiceAsync("power.get"))["mode"]?.GetValue<string>();
                    var next = current switch { "quiet" => "balanced", "balanced" => "performance", "performance" => "custom", _ => "quiet" };
                    await Program.CallServiceAsync("power.set", new JsonObject { ["mode"] = next });
                }
                else await Task.Delay(400);
                await ShowModeToastAsync();
                break;
        }
    }

    private static async Task ShowModeToastAsync()
    {
        var power = await Program.CallServiceAsync("power.get");
        var mode = power["mode"]?.GetValue<string>();
        // Vatios reales del modo (los que aplica el firmware, enchufada o con batería)
        var watts = mode != null && power["limits"]?[mode]?["spl"] is JsonNode modeSpl ? $"{modeSpl.GetValue<double>():0} W" : null;
        switch (mode)
        {
            case "quiet": GameOverlay.ShowToast("Silencio", watts, ''); break;
            case "balanced": GameOverlay.ShowToast("Equilibrado", watts, ''); break;
            case "performance": GameOverlay.ShowToast("Rendimiento", watts, ''); break;
            case "custom":
                var spl = (await Program.CallServiceAsync("tdp.get"))["spl"]?.GetValue<double>();
                GameOverlay.ShowToast("Manual", spl.HasValue ? $"{spl:0} W" : null, '');
                break;
        }
    }

    private static void Launch(string uri) => Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });

    private const ushort VkLWin = 0x5B, VkTab = 0x09, VkMenu = 0x12;

    /// <summary>Pulsa las teclas en orden y las suelta en orden inverso.</summary>
    private static void Keys(params ushort[] keys)
    {
        var inputs = new List<INPUT>();
        foreach (var k in keys) inputs.Add(Key(k, false));
        foreach (var k in keys.Reverse()) inputs.Add(Key(k, true));
        SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<INPUT>());
    }

    private static INPUT Key(ushort vk, bool up) => new()
    {
        type = 1,
        u = new InputUnion { ki = new KEYBDINPUT { wVk = vk, dwFlags = up ? 2u : 0u } },
    };

    [StructLayout(LayoutKind.Sequential)] private struct INPUT { public uint type; public InputUnion u; }
    [StructLayout(LayoutKind.Explicit)] private struct InputUnion { [FieldOffset(0)] public KEYBDINPUT ki; [FieldOffset(0)] public MOUSEINPUT mi; }
    [StructLayout(LayoutKind.Sequential)] private struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] private struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }
    [DllImport("user32.dll")] private static extern uint SendInput(uint count, INPUT[] inputs, int size);

    // ---------------------------------------------------------------------
    // Búsqueda de la interfaz MI_06 del mando
    // ---------------------------------------------------------------------

    private static string? FindDevice()
    {
        HidD_GetHidGuid(out var guid);
        var set = SetupDiGetClassDevs(ref guid, IntPtr.Zero, IntPtr.Zero, 0x12 /*PRESENT | DEVICEINTERFACE*/);
        try
        {
            for (uint i = 0; ; i++)
            {
                var data = new SpDeviceInterfaceData { cbSize = Marshal.SizeOf<SpDeviceInterfaceData>() };
                if (!SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref guid, i, ref data)) return null;
                SetupDiGetDeviceInterfaceDetail(set, ref data, IntPtr.Zero, 0, out var size, IntPtr.Zero);
                var detail = Marshal.AllocHGlobal((int)size);
                try
                {
                    Marshal.WriteInt32(detail, 8);
                    if (!SetupDiGetDeviceInterfaceDetail(set, ref data, detail, size, out _, IntPtr.Zero)) continue;
                    var path = Marshal.PtrToStringUni(detail + 4)!;
                    if (path.Contains("vid_1a86&pid_e310&mi_06", StringComparison.OrdinalIgnoreCase)
                        || path.Contains("vid_1a86&pid_e311&mi_06", StringComparison.OrdinalIgnoreCase))
                        return path;
                }
                finally
                {
                    Marshal.FreeHGlobal(detail);
                }
            }
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(set);
        }
    }

    [StructLayout(LayoutKind.Sequential)] private struct SpDeviceInterfaceData { public int cbSize; public Guid InterfaceClassGuid; public int Flags; public IntPtr Reserved; }
    [DllImport("hid.dll")] private static extern void HidD_GetHidGuid(out Guid guid);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode)] private static extern IntPtr SetupDiGetClassDevs(ref Guid classGuid, IntPtr enumerator, IntPtr parent, int flags);
    [DllImport("setupapi.dll")] private static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr devInfo, ref Guid classGuid, uint index, ref SpDeviceInterfaceData data);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode)] private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref SpDeviceInterfaceData data, IntPtr detail, uint detailSize, out uint requiredSize, IntPtr devInfo);
    [DllImport("setupapi.dll")] private static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
}
