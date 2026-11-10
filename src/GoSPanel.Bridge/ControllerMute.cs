using System.Text.Json.Nodes;

namespace GoSPanel.Bridge;

/// <summary>
/// Silencia el mando para los juegos mientras GoPilot Home enseña su menú encima: la orden del firmware
/// SET_GAMEPAD_CFG (0x04) sub 0x03 —la que usa Legion Space al empezar la prueba de botones— hace que el mando deje de
/// enviar datos a Windows durante 2 s, así que se repite cada 1,2 s mientras dure. Si GoPilot se cierra o se cuelga,
/// el mando vuelve solo a los 2 s. Mientras está silenciado, los botones que siguen llegando por la interfaz de
/// Lenovo (MI_06) se reenvían al launcher como avisos { evt: "pad", key, down }.
/// </summary>
internal static class ControllerMute
{
    private static readonly object Lock = new();
    private static CancellationTokenSource? _cts;
    private static readonly TimeSpan MaxDuration = TimeSpan.FromMinutes(10);   // por si el launcher se olvida de quitarlo

    public static bool Active { get { lock (Lock) return _cts != null; } }

    public static JsonObject Set(bool on)
    {
        lock (Lock)
        {
            if (on && _cts == null)
            {
                var cts = _cts = new CancellationTokenSource(MaxDuration);
                _ = Task.Run(() => KeepMutedAsync(cts));
            }
            else if (!on && _cts != null)
            {
                _cts.Cancel();
                _cts = null;
            }
        }
        if (!on) Pad.Reset();
        return new JsonObject { ["muted"] = on };
    }

    private static async Task KeepMutedAsync(CancellationTokenSource cts)
    {
        string? last = null;
        while (!cts.IsCancellationRequested)
        {
            try
            {
                var reply = await StickLights.RawAsync(0x04, 0x03, Array.Empty<byte>());
                if (reply != last) { last = reply; System.Diagnostics.Debug.WriteLine("mute: " + reply); }
            }
            catch (Exception) { /* mando ocupado o desconectado: se reintenta */ }
            try { await Task.Delay(1200, cts.Token); } catch (OperationCanceledException) { break; }
        }
        lock (Lock) if (_cts == cts) _cts = null;   // se acabó el tiempo máximo
        Pad.Reset();
    }

    /// <summary>Botones del informe de MI_06 (byte 1 cruceta, byte 2 A B X Y LB RB, bytes 5-6 stick izquierdo).</summary>
    internal static class Pad
    {
        private static readonly (string Key, int Byte, int Mask)[] Buttons =
        {
            ("up", 1, 0x10), ("down", 1, 0x20), ("left", 1, 0x40), ("right", 1, 0x80),
            ("a", 2, 0x01), ("b", 2, 0x02), ("x", 2, 0x04), ("y", 2, 0x08), ("lb", 2, 0x10), ("rb", 2, 0x40),
        };
        private static readonly HashSet<string> Down = new();

        public static void Reset() { lock (Down) Down.Clear(); }

        public static void Update(byte[] report)
        {
            if (!Active) return;
            var now = new HashSet<string>();
            foreach (var (key, b, mask) in Buttons)
                if ((report[b] & mask) != 0) now.Add(key);
            var lx = (sbyte)report[5];
            var ly = (sbyte)report[6];
            if (lx < -64) now.Add("left"); else if (lx > 64) now.Add("right");
            if (ly < -64) now.Add("up"); else if (ly > 64) now.Add("down");
            List<(string, bool)> changes = new();
            lock (Down)
            {
                foreach (var k in now) if (Down.Add(k)) changes.Add((k, true));
                foreach (var k in Down.ToList()) if (!now.Contains(k)) { Down.Remove(k); changes.Add((k, false)); }
            }
            foreach (var (key, down) in changes)
                HomeBridge.Notify(new JsonObject { ["evt"] = "pad", ["key"] = key, ["down"] = down });
        }
    }
}
