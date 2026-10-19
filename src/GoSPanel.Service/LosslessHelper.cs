using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
using System.Security;
using System.Text.Json.Nodes;
using System.Xml.Linq;

namespace GoSPanel.Service;

/// <summary>
/// Activa Lossless Scaling sobre un juego aunque este se ejecute como administrador.
/// Windows impide que un proceso normal envíe pulsaciones o cambie el foco a una ventana elevada, así que el
/// servicio lanza este mismo ejecutable («ls-hotkey pid») como tarea programada con privilegios elevados en
/// la sesión del usuario. Ese ayudante espera a que el juego vuelva a primer plano (el usuario cierra Game Bar),
/// reinicia Lossless elevado si hace falta y envía su atajo.
/// </summary>
internal static class LosslessHelper
{
    private const string TaskName = "GoSPanel-LosslessHotkey";
    private const string DefaultLsExe = @"C:\Program Files (x86)\Steam\steamapps\common\Lossless Scaling\LosslessScaling.exe";

    // ---------- En el servicio (SYSTEM): lanzar el ayudante elevado en la sesión del usuario ----------

    public static JsonObject Launch(int gamePid)
    {
        var user = InteractiveUser() ?? throw new InvalidOperationException("No hay ningún usuario con sesión iniciada");
        var exe = Environment.ProcessPath ?? throw new InvalidOperationException("Ruta del servicio desconocida");

        var xml = $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <Principals>
                <Principal id="Author">
                  <UserId>{SecurityElement.Escape(user)}</UserId>
                  <LogonType>InteractiveToken</LogonType>
                  <RunLevel>HighestAvailable</RunLevel>
                </Principal>
              </Principals>
              <Settings>
                <MultipleInstancesPolicy>StopExisting</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <ExecutionTimeLimit>PT2M</ExecutionTimeLimit>
                <Priority>4</Priority>
              </Settings>
              <Actions Context="Author">
                <Exec>
                  <Command>{SecurityElement.Escape(exe)}</Command>
                  <Arguments>ls-hotkey {gamePid}</Arguments>
                </Exec>
              </Actions>
            </Task>
            """;
        var file = Path.Combine(Path.GetTempPath(), "gospanel-ls-task.xml");
        File.WriteAllText(file, xml, System.Text.Encoding.Unicode);
        try
        {
            RunSchtasks($"/Create /TN \"{TaskName}\" /XML \"{file}\" /F");
            RunSchtasks($"/Run /TN \"{TaskName}\"");
        }
        finally
        {
            File.Delete(file);
        }
        return new JsonObject { ["armed"] = true, ["pid"] = gamePid };
    }

    private static string? InteractiveUser()
    {
        using var searcher = new ManagementObjectSearcher("SELECT UserName FROM Win32_ComputerSystem");
        foreach (ManagementObject o in searcher.Get())
            using (o) return o["UserName"] as string;
        return null;
    }

    private static void RunSchtasks(string args)
    {
        using var p = Process.Start(new ProcessStartInfo("schtasks.exe", args)
        {
            CreateNoWindow = true, UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true,
        })!;
        p.WaitForExit(10000);
        if (p.ExitCode != 0) throw new InvalidOperationException("schtasks: " + p.StandardError.ReadToEnd().Trim());
    }

    // ---------- En el ayudante (sesión del usuario, elevado) ----------

    public static int RunHotkey(int gamePid)
    {
        Log($"--- ayudante: juego pid {gamePid}, elevado={IsElevated(Environment.ProcessId) }");
        Process game;
        try { game = Process.GetProcessById(gamePid); }
        catch (ArgumentException) { Log("el juego ya no existe"); return 1; }
        Log($"juego: {game.ProcessName}, elevado={IsElevated(gamePid)}, ventana={game.MainWindowHandle}");

        // 1. Esperar a que el juego vuelva a primer plano (hasta 20 s): el usuario cierra Game Bar.
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (DateTime.UtcNow < deadline && ForegroundPid() != gamePid)
        {
            if (game.HasExited) { Log("el juego se cerró mientras se esperaba"); return 1; }
            Thread.Sleep(250);
        }
        Log($"primer plano tras esperar: pid {ForegroundPid()} ({NameOf(ForegroundPid())})");
        if (ForegroundPid() != gamePid)
        {
            var hwnd = game.MainWindowHandle;
            if (hwnd == IntPtr.Zero) return 2;
            keybd_event(VkMenu, 0, 0, UIntPtr.Zero);
            keybd_event(VkMenu, 0, KeyUp, UIntPtr.Zero);
            SetForegroundWindow(hwnd);
        }

        // 2. Lossless debe tener al menos los mismos permisos que el juego para poder capturarlo.
        var ls = Process.GetProcessesByName("LosslessScaling").FirstOrDefault();
        var lsPath = DefaultLsExe;
        if (ls != null)
        {
            try
            {
                // Solo se relanza con privilegios desde carpetas que únicamente un administrador puede modificar;
                // si no, cualquier programa llamado «LosslessScaling.exe» podría obtener permisos elevados.
                var running = ls.MainModule?.FileName;
                if (running != null && IsAdminOnlyLocation(running)) lsPath = running;
            }
            catch (Exception) { }
            Log($"Lossless abierto: pid {ls.Id}, elevado={IsElevated(ls.Id)}, ruta={lsPath}");
            if (IsElevated(gamePid) && !IsElevated(ls.Id))
            {
                Log("Lossless sin permisos y el juego elevado: se reinicia elevado");
                ls.Kill();
                ls.WaitForExit(5000);
                ls = null;
            }
        }
        if (ls == null)
        {
            // Minimizar su ventana en el instante en que se muestra (aviso EVENT_OBJECT_SHOW de Windows), sin
            // activarla, para que no tape el juego ni le quite el foco.
            WinEventProc onShow = (hook, evt, hwnd, idObject, idChild, thread, time) =>
            {
                if (idObject != 0 || hwnd == IntPtr.Zero || GetAncestor(hwnd, 2) != hwnd) return;   // solo ventanas de primer nivel
                GetWindowThreadProcessId(hwnd, out var pid);
                string name;
                try { name = Process.GetProcessById((int)pid).ProcessName; } catch (Exception) { return; }
                if (name != "LosslessScaling" || !GetWindowRect(hwnd, out var r)) return;
                if (r.Left == 0 && r.Top == 0 && r.Right == GetSystemMetrics(0) && r.Bottom == GetSystemMetrics(1)) return;   // pantalla de escalado
                ShowWindow(hwnd, 7);   // SW_SHOWMINNOACTIVE
                Log("ventana de Lossless minimizada al mostrarse");
            };
            var hook = SetWinEventHook(EventObjectShow, EventObjectShow, IntPtr.Zero, onShow, 0, 0, WineventOutOfContext);

            Process.Start(new ProcessStartInfo(lsPath)
            {
                UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(lsPath), WindowStyle = ProcessWindowStyle.Minimized,
            });
            // Bombear mensajes mientras carga: los avisos del gancho llegan por la cola de este hilo.
            var launchDeadline = DateTime.UtcNow.AddSeconds(6);
            while (DateTime.UtcNow < launchDeadline)
            {
                while (PeekMessage(out var msg, IntPtr.Zero, 0, 0, 1)) { TranslateMessage(ref msg); DispatchMessage(ref msg); }
                Thread.Sleep(10);
            }
            if (hook != IntPtr.Zero) UnhookWinEvent(hook);
            if (MinimizeLosslessMainWindow()) Log("ventana de Lossless minimizada (respaldo)");
            GC.KeepAlive(onShow);
            var started = Process.GetProcessesByName("LosslessScaling").FirstOrDefault();
            Log(started == null ? "Lossless NO quedó abierto tras lanzarlo (¿lo relanzó Steam?)"
                : $"Lossless lanzado: pid {started.Id}, elevado={IsElevated(started.Id)}");
            var hwnd = game.MainWindowHandle;
            keybd_event(VkMenu, 0, 0, UIntPtr.Zero);
            keybd_event(VkMenu, 0, KeyUp, UIntPtr.Zero);
            SetForegroundWindow(hwnd);
            Thread.Sleep(400);
        }

        // 3. Enviar el atajo de Lossless sobre el juego.
        var (modifiers, key) = ReadHotkey();
        Log($"antes del atajo: primer plano {NameOf(ForegroundPid())}; ventanas de Lossless: {LsWindows()}");
        foreach (var m in modifiers) keybd_event(m, 0, 0, UIntPtr.Zero);
        keybd_event(key, 0, 0, UIntPtr.Zero);
        Thread.Sleep(40);
        keybd_event(key, 0, KeyUp, UIntPtr.Zero);
        foreach (var m in modifiers.Reverse()) keybd_event(m, 0, KeyUp, UIntPtr.Zero);
        Log($"atajo enviado: {string.Join("+", modifiers.Select(m => $"0x{m:X2}"))}+'{(char)key}'");
        Thread.Sleep(2500);
        Log($"2,5 s después: primer plano {NameOf(ForegroundPid())}; ventanas de Lossless: {LsWindows()}");
        return 0;
    }

    // ---------- Diagnóstico: %ProgramData%\GoSPanel\lossless.log ----------

    private static void Log(string line)
    {
        try
        {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "GoSPanel", "lossless.log");
            File.AppendAllText(path, $"{DateTime.Now:HH:mm:ss.fff}  {line}{Environment.NewLine}");
        }
        catch (Exception) { }
    }

    private static string NameOf(int pid)
    {
        try { return Process.GetProcessById(pid).ProcessName; } catch (Exception) { return "?"; }
    }

    /// <summary>Ventanas visibles de Lossless (al escalar crea una ventana a pantalla completa).</summary>
    private static string LsWindows()
    {
        var pids = Process.GetProcessesByName("LosslessScaling").Select(p => p.Id).ToHashSet();
        if (pids.Count == 0) return "Lossless cerrado";
        var found = new List<string>();
        EnumWindows((hwnd, _) =>
        {
            GetWindowThreadProcessId(hwnd, out var pid);
            if (pids.Contains((int)pid) && IsWindowVisible(hwnd) && GetWindowRect(hwnd, out var r))
                found.Add($"{r.Right - r.Left}x{r.Bottom - r.Top}");
            return true;
        }, IntPtr.Zero);
        return found.Count == 0 ? "ninguna visible" : string.Join(", ", found);
    }

    /// <summary>Minimiza la ventana principal de Lossless (la que no ocupa toda la pantalla). True si lo hizo.</summary>
    private static bool MinimizeLosslessMainWindow()
    {
        var pids = Process.GetProcessesByName("LosslessScaling").Select(p => p.Id).ToHashSet();
        if (pids.Count == 0) return false;
        int screenW = GetSystemMetrics(0), screenH = GetSystemMetrics(1);
        var done = false;
        EnumWindows((hwnd, _) =>
        {
            GetWindowThreadProcessId(hwnd, out var pid);
            if (pids.Contains((int)pid) && IsWindowVisible(hwnd) && !IsIconic(hwnd) && GetWindowRect(hwnd, out var r)
                && !(r.Left == 0 && r.Top == 0 && r.Right == screenW && r.Bottom == screenH) && r.Right - r.Left > 200)
            {
                ShowWindow(hwnd, 6);   // SW_MINIMIZE
                done = true;
            }
            return true;
        }, IntPtr.Zero);
        return done;
    }

    private delegate void WinEventProc(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);
    [StructLayout(LayoutKind.Sequential)]
    private struct Msg { public IntPtr Hwnd; public uint Message; public IntPtr WParam, LParam; public uint Time; public int X, Y; }
    private const uint EventObjectShow = 0x8002, WineventOutOfContext = 0;

    [DllImport("user32.dll")] private static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr module, WinEventProc proc, uint pid, uint thread, uint flags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr hook);
    [DllImport("user32.dll")] private static extern bool PeekMessage(out Msg msg, IntPtr hwnd, uint min, uint max, uint remove);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref Msg msg);
    [DllImport("user32.dll")] private static extern IntPtr DispatchMessage(ref Msg msg);
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hwnd, int command);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr param);
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr param);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);

    private static (byte[] Modifiers, byte Key) ReadHotkey()
    {
        string keyName = "S", modifierNames = "Alt Control";
        try
        {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lossless Scaling", "Settings.xml");
            var xml = XDocument.Load(path);
            keyName = xml.Descendants("Hotkey").FirstOrDefault()?.Value ?? keyName;
            modifierNames = xml.Descendants("HotkeyModifierKeys").FirstOrDefault()?.Value ?? modifierNames;
        }
        catch (Exception) { }

        var modifiers = new List<byte>();
        foreach (var part in modifierNames.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries))
            switch (part.ToLowerInvariant())
            {
                case "control": modifiers.Add(VkControl); break;
                case "alt": modifiers.Add(VkMenu); break;
                case "shift": modifiers.Add(VkShift); break;
                case "windows": modifiers.Add(VkLWin); break;
            }
        modifiers.Sort((a, b) => Order(a).CompareTo(Order(b)));
        var key = keyName.Length == 1 ? (byte)char.ToUpperInvariant(keyName[0])
            : Enum.TryParse<ConsoleKey>(keyName, true, out var ck) ? (byte)ck : (byte)'S';
        return (modifiers.ToArray(), key);
    }

    private static int Order(byte vk) => vk switch { VkControl => 0, VkMenu => 1, VkShift => 2, _ => 3 };

    private static bool IsAdminOnlyLocation(string path)
    {
        var full = Path.GetFullPath(path);
        return new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 }
            .Select(f => Environment.GetFolderPath(f) + Path.DirectorySeparatorChar)
            .Any(root => full.StartsWith(root, StringComparison.OrdinalIgnoreCase));
    }

    private static int ForegroundPid()
    {
        GetWindowThreadProcessId(GetForegroundWindow(), out var pid);
        return (int)pid;
    }

    private static bool IsElevated(int pid)
    {
        var process = OpenProcess(0x1000, false, pid);   // PROCESS_QUERY_LIMITED_INFORMATION
        if (process == IntPtr.Zero) return true;        // no accesible: tratar como elevado/protegido
        try
        {
            if (!OpenProcessToken(process, 0x0008, out var token)) return true;
            try { return GetTokenInformation(token, 20, out var elevated, 4, out _) && elevated != 0; }
            finally { CloseHandle(token); }
        }
        finally { CloseHandle(process); }
    }

    private const byte VkShift = 0x10, VkControl = 0x11, VkMenu = 0x12, VkLWin = 0x5B;
    private const uint KeyUp = 0x0002;

    [DllImport("user32.dll")] private static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("kernel32.dll")] private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
    [DllImport("advapi32.dll")] private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
    [DllImport("advapi32.dll")] private static extern bool GetTokenInformation(IntPtr token, int infoClass, out int value, int length, out int returned);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
}
