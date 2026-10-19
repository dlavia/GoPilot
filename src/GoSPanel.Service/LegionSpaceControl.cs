using System.Diagnostics;
using Microsoft.Win32;
using System.ServiceProcess;
using System.Text.Json.Nodes;

namespace GoSPanel.Service;

/// <summary>
/// Activa o desactiva Legion Space sin desinstalarlo. Todo lo arranca el servicio de Lenovo «DAService»
/// (LSDaemon.exe), que a su vez lanza LegionSpace.exe y LegionSettingMenu.exe en la sesión del usuario.
/// Desactivar = parar el servicio, quitarle el inicio automático y cerrar sus procesos.
/// Activar = inicio automático y arrancarlo (él vuelve a abrir el resto).
/// </summary>
internal static class LegionSpaceControl
{
    private const string ServiceName = "DAService";

    // Hueco del fabricante en Game Bar (el primer icono de la izquierda): Game Bar lee aquí el PFN del widget
    // (GameBar.exe › FindOemWidget). Con Legion Space desactivado se pone GoPilot; al activarlo vuelve Lenovo.
    private const string GamingConfigKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\GamingConfiguration";
    private const string OemWidgetValue = "OEMGameBarwidget";
    private const string GoPilotFamily = "GoSPanel_3ncfs0ed3kzx8";
    private const string LenovoFamily = "LegionGoGameBarWidget_ra2g5j82mn5h8";
    private static readonly string[] Processes = { "LegionSpace", "LegionSettingMenu", "LegionGoGameBarWidget", "LSDaemon" };

    public static JsonObject Get()
    {
        using var service = Find();
        if (service == null) return new JsonObject { ["installed"] = false, ["enabled"] = false, ["running"] = false };
        return new JsonObject
        {
            ["installed"] = true,
            ["enabled"] = service.StartType != ServiceStartMode.Disabled,
            // LegionGoGameBarWidget no cuenta: es su widget de Game Bar y Game Bar lo abre por su cuenta.
            ["oemWidget"] = Registry.LocalMachine.OpenSubKey(GamingConfigKey)?.GetValue(OemWidgetValue) as string,
            ["running"] = service.Status == ServiceControllerStatus.Running
                          || Processes.Where(p => p != "LegionGoGameBarWidget").Any(p => Process.GetProcessesByName(p).Length > 0),
        };
    }

    public static JsonObject Set(bool enabled)
    {
        using var service = Find() ?? throw new InvalidOperationException("Legion Space no está instalado");
        if (enabled)
        {
            Run("sc.exe", $"config {ServiceName} start= auto");
            service.Refresh();
            if (service.Status != ServiceControllerStatus.Running)
            {
                service.Start();
                service.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(15));
            }
        }
        else
        {
            Run("sc.exe", $"config {ServiceName} start= disabled");
            service.Refresh();
            if (service.Status != ServiceControllerStatus.Stopped)
            {
                try
                {
                    service.Stop();
                    service.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(15));
                }
                catch (Exception) { /* si no se deja parar, se cierra a la fuerza abajo */ }
            }
            foreach (var name in Processes)
                foreach (var process in Process.GetProcessesByName(name))
                    try { process.Kill(entireProcessTree: true); } catch (Exception) { }
        }
        SetOemWidget(enabled);
        return Get();
    }

    /// <summary>GoPilot en el hueco del fabricante (Legion Space desactivado) o el valor original de Lenovo.</summary>
    private static void SetOemWidget(bool legionSpace)
    {
        try
        {
            using var key = Registry.LocalMachine.CreateSubKey(GamingConfigKey);
            var current = key.GetValue(OemWidgetValue) as string;
            if (!legionSpace)
            {
                if (current != null && current != GoPilotFamily) SavedState.Update(s => s["oemWidgetOriginal"] = current);
                key.SetValue(OemWidgetValue, GoPilotFamily, RegistryValueKind.String);
            }
            else
            {
                var original = SavedState.Load()["oemWidgetOriginal"]?.GetValue<string>() ?? LenovoFamily;
                key.SetValue(OemWidgetValue, original, RegistryValueKind.String);
            }
            // Game Bar solo lo lee al arrancar
            foreach (var name in new[] { "GameBar", "GameBarFTServer" })
                foreach (var p in Process.GetProcessesByName(name))
                    try { p.Kill(); } catch (Exception) { }
        }
        catch (Exception) { /* el hueco es opcional */ }
    }

    private static ServiceController? Find() =>
        ServiceController.GetServices().FirstOrDefault(s => s.ServiceName.Equals(ServiceName, StringComparison.OrdinalIgnoreCase));

    private static void Run(string file, string args)
    {
        using var p = Process.Start(new ProcessStartInfo(file, args) { CreateNoWindow = true, UseShellExecute = false })!;
        p.WaitForExit(10000);
    }
}
