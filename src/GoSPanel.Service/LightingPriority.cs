using System.Security.Principal;
using System.Text.Json.Nodes;
using Microsoft.Win32;

namespace GoSPanel.Service;

/// <summary>
/// Iluminación dinámica de Windows: pone GoPilot el primero en la lista de apps que controlan las luces en segundo
/// plano (Configuración › Personalización › Iluminación dinámica), para que el efecto «Reactivo» de los sticks
/// funcione con el juego delante sin que el usuario tenga que tocar nada. Windows guarda el orden en
/// HKCU\Software\Microsoft\Lighting\Providers (y en cada dispositivo, …\Devices\&lt;id&gt;\Providers) como valores
/// «1», «2»… con el nombre de familia del paquete; el primero manda. Lo hace el servicio porque el agente, al ser una
/// app empaquetada, escribe en una copia privada del registro del usuario.
/// </summary>
internal static class LightingPriority
{
    private const string GoPilot = "GoSPanel_3ncfs0ed3kzx8";

    public static JsonObject Prioritize(string sid)
    {
        // Solo un SID de usuario con su perfil cargado (el de la sesión que lo pide).
        var parsed = new SecurityIdentifier(sid);
        using var root = Registry.Users.OpenSubKey(parsed.Value + @"\Software\Microsoft\Lighting", writable: true)
            ?? throw new InvalidOperationException("Ese usuario no tiene iluminación dinámica");

        var changed = 0;
        changed += Reorder(root, "Providers");
        using (var devices = root.OpenSubKey("Devices"))
        {
            foreach (var device in devices?.GetSubKeyNames() ?? Array.Empty<string>())
                changed += Reorder(root, @"Devices\" + device + @"\Providers");
        }
        return new JsonObject { ["changed"] = changed };
    }

    /// <summary>Deja GoPilot como «1» y el resto detrás, en su orden. Devuelve 1 si ha cambiado algo.</summary>
    private static int Reorder(RegistryKey root, string path)
    {
        using var key = root.OpenSubKey(path, writable: true);
        if (key == null) return 0;
        var providers = key.GetValueNames()
            .Where(n => int.TryParse(n, out _))
            .OrderBy(n => int.Parse(n))
            .Select(n => key.GetValue(n) as string)
            .Where(v => !string.IsNullOrEmpty(v))
            .Cast<string>()
            .ToList();
        if (providers.FirstOrDefault() == GoPilot || !providers.Contains(GoPilot)) return 0;

        providers.Remove(GoPilot);
        providers.Insert(0, GoPilot);
        for (var i = 0; i < providers.Count; i++) key.SetValue((i + 1).ToString(), providers[i], RegistryValueKind.String);
        return 1;
    }
}
