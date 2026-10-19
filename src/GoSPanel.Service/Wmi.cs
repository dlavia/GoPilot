using System.Globalization;
using System.Management;

namespace GoSPanel.Service;

internal static class Wmi
{
    public static ManagementObject? First(string ns, string cls)
    {
        using var searcher = new ManagementObjectSearcher(ns, $"SELECT * FROM {cls}");
        foreach (ManagementObject o in searcher.Get())
            return o;
        return null;
    }

    public static double? Number(ManagementBaseObject? o, string prop)
    {
        try
        {
            var v = o?[prop];
            return v is null ? null : Convert.ToDouble(v, CultureInfo.InvariantCulture);
        }
        catch (ManagementException) { return null; }
    }

    public static bool? Bool(ManagementBaseObject? o, string prop)
    {
        try { return o?[prop] as bool?; }
        catch (ManagementException) { return null; }
    }

    /// <summary>Convierte un valor (número, texto o "0x..." hexadecimal) al tipo CIM del parámetro.</summary>
    public static object ToCim(object value, CimType type)
    {
        if (value is string s && s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            value = ulong.Parse(s[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture);

        var c = CultureInfo.InvariantCulture;
        return type switch
        {
            CimType.UInt8 => Convert.ToByte(value, c),
            CimType.SInt8 => Convert.ToSByte(value, c),
            CimType.UInt16 => Convert.ToUInt16(value, c),
            CimType.SInt16 => Convert.ToInt16(value, c),
            CimType.UInt32 => Convert.ToUInt32(value, c),
            CimType.SInt32 => Convert.ToInt32(value, c),
            CimType.UInt64 => Convert.ToUInt64(value, c),
            CimType.SInt64 => Convert.ToInt64(value, c),
            CimType.Boolean => Convert.ToBoolean(value, c),
            CimType.Real32 => Convert.ToSingle(value, c),
            CimType.Real64 => Convert.ToDouble(value, c),
            _ => Convert.ToString(value, c) ?? "",
        };
    }
}
