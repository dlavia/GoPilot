﻿using System.Runtime.InteropServices;
using System.Text.Json.Nodes;

namespace GoSPanel.Service;

/// <summary>
/// Límites nominales (SPL / sPPT / fPPT) de cada modo de Lenovo, para las baldosas del widget. Cambian enchufada o con
/// batería. No se leen del firmware: sus valores activos van cambiando (Rendimiento oscila entre 25/25/40 y 30/35/40
/// según la temperatura, y tras cambiar de modo da un segundo los del anterior), así que una lectura suelta engañaba
/// (llegaron a verse 22 W en Silencio). Medidos en una Legion Go S Z1 Extreme (2026-10-19); el consumo real de cada
/// momento lo muestra el pie del widget.
/// </summary>
internal static class ModeLimits
{
    private static readonly Dictionary<string, (int Spl, int Sppt, int Fppt)> Ac = new()
    {
        ["quiet"] = (10, 15, 20), ["balanced"] = (16, 25, 30), ["performance"] = (30, 35, 40),
    };
    private static readonly Dictionary<string, (int Spl, int Sppt, int Fppt)> Dc = new()
    {
        ["quiet"] = (10, 10, 20), ["balanced"] = (15, 15, 30), ["performance"] = (25, 25, 35),
    };

    /// <summary>Añade a power.get si está enchufada y los límites de los tres modos para esa fuente.</summary>
    public static JsonObject With(JsonObject power)
    {
        var ac = OnAc();
        var result = new JsonObject();
        foreach (var (mode, d) in ac ? Ac : Dc)
            result[mode] = new JsonObject { ["spl"] = d.Spl, ["sppt"] = d.Sppt, ["fppt"] = d.Fppt };
        power["ac"] = ac;
        power["limits"] = result;
        return power;
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
