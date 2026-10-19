using System.Text.Json.Nodes;

namespace GoSPanel.Service;

/// <summary>
/// Protocolo: una línea JSON por petición {"cmd":"...","args":{...}}
/// y una línea JSON por respuesta {"ok":true,"data":...} | {"ok":false,"error":"..."}.
/// Solo se exponen comandos concretos; nunca llamadas WMI arbitrarias (el servicio corre como SYSTEM).
/// </summary>
public sealed class CommandRouter
{
    private readonly object _hardwareLock = new();

    public JsonObject Handle(JsonObject request)
    {
        var cmd = request["cmd"]?.GetValue<string>() ?? "";
        var args = request["args"] as JsonObject ?? new JsonObject();
        try
        {
            // Actualizaciones y desinstalación: no tocan hardware y pueden tardar (red), así que van sin el candado.
            JsonNode? data = cmd switch
            {
                "update.check" => Updater.CheckAsync(args["force"]?.GetValue<bool>() == true).GetAwaiter().GetResult(),
                "update.status" => Updater.Status(),
                "update.install" => Updater.Install(),
                "uninstall" => Updater.Uninstall(),
                _ => null,
            };
            if (data != null) return new JsonObject { ["ok"] = true, ["data"] = data };

            lock (_hardwareLock)
            {
                data = cmd switch
                {
                    "ping" => "pong",
                    "status" => SystemInfo.Status(),
                    "lenovo.probe" => LenovoWmi.Probe(),
                    "power.get" => ModeLimits.With(LenovoWmi.GetPowerMode()),
                    "power.set" => WithFanCurve(Remember(LenovoWmi.SetPowerMode(Required<string>(args, "mode")),
                        s => s["mode"] = Required<string>(args, "mode"))),
                    "brightness.get" => Brightness.Get(),
                    "brightness.set" => Brightness.Set((int)Required<double>(args, "value")),
                    "tdp.get" => LenovoWmi.GetTdp(),
                    "tdp.set" => WithFanCurve(Remember(LenovoWmi.SetTdp(
                            Required<double>(args, "spl"),
                            Required<double>(args, "sppt"),
                            Required<double>(args, "fppt")),
                        s =>
                        {
                            s["mode"] = "custom";
                            s["tdp"] = new JsonObject { ["spl"] = args["spl"]!.GetValue<double>(), ["sppt"] = args["sppt"]!.GetValue<double>(), ["fppt"] = args["fppt"]!.GetValue<double>() };
                        })),
                    "fancurve.get" => FanCurve.Get(),
                    "fancurve.set" => FanCurve.Set(args),
                    "fps.get" => FpsMonitor.Get(args["pid"]?.GetValue<int>()),
                    "fps.top" => FpsMonitor.Top(),
                    "boost.get" => CpuBoost.Get(),
                    "boost.set" => Remember(CpuBoost.Set(Required<bool>(args, "enabled")),
                        s => s["boost"] = Required<bool>(args, "enabled")),
                    "ls.launch" => LosslessHelper.Launch(Required<int>(args, "pid")),
                    "fan.get" => LenovoWmi.GetFullFan(),
                    "fan.set" => Remember(LenovoWmi.SetFullFan(Required<bool>(args, "enabled")),
                        s => s["fullFan"] = Required<bool>(args, "enabled")),
                    "charge.get" => LenovoWmi.GetChargeLimit(),
                    "charge.set" => Remember(LenovoWmi.SetChargeLimit(Required<bool>(args, "enabled")),
                        s => s["chargeLimit"] = Required<bool>(args, "enabled")),
                    "touchpad.get" => TouchDevices.GetTouchpad(),
                    "touchpad.set" => TouchDevices.SetTouchpad(Required<bool>(args, "enabled")),
                    "sas" => SecureAttention.Send(),
                    "legionspace.get" => LegionSpaceControl.Get(),
                    "legionspace.set" => LegionSpaceControl.Set(Required<bool>(args, "enabled")),
                    "lighting.prioritize" => LightingPriority.Prioritize(Required<string>(args, "sid")),
                    "touchscreen.get" => TouchDevices.GetTouchscreen(),
                    "touchscreen.set" => TouchDevices.SetTouchscreen(Required<bool>(args, "enabled")),
                    "settings.get" => new JsonObject { ["reapplyOnResume"] = SavedState.ReapplyOnResume },
                    "settings.set" => Remember(new JsonObject { ["reapplyOnResume"] = Required<bool>(args, "reapplyOnResume") },
                        s => s["reapplyOnResume"] = Required<bool>(args, "reapplyOnResume")),
                    _ => throw new ArgumentException($"Comando desconocido: {cmd}"),
                };
            }
            return new JsonObject { ["ok"] = true, ["data"] = data };
        }
        catch (Exception ex)
        {
            return Error(ex.Message);
        }
    }

    public static JsonObject Error(string message) => new() { ["ok"] = false, ["error"] = message };

    /// <summary>Guarda el ajuste aplicado (solo si se aplicó sin error) para reaplicarlo tras suspender.</summary>
    /// <summary>Tras cambiar de modo: en Manual se vuelve a aplicar la curva de ventilador guardada.</summary>
    private static JsonObject WithFanCurve(JsonObject result)
    {
        try
        {
            if ((string?)LenovoWmi.GetPowerMode()["mode"] == "custom") FanCurve.ApplySaved();
        }
        catch (Exception) { /* la curva es opcional: no debe hacer fallar el cambio de modo */ }
        return result;
    }

    private static JsonObject Remember(JsonObject result, Action<JsonObject> save)
    {
        SavedState.Update(save);
        return result;
    }

    private static T Required<T>(JsonObject args, string name) =>
        args[name] is JsonNode n ? n.GetValue<T>() : throw new ArgumentException($"Falta el argumento '{name}'");
}
