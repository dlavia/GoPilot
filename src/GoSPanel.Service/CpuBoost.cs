using System.Runtime.InteropServices;
using System.Text.Json.Nodes;

namespace GoSPanel.Service;

/// <summary>
/// CPU Boost mediante el ajuste oculto «Modo de aumento del rendimiento del procesador» (PERFBOOSTMODE)
/// del plan de energía activo, para corriente y batería. 0 = desactivado; 2 = agresivo (valor de Windows).
/// </summary>
internal static class CpuBoost
{
    private static Guid _subProcessor = new("54533251-82be-4824-96c1-47b60b740d00");
    private static Guid _perfBoostMode = new("be337238-0d82-4146-a960-4f3749d470c7");
    private const uint Disabled = 0, Aggressive = 2;

    public static JsonObject Get()
    {
        var scheme = ActiveScheme();
        Check(PowerReadACValueIndex(IntPtr.Zero, ref scheme, ref _subProcessor, ref _perfBoostMode, out var ac));
        Check(PowerReadDCValueIndex(IntPtr.Zero, ref scheme, ref _subProcessor, ref _perfBoostMode, out var dc));
        return new JsonObject { ["enabled"] = ac != Disabled || dc != Disabled, ["ac"] = ac, ["dc"] = dc };
    }

    public static JsonObject Set(bool enabled)
    {
        var scheme = ActiveScheme();
        var value = enabled ? Aggressive : Disabled;
        Check(PowerWriteACValueIndex(IntPtr.Zero, ref scheme, ref _subProcessor, ref _perfBoostMode, value));
        Check(PowerWriteDCValueIndex(IntPtr.Zero, ref scheme, ref _subProcessor, ref _perfBoostMode, value));
        Check(PowerSetActiveScheme(IntPtr.Zero, ref scheme));   // aplicar el cambio al momento
        return Get();
    }

    private static Guid ActiveScheme()
    {
        Check(PowerGetActiveScheme(IntPtr.Zero, out var ptr));
        try { return Marshal.PtrToStructure<Guid>(ptr); }
        finally { LocalFree(ptr); }
    }

    private static void Check(uint result)
    {
        if (result != 0) throw new InvalidOperationException($"Error del plan de energía ({result})");
    }

    [DllImport("powrprof.dll")] private static extern uint PowerGetActiveScheme(IntPtr root, out IntPtr scheme);
    [DllImport("powrprof.dll")] private static extern uint PowerSetActiveScheme(IntPtr root, ref Guid scheme);
    [DllImport("powrprof.dll")] private static extern uint PowerReadACValueIndex(IntPtr root, ref Guid scheme, ref Guid subgroup, ref Guid setting, out uint value);
    [DllImport("powrprof.dll")] private static extern uint PowerReadDCValueIndex(IntPtr root, ref Guid scheme, ref Guid subgroup, ref Guid setting, out uint value);
    [DllImport("powrprof.dll")] private static extern uint PowerWriteACValueIndex(IntPtr root, ref Guid scheme, ref Guid subgroup, ref Guid setting, uint value);
    [DllImport("powrprof.dll")] private static extern uint PowerWriteDCValueIndex(IntPtr root, ref Guid scheme, ref Guid subgroup, ref Guid setting, uint value);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr memory);
}
