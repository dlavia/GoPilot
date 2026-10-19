using System.Text.Json.Nodes;

namespace GoSPanel.Bridge;

/// <summary>
/// Ajustes avanzados del mando, con el protocolo de la librería de Lenovo (LegionHandle, incluida en Legion Space):
///   0x09/0x0A gatillos · 01 izq. / 02 der. = [inicio %, fin %] de la zona muerta · 05/07 calibrar · 06/08 estado
///   0x0B/0x0C sticks   · 01 izq. / 02 der. = [centro %, borde %] · 05/07 calibrar · 06/08 estado
///   0x0D/0x0E giroscopio · 01 modo (0 nada, 1 ratón, 2 stick izq., 3 stick der.) · 02 mapa ratón · 03 mapa stick
///                          · 04 calibrar (1 empezar / 0 terminar) · 05 estado
///   0x11/0x12 botones  · 02 turbo (20 bytes, uno por botón) · 03 frecuencia del turbo (1 = 2 Hz, 2 = 5 Hz, 3 = 8 Hz)
/// La respuesta es [id, cmd, sub, datos…] y a los SET el mando contesta 0 si todo fue bien.
/// </summary>
internal static partial class StickLights
{
    private const byte GetTrigger = 0x09, SetTrigger = 0x0A, GetStick = 0x0B, SetStick = 0x0C;
    private const byte GetGyro = 0x0D, SetGyro = 0x0E, GetKeys = 0x11, SetKeys = 0x12;

    /// <summary>Botones con turbo: nombre e índice en la máscara de 20 (código de tecla de Lenovo − 1).</summary>
    public static readonly (string Key, int Bit)[] TurboButtons =
    {
        ("A", 4), ("B", 5), ("X", 6), ("Y", 7), ("LB", 12), ("RB", 13), ("LT", 16), ("RT", 17),
    };

    private static readonly int[] TurboRates = { 2, 5, 8 };   // índice + 1 = valor del firmware

    public static async Task<JsonObject> GetAdvancedAsync()
    {
        await Gate.WaitAsync();
        try
        {
            var path = FindDevice();
            if (path == null) return new JsonObject { ["available"] = false };
            using var device = Open(path);

            async Task<JsonObject?> Pair(byte cmd, byte sub, string a, string b)
            {
                var r = await QueryRawAsync(device, cmd, sub);
                return r == null ? null : new JsonObject { [a] = (sbyte)r[3], [b] = (sbyte)r[4] };
            }

            var gyroMode = await QueryRawAsync(device, GetGyro, 0x01);
            var stickMap = await QueryRawAsync(device, GetGyro, 0x03);
            var mouseMap = await QueryRawAsync(device, GetGyro, 0x02);
            var turbo = await QueryRawAsync(device, GetKeys, 0x02);
            var turboHz = await QueryRawAsync(device, GetKeys, 0x03);

            var turboKeys = new JsonArray();
            if (turbo != null)
                foreach (var (key, bit) in TurboButtons)
                    if ((turbo[3 + bit] & 0x01) != 0) turboKeys.Add(key);

            return new JsonObject
            {
                ["available"] = true,
                ["leftStick"] = await Pair(GetStick, 0x01, "center", "edge"),
                ["rightStick"] = await Pair(GetStick, 0x02, "center", "edge"),
                ["leftTrigger"] = await Pair(GetTrigger, 0x01, "low", "high"),
                ["rightTrigger"] = await Pair(GetTrigger, 0x02, "low", "high"),
                ["gyroMode"] = gyroMode == null ? null : gyroMode[3] & 0x0F,
                // Sensibilidad 1-30: la del mapa de stick (o la del ratón si no contesta)
                ["gyroSensitivity"] = stickMap != null ? stickMap[6] : mouseMap?[5],
                ["turbo"] = turboKeys,
                ["turboHz"] = turboHz is { } hz && hz[3] >= 1 && hz[3] <= 3 ? TurboRates[hz[3] - 1] : null,
            };
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>
    /// args (cualquiera de ellos): leftStick / rightStick {center, edge}, leftTrigger / rightTrigger {low, high},
    /// gyroMode (0-3), gyroSensitivity (1-100), turbo [botones], turboHz (2/5/8).
    /// </summary>
    public static async Task<JsonObject> SetAdvancedAsync(JsonObject args)
    {
        await Gate.WaitAsync();
        try
        {
            var path = FindDevice() ?? throw new InvalidOperationException("No se encontró el mando de la Legion Go S");
            using var device = Open(path);

            byte Pct(JsonNode? n) => (byte)Math.Clamp((int)(n?.GetValue<double>() ?? 0), 0, 100);
            if (args["leftStick"] is JsonObject ls) await SendAsync(device, SetStick, 0x01, Pct(ls["center"]), Pct(ls["edge"]));
            if (args["rightStick"] is JsonObject rs) await SendAsync(device, SetStick, 0x02, Pct(rs["center"]), Pct(rs["edge"]));
            if (args["leftTrigger"] is JsonObject lt) await SendAsync(device, SetTrigger, 0x01, Pct(lt["low"]), Pct(lt["high"]));
            if (args["rightTrigger"] is JsonObject rt) await SendAsync(device, SetTrigger, 0x02, Pct(rt["low"]), Pct(rt["high"]));
            if (args["gyroMode"] is JsonValue gm)
            {
                var mode = (byte)Math.Clamp((int)gm.GetValue<double>(), 0, 3);
                // Sin el sensor encendido (Equipo › Giroscopio) el mando no puede usarlo como ratón ni como stick.
                if (mode > 0)
                {
                    await SendAsync(device, SetGamepadCfg, FeatureImuEnable, 0x01);
                    var state = LoadState();
                    state["gyro"] = true;
                    File.WriteAllText(StatePath, state.ToJsonString());
                }
                await SendAsync(device, SetGyro, 0x01, mode);
            }
            if (args["gyroSensitivity"] is JsonValue gs)
            {
                // Mapa de stick: 1-100 (el mando trae 50); mapa de ratón: 1-30 (trae 30). Se escala uno al otro.
                var sens = (byte)Math.Clamp((int)gs.GetValue<double>(), 1, 100);
                // Se conservan los ejes y la zona muerta que tenga el mando; solo cambia la sensibilidad.
                var stickMap = await QueryRawAsync(device, GetGyro, 0x03);
                var stick = stickMap != null ? stickMap.Skip(3).Take(7).ToArray() : new byte[] { 0x03, 0x01, 0x00, 0x0A, 0x0A, 0x01, 0x01 };
                stick[3] = stick[4] = sens;
                await SendAsync(device, SetGyro, 0x03, stick);
                var mouseMap = await QueryRawAsync(device, GetGyro, 0x02);
                var mouse = mouseMap != null ? mouseMap.Skip(3).Take(4).ToArray() : new byte[] { 0x03, 0x01, 0x0A, 0x0A };
                mouse[2] = mouse[3] = (byte)Math.Clamp((int)Math.Round(sens * 30 / 100.0), 1, 30);
                await SendAsync(device, SetGyro, 0x02, mouse);
            }
            if (args["turbo"] is JsonArray keys)
            {
                var mask = new byte[20];
                foreach (var k in keys)
                    foreach (var (key, bit) in TurboButtons)
                        if (key == k?.GetValue<string>()) mask[bit] = 1;
                await SendAsync(device, SetKeys, 0x02, mask);
            }
            if (args["turboHz"] is JsonValue th)
            {
                var index = Array.IndexOf(TurboRates, (int)th.GetValue<double>());
                if (index >= 0) await SendAsync(device, SetKeys, 0x03, (byte)(index + 1));
            }
        }
        finally
        {
            Gate.Release();
        }
        return await GetAdvancedAsync();
    }

    /// <summary>
    /// Calibración. target: lstick, rstick, ltrigger, rtrigger, gyro · action: start, stop o status.
    /// Estado: sticks y gatillos 4 = correcta; giroscopio 2 = correcta; 0 = sin calibrar; 1/3 = en curso.
    /// </summary>
    public static async Task<JsonObject> CalibrateAsync(string target, string action)
    {
        var (cmd, getCmd, sub, statusSub, okValue) = target switch
        {
            "lstick" => (SetStick, GetStick, (byte)0x05, (byte)0x06, 4),
            "rstick" => (SetStick, GetStick, (byte)0x07, (byte)0x08, 4),
            "ltrigger" => (SetTrigger, GetTrigger, (byte)0x05, (byte)0x06, 4),
            "rtrigger" => (SetTrigger, GetTrigger, (byte)0x07, (byte)0x08, 4),
            "gyro" => (SetGyro, GetGyro, (byte)0x04, (byte)0x05, 2),
            _ => throw new ArgumentException("Calibración desconocida"),
        };
        await Gate.WaitAsync();
        try
        {
            var path = FindDevice() ?? throw new InvalidOperationException("No se encontró el mando de la Legion Go S");
            using var device = Open(path);
            if (action == "start") await SendAsync(device, cmd, sub, 0x01);
            else if (action == "stop") await SendAsync(device, cmd, sub, 0x00);
            await Task.Delay(action == "stop" ? 300 : 50);
            var status = await QueryRawAsync(device, getCmd, statusSub);
            var value = status == null ? -1 : status[3] & 0x0F;
            return new JsonObject { ["target"] = target, ["status"] = value, ["ok"] = value == okValue };
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>Diagnóstico: envía [cmd, sub, datos…] tal cual y devuelve la respuesta en hexadecimal.</summary>
    public static async Task<string> RawAsync(byte command, byte index, byte[] payload)
    {
        await Gate.WaitAsync();
        try
        {
            using var device = Open(FindDevice()!);
            var buffer = new byte[PacketSize + 1];
            buffer[1] = command;
            buffer[2] = index;
            payload.CopyTo(buffer, 3);
            if (!await WriteWithTimeoutAsync(device, buffer, 500)) return "sin respuesta (escritura)";
            var reply = await ReadReplyAsync(device, 300);
            return reply == null ? "sin respuesta" : BitConverter.ToString(reply, 0, 16);
        }
        finally { Gate.Release(); }
    }

    public static async Task<string> DumpAsync()
    {
        await Gate.WaitAsync();
        try
        {
            using var device = Open(FindDevice()!);
            var sb = new System.Text.StringBuilder();
            foreach (var (c, sub) in new (byte, byte)[] { (GetKeys, 0x02), (GetKeys, 0x03), (GetGyro, 0x01), (GetGyro, 0x02), (GetGyro, 0x03), (GetStick, 0x01), (GetTrigger, 0x01) })
            {
                var r = await QueryRawAsync(device, c, sub);
                sb.AppendLine($"{c:X2}/{sub:X2}: " + (r == null ? "null" : BitConverter.ToString(r, 0, 26)));
            }
            return sb.ToString();
        }
        finally { Gate.Release(); }
    }

    /// <summary>Restablece el mando de fábrica (orden 0xEE con «RESTORE», como Legion Space).</summary>
    public static async Task<JsonObject> FactoryResetAsync()
    {
        await Gate.WaitAsync();
        try
        {
            var path = FindDevice() ?? throw new InvalidOperationException("No se encontró el mando de la Legion Go S");
            using var device = Open(path);
            await SendAsync(device, 0xEE, 0x00, "RESTORE"u8.ToArray());
        }
        finally
        {
            Gate.Release();
        }
        await Task.Delay(1500);   // el mando se reinicia por dentro
        return await GetAdvancedAsync();
    }

    /// <summary>Consulta y devuelve la respuesta completa [id, cmd, sub, datos…] (o null si no contesta).</summary>
    private static async Task<byte[]?> QueryRawAsync(FileStream device, byte command, byte index)
    {
        var buffer = new byte[PacketSize + 1];
        buffer[1] = command;
        buffer[2] = index;
        if (!await WriteWithTimeoutAsync(device, buffer, 500)) return null;
        for (var attempt = 0; attempt < 4; attempt++)
        {
            var reply = await ReadReplyAsync(device, 200);
            if (reply == null) return null;
            if (reply[1] == command && reply[2] == index) return reply;
        }
        return null;
    }
}
