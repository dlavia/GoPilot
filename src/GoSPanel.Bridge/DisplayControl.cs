using System.Runtime.InteropServices;
using System.Text.Json.Nodes;

namespace GoSPanel.Bridge;

/// <summary>Resolución y tasa de refresco de la pantalla principal (Win32 ChangeDisplaySettingsEx).</summary>
internal static class DisplayControl
{
    public static JsonObject Get()
    {
        var current = NewDevMode();
        if (!EnumDisplaySettings(null, EnumCurrentSettings, ref current))
            throw new InvalidOperationException("No se pudo leer el modo de pantalla actual");

        var modes = new SortedSet<(int W, int H, int Hz)>();
        var dm = NewDevMode();
        for (var i = 0; EnumDisplaySettings(null, i, ref dm); i++)
            if (dm.dmBitsPerPel == 32 && dm.dmDisplayFrequency > 1)
                modes.Add((dm.dmPelsWidth, dm.dmPelsHeight, dm.dmDisplayFrequency));

        var list = new JsonArray();
        foreach (var (w, h, hz) in modes.Reverse())
            list.Add(new JsonObject { ["width"] = w, ["height"] = h, ["hz"] = hz });

        return new JsonObject
        {
            ["current"] = new JsonObject
            {
                ["width"] = current.dmPelsWidth,
                ["height"] = current.dmPelsHeight,
                ["hz"] = current.dmDisplayFrequency,
            },
            ["modes"] = list,
        };
    }

    public static JsonObject Set(int width, int height, int hz)
    {
        var dm = NewDevMode();
        if (!EnumDisplaySettings(null, EnumCurrentSettings, ref dm))
            throw new InvalidOperationException("No se pudo leer el modo de pantalla actual");

        dm.dmPelsWidth = width;
        dm.dmPelsHeight = height;
        dm.dmDisplayFrequency = hz;
        dm.dmFields = DmPelsWidth | DmPelsHeight | DmDisplayFrequency;

        var test = ChangeDisplaySettingsEx(null, ref dm, IntPtr.Zero, CdsTest, IntPtr.Zero);
        if (test != DispChangeSuccessful)
            throw new InvalidOperationException($"Modo no admitido: {width}x{height} @ {hz} Hz ({test})");

        var result = ChangeDisplaySettingsEx(null, ref dm, IntPtr.Zero, CdsUpdateRegistry, IntPtr.Zero);
        if (result != DispChangeSuccessful)
            throw new InvalidOperationException($"No se pudo cambiar el modo de pantalla ({result})");

        return Get();
    }

    private static DEVMODE NewDevMode() => new() { dmSize = (short)Marshal.SizeOf<DEVMODE>() };

    private const int EnumCurrentSettings = -1;
    private const int DmPelsWidth = 0x80000, DmPelsHeight = 0x100000, DmDisplayFrequency = 0x400000;
    private const int CdsUpdateRegistry = 0x1, CdsTest = 0x2;
    private const int DispChangeSuccessful = 0;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public short dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
        public int dmFields;
        public int dmPositionX, dmPositionY, dmDisplayOrientation, dmDisplayFixedOutput;
        public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public short dmLogPixels;
        public int dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
        public int dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2;
        public int dmPanningWidth, dmPanningHeight;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplaySettings(string? deviceName, int modeNum, ref DEVMODE devMode);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int ChangeDisplaySettingsEx(string? deviceName, ref DEVMODE devMode, IntPtr hwnd, int flags, IntPtr lParam);
}
