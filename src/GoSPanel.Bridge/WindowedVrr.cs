using System.Text.Json.Nodes;
using Microsoft.Win32;

namespace GoSPanel.Bridge;

/// <summary>
/// Ajuste de Windows «Optimizaciones para juegos en ventana» (Configuración › Pantalla › Gráficos): convierte los
/// juegos en ventana / sin bordes al modo de presentación moderno para que puedan usar VRR (FreeSync).
/// Vive en HKCU\Software\Microsoft\DirectX\UserGpuPreferences, valor DirectXUserGlobalSettings
/// («SwapEffectUpgradeEnable=1;»). Se aplica al abrir el juego.
/// </summary>
internal static class WindowedVrr
{
    private const string KeyPath = @"Software\Microsoft\DirectX\UserGpuPreferences";
    private const string ValueName = "DirectXUserGlobalSettings";
    private const string Setting = "SwapEffectUpgradeEnable";

    public static JsonObject Get() => new() { ["enabled"] = Read().TryGetValue(Setting, out var v) && v == "1" };

    public static JsonObject Set(bool enabled)
    {
        var settings = Read();
        settings[Setting] = enabled ? "1" : "0";
        using var key = Registry.CurrentUser.CreateSubKey(KeyPath);
        key.SetValue(ValueName, string.Concat(settings.Select(kv => $"{kv.Key}={kv.Value};")), RegistryValueKind.String);
        return Get();
    }

    /// <summary>«A=1;B=0;» → diccionario (conserva el resto de ajustes de Windows).</summary>
    private static Dictionary<string, string> Read()
    {
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
        var raw = key?.GetValue(ValueName) as string ?? "";
        return raw.Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split('=', 2))
            .Where(p => p.Length == 2)
            .GroupBy(p => p[0])
            .ToDictionary(g => g.Key, g => g.Last()[1]);
    }
}
