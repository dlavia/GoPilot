using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;

namespace GoSPanel.Bridge;

/// <summary>Muestra u oculta el teclado táctil de Windows (TabTip) mediante su interfaz COM ITipInvocation.</summary>
internal static class TouchKeyboard
{
    private const string TabTipPath = @"C:\Program Files\Common Files\microsoft shared\ink\TabTip.exe";

    public static JsonObject Toggle()
    {
        if (Process.GetProcessesByName("TabTip").Length == 0)
        {
            Process.Start(new ProcessStartInfo(TabTipPath) { UseShellExecute = true });
            Thread.Sleep(400);   // dar tiempo a que registre su interfaz COM
        }

        var tip = (ITipInvocation)new UIHostNoLaunch();
        try
        {
            tip.Toggle(GetDesktopWindow());
        }
        finally
        {
            Marshal.ReleaseComObject(tip);
        }
        return new JsonObject { ["toggled"] = true };
    }

    /// <summary>
    /// ¿Está el teclado táctil en pantalla? En Windows 11 lo dibuja TextInputHost en una ventana CoreWindow que, oculta,
    /// queda «cloaked» (DWM); en Windows 10, la ventana IPTip_Main_Window de TabTip.
    /// </summary>
    public static bool IsVisible() => VisibleCenter() != null;

    /// <summary>Centro del teclado en pantalla (píxeles físicos), o null si no se ve.</summary>
    public static (int X, int Y)? VisibleCenter()
    {
        var hosts = Process.GetProcessesByName("TextInputHost").Select(p => p.Id).ToHashSet();
        (int X, int Y)? center = null;
        EnumWindows((hwnd, _) =>
        {
            GetWindowThreadProcessId(hwnd, out var pid);
            var cls = new System.Text.StringBuilder(64);
            GetClassName(hwnd, cls, cls.Capacity);
            var name = cls.ToString();
            var candidate = (hosts.Contains((int)pid) && name == "Windows.UI.Core.CoreWindow") || name == "IPTip_Main_Window";
            if (candidate && IsWindowVisible(hwnd) && !IsCloaked(hwnd) && GetWindowRect(hwnd, out var r) && r.Bottom - r.Top > 100)
            {
                center = ((r.Left + r.Right) / 2, (r.Top + r.Bottom) / 2);
                return false;
            }
            return true;
        }, IntPtr.Zero);
        return center;
    }

    private static bool IsCloaked(IntPtr hwnd) => DwmGetWindowAttribute(hwnd, 14 /*DWMWA_CLOAKED*/, out var cloaked, 4) == 0 && cloaked != 0;

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr param);
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr param);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd, System.Text.StringBuilder name, int max);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out int value, int size);

    [ComImport, Guid("4ce576fa-83dc-4F88-951c-9d0782b4e376")]
    private class UIHostNoLaunch { }

    [ComImport, Guid("37c994e7-432b-4834-a2f7-dce1f13b834b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITipInvocation
    {
        void Toggle(IntPtr hwnd);
    }

    [DllImport("user32.dll")] private static extern IntPtr GetDesktopWindow();
}
