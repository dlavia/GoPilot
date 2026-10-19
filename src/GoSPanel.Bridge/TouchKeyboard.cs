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

    [ComImport, Guid("4ce576fa-83dc-4F88-951c-9d0782b4e376")]
    private class UIHostNoLaunch { }

    [ComImport, Guid("37c994e7-432b-4834-a2f7-dce1f13b834b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITipInvocation
    {
        void Toggle(IntPtr hwnd);
    }

    [DllImport("user32.dll")] private static extern IntPtr GetDesktopWindow();
}
