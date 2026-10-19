using System.Runtime.InteropServices;
using System.Text.Json.Nodes;

namespace GoSPanel.Service;

/// <summary>
/// SPL / sPPT / fPPT que aplica el firmware de Lenovo en cada modo (Silencio, Equilibrado, Rendimiento), para que el
/// widget muestre los vatios reales. Cambian enchufada o con batería, y solo se pueden leer los del modo activo (los
/// identificadores de cada modo devuelven valores sin sentido): se apuntan en state.json («modeLimits», por modo y
/// fuente) cuando el modo lleva unos segundos activo. Hasta entonces, los medidos en una Legion Go S Z1 Extreme
/// (2026-10-19): enchufada 10/15/20, 16/25/30 y 30/33/40 W; con batería 10/10/20, 15/15/30 y 25/25/35 W.
/// </summary>
internal static class ModeLimits
{
    private static readonly Dictionary<string, (int Spl, int Sppt, int Fppt)> DefaultAc = new()
    {
        ["quiet"] = (10, 15, 20), ["balanced"] = (16, 25, 30), ["performance"] = (30, 33, 40),
    };
    private static readonly Dictionary<string, (int Spl, int Sppt, int Fppt)> DefaultDc = new()
    {
        ["quiet"] = (10, 10, 20), ["balanced"] = (15, 15, 30), ["performance"] = (25, 25, 35),
    };
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
        else if (mode != null && DefaultAc.ContainsKey(mode) && !Learned.Contains(key) && DateTime.UtcNow - _since > TimeSpan.FromSeconds(3))
        {
            try
            {
                var tdp = LenovoWmi.GetTdp();
                // Al pasar de Manual a un modo, el firmware puede seguir dando un rato los valores manuales: esos no
                // son los del modo (se vio con 22 W apuntados en los tres modos) y no se guardan.
                if (IsManual(tdp)) return Finish(power, source);
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
        return Finish(power, source);
    }

    private static JsonObject Finish(JsonObject power, string source)
    {
        power["ac"] = source == "ac";
        power["limits"] = For(source);
        return power;
    }

    /// <summary>¿Son los límites del modo Manual guardado (no los de un modo de Lenovo)?</summary>
    private static bool IsManual(JsonNode? limits)
    {
        var manual = SavedState.Load()["tdp"];
        if (limits == null || manual == null) return false;
        return new[] { "spl", "sppt", "fppt" }.All(k =>
            limits[k]?.GetValue<double>() is double a && manual[k]?.GetValue<double>() is double b && Math.Abs(a - b) < 0.5);
    }

    /// <summary>Límites de los tres modos para la fuente de alimentación actual.</summary>
    private static JsonObject For(string source)
    {
        var saved = SavedState.Load()["modeLimits"] as JsonObject;
        var defaults = source == "ac" ? DefaultAc : DefaultDc;
        var result = new JsonObject();
        foreach (var (mode, d) in defaults)
        {
            // Lo apuntado mal en versiones anteriores (los valores de Manual) se ignora y vale el medido
            var learned = saved?[mode + "-" + source];
            result[mode] = learned != null && !IsManual(learned)
                ? learned.DeepClone()
                : new JsonObject { ["spl"] = d.Spl, ["sppt"] = d.Sppt, ["fppt"] = d.Fppt };
        }
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
