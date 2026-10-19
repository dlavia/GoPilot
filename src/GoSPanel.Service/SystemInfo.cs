using System.Runtime.InteropServices;
using System.Text.Json.Nodes;

namespace GoSPanel.Service;

internal static class SystemInfo
{
    public static JsonObject Status() => new()
    {
        ["battery"] = Battery(),
        ["cpu"] = new JsonObject
        {
            ["loadPct"] = CpuLoad.Sample(),
            ["tempC"] = LenovoWmi.TryApuTemp() ?? AcpiTempC(),
        },
        ["fan"] = new JsonObject { ["rpm"] = LenovoWmi.TryFanRpm() },
    };

    private static JsonObject Battery()
    {
        const string ns = @"root\WMI";
        try
        {
            using var status = Wmi.First(ns, "BatteryStatus");
            using var full = Wmi.First(ns, "BatteryFullChargedCapacity");
            using var design = Wmi.First(ns, "BatteryStaticData");

            var remaining = Wmi.Number(status, "RemainingCapacity");
            var fullCap = Wmi.Number(full, "FullChargedCapacity");
            var designCap = Wmi.Number(design, "DesignedCapacity");
            var charging = Wmi.Bool(status, "Charging") ?? false;
            var rateMw = charging ? Wmi.Number(status, "ChargeRate") : Wmi.Number(status, "DischargeRate");

            return new JsonObject
            {
                ["percent"] = remaining is > 0 && fullCap is > 0 ? Math.Round(remaining.Value / fullCap.Value * 100) : null,
                ["charging"] = charging,
                ["acOnline"] = Wmi.Bool(status, "PowerOnline"),
                ["rateW"] = rateMw is null ? null : Math.Round(rateMw.Value / 1000, 1),
                ["remainingWh"] = remaining is null ? null : Math.Round(remaining.Value / 1000, 2),
                ["fullWh"] = fullCap is null ? null : Math.Round(fullCap.Value / 1000, 2),
                ["healthPct"] = fullCap is > 0 && designCap is > 0 ? Math.Round(fullCap.Value / designCap.Value * 100) : null,
            };
        }
        catch (Exception)
        {
            return new JsonObject();
        }
    }

    private static double? AcpiTempC()
    {
        try
        {
            using var zone = Wmi.First(@"root\WMI", "MSAcpi_ThermalZoneTemperature");
            var tenthsKelvin = Wmi.Number(zone, "CurrentTemperature");
            var celsius = tenthsKelvin is null ? 0 : Math.Round(tenthsKelvin.Value / 10 - 273.15, 1);
            return celsius > 0 ? celsius : null;   // algunas placas exponen la zona pero no la rellenan
        }
        catch (Exception) { return null; }
    }

    /// <summary>Uso de CPU entre dos muestras consecutivas (la primera devuelve null).</summary>
    private static class CpuLoad
    {
        private static readonly object Lock = new();
        private static long _idle, _kernel, _user;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetSystemTimes(out long idle, out long kernel, out long user);

        public static double? Sample()
        {
            lock (Lock)
            {
                if (!GetSystemTimes(out var idle, out var kernel, out var user)) return null;
                double? load = null;
                var total = (kernel - _kernel) + (user - _user);
                if (_kernel != 0 && total > 0)
                    load = Math.Round((1 - (double)(idle - _idle) / total) * 100);
                (_idle, _kernel, _user) = (idle, kernel, user);
                return load;
            }
        }
    }
}
