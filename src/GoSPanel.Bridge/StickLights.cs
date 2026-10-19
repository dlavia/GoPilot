using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Win32.SafeHandles;
using Windows.Storage;

namespace GoSPanel.Bridge;

/// <summary>
/// Anillos RGB de los sticks de la Legion Go S. Se configuran hablando con el microcontrolador del mando
/// (USB 1A86:E310/E311) por su interfaz HID de configuración, con el protocolo del driver hid-lenovo-go-s
/// de Linux: informes de 64 bytes [comando, índice, datos...].
///   SET_GAMEPAD_CFG (0x04) · FEATURE_RGB_ENABLE (0x06) · 1 = encendidas / sin datos = apagadas
///   SET_RGB_CFG (0x10) · LIGHT_MODE_SEL (0x01) · 1 = personalizado
///   SET_RGB_CFG (0x10) · LIGHT_PROFILE_SEL (0x02) · perfil 1
///   SET_RGB_CFG (0x10) · USR_LIGHT_PROFILE_1 (0x03) · [efecto, R, G, B, brillo 0-100, velocidad 0-100]
/// </summary>
internal static partial class StickLights
{
    private const ushort VendorId = 0x1A86;
    private static readonly ushort[] ProductIds = { 0xE310, 0xE311 };
    private const int PacketSize = 64;

    private const byte GetVersion = 0x01, GetGamepadCfg = 0x03, SetGamepadCfg = 0x04, SetRgbCfg = 0x10;
    private const byte FeatureAutoSleep = 0x04, FeaturePollRate = 0x10, FeatureDpadMode = 0x11;
    // Vibración (protocolo de Legion Space): GET 0x07 / SET 0x08 · 0x01 = mando, 0x03 = touchpad · nivel 0 (apagada) a 3
    private const byte GetVibration = 0x07, SetVibration = 0x08, VibrationGamepad = 0x01, VibrationTouchpad = 0x03;
    private static readonly int[] PollRates = { 125, 250, 500, 1000 };   // índice = valor del firmware
    private const byte FeatureRgbEnable = 0x06, FeatureImuEnable = 0x07, LightModeSel = 0x01, LightProfileSel = 0x02, UserProfile1 = 0x03;
    private const byte ModeCustom = 0x01, ModeDynamicLighting = 0x00;

    public static readonly string[] Effects = { "solid", "breathe", "chroma", "rainbow" };

    // Propiedad (no campo) para que la prueba de diagnóstico funcione fuera del contexto del paquete.
    private static string StatePath => Path.Combine(ApplicationData.Current.LocalFolder.Path, "lights.json");
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static string? _devicePath;

    public static JsonObject Get()
    {
        var state = LoadState();
        state["available"] = FindDevice() != null;
        return state;
    }

    /// <summary>Aplica los campos indicados (enabled, effect, color "#RRGGBB", brightness, speed) y los guarda.</summary>
    public static async Task<JsonObject> SetAsync(JsonObject changes)
    {
        await Gate.WaitAsync();
        try
        {
            var state = LoadState();
            foreach (var (key, value) in changes)
                if (value != null) state[key] = value.DeepClone();

            var path = FindDevice() ?? throw new InvalidOperationException("No se encontró el mando de la Legion Go S");
            using var device = Open(path);

            var enabled = state["enabled"]!.GetValue<bool>();
            if (!enabled)
            {
                ReactiveLights.Stop();
                await SendAsync(device, SetGamepadCfg, FeatureRgbEnable);
            }
            else if (state["effect"]!.GetValue<string>() == "reactive")
            {
                // Reactivo: el mando pasa a iluminación dinámica de Windows (una sola vez) y GoPilot dibuja los colores.
                // El brillo lo marca el propio efecto (reposo y pico): se pasa el color a pleno.
                var (r, g, b) = ParseColor(state["color"]!.GetValue<string>());
                if (!ReactiveLights.Running)
                {
                    await SendAsync(device, SetGamepadCfg, FeatureRgbEnable, 0x01);
                    await SendAsync(device, SetRgbCfg, LightModeSel, ModeDynamicLighting);
                }
                ReactiveLights.Start(r, g, b, (int)(state["reactiveLow"]?.GetValue<double>() ?? 20), (int)(state["reactiveHigh"]?.GetValue<double>() ?? 80));
            }
            else
            {
                ReactiveLights.Stop();
                var effect = Array.IndexOf(Effects, state["effect"]!.GetValue<string>());
                var (r, g, b) = ParseColor(state["color"]!.GetValue<string>());
                var brightness = (byte)Math.Clamp((int)state["brightness"]!.GetValue<double>(), 0, 100);
                // El firmware de la Go S no aplica el byte de brillo en el modo personalizado: se atenúa el
                // propio color (en croma y arcoíris el mando genera los colores y solo cuenta ese byte).
                (r, g, b) = ((byte)(r * brightness / 100), (byte)(g * brightness / 100), (byte)(b * brightness / 100));
                var speed = (byte)Math.Clamp((int)state["speed"]!.GetValue<double>(), 0, 100);

                await SendAsync(device, SetGamepadCfg, FeatureRgbEnable, 0x01);
                await SendAsync(device, SetRgbCfg, LightModeSel, ModeCustom);
                await SendAsync(device, SetRgbCfg, LightProfileSel, 0x01);
                await SendAsync(device, SetRgbCfg, UserProfile1, (byte)Math.Max(0, effect), r, g, b, brightness, speed);
            }

            File.WriteAllText(StatePath, state.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            state["available"] = true;
            return state;
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>Activa o desactiva el giroscopio (IMU) del mando: SET_GAMEPAD_CFG · FEATURE_IMU_ENABLE (0x07).</summary>
    public static async Task<JsonObject> SetGyroAsync(bool enabled)
    {
        await Gate.WaitAsync();
        try
        {
            var path = FindDevice() ?? throw new InvalidOperationException("No se encontró el mando de la Legion Go S");
            using (var device = Open(path))
            {
                if (enabled) await SendAsync(device, SetGamepadCfg, FeatureImuEnable, 0x01);
                else await SendAsync(device, SetGamepadCfg, FeatureImuEnable);
            }
            var state = LoadState();
            state["gyro"] = enabled;
            File.WriteAllText(StatePath, state.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            state["available"] = true;
            return state;
        }
        finally
        {
            Gate.Release();
        }
    }

    // ---------------------------------------------------------------------
    // Ajustes propios del mando (firmware): apagado automático, frecuencia de sondeo y cruceta
    // ---------------------------------------------------------------------

    public static async Task<JsonObject> GetControllerAsync()
    {
        await Gate.WaitAsync();
        try
        {
            var path = FindDevice();
            if (path == null) return new JsonObject { ["available"] = false };
            using var device = Open(path);
            var sleep = await QueryAsync(device, GetGamepadCfg, FeatureAutoSleep);
            var poll = await QueryAsync(device, GetGamepadCfg, FeaturePollRate);
            var dpad = await QueryAsync(device, GetGamepadCfg, FeatureDpadMode);
            var rumble = await QueryAsync(device, GetVibration, VibrationGamepad);
            var touchRumble = await QueryAsync(device, GetVibration, VibrationTouchpad);
            return new JsonObject
            {
                ["available"] = true,
                ["autoSleep"] = sleep,
                ["pollRate"] = poll is int i && i < PollRates.Length ? PollRates[i] : null,
                ["dpad"] = dpad switch { 0 => "8", 1 => "4", _ => null },
                ["vibration"] = rumble is >= 0 and <= 3 ? rumble : null,
                ["touchpadVibration"] = touchRumble is >= 0 and <= 3 ? touchRumble : null,
            };
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>args: autoSleep (minutos 0-255, 0 = nunca), pollRate (125/250/500/1000), dpad ("8" / "4"),
    /// vibration y touchpadVibration (0 apagada … 3 fuerte).</summary>
    public static async Task<JsonObject> SetControllerAsync(JsonObject args)
    {
        await Gate.WaitAsync();
        try
        {
            var path = FindDevice() ?? throw new InvalidOperationException("No se encontró el mando de la Legion Go S");
            using var device = Open(path);
            if (args["autoSleep"] is JsonValue sleep)
                await SendAsync(device, SetGamepadCfg, FeatureAutoSleep, (byte)Math.Clamp((int)sleep.GetValue<double>(), 0, 255));
            if (args["pollRate"] is JsonValue poll)
            {
                var index = Array.IndexOf(PollRates, (int)poll.GetValue<double>());
                if (index < 0) throw new ArgumentException("Frecuencia no válida");
                await SendAsync(device, SetGamepadCfg, FeaturePollRate, (byte)index);
            }
            if (args["vibration"] is JsonValue vib)
                await SendAsync(device, SetVibration, VibrationGamepad, (byte)Math.Clamp((int)vib.GetValue<double>(), 0, 3));
            if (args["touchpadVibration"] is JsonValue tvib)
                await SendAsync(device, SetVibration, VibrationTouchpad, (byte)Math.Clamp((int)tvib.GetValue<double>(), 0, 3));
            if (args["dpad"] is JsonValue dpad)
                await SendAsync(device, SetGamepadCfg, FeatureDpadMode, dpad.GetValue<string>() == "4" ? (byte)1 : (byte)0);
        }
        finally
        {
            Gate.Release();
        }
        return await GetControllerAsync();
    }

    /// <summary>Envía una consulta y devuelve el primer byte de datos de la respuesta (o null si no contesta).</summary>
    private static async Task<int?> QueryAsync(FileStream device, byte command, byte index)
    {
        var buffer = new byte[PacketSize + 1];
        buffer[1] = command;
        buffer[2] = index;
        if (!await WriteWithTimeoutAsync(device, buffer, 500)) return null;
        // La respuesta repite el comando y el índice: [id, cmd, sub, dato...]
        for (var attempt = 0; attempt < 4; attempt++)
        {
            var reply = await ReadReplyAsync(device, 200);
            if (reply == null) return null;
            if (reply[1] == command && reply[2] == index) return reply[3];
        }
        return null;
    }

    private static JsonObject LoadState()
    {
        try
        {
            if (File.Exists(StatePath) && JsonNode.Parse(File.ReadAllText(StatePath)) is JsonObject saved) return saved;
        }
        catch (Exception) { }
        // Valores por defecto del firmware según el driver de Linux (azul violeta, brillo 80).
        return new JsonObject { ["enabled"] = true, ["effect"] = "solid", ["color"] = "#242299", ["brightness"] = 80, ["speed"] = 50, ["gyro"] = true };
    }

    /// <summary>Al arrancar el agente: si el efecto guardado es «Reactivo», se vuelve a poner en marcha.</summary>
    public static async Task ResumeReactiveAsync()
    {
        var state = LoadState();
        if (state["enabled"]?.GetValue<bool>() != true || state["effect"]?.GetValue<string>() != "reactive") return;
        for (var attempt = 0; attempt < 10 && FindDevice() == null; attempt++) await Task.Delay(3000);
        try { await SetAsync(new JsonObject()); } catch (Exception) { }
    }

    private static (byte R, byte G, byte B) ParseColor(string hex)
    {
        hex = hex.TrimStart('#');
        if (hex.Length != 6) throw new ArgumentException("Color no válido");
        return (Convert.ToByte(hex[..2], 16), Convert.ToByte(hex[2..4], 16), Convert.ToByte(hex[4..], 16));
    }

    // ---------------------------------------------------------------------
    // HID
    // ---------------------------------------------------------------------

    private static async Task SendAsync(FileStream device, byte command, byte index, params byte[] payload)
    {
        var buffer = new byte[PacketSize + 1];   // byte 0 = id de informe (0: sin id)
        buffer[1] = command;
        buffer[2] = index;
        payload.CopyTo(buffer, 3);
        if (!await WriteWithTimeoutAsync(device, buffer, 500))
            throw new InvalidOperationException("El mando no respondió");
        await ReadReplyAsync(device, 150);        // el mando contesta a cada comando; se descarta
    }

    /// <summary>Escritura con límite de tiempo: algunas interfaces HID no aceptan informes y se quedarían bloqueadas.</summary>
    private static async Task<bool> WriteWithTimeoutAsync(FileStream device, byte[] buffer, int timeoutMs)
    {
        using var cts = new CancellationTokenSource(timeoutMs);
        try
        {
            await device.WriteAsync(buffer, cts.Token);
            return true;
        }
        catch (OperationCanceledException) { return false; }
        catch (IOException) { return false; }
    }

    private static async Task<byte[]?> ReadReplyAsync(FileStream device, int timeoutMs)
    {
        var reply = new byte[PacketSize + 1];
        using var cts = new CancellationTokenSource(timeoutMs);
        try
        {
            var read = await device.ReadAsync(reply, cts.Token);
            return read > 0 ? reply : null;
        }
        catch (OperationCanceledException) { return null; }
    }

    public static string? FindDeviceForTest() => FindDevice();

    /// <summary>Interfaz de configuración: HID de fabricante con informes de 64 bytes que responde a GET_VERSION.</summary>
    // La búsqueda escribe una consulta en cada interfaz del mando: dos búsquedas a la vez se roban las
    // respuestas (y el mando parece desaparecido). Se hace de una en una y la ruta encontrada se recuerda.
    private static readonly object ProbeLock = new();

    private static string? FindDevice()
    {
        lock (ProbeLock)
        {
            return _devicePath ??= ProbeDevice();
        }
    }

    private static string? ProbeDevice()
    {
        foreach (var path in EnumerateHidPaths())
        {
            try
            {
                using var handle = CreateFile(path, GenericRead | GenericWrite, FileShareRead | FileShareWrite, IntPtr.Zero, OpenExisting, FileFlagOverlapped, IntPtr.Zero);
                if (handle.IsInvalid) continue;

                var attributes = new HiddAttributes { Size = Marshal.SizeOf<HiddAttributes>() };
                if (!HidD_GetAttributes(handle, ref attributes) || attributes.VendorId != VendorId || !ProductIds.Contains(attributes.ProductId)) continue;
                if (!HidD_GetPreparsedData(handle, out var preparsed)) continue;
                try
                {
                    if (HidP_GetCaps(preparsed, out var caps) != HidpStatusSuccess) continue;
                    if (caps.UsagePage < 0xFF00 || caps.OutputReportByteLength != PacketSize + 1 || caps.InputReportByteLength != PacketSize + 1) continue;
                }
                finally { HidD_FreePreparsedData(preparsed); }

                // Confirmar que es el microcontrolador de configuración: responde a GET_VERSION con el mismo comando.
                using var stream = new FileStream(handle, FileAccess.ReadWrite, PacketSize + 1, isAsync: true);
                var probe = new byte[PacketSize + 1];
                probe[1] = GetVersion;
                if (!WriteWithTimeoutAsync(stream, probe, 300).GetAwaiter().GetResult()) continue;
                var reply = ReadReplyAsync(stream, 300).GetAwaiter().GetResult();
                if (reply != null && reply[1] == GetVersion)
                {
                    _devicePath = path;
                    return path;
                }
            }
            catch (Exception) { /* interfaz ocupada o sin respuesta: probar la siguiente */ }
        }
        return null;
    }

    private static FileStream Open(string path)
    {
        var handle = CreateFile(path, GenericRead | GenericWrite, FileShareRead | FileShareWrite, IntPtr.Zero, OpenExisting, FileFlagOverlapped, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            _devicePath = null;   // el mando se reconectó o cambió de ruta: se volverá a buscar
            throw new InvalidOperationException("No se pudo abrir el mando");
        }
        return new FileStream(handle, FileAccess.ReadWrite, PacketSize + 1, isAsync: true);
    }

    private static IEnumerable<string> EnumerateHidPaths()
    {
        HidD_GetHidGuid(out var hidGuid);
        var set = SetupDiGetClassDevs(ref hidGuid, IntPtr.Zero, IntPtr.Zero, DigcfPresent | DigcfDeviceInterface);
        if (set == IntPtr.Zero || set == new IntPtr(-1)) yield break;
        try
        {
            var data = new SpDeviceInterfaceData { CbSize = Marshal.SizeOf<SpDeviceInterfaceData>() };
            for (uint i = 0; SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref hidGuid, i, ref data); i++)
            {
                SetupDiGetDeviceInterfaceDetail(set, ref data, IntPtr.Zero, 0, out var size, IntPtr.Zero);
                var detail = Marshal.AllocHGlobal((int)size);
                try
                {
                    Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);   // cbSize de SP_DEVICE_INTERFACE_DETAIL_DATA_W
                    if (SetupDiGetDeviceInterfaceDetail(set, ref data, detail, size, out _, IntPtr.Zero))
                    {
                        var path = Marshal.PtrToStringUni(detail + 4);
                        if (path != null && path.Contains("vid_1a86", StringComparison.OrdinalIgnoreCase)) yield return path;
                    }
                }
                finally { Marshal.FreeHGlobal(detail); }
            }
        }
        finally { SetupDiDestroyDeviceInfoList(set); }
    }

    private const uint GenericRead = 0x80000000, GenericWrite = 0x40000000;
    private const uint FileShareRead = 1, FileShareWrite = 2, OpenExisting = 3, FileFlagOverlapped = 0x40000000;
    private const int DigcfPresent = 0x2, DigcfDeviceInterface = 0x10;
    private const int HidpStatusSuccess = 0x00110000;

    [StructLayout(LayoutKind.Sequential)]
    private struct SpDeviceInterfaceData { public int CbSize; public Guid InterfaceClassGuid; public int Flags; public IntPtr Reserved; }

    [StructLayout(LayoutKind.Sequential)]
    private struct HiddAttributes { public int Size; public ushort VendorId; public ushort ProductId; public ushort VersionNumber; }

    [StructLayout(LayoutKind.Sequential)]
    private struct HidpCaps
    {
        public ushort Usage, UsagePage, InputReportByteLength, OutputReportByteLength, FeatureReportByteLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
        public ushort NumberLinkCollectionNodes, NumberInputButtonCaps, NumberInputValueCaps, NumberInputDataIndices,
            NumberOutputButtonCaps, NumberOutputValueCaps, NumberOutputDataIndices,
            NumberFeatureButtonCaps, NumberFeatureValueCaps, NumberFeatureDataIndices;
    }

    [DllImport("hid.dll")] private static extern void HidD_GetHidGuid(out Guid guid);
    [DllImport("hid.dll")] private static extern bool HidD_GetAttributes(SafeFileHandle device, ref HiddAttributes attributes);
    [DllImport("hid.dll")] private static extern bool HidD_GetPreparsedData(SafeFileHandle device, out IntPtr data);
    [DllImport("hid.dll")] private static extern bool HidD_FreePreparsedData(IntPtr data);
    [DllImport("hid.dll")] private static extern int HidP_GetCaps(IntPtr data, out HidpCaps caps);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode)] private static extern IntPtr SetupDiGetClassDevs(ref Guid classGuid, IntPtr enumerator, IntPtr parent, int flags);
    [DllImport("setupapi.dll")] private static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr devInfo, ref Guid classGuid, uint index, ref SpDeviceInterfaceData data);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode)] private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref SpDeviceInterfaceData data, IntPtr detail, uint detailSize, out uint requiredSize, IntPtr devInfo);
    [DllImport("setupapi.dll")] private static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
}
