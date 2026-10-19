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
            var current = key.GetValue("SoftwareSASGeneration") as int? ?? 0;
            if ((current & 1) == 0) key.SetValue("SoftwareSASGeneration", current | 1, RegistryValueKind.DWord);
        }
        SendSAS(false);
        return new JsonObject { ["sent"] = true };
    }

    [DllImport("sas.dll")] private static extern void SendSAS(bool asUser);
}
