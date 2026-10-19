using System.Runtime.InteropServices;

namespace GoSPanel.Service;

/// <summary>
/// Consumo real de la APU (CPU + gráfica) en vatios: contador «Energy Meter(RAPL_Package0_PKG)\Power» que Windows
/// expone a partir del RAPL de AMD (en mW). Funciona enchufada o con batería; la descarga de la batería solo sirve
/// desenchufada. Se usa el nombre en inglés (PdhAddEnglishCounter) porque el sistema puede estar en otro idioma.
/// </summary>
internal static class ApuPower
{
    private const string Counter = @"\Energy Meter(RAPL_Package0_PKG)\Power";
    private static readonly object Lock = new();
    private static IntPtr _query, _counter;
    private static bool _unavailable;

    public static double? Watts()
    {
        lock (Lock)
        {
            if (_unavailable) return null;
            if (_query == IntPtr.Zero)
            {
                if (PdhOpenQuery(null, IntPtr.Zero, out _query) != 0
                    || PdhAddEnglishCounter(_query, Counter, IntPtr.Zero, out _counter) != 0)
                {
                    _unavailable = true;   // equipo sin medidor RAPL: no se vuelve a intentar
                    return null;
                }
            }
            if (PdhCollectQueryData(_query) != 0) return null;
            if (PdhGetFormattedCounterValue(_counter, PdhFmtDouble, IntPtr.Zero, out var value) != 0 || value.CStatus != 0) return null;
            return Math.Round(value.DoubleValue / 1000, 1);
        }
    }

    private const uint PdhFmtDouble = 0x00000200;

    [StructLayout(LayoutKind.Sequential)]
    private struct PdhFmtCounterValue { public uint CStatus; public double DoubleValue; }

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern uint PdhOpenQuery(string? source, IntPtr user, out IntPtr query);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern uint PdhAddEnglishCounter(IntPtr query, string path, IntPtr user, out IntPtr counter);
    [DllImport("pdh.dll")] private static extern uint PdhCollectQueryData(IntPtr query);
    [DllImport("pdh.dll")] private static extern uint PdhGetFormattedCounterValue(IntPtr counter, uint format, IntPtr type, out PdhFmtCounterValue value);
}
