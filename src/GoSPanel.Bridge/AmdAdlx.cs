using System.Runtime.InteropServices;
using System.Text.Json.Nodes;

namespace GoSPanel.Bridge;

/// <summary>
/// Ajustes de Radeon mediante AMD ADLX (amdadlx64.dll, incluida en el driver). ADLX es una API C++ sin
/// enlaces para .NET: se llama directamente a sus tablas de métodos. Los índices salen de las estructuras
/// «...Vtbl» de las cabeceras oficiales (GPUOpen-LibrariesAndSDKs/ADLX, SDK/Include) y NO deben cambiarse.
/// Todos los ajustes son globales (perfil «Gráficos globales» del software Radeon).
/// </summary>
internal static unsafe class AmdAdlx
{
    // IADLXSystem (sin Acquire/Release)
    private const int SysGetGpus = 1, SysGetDisplaysServices = 3, SysGet3DSettingsServices = 7;
    // Comunes a todas las interfaces derivadas de IADLXInterface
    private const int Release = 1, QueryInterface = 2;
    // IADLXGPUList / IADLXDisplayList
    private const int ListSize = 3, ListAtGpu = 11, ListAtDisplay = 11;
    // IADLX3DSettingsServices / IADLX3DSettingsServices1
    private const int S3DGetAntiLag = 3, S3DGetChill = 4, S3DGetImageSharpening = 6, S3DGetRsr = 14, S3D1GetAfmf = 17;
    // IADLXDisplayServices
    private const int DispGetDisplays = 4, DispGetFreeSync = 9, DispGetVariBright = 19;
    // Interfaces de «interruptor» (AFMF, RSR, Anti-Lag, Vari-Bright): IsSupported, IsEnabled, SetEnabled
    private const int IsSupported = 3, IsEnabled = 4, SetEnabled = 5;
    // IADLX3DChill
    private const int ChillGetFpsRange = 5, ChillGetMinFps = 6, ChillGetMaxFps = 7, ChillSetEnabled = 8, ChillSetMinFps = 9, ChillSetMaxFps = 10;

    [StructLayout(LayoutKind.Sequential)]
    private struct IntRange { public int Min, Max, Step; }
    // IADLX3DImageSharpening
    private const int SharpGetSharpness = 6, SharpSetEnabled = 7;

    private const int AdlxOk = 0, AdlxAlreadyEnabled = 1, AdlxAlreadyInitialized = 2;

    private static readonly object Lock = new();
    private static IntPtr _system, _gpu, _services3D, _services3D1, _displayServices, _display;
    private static string? _initError;

    public static readonly string[] Features = { "afmf", "rsr", "chill", "antilag", "sharpening", "varibright", "freesync" };

    public static JsonObject Get()
    {
        lock (Lock)
        {
            if (!Init()) return new JsonObject { ["available"] = false, ["error"] = _initError };
            var result = new JsonObject { ["available"] = true };
            foreach (var feature in Features)
            {
                try { result[feature] = Read(feature); }
                catch (Exception ex) { result[feature] = new JsonObject { ["supported"] = false, ["error"] = ex.Message }; }
            }
            return result;
        }
    }

    public static JsonObject Set(string feature, bool enabled)
    {
        lock (Lock)
        {
            if (!Init()) throw new InvalidOperationException(_initError ?? "ADLX no disponible");
            var obj = Acquire(feature);
            try
            {
                var flag = (byte)(enabled ? 1 : 0);
                var index = feature switch { "chill" => ChillSetEnabled, "sharpening" => SharpSetEnabled, _ => SetEnabled };
                Check(((delegate* unmanaged<IntPtr, byte, int>)Fn(obj, index))(obj, flag), $"activar {feature}");
            }
            finally { ReleaseObj(obj); }
        }
        // VRR: al encender FreeSync se activa también el ajuste de Windows para juegos en ventana / sin bordes,
        // que es lo que deja actuar al VRR fuera de la pantalla completa exclusiva (al apagarlo se deja como está).
        if (feature == "freesync" && enabled)
        {
            try { WindowedVrr.Set(true); } catch (Exception) { }
        }
        return Get();
    }

    /// <summary>FPS mínimos y máximos de Radeon Chill (el mínimo no puede superar al máximo).</summary>
    public static JsonObject SetChillRange(int minFps, int maxFps)
    {
        if (minFps > maxFps) throw new ArgumentException("El mínimo no puede superar al máximo");
        lock (Lock)
        {
            if (!Init()) throw new InvalidOperationException(_initError ?? "ADLX no disponible");
            var chill = Acquire("chill");
            try
            {
                int currentMax;
                ((delegate* unmanaged<IntPtr, int*, int>)Fn(chill, ChillGetMaxFps))(chill, &currentMax);
                var setMin = (delegate* unmanaged<IntPtr, int, int>)Fn(chill, ChillSetMinFps);
                var setMax = (delegate* unmanaged<IntPtr, int, int>)Fn(chill, ChillSetMaxFps);
                // Orden para no cruzar nunca los límites: si el nuevo mínimo supera el máximo actual, primero el máximo.
                if (minFps > currentMax)
                {
                    Check(setMax(chill, maxFps), "SetMaxFPS");
                    Check(setMin(chill, minFps), "SetMinFPS");
                }
                else
                {
                    Check(setMin(chill, minFps), "SetMinFPS");
                    Check(setMax(chill, maxFps), "SetMaxFPS");
                }
            }
            finally { ReleaseObj(chill); }
        }
        return Get();
    }

    private static JsonObject Read(string feature)
    {
        var obj = Acquire(feature);
        try
        {
            byte supported = 0, enabled = 0;
            Check(((delegate* unmanaged<IntPtr, byte*, int>)Fn(obj, IsSupported))(obj, &supported), "IsSupported");
            var info = new JsonObject { ["supported"] = supported != 0 };
            if (supported == 0) return info;
            Check(((delegate* unmanaged<IntPtr, byte*, int>)Fn(obj, IsEnabled))(obj, &enabled), "IsEnabled");
            info["enabled"] = enabled != 0;
            if (feature == "chill")
            {
                int min, max;
                if (((delegate* unmanaged<IntPtr, int*, int>)Fn(obj, ChillGetMinFps))(obj, &min) == AdlxOk) info["minFps"] = min;
                if (((delegate* unmanaged<IntPtr, int*, int>)Fn(obj, ChillGetMaxFps))(obj, &max) == AdlxOk) info["maxFps"] = max;
                IntRange range;
                if (((delegate* unmanaged<IntPtr, IntRange*, int>)Fn(obj, ChillGetFpsRange))(obj, &range) == AdlxOk)
                {
                    info["rangeMin"] = range.Min;
                    info["rangeMax"] = range.Max;
                    info["step"] = range.Step;
                }
            }
            else if (feature == "sharpening")
            {
                int sharpness;
                if (((delegate* unmanaged<IntPtr, int*, int>)Fn(obj, SharpGetSharpness))(obj, &sharpness) == AdlxOk) info["sharpness"] = sharpness;
            }
            return info;
        }
        finally { ReleaseObj(obj); }
    }

    /// <summary>Obtiene la interfaz del ajuste (hay que liberarla con ReleaseObj).</summary>
    private static IntPtr Acquire(string feature)
    {
        IntPtr obj = IntPtr.Zero;
        int result = feature switch
        {
            "afmf" => _services3D1 == IntPtr.Zero ? -1
                : ((delegate* unmanaged<IntPtr, IntPtr*, int>)Fn(_services3D1, S3D1GetAfmf))(_services3D1, &obj),
            "rsr" => ((delegate* unmanaged<IntPtr, IntPtr*, int>)Fn(_services3D, S3DGetRsr))(_services3D, &obj),
            "chill" => ((delegate* unmanaged<IntPtr, IntPtr, IntPtr*, int>)Fn(_services3D, S3DGetChill))(_services3D, _gpu, &obj),
            "antilag" => ((delegate* unmanaged<IntPtr, IntPtr, IntPtr*, int>)Fn(_services3D, S3DGetAntiLag))(_services3D, _gpu, &obj),
            "sharpening" => ((delegate* unmanaged<IntPtr, IntPtr, IntPtr*, int>)Fn(_services3D, S3DGetImageSharpening))(_services3D, _gpu, &obj),
            "freesync" => _display == IntPtr.Zero ? -1
                : ((delegate* unmanaged<IntPtr, IntPtr, IntPtr*, int>)Fn(_displayServices, DispGetFreeSync))(_displayServices, _display, &obj),
            "varibright" => _display == IntPtr.Zero ? -1
                : ((delegate* unmanaged<IntPtr, IntPtr, IntPtr*, int>)Fn(_displayServices, DispGetVariBright))(_displayServices, _display, &obj),
            _ => throw new ArgumentException($"Ajuste desconocido: {feature}"),
        };
        if (result != AdlxOk || obj == IntPtr.Zero) throw new InvalidOperationException($"{feature} no disponible ({result})");
        return obj;
    }

    private static bool Init()
    {
        if (_system != IntPtr.Zero) return true;
        if (_initError != null) return false;
        try
        {
            var lib = NativeLibrary.Load("amdadlx64.dll");
            var queryVersion = (delegate* unmanaged[Cdecl]<ulong*, int>)NativeLibrary.GetExport(lib, "ADLXQueryFullVersion");
            var initialize = (delegate* unmanaged[Cdecl]<ulong, IntPtr*, int>)NativeLibrary.GetExport(lib, "ADLXInitialize");

            ulong version;
            Check(queryVersion(&version), "ADLXQueryFullVersion");
            IntPtr system;
            var init = initialize(version, &system);
            if (init != AdlxOk && init != AdlxAlreadyInitialized) Check(init, "ADLXInitialize");

            // Primera GPU (la Legion Go S solo tiene la integrada).
            IntPtr gpus, gpu;
            Check(((delegate* unmanaged<IntPtr, IntPtr*, int>)Fn(system, SysGetGpus))(system, &gpus), "GetGPUs");
            try
            {
                var count = ((delegate* unmanaged<IntPtr, uint>)Fn(gpus, ListSize))(gpus);
                if (count == 0) throw new InvalidOperationException("No hay GPU AMD");
                Check(((delegate* unmanaged<IntPtr, uint, IntPtr*, int>)Fn(gpus, ListAtGpu))(gpus, 0, &gpu), "At_GPUList");
            }
            finally { ReleaseObj(gpus); }

            IntPtr services3D, services3D1 = IntPtr.Zero;
            Check(((delegate* unmanaged<IntPtr, IntPtr*, int>)Fn(system, SysGet3DSettingsServices))(system, &services3D), "Get3DSettingsServices");
            fixed (char* iid = "IADLX3DSettingsServices1")
                ((delegate* unmanaged<IntPtr, char*, IntPtr*, int>)Fn(services3D, QueryInterface))(services3D, iid, &services3D1);

            IntPtr displayServices, displays, display = IntPtr.Zero;
            Check(((delegate* unmanaged<IntPtr, IntPtr*, int>)Fn(system, SysGetDisplaysServices))(system, &displayServices), "GetDisplaysServices");
            if (((delegate* unmanaged<IntPtr, IntPtr*, int>)Fn(displayServices, DispGetDisplays))(displayServices, &displays) == AdlxOk)
            {
                try
                {
                    if (((delegate* unmanaged<IntPtr, uint>)Fn(displays, ListSize))(displays) > 0)
                        ((delegate* unmanaged<IntPtr, uint, IntPtr*, int>)Fn(displays, ListAtDisplay))(displays, 0, &display);
                }
                finally { ReleaseObj(displays); }
            }

            (_system, _gpu, _services3D, _services3D1, _displayServices, _display) =
                (system, gpu, services3D, services3D1, displayServices, display);
            return true;
        }
        catch (Exception ex)
        {
            _initError = ex.Message;
            return false;
        }
    }

    private static IntPtr Fn(IntPtr obj, int index) => ((IntPtr*)*(IntPtr*)obj)[index];

    private static void ReleaseObj(IntPtr obj)
    {
        if (obj != IntPtr.Zero) ((delegate* unmanaged<IntPtr, long>)Fn(obj, Release))(obj);
    }

    private static void Check(int result, string what)
    {
        if (result != AdlxOk && result != AdlxAlreadyEnabled)
            throw new InvalidOperationException($"ADLX {what} falló ({result})");
    }
}
