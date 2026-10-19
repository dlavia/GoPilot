using System.Management;
using System.Text.Json.Nodes;

namespace GoSPanel.Service;

/// <summary>
/// Acceso a la interfaz WMI de Lenovo (root\WMI), la misma que usa Legion Space.
/// Verificado en Legion Go S Z1 Extreme (83N6) el 2026-10-16 con "GoSPanel.Service.exe probe".
/// </summary>
internal static class LenovoWmi
{
    private const string Ns = @"root\WMI";
    private const string GameZone = "LENOVO_GAMEZONE_DATA";
    private const string OtherMethod = "LENOVO_OTHER_METHOD";
    private const string Capabilities = "LENOVO_CAPABILITY_DATA_01";

    // IDs de feature: 0x01XXMMSS → 01 = CPU, XX = límite, MM = modo (01 silencio, 02 equilibrado, 03 rendimiento, FF custom).
    // Orden de XX según el driver lenovo-wmi-other de Linux: 01 = sPPT, 02 = SPL, 03 = fPPT.
    // Comprobado con los valores por defecto del firmware (SPL 7/15/20 W), que son los que muestra Legion Space.
    private const uint SpptId = 0x0101FF00;
    private const uint SplId = 0x0102FF00;
    private const uint FpptId = 0x0103FF00;

    // Sensores leídos con GetFeatureValue.
    private const uint FanRpmId = 0x04030001;
    private const uint ApuTempId = 0x05040000;

    // Rango por defecto si el firmware no informa; los reales se leen de LENOVO_CAPABILITY_DATA_01.
    private const double FallbackMin = 5, FallbackMax = 30;
    private static Dictionary<uint, (double Min, double Max)>? _limits;

    private static readonly Dictionary<string, uint> Modes = new()
    {
        ["quiet"] = 1,
        ["balanced"] = 2,
        ["performance"] = 3,
        ["custom"] = 255,
    };

    private static bool _apuTempUnsupported, _fanUnsupported;

    // ---------- Diagnóstico ----------

    public static JsonObject Probe()
    {
        var classes = new JsonObject();
        using var searcher = new ManagementObjectSearcher(Ns,
            "SELECT * FROM meta_class WHERE __CLASS LIKE '%LENOVO%'");
        foreach (ManagementClass cls in searcher.Get())
        {
            var methods = new JsonObject();
            foreach (var m in cls.Methods)
            {
                methods[m.Name] = new JsonObject
                {
                    ["in"] = Params(m.InParameters),
                    ["out"] = Params(m.OutParameters),
                };
            }

            var props = new JsonArray();
            foreach (var p in cls.Properties)
                if (!p.Name.StartsWith("__")) props.Add(JsonValue.Create($"{p.Name}:{p.Type}"));

            int instances = 0;
            try { instances = cls.GetInstances().Count; } catch (ManagementException) { }

            classes[cls.ClassPath.ClassName] = new JsonObject
            {
                ["instances"] = instances,
                ["properties"] = props,
                ["methods"] = methods,
            };
        }
        return classes;
    }

    private static JsonArray Params(ManagementBaseObject? p)
    {
        var arr = new JsonArray();
        if (p is null) return arr;
        foreach (var prop in p.Properties)
            if (prop.Name != "ReturnValue") arr.Add(JsonValue.Create($"{prop.Name}:{prop.Type}"));
        return arr;
    }

    /// <summary>Llama a un método WMI de una clase LENOVO_*. Solo se usa desde la CLI y desde este archivo.</summary>
    public static Dictionary<string, object?> Call(string cls, string method, IReadOnlyDictionary<string, object>? args = null)
    {
        if (!cls.StartsWith("LENOVO_", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Solo se permiten clases LENOVO_*");

        using var obj = Wmi.First(Ns, cls) ?? throw new InvalidOperationException($"No existe ninguna instancia de {cls}");
        using var inParams = obj.GetMethodParameters(method);
        if (args is { Count: > 0 })
        {
            if (inParams is null) throw new ArgumentException($"{method} no admite parámetros");
            foreach (var (name, value) in args)
                inParams[name] = Wmi.ToCim(value, inParams.Properties[name].Type);
        }

        using var outParams = obj.InvokeMethod(method, inParams, null);
        var result = new Dictionary<string, object?>();
        if (outParams is not null)
            foreach (var p in outParams.Properties)
                result[p.Name] = p.Value;
        return result;
    }

    private static double CallNumber(string cls, string method, string outName, IReadOnlyDictionary<string, object>? args = null) =>
        Convert.ToDouble(Call(cls, method, args)[outName]);

    // ---------- Modo de energía ----------

    public static JsonObject GetPowerMode()
    {
        var value = (uint)CallNumber(GameZone, "GetSmartFanMode", "Data");
        var name = Modes.FirstOrDefault(m => m.Value == value).Key ?? "unknown";
        return new JsonObject { ["mode"] = name, ["raw"] = value };
    }

    public static JsonObject SetPowerMode(string mode)
    {
        if (!Modes.TryGetValue(mode, out var value))
            throw new ArgumentException($"Modo no válido: {mode}");
        Call(GameZone, "SetSmartFanMode", new Dictionary<string, object> { ["Data"] = value });
        return GetPowerMode();
    }

    // ---------- TDP ----------

    public static JsonObject GetTdp()
    {
        var limits = new JsonObject();
        foreach (var (name, id) in new[] { ("spl", SplId), ("sppt", SpptId), ("fppt", FpptId) })
        {
            var (min, max) = Limits(id);
            limits[name] = new JsonObject { ["min"] = min, ["max"] = max };
        }
        return new JsonObject
        {
            ["spl"] = GetFeature(SplId),
            ["sppt"] = GetFeature(SpptId),
            ["fppt"] = GetFeature(FpptId),
            ["limits"] = limits,
        };
    }

    public static JsonObject SetTdp(double spl, double sppt, double fppt)
    {
        foreach (var (name, id, watts) in new[] { ("SPL", SplId, spl), ("sPPT", SpptId, sppt), ("fPPT", FpptId, fppt) })
        {
            var (min, max) = Limits(id);
            if (watts < min || watts > max)
                throw new ArgumentOutOfRangeException(name, $"{name} debe estar entre {min} y {max} W");
        }
        if (!(spl <= sppt && sppt <= fppt))
            throw new ArgumentException("Debe cumplirse SPL ≤ sPPT ≤ fPPT");

        // Los límites manuales solo se respetan en modo Custom.
        if ((string?)GetPowerMode()["mode"] != "custom")
            SetPowerMode("custom");

        // Al subir, primero el límite más alto; al bajar, primero el más bajo.
        var raising = spl > GetFeature(SplId);
        var order = raising
            ? new[] { (FpptId, fppt), (SpptId, sppt), (SplId, spl) }
            : new[] { (SplId, spl), (SpptId, sppt), (FpptId, fppt) };
        foreach (var (id, watts) in order)
            Call(OtherMethod, "SetFeatureValue", new Dictionary<string, object> { ["IDs"] = id, ["value"] = (uint)Math.Round(watts) });

        return GetTdp();
    }

    private static double GetFeature(uint id) =>
        CallNumber(OtherMethod, "GetFeatureValue", "value", new Dictionary<string, object> { ["IDs"] = id });

    /// <summary>
    /// Rango de un límite según el firmware: mínimo del modo Custom (FF) y máximo del modo Rendimiento (03),
    /// que es la envolvente que Lenovo usa en sus modos (SPL 30 / sPPT 32 / fPPT 41 W). Custom admitiría
    /// hasta 40 / 43 / 53 W, pero eso queda fuera de lo que Legion Space expone.
    /// </summary>
    private static (double Min, double Max) Limits(uint customId)
    {
        if (_limits is null)
        {
            var limits = new Dictionary<uint, (double, double)>();
            using var searcher = new ManagementObjectSearcher(Ns, $"SELECT IDs, MinValue, MaxValue FROM {Capabilities}");
            foreach (ManagementObject o in searcher.Get())
                using (o)
                    limits[Convert.ToUInt32(o["IDs"])] = (Convert.ToDouble(o["MinValue"]), Convert.ToDouble(o["MaxValue"]));
            _limits = limits;
        }

        var performanceId = (customId & 0xFFFF0000) | 0x0300;
        var min = _limits.TryGetValue(customId, out var custom) && custom.Max > 0 ? custom.Min : FallbackMin;
        var max = _limits.TryGetValue(performanceId, out var perf) && perf.Max > 0 ? perf.Max : FallbackMax;
        return (min, max);
    }

    // ---------- Ventilador al máximo (0x04020000: 0 = curva automática, 1 = velocidad máxima) ----------

    private const uint FanFullSpeedId = 0x04020000;

    public static JsonObject GetFullFan() => new() { ["enabled"] = GetFeature(FanFullSpeedId) != 0 };

    public static JsonObject SetFullFan(bool enabled)
    {
        Call(OtherMethod, "SetFeatureValue", new Dictionary<string, object> { ["IDs"] = FanFullSpeedId, ["value"] = enabled ? 1u : 0u });
        return GetFullFan();
    }

    // ---------- Límite de carga al 80 % (0x03010001, «modo larga vida»: 1 = activo, 0 = carga normal) ----------
    // Según el driver lenovo-wmi-other de Linux, en la Go S la lectura puede no reflejar el estado real,
    // así que se informa también del último valor pedido (guardado en SavedState).

    private const uint ChargeLimitId = 0x03010001;

    public static JsonObject GetChargeLimit()
    {
        var requested = SavedState.Load()["chargeLimit"]?.GetValue<bool>();
        return new JsonObject { ["enabled"] = requested ?? GetFeature(ChargeLimitId) != 0, ["firmware"] = GetFeature(ChargeLimitId) };
    }

    public static JsonObject SetChargeLimit(bool enabled)
    {
        Call(OtherMethod, "SetFeatureValue", new Dictionary<string, object> { ["IDs"] = ChargeLimitId, ["value"] = enabled ? 1u : 0u });
        return new JsonObject { ["enabled"] = enabled, ["firmware"] = GetFeature(ChargeLimitId) };
    }

    // ---------- Touchpad (GetTPStatus: 0 = activo, 1 = desactivado) ----------

    public static JsonObject GetTouchpad() =>
        new() { ["enabled"] = CallNumber(GameZone, "GetTPStatus", "Data") == 0 };

    public static JsonObject SetTouchpad(bool enabled)
    {
        Call(GameZone, "SetTPStatus", new Dictionary<string, object> { ["Data"] = enabled ? 0u : 1u });
        return GetTouchpad();
    }

    // ---------- Curva del ventilador (LENOVO_FAN_METHOD) ----------
    // Tabla de 10 puntos: velocidad (%) a 10, 20 … 100 °C. Formato de Fan_Set_Table (el mismo que usa HHD):
    //   [FSTM 0][FSID 0][FSTL = 10 (u32)][10 × velocidad (u16)][SSID 0][STL = 10 (u32)][10 × temperatura (u16)][0]
    // Solo actúa en modo Manual (custom): al cambiar de modo el firmware vuelve a su curva.

    public static readonly int[] FanTemps = { 10, 20, 30, 40, 50, 60, 70, 80, 90, 100 };

    /// <summary>
    /// Curva de fábrica de la Go S (leída con Fan_Get_Table en modo Equilibrado el 2026-10-17): no se permite
    /// bajar de ella. (La mínima de HHD, 44…100, es la de la Legion Go original.)
    /// </summary>
    public static readonly int[] MinFanCurve = { 25, 27, 32, 36, 41, 45, 51, 55, 59, 64 };

    public static int[]? GetFanTable()
    {
        var result = Call("LENOVO_FAN_METHOD", "Fan_Get_Table", new Dictionary<string, object> { ["FanID"] = 0, ["SensorID"] = 0 });
        return result.TryGetValue("FanTable", out var table) && table is Array array
            ? array.Cast<object>().Select(Convert.ToInt32).Take(10).ToArray()
            : null;
    }

    public static void SetFanTable(IReadOnlyList<int> speeds)
    {
        if (speeds.Count != 10) throw new ArgumentException("La curva necesita 10 puntos");
        var bytes = new List<byte> { 0x00, 0x00, 0x0A, 0x00, 0x00, 0x00 };
        for (var i = 0; i < 10; i++)
            bytes.AddRange(new[] { (byte)Math.Clamp(Math.Max(speeds[i], MinFanCurve[i]), 0, 100), (byte)0x00 });
        bytes.AddRange(new byte[] { 0x00, 0x0A, 0x00, 0x00, 0x00 });
        foreach (var t in FanTemps) bytes.AddRange(new[] { (byte)t, (byte)0x00 });
        bytes.Add(0x00);

        using var obj = Wmi.First(Ns, "LENOVO_FAN_METHOD") ?? throw new InvalidOperationException("No existe LENOVO_FAN_METHOD");
        using var inParams = obj.GetMethodParameters("Fan_Set_Table");
        inParams["FanTable"] = bytes.ToArray();
        obj.InvokeMethod("Fan_Set_Table", inParams, null)?.Dispose();
    }

    // ---------- Sensores (opcionales; si fallan se dejan de consultar) ----------

    public static double? TryApuTemp()
    {
        if (_apuTempUnsupported) return null;
        try { var t = GetFeature(ApuTempId); return t > 0 ? t : null; }
        catch (Exception) { _apuTempUnsupported = true; return null; }
    }

    public static double? TryFanRpm()
    {
        if (_fanUnsupported) return null;
        try { return GetFeature(FanRpmId); }
        catch (Exception) { _fanUnsupported = true; return null; }
    }
}
