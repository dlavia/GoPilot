using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;

namespace GoSPanel.Bridge;

/// <summary>
/// Recuerda el último juego que estuvo en primer plano, ignorando Game Bar, el escritorio, las consolas y los
/// programas que no son juegos (ver <see cref="IsGame"/>). Así, al abrir el widget, se sabe sobre qué juego actuar
/// y no se ofrece crear perfiles para cmd, el navegador, etc.
/// </summary>
internal static class ForegroundTracker
{
    private static readonly HashSet<string> Ignored = new(StringComparer.OrdinalIgnoreCase)
    {
        "explorer", "GameBar", "GameBarFTServer", "GameBarPresenceWriter", "GoSPanel.Widget", "GoSPanel.Bridge",
        "ShellExperienceHost", "StartMenuExperienceHost", "SearchHost", "SearchApp", "TextInputHost", "LockApp",
        "SystemSettings", "ApplicationFrameHost", "Taskmgr", "LegionSpace", "LSDaemon", "LegionSettingMenu",
        "LegionGoGameBarWidget", "WindowsTerminal", "OpenConsole", "conhost", "TabTip", "osk", "ShellHost",
        "XboxPcApp", "XboxPcAppFT",
        // Lanzadores y tiendas: no son el juego (al cerrarlo, su ventana pasa a primer plano).
        "steam", "steamwebhelper", "steamservice", "EpicGamesLauncher", "EpicWebHelper", "Battle.net", "Agent",
        "EADesktop", "EABackgroundService", "Origin", "GalaxyClient", "GalaxyClientService", "upc", "UbisoftConnect",
        "RiotClientServices", "RiotClientUx", "LeagueClient", "LeagueClientUx", "Playnite.DesktopApp",
        "Playnite.FullscreenApp", "GOG Galaxy", "AmazonGames", "HeroicGamesLauncher",
        // Utilidades que viven en carpetas de juegos (Steam) pero no lo son.
        "LosslessScaling", "Lossless Scaling",
        // Apps de uso normal: en la consola suelen ir maximizadas a toda la pantalla y no son juegos.
        "msedge", "msedgewebview2", "chrome", "firefox", "brave", "opera", "opera_gx", "vivaldi", "arc",
        "Discord", "Spotify", "Telegram", "WhatsApp", "Teams", "ms-teams", "slack", "Zoom",
        "WINWORD", "EXCEL", "POWERPNT", "OUTLOOK", "olk", "ONENOTE", "Acrobat", "AcroRd32", "notepad++", "Code",
        "vlc", "mpc-hc64", "mpc-be64", "PotPlayerMini64", "obs64", "PowerToys", "PhotosApp", "Microsoft.Photos",
        "AIDA64", "HWiNFO64", "MSIAfterburner", "RTSS", "Playnite.DesktopApp", "SnippingTool", "ScreenClippingHost",
        "WindowsCamera", "Calculator", "CalculatorApp", "Notepad", "mspaint", "Paint", "ms-settings",
    };

    /// <summary>¿Es una app que nunca cuenta como juego? («juego.exe» o nombre del proceso)</summary>
    public static bool IsIgnored(string exeOrName) =>
        Ignored.Contains(exeOrName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? exeOrName[..^4] : exeOrName);

    // Bibliotecas de juegos: lo que se ejecuta desde aquí cuenta como juego.
    private static readonly string[] GameFolders =
    {
        @"\steamapps\common\", @"\Epic Games\", @"\XboxGames\", @"\GOG Games\", @"\GOG Galaxy\Games\", @"\EA Games\",
        @"\Ubisoft Game Launcher\games\", @"\Riot Games\", @"\Amazon Games\Library\", @"\Heroic\", @"\itch\apps\", @"\Games\",
    };

    private static readonly object Lock = new();
    private static Timer? _timer;
    private static Tracked? _current;

    // Library: reconocido por estar en una biblioteca de juegos o tener perfil (no solo por ocupar la pantalla)
    private sealed record Tracked(int Pid, DateTime StartTime, string Name, string ProcessName, string? Path, bool Library);

    /// <summary>¿El juego actual se reconoció por su biblioteca (Steam, Epic…) o por tener perfil?</summary>
    public static bool CurrentIsLibrary
    {
        get { lock (Lock) return _current is { } c && IsAlive(c) && c.Library; }
    }

    public static void Start() => _timer ??= new Timer(_ => Sample(), null, TimeSpan.Zero, TimeSpan.FromSeconds(1));

    /// <summary>Juego actual (pid, ejecutable en minúsculas con .exe, nombre visible) o null si no hay.</summary>
    public static (int Pid, string Exe, string Name)? Current
    {
        get
        {
            lock (Lock)
            {
                if (_current is null || !IsAlive(_current)) { _current = null; return null; }
                return (_current.Pid, _current.ProcessName.ToLowerInvariant() + ".exe", _current.Name);
            }
        }
    }

    public static int CurrentPid => Current?.Pid ?? 0;

    public static JsonObject Get()
    {
        lock (Lock)
        {
            if (_current is null || !IsAlive(_current)) _current = null;
            return _current is null
                ? new JsonObject { ["active"] = false }
                : new JsonObject
                {
                    ["active"] = true, ["pid"] = _current.Pid, ["name"] = _current.Name,
                    ["process"] = _current.ProcessName, ["path"] = _current.Path,
                };
        }
    }

    /// <summary>
    /// Cierra la aplicación recordada como lo haría Windows: primero le pide que se cierre (como pulsar la X),
    /// para que Steam y los lanzadores se enteren y liberen el mando; solo si no responde en 3 s se fuerza.
    /// </summary>
    public static JsonObject Kill()
    {
        Tracked target;
        lock (Lock)
        {
            target = _current ?? throw new InvalidOperationException("No hay ningún juego detectado");
        }
        using var process = Process.GetProcessById(target.Pid);
        if (process.StartTime != target.StartTime) throw new InvalidOperationException("El juego ya se había cerrado");

        var forced = false;
        if (!process.CloseMainWindow() || !process.WaitForExit(3000))
        {
            try
            {
                process.Kill();
                forced = true;
            }
            catch (System.ComponentModel.Win32Exception)
            {
                throw new InvalidOperationException($"No se pudo cerrar {target.Name} (¿se ejecuta como administrador?)");
            }
        }
        lock (Lock) _current = null;
        return new JsonObject { ["closed"] = target.Name, ["forced"] = forced };
    }

    private static void Sample()
    {
        try
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero || !IsWindowVisible(hwnd)) return;
            GetWindowThreadProcessId(hwnd, out var pid);
            if (pid == 0 || pid == Environment.ProcessId) return;

            using var process = Process.GetProcessById((int)pid);
            if (Ignored.Contains(process.ProcessName) || process.SessionId == 0) return;
            lock (Lock) if (_current?.Pid == process.Id) return;

            // Ruta con QueryFullProcessImageName (solo pide permiso de consulta): MainModule necesita leer la memoria
            // del proceso y los antitrampas (Call of Duty, por ejemplo) se lo niegan al agente.
            var path = ImagePath(process.Id);
            string name = process.ProcessName;
            try
            {
                var description = path is null ? null : FileVersionInfo.GetVersionInfo(path).FileDescription;
                if (!string.IsNullOrWhiteSpace(description)) name = description.Trim();
            }
            catch (Exception) { /* sin información de versión: basta el nombre del proceso */ }

            // Lo que no es un juego no sustituye al juego recordado (que puede seguir abierto detrás).
            var kind = GameKind(process.ProcessName, path, hwnd);
            if (kind == 0)
            {
                LogOnce(process.Id, $"no es juego: {process.ProcessName} ({path ?? "ruta no legible"})");
                return;
            }
            lock (Lock) _current = new Tracked(process.Id, process.StartTime, name, process.ProcessName, path, kind == 2);
            LogOnce(process.Id, $"juego: {name} ({path})");
        }
        catch (Exception ex)
        {
            // El proceso pudo cerrarse entre llamadas; se reintenta en la siguiente muestra.
            LogOnce(ex.Message.GetHashCode() | int.MinValue, "error: " + ex.Message);   // una vez por mensaje
        }
    }

    // Diagnóstico de la detección (LocalStateoreground.log): cada proceso se anota una sola vez.
    private static readonly HashSet<int> Logged = new();

    private static void LogOnce(int pid, string line)
    {
        try
        {
            lock (Logged)
            {
                if (!Logged.Add(pid)) return;
                if (Logged.Count > 500) Logged.Clear();
            }
            var file = Path.Combine(Windows.Storage.ApplicationData.Current.LocalFolder.Path, "foreground.log");
            if (File.Exists(file) && new FileInfo(file).Length > 64 * 1024) File.Delete(file);
            File.AppendAllText(file, $"{DateTime.Now:HH:mm:ss} {line}{Environment.NewLine}");
        }
        catch (Exception) { }
    }

    /// <summary>
    /// ¿Es un juego? Sí si tiene perfil guardado, si se ejecuta desde una biblioteca de juegos (Steam, Epic, Xbox…)
    /// o si su ventana ocupa toda la pantalla (emuladores, juegos sueltos). Nunca lo de Windows ni las consolas.
    /// </summary>
    /// <summary>
    /// ¿Es un juego? 2 = sí, por tener perfil o ejecutarse desde una biblioteca (Steam, Epic, Xbox…); 1 = sí, porque su
    /// ventana ocupa toda la pantalla sin marco (emuladores, juegos sueltos); 0 = no (Windows, consolas, apps de la
    /// lista Ignored, o ventanas maximizadas normales: en una consola casi todo va maximizado).
    /// </summary>
    private static int GameKind(string processName, string? path, IntPtr hwnd)
    {
        if (ProfileManager.HasProfile(processName.ToLowerInvariant() + ".exe")) return 2;
        if (IsConsole(hwnd)) return 0;
        if (path != null)
        {
            var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows) + @"\";
            if (path.StartsWith(windows, StringComparison.OrdinalIgnoreCase)) return 0;   // cmd, notepad, mmc…
            if (GameFolders.Any(f => path.Contains(f, StringComparison.OrdinalIgnoreCase))) return 2;
        }
        // Sin ruta legible (proceso protegido) o fuera de una biblioteca: pantalla completa sin marco
        return CoversMonitor(hwnd) && !HasFrame(hwnd) ? 1 : 0;
    }

    /// <summary>Ventana con barra de título o borde redimensionable (una app maximizada, no un juego).</summary>
    private static bool HasFrame(IntPtr hwnd) => (GetWindowLong(hwnd, -16 /*GWL_STYLE*/) & (0x00C00000 /*WS_CAPTION*/ | 0x00040000 /*WS_THICKFRAME*/)) != 0;
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int index);

    private static bool IsConsole(IntPtr hwnd)
    {
        var cls = new System.Text.StringBuilder(64);
        GetClassName(hwnd, cls, cls.Capacity);
        var name = cls.ToString();
        return name is "ConsoleWindowClass" or "CASCADIA_HOSTING_WINDOW_CLASS" or "PseudoConsoleWindow";
    }

    /// <summary>Pantalla completa o ventana sin bordes del tamaño del monitor.</summary>
    private static bool CoversMonitor(IntPtr hwnd)
    {
        if (!GetWindowRect(hwnd, out var r)) return false;
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(MonitorFromWindow(hwnd, 2 /*MONITOR_DEFAULTTONEAREST*/), ref info)) return false;
        var m = info.Monitor;
        return r.Left <= m.Left && r.Top <= m.Top && r.Right >= m.Right && r.Bottom >= m.Bottom;
    }

    private static string? ImagePath(int pid)
    {
        var handle = OpenProcess(0x1000 /*PROCESS_QUERY_LIMITED_INFORMATION*/, false, pid);
        if (handle == IntPtr.Zero) return null;
        try
        {
            var buffer = new System.Text.StringBuilder(1024);
            var size = buffer.Capacity;
            return QueryFullProcessImageName(handle, 0, buffer, ref size) ? buffer.ToString() : null;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    [DllImport("kernel32.dll")] private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool QueryFullProcessImageName(IntPtr process, int flags, System.Text.StringBuilder name, ref int size);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);

    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public Rect Monitor, Work; public uint Flags; }
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd, System.Text.StringBuilder name, int max);

    private static bool IsAlive(Tracked t)
    {
        try
        {
            using var p = Process.GetProcessById(t.Pid);
            return !p.HasExited && p.StartTime == t.StartTime;
        }
        catch (Exception) { return false; }
    }

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
}
