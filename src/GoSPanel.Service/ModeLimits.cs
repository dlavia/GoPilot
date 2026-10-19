using System.Runtime.InteropServices;
using System.Text.Json.Nodes;

namespace GoSPanel.Service;

/// <summary>
/// SPL / sPPT / fPPT que aplica el firmware de Lenovo en cada modo (Silencio, Equilibrado, Rendimiento), para que el
/// widget muestre los vatios reales. Cambian enchufada o con batería, y solo se pueden leer los del modo activo (los
/// identificadores de cada modo devuelven valores sin sentido): se apuntan en state.json («modeLimits», por modo y
/// fuente) cuando el modo lleva unos segundos activo. Hasta entonces, los vistos en una Legion Go S Z1 Extreme:
/// enchufada 10/16/30 W (medido 2026-10-19) y con batería 7/15/20 W (los que muestra Legion Space).
/// </summary>
internal static class ModeLimits
{
    private static readonly Dictionary<string, int> DefaultSplAc = new() { ["quiet"] = 10, ["balanced"] = 16, ["performance"] = 30 };
    private static readonly Dictionary<string, int> DefaultSplDc = new() { ["quiet"] = 7, ["balanced"] = 15, ["performance"] = 20 };
    private static readonly HashSet<string> Learned = new();
    private static string? _key;
    private static DateTime _since;

    /// <summary>Con el modo actual (lo llama power.get): si lleva 3 s activo, se leen y guardan sus límites una vez.</summary>
    public static JsonObject With(JsonObject power)
    {
        var mode = (string?)power["mode"];
        var source = OnAc() ? "ac" : "dc";
        var key = mode + "-" + source;
        if (key != _key) { _key = key; _since = DateTime.UtcNow; }
        else if (mode != null && DefaultSplAc.ContainsKey(mode) && !Learned.Contains(key) && DateTime.UtcNow - _since > TimeSpan.FromSeconds(3))
        {
            try
            {
                var tdp = LenovoWmi.GetTdp();
                SavedState.Update(s =>
                {
                    var all = s["modeLimits"] as JsonObject ?? new JsonObject();
                    all[key] = new JsonObject { ["spl"] = tdp["spl"]?.DeepClone(), ["sppt"] = tdp["sppt"]?.DeepClone(), ["fppt"] = tdp["fppt"]?.DeepClone() };
                    s["modeLimits"] = all;
                });
                Learned.Add(key);
            }
            catch (Exception) { /* se reintenta en la próxima lectura */ }
        }
        power["ac"] = source == "ac";
        power["limits"] = For(source);
        return power;
    }

    /// <summary>Límites de los tres modos para la fuente de alimentación actual.</summary>
    private static JsonObject For(string source)
    {
        var saved = SavedState.Load()["modeLimits"] as JsonObject;
        var defaults = source == "ac" ? DefaultSplAc : DefaultSplDc;
        var result = new JsonObject();
        foreach (var (mode, spl) in defaults)
            result[mode] = saved?[mode + "-" + source]?.DeepClone() ?? new JsonObject { ["spl"] = spl };
        return result;
    }

    private static bool OnAc() => GetSystemPowerStatus(out var status) && status.AcLineStatus == 1;

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatus
    {
        public byte AcLineStatus, BatteryFlag, BatteryLifePercent, SystemStatusFlag;
        public int BatteryLifeTime, BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll")] private static extern bool GetSystemPowerStatus(out SystemPowerStatus status);
}
