using System.Management;

namespace GoSPanel.Service;

/// <summary>
/// Escucha los cambios de modo térmico que hace el propio firmware (Legion L + Y cambia de modo sin Legion Space:
/// Silencio → Equilibrado → Rendimiento → Manual). Así el modo guardado no se queda atrás y, al entrar en
/// Manual con el botón, se aplica la curva de ventilador propia.
/// </summary>
public sealed class ThermalModeWatcher(ILogger<ThermalModeWatcher> log) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken ct)
    {
        try
        {
            var watcher = new ManagementEventWatcher(new ManagementScope(@"root\WMI"),
                new WqlEventQuery("SELECT * FROM LENOVO_GAMEZONE_THERMAL_MODE_EVENT"));
            watcher.EventArrived += (_, e) =>
            {
                try
                {
                    var mode = (string?)LenovoWmi.GetPowerMode()["mode"];
                    if (mode is null or "unknown") return;
                    SavedState.Update(s => s["mode"] = mode);
                    if (mode == "custom") FanCurve.ApplySaved();
                }
                catch (Exception ex)
                {
                    log.LogWarning(ex, "No se pudo procesar el cambio de modo térmico");
                }
            };
            watcher.Start();
            ct.Register(() => { watcher.Stop(); watcher.Dispose(); });
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "No se pueden escuchar los cambios de modo del firmware");
        }
        return Task.CompletedTask;
    }
}
