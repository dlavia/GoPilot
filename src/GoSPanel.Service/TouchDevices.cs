using System.Runtime.InteropServices;
using System.Text.Json.Nodes;

namespace GoSPanel.Service;

/// <summary>
/// Touchpad y pantalla táctil: se activan o desactivan como dispositivos (igual que en el Administrador de
/// dispositivos). En la Go S el touchpad es una colección HID del mando y la orden WMI de Lenovo (SetTPStatus)
/// no tiene efecto. Se localizan por su uso HID: 0x0D/0x05 = touch pad, 0x0D/0x04 = touch screen.
/// </summary>
internal static class TouchDevices
{
    private const string TouchpadId = "HID_DEVICE_UP:000D_U:0005";
    private const string TouchscreenId = "HID_DEVICE_UP:000D_U:0004";

    public static JsonObject GetTouchpad() => Get(TouchpadId);
    // El cursor del touchpad de la Go S también llega por la colección de ratón de la misma interfaz del mando
    // (MI_02&COL01): desactivar solo la de «touch pad» no basta, así que se cambian las dos.
    public static JsonObject SetTouchpad(bool enabled) => Set(TouchpadId, enabled, includeSiblingMouse: true);
    public static JsonObject GetTouchscreen() => Get(TouchscreenId);
    public static JsonObject SetTouchscreen(bool enabled) => Set(TouchscreenId, enabled);

    private static JsonObject Get(string hardwareId)
    {
        var devices = Find(hardwareId);
        if (devices.Count == 0) return new JsonObject { ["supported"] = false, ["enabled"] = false };
        return new JsonObject { ["supported"] = true, ["enabled"] = devices.Any(d => !IsDisabled(d)) };
    }

    private static JsonObject Set(string hardwareId, bool enabled, bool includeSiblingMouse = false)
    {
        var devices = Find(hardwareId);
        if (devices.Count == 0) throw new InvalidOperationException("No se encontró el dispositivo");
        if (includeSiblingMouse) devices.AddRange(devices.SelectMany(SiblingMice).ToList());
        foreach (var dev in devices)
        {
            if (IsDisabled(dev) != enabled) continue;   // ya está como se pide
            var result = enabled ? CM_Enable_DevNode(dev, 0) : CM_Disable_DevNode(dev, CmDisablePersist | CmDisableUiNotOk);
            if (result != 0) throw new InvalidOperationException($"No se pudo cambiar el dispositivo (CR {result})");
        }
        return Get(hardwareId);
    }

    private static bool IsDisabled(uint devInst) =>
        CM_Get_DevNode_Status(out _, out var problem, devInst, 0) == 0 && problem == CmProbDisabled;

    /// <summary>Nodos HID presentes (incluidos los desactivados) cuyo identificador de hardware coincide.</summary>
    private static List<uint> Find(string hardwareId)
    {
        var found = new List<uint>();
        const uint flags = CmGetIdListFilterEnumerator | CmGetIdListFilterPresent;
        if (CM_Get_Device_ID_List_Size(out var size, "HID", flags) != 0 || size == 0) return found;
        var buffer = new char[size];
        if (CM_Get_Device_ID_List("HID", buffer, size, flags) != 0) return found;

        foreach (var id in new string(buffer).Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            if (CM_Locate_DevNode(out var dev, id, 0) != 0) continue;
            if (HardwareIds(dev).Any(h => h.Equals(hardwareId, StringComparison.OrdinalIgnoreCase))) found.Add(dev);
        }
        return found;
    }

    /// <summary>Colecciones de ratón que cuelgan del mismo dispositivo HID que <paramref name="dev"/>.</summary>
    private static IEnumerable<uint> SiblingMice(uint dev)
    {
        if (CM_Get_Parent(out var parent, dev, 0) != 0 || CM_Get_Child(out var child, parent, 0) != 0) yield break;
        do
        {
            if (child != dev && HardwareIds(child).Any(h => h.Equals("HID_DEVICE_SYSTEM_MOUSE", StringComparison.OrdinalIgnoreCase)))
                yield return child;
        } while (CM_Get_Sibling(out child, child, 0) == 0);
    }

    private static string[] HardwareIds(uint dev)
    {
        uint length = 0;
        CM_Get_DevNode_Registry_Property(dev, CmDrpHardwareId, out _, null, ref length, 0);
        if (length == 0) return Array.Empty<string>();
        var data = new byte[length];
        if (CM_Get_DevNode_Registry_Property(dev, CmDrpHardwareId, out _, data, ref length, 0) != 0) return Array.Empty<string>();
        return System.Text.Encoding.Unicode.GetString(data, 0, (int)length).Split('\0', StringSplitOptions.RemoveEmptyEntries);
    }

    private const uint CmGetIdListFilterEnumerator = 0x1, CmGetIdListFilterPresent = 0x100;
    private const uint CmDrpHardwareId = 0x2;
    private const uint CmProbDisabled = 22;
    private const uint CmDisableUiNotOk = 0x4, CmDisablePersist = 0x8;

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern uint CM_Get_Device_ID_List_Size(out uint length, string filter, uint flags);
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern uint CM_Get_Device_ID_List(string filter, char[] buffer, uint length, uint flags);
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern uint CM_Locate_DevNode(out uint devInst, string deviceId, uint flags);
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern uint CM_Get_DevNode_Registry_Property(uint devInst, uint property, out uint type, byte[]? buffer, ref uint length, uint flags);
    [DllImport("cfgmgr32.dll")]
    private static extern uint CM_Get_DevNode_Status(out uint status, out uint problem, uint devInst, uint flags);
    [DllImport("cfgmgr32.dll")]
    private static extern uint CM_Get_Parent(out uint parent, uint devInst, uint flags);
    [DllImport("cfgmgr32.dll")]
    private static extern uint CM_Get_Child(out uint child, uint devInst, uint flags);
    [DllImport("cfgmgr32.dll")]
    private static extern uint CM_Get_Sibling(out uint sibling, uint devInst, uint flags);
    [DllImport("cfgmgr32.dll")]
    private static extern uint CM_Enable_DevNode(uint devInst, uint flags);
    [DllImport("cfgmgr32.dll")]
    private static extern uint CM_Disable_DevNode(uint devInst, uint flags);
}
