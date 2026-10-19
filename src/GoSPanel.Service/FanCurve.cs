using System.Text.Json.Nodes;

namespace GoSPanel.Service;

/// <summary>
/// Curva de ventilador personalizada. Se guarda en el estado del servicio y se aplica cada vez que la consola
/// entra en modo Manual (el firmware solo la usa en ese modo y la olvida al cambiar de modo o al suspender).
/// </summary>
internal static class FanCurve
{
    public static JsonObject Get()
    {
        var saved = SavedState.Load()["fanCurve"] as JsonObject;
        var speeds = saved?["speeds"] is JsonArray a && a.Count == 10
            ? a.Select(v => v!.GetValue<int>()).ToArray()
            : LenovoWmi.MinFanCurve.ToArray();
        int[]? current = null;
        try { current = LenovoWmi.GetFanTable(); } catch (Exception) { }
        return new JsonObject
        {
            ["enabled"] = saved?["enabled"]?.GetValue<bool>() ?? false,
            ["speeds"] = new JsonArray(speeds.Select(v => (JsonNode)v).ToArray()),
            ["temps"] = new JsonArray(LenovoWmi.FanTemps.Select(v => (JsonNode)v).ToArray()),
            ["min"] = new JsonArray(LenovoWmi.MinFanCurve.Select(v => (JsonNode)v).ToArray()),
            ["firmware"] = current is null ? null : new JsonArray(current.Select(v => (JsonNode)v).ToArray()),
            ["active"] = (string?)LenovoWmi.GetPowerMode()["mode"] == "custom",
        };
    }

    public static JsonObject Set(JsonObject args)
    {
        SavedState.Update(s =>
        {
            var curve = s["fanCurve"] as JsonObject ?? new JsonObject();
            if (args["enabled"] is JsonValue e) curve["enabled"] = e.GetValue<bool>();
            if (args["speeds"] is JsonArray speeds)
            {
                if (speeds.Count != 10) throw new ArgumentException("La curva necesita 10 puntos");
                curve["speeds"] = new JsonArray(speeds.Select((v, i) =>
                    (JsonNode)Math.Clamp(Math.Max((int)v!.GetValue<double>(), LenovoWmi.MinFanCurve[i]), 0, 100)).ToArray());
            }
            s["fanCurve"] = curve;
        });

        var enabled = SavedState.Load()["fanCurve"]?["enabled"]?.GetValue<bool>() ?? false;
        if ((string?)LenovoWmi.GetPowerMode()["mode"] == "custom")
        {
            if (enabled) ApplySaved();
            else LenovoWmi.SetPowerMode("custom");   // volver a entrar en Manual restaura la curva de fábrica
        }
        return Get();
    }

    /// <summary>Aplica la curva guardada si está activada (llamar tras entrar en modo Manual).</summary>
    public static void ApplySaved()
    {
        if (SavedState.Load()["fanCurve"] is not JsonObject curve || curve["enabled"]?.GetValue<bool>() != true) return;
        if (curve["speeds"] is not JsonArray speeds || speeds.Count != 10) return;
        LenovoWmi.SetFanTable(speeds.Select(v => v!.GetValue<int>()).ToArray());
    }
}
