using System.Runtime.InteropServices;
using System.Text.Json.Nodes;

namespace GoSPanel.Bridge;

/// <summary>
/// Brillo automático de Windows («Cambiar el brillo automáticamente cuando cambie la iluminación»): es el ajuste
/// ADAPTBRIGHT del plan de energía activo, con y sin cargador. Solo tiene sentido si hay sensor de luz.
/// </summary>
internal static class AutoBrightness
{
    private static Guid _videoGroup = new("7516b95f-f776-4464-8c53-06167f40cc99");
    private static Guid _adaptive = new("fbd9aa66-9553-4097-ba44-ed6e9d65eab8");

    public static JsonObject Get()
    {
        var scheme = ActiveScheme();
        PowerReadACValueIndex(IntPtr.Zero, ref scheme, ref _videoGroup, ref _adaptive, out var ac);
        PowerReadDCValueIndex(IntPtr.Zero, ref scheme, ref _videoGroup, ref _adaptive, out var dc);
        bool sensor;
        try { sensor = Windows.Devices.Sensors.LightSensor.GetDefault() != null; }
        catch (Exception) { sensor = false; }
        return new JsonObject { ["enabled"] = ac != 0 || dc != 0, ["sensor"] = sensor };
    }

    public static JsonObject Set(bool enabled)
    {
        var scheme = ActiveScheme();
        var value = enabled ? 1u : 0u;
        Check(PowerWriteACValueIndex(IntPtr.Zero, ref scheme, ref _videoGroup, ref _adaptive, value), "AC");
        Check(PowerWriteDCValueIndex(IntPtr.Zero, ref scheme, ref _videoGroup, ref _adaptive, value), "DC");
        Check(PowerSetActiveScheme(IntPtr.Zero, ref scheme), "activar el plan");   // sin esto no se aplica hasta cambiar de plan
        return Get();
    }

    private static Guid ActiveScheme()
    {
        Check(PowerGetActiveScheme(IntPtr.Zero, out var pointer), "leer el plan");
        try { return Marshal.PtrToStructure<Guid>(pointer); }
        finally { LocalFree(pointer); }
    }

    private static void Check(uint result, string what)
    {
        if (result != 0) throw new InvalidOperationException($"Brillo automático: no se pudo {what} (error {result})");
    }

    [DllImport("powrprof.dll")] private static extern uint PowerGetActiveScheme(IntPtr root, out IntPtr scheme);
    [DllImport("powrprof.dll")] private static extern uint PowerSetActiveScheme(IntPtr root, ref Guid scheme);
    [DllImport("powrprof.dll")] private static extern uint PowerReadACValueIndex(IntPtr root, ref Guid scheme, ref Guid group, ref Guid setting, out uint value);
    [DllImport("powrprof.dll")] private static extern uint PowerReadDCValueIndex(IntPtr root, ref Guid scheme, ref Guid group, ref Guid setting, out uint value);
    [DllImport("powrprof.dll")] private static extern uint PowerWriteACValueIndex(IntPtr root, ref Guid scheme, ref Guid group, ref Guid setting, uint value);
    [DllImport("powrprof.dll")] private static extern uint PowerWriteDCValueIndex(IntPtr root, ref Guid scheme, ref Guid group, ref Guid setting, uint value);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr memory);
}
