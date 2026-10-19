using System.Management;
using System.Text.Json.Nodes;

namespace GoSPanel.Service;

/// <summary>Brillo de la pantalla interna mediante la WMI estándar de Windows (root\WMI).</summary>
internal static class Brightness
{
    private const string Ns = @"root\WMI";

    public static JsonObject Get()
    {
        using var monitor = Wmi.First(Ns, "WmiMonitorBrightness")
            ?? throw new InvalidOperationException("La pantalla no admite control de brillo");
        return new JsonObject { ["value"] = Convert.ToInt32(monitor["CurrentBrightness"]) };
    }

    public static JsonObject Set(int value)
    {
        value = Math.Clamp(value, 0, 100);
        using var methods = Wmi.First(Ns, "WmiMonitorBrightnessMethods")
            ?? throw new InvalidOperationException("La pantalla no admite control de brillo");
        methods.InvokeMethod("WmiSetBrightness", new object[] { (uint)0, (byte)value });
        return new JsonObject { ["value"] = value };
    }
}
