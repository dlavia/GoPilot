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

    // ¿Está el teclado táctil a la vista? Lo dice IFrameworkInputPane::Location (rectángulo vacío si está oculto). La
    // ventana de TextInputHost no sirve: sigue «cloaked» aunque el teclado se vea. Se consulta desde un hilo STA propio
    // cuatro veces por segundo y se guarda el resultado.
    private static volatile bool _visible;
    private static Thread? _paneThread;

    public static bool IsVisible()
    {
        if (_paneThread == null)
        {
            _paneThread = new Thread(WatchPane) { IsBackground = true, Name = "InputPane" };
            _paneThread.SetApartmentState(ApartmentState.STA);
            _paneThread.Start();
        }
        return _visible;
    }

    private static void WatchPane()
    {
        while (true)
        {
            try
            {
                var pane = (IFrameworkInputPane)new FrameworkInputPane();
                try
                {
                    while (true)
                    {
                        _visible = pane.Location(out var r) == 0 && r.Right - r.Left > 0 && r.Bottom - r.Top > 0;
                        Thread.Sleep(250);
                    }
                }
                finally
                {
                    Marshal.ReleaseComObject(pane);
                }
            }
            catch (Exception)
            {
                _visible = false;
                Thread.Sleep(2000);
            }
        }
    }

    [ComImport, Guid("D5120AA3-46BA-44C5-822D-CA8092C1FC72")]
    private class FrameworkInputPane { }

    [ComImport, Guid("5752238B-24F0-495A-82F1-2FD593056796"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFrameworkInputPane
    {
        [PreserveSig] int Advise([MarshalAs(UnmanagedType.IUnknown)] object window, IntPtr handler, out int cookie);
        [PreserveSig] int AdviseWithHWND(IntPtr hwnd, IntPtr handler, out int cookie);
        [PreserveSig] int Unadvise(int cookie);
        [PreserveSig] int Location(out Rect location);
    }


    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }

    [ComImport, Guid("4ce576fa-83dc-4F88-951c-9d0782b4e376")]
    private class UIHostNoLaunch { }

    [ComImport, Guid("37c994e7-432b-4834-a2f7-dce1f13b834b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITipInvocation
    {
        void Toggle(IntPtr hwnd);
    }

    [DllImport("user32.dll")] private static extern IntPtr GetDesktopWindow();
}
