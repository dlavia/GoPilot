using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using Microsoft.Win32;

namespace GoSPanel.Service;

/// <summary>
/// Equivalente a Ctrl + Alt + Supr (pantalla de seguridad de Windows), para el atajo Legion L + A.
/// Solo un servicio puede generarlo con SendSAS, y Windows lo permite si la directiva SoftwareSASGeneration
/// lo autoriza (1 = servicios); si no está, se activa la primera vez.
/// </summary>
internal static class SecureAttention
{
    private const string PolicyKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System";

    public static JsonObject Send()
    {
        using (var key = Registry.LocalMachine.CreateSubKey(PolicyKey))
        {
            var original = key.GetValue("SoftwareSASGeneration") as int?;
            var current = original ?? 0;
            if ((current & 1) == 0)
            {
                // El valor de antes (-1 = no existía) se guarda para dejarlo como estaba al desinstalar
                if (SavedState.Load()["sasOriginal"] == null) SavedState.Update(s => s["sasOriginal"] = original ?? -1);
                key.SetValue("SoftwareSASGeneration", current | 1, RegistryValueKind.DWord);
            }
        }
        SendSAS(false);
        return new JsonObject { ["sent"] = true };
    }

    /// <summary>Al desinstalar: la directiva vuelve a como estaba antes de GoPilot.</summary>
    public static void Restore()
    {
        using var key = Registry.LocalMachine.OpenSubKey(PolicyKey, writable: true);
        if (key == null) return;
        var saved = SavedState.Load()["sasOriginal"]?.GetValue<int>();
        // Versiones anteriores no guardaban el original: 1 (solo servicios) es justo lo que pone GoPilot sobre «no existía».
        var original = saved ?? (key.GetValue("SoftwareSASGeneration") as int? == 1 ? -1 : (int?)null);
        if (original == null) return;
        if (original == -1) key.DeleteValue("SoftwareSASGeneration", false);
        else key.SetValue("SoftwareSASGeneration", original.Value, RegistryValueKind.DWord);
    }

    [DllImport("sas.dll")] private static extern void SendSAS(bool asUser);
}
