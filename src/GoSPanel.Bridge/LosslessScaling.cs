using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using System.Xml.Linq;

namespace GoSPanel.Bridge;

/// <summary>
/// Lossless Scaling no tiene API: se activa con su atajo de teclado sobre la ventana del juego. El atajo se lee
/// de su Settings.xml (por defecto Ctrl+Alt+S); antes de enviarlo se pone el juego en primer plano.
/// </summary>
internal static class LosslessScaling
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lossless Scaling", "Settings.xml");
    private const string SteamExe = @"C:\Program Files (x86)\Steam\steamapps\common\Lossless Scaling\LosslessScaling.exe";

    // Tras pulsar el botón, el atajo se envía cuando el usuario vuelve al juego (cierra Game Bar). Mientras tanto
    // el estado es «pendiente»; 3 s después de que el juego recupere el primer plano se muestra el estado real.
    private static DateTime _pendingUntil;
    private static int _pendingPid;
    private static DateTime? _gameBackAt;

    public static bool Installed => File.Exists(SteamExe) || Process.GetProcessesByName("LosslessScaling").Length > 0;

    /// <summary>
    /// Estado real: al escalar, Lossless crea ventanas a pantalla completa (sin escalar solo tiene su ventana
    /// principal, más pequeña). Se mira eso en lugar de recordar lo último que se hizo.
    /// </summary>
    public static bool IsScaling
    {
        get
        {
            var pids = Process.GetProcessesByName("LosslessScaling").Select(p => p.Id).ToHashSet();
            if (pids.Count == 0) return false;
            int screenW = GetSystemMetrics(0), screenH = GetSystemMetrics(1);
            var fullscreen = false;
            EnumWindows((hwnd, _) =>
            {
                GetWindowThreadProcessId(hwnd, out var pid);
                // La pantalla de escalado coincide exactamente con el monitor desde (0,0). La ventana principal
                // maximizada también «ocupa» la pantalla, pero Windows la coloca en (-7,-7) y algo más grande.
                if (pids.Contains((int)pid) && IsWindowVisible(hwnd) && !IsCloaked(hwnd) && GetWindowRect(hwnd, out var r)
                    && r.Left == 0 && r.Top == 0 && r.Right == screenW && r.Bottom == screenH)
                    fullscreen = true;
                return !fullscreen;
            }, IntPtr.Zero);
            return fullscreen;
        }
    }

    public static JsonObject Get()
    {
        var active = IsScaling;
        if (DateTime.UtcNow < _pendingUntil)
        {
            GetWindowThreadProcessId(GetForegroundWindow(), out var foreground);
            if (foreground == _pendingPid)
            {
                _gameBackAt ??= DateTime.UtcNow;
                if (DateTime.UtcNow - _gameBackAt > TimeSpan.FromSeconds(3)) _pendingUntil = DateTime.MinValue;
            }
            else
            {
                _gameBackAt = null;
            }
        }
        var pending = DateTime.UtcNow < _pendingUntil;
        var (modifiers, key) = ReadHotkey();
        return new JsonObject
        {
            ["installed"] = Installed,
            ["active"] = active,
            ["pending"] = pending,
            ["hotkey"] = string.Join("+", modifiers.Select(ModifierName).Append(key.ToString())),
        };
    }

    /// <summary>
    /// Desde Game Bar el juego no está en primer plano y puede ejecutarse como administrador, así que el atajo
    /// lo envía un ayudante elevado del servicio cuando el usuario vuelve al juego (cierra Game Bar).
    /// </summary>
    public static async Task<JsonObject> ToggleAsync()
    {
        var pid = ForegroundTracker.CurrentPid;
        if (pid == 0) throw new InvalidOperationException("No hay ningún juego detectado");
        if (!Installed) throw new InvalidOperationException("Lossless Scaling no está instalado");
        _pendingPid = pid;
        _gameBackAt = null;
        _pendingUntil = DateTime.UtcNow.AddSeconds(30);
        await Program.CallServiceAsync("ls.launch", new JsonObject { ["pid"] = pid });
        return Get();
    }

    /// <summary>Activa el escalado sobre un juego (usado por los perfiles). No hace nada si ya está activo.</summary>
    public static async Task EnsureActiveAsync(int pid)
    {
        if (IsScaling) return;
        await Program.CallServiceAsync("ls.launch", new JsonObject { ["pid"] = pid });
    }


    /// <summary>Atajo configurado en Lossless Scaling: &lt;Hotkey&gt;S&lt;/Hotkey&gt; y &lt;HotkeyModifierKeys&gt;Alt Control&lt;/...&gt;.</summary>
    private static (byte[] Modifiers, byte Key) ReadHotkey()
    {
        string keyName = "S", modifierNames = "Alt Control";
        try
        {
            var xml = XDocument.Load(SettingsPath);
            keyName = xml.Descendants("Hotkey").FirstOrDefault()?.Value ?? keyName;
            modifierNames = xml.Descendants("HotkeyModifierKeys").FirstOrDefault()?.Value ?? modifierNames;
        }
        catch (Exception) { /* sin configuración: atajo por defecto */ }

        var modifiers = new List<byte>();
        foreach (var part in modifierNames.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries))
        {
            switch (part.ToLowerInvariant())
            {
                case "control": modifiers.Add(VkControl); break;
                case "alt": modifiers.Add(VkMenu); break;
                case "shift": modifiers.Add(VkShift); break;
                case "windows": modifiers.Add(VkLWin); break;
            }
        }
        // Orden habitual al pulsar: Ctrl, Alt, Shift.
        modifiers.Sort((a, b) => Order(a).CompareTo(Order(b)));
        var key = keyName.Length == 1 ? (byte)char.ToUpperInvariant(keyName[0])
            : Enum.TryParse<ConsoleKey>(keyName, true, out var ck) ? (byte)ck : (byte)'S';
        return (modifiers.ToArray(), key);
    }

    private static int Order(byte vk) => vk switch { VkControl => 0, VkMenu => 1, VkShift => 2, _ => 3 };
    private static string ModifierName(byte vk) => vk switch { VkControl => "Ctrl", VkMenu => "Alt", VkShift => "Shift", _ => "Win" };

    private const byte VkShift = 0x10, VkControl = 0x11, VkMenu = 0x12, VkLWin = 0x5B;

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr param);
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr param);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out int value, int size);

    private static bool IsCloaked(IntPtr hwnd) => DwmGetWindowAttribute(hwnd, 14, out var cloaked, 4) == 0 && cloaked != 0;   // DWMWA_CLOAKED
}
