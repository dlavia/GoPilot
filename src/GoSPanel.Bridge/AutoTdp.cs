using System.Text.Json;
using System.Text.Json.Nodes;
using Windows.Storage;

namespace GoSPanel.Bridge;

/// <summary>
/// AutoTDP: ajusta el límite de potencia (SPL, con ráfagas y picos ×1,25 y ×1,5) para mantener los FPS objetivo del
/// juego gastando lo mínimo. Cada segundo mira los FPS reales (contador ETW del servicio): si no llega sube 1–2 W y si
/// sobra durante 4 s baja 1 W, siempre entre el mínimo y el máximo elegidos. Sin juego se queda en el mínimo.
/// Con «Limitar con Chill», Radeon Chill fija los FPS en el objetivo para que el juego no gaste en FPS que sobran.
/// Si el modo cambia por otro lado (Legion L + Y, otra app) se desactiva. Se guarda en los perfiles como modo «auto».
/// </summary>
internal static class AutoTdp
{
    public static readonly int[] Targets = { 30, 40, 45, 60, 90, 120 };

    private static string FilePath => Path.Combine(ApplicationData.Current.LocalFolder.Path, "autotdp.json");
    private static readonly object Lock = new();
    private static bool _enabled;
    private static int _target = 60, _min = 8, _max = 25;
    private static bool _chill = true;
    private static bool _chillOwned;      // Chill lo encendió AutoTDP (se apaga al salir)

    // Estado en vivo para el widget
    private static int _watts;
    private static double? _fps;
    private static string _status = "";
    private static bool _afmf;            // AFMF activo: el contador ve los FPS reales y en pantalla salen ~el doble

    public static bool Enabled { get { lock (Lock) return _enabled; } }

    public static void Start()
    {
        Load();
        _ = Task.Run(LoopAsync);
    }

    // ---------------------------------------------------------------------
    // Configuración
    // ---------------------------------------------------------------------

    public static JsonObject Get()
    {
        lock (Lock)
        {
            var targets = new JsonArray();
            foreach (var t in Targets) targets.Add(t);
            return new JsonObject
            {
                ["enabled"] = _enabled, ["target"] = _target, ["min"] = _min, ["max"] = _max, ["chill"] = _chill,
                ["watts"] = _watts == 0 ? null : _watts, ["fps"] = _fps, ["status"] = _status, ["targets"] = targets,
                ["afmf"] = _afmf,
            };
        }
    }

    /// <summary>Lo que se guarda en un perfil.</summary>
    public static JsonObject Config()
    {
        lock (Lock) return new JsonObject { ["target"] = _target, ["min"] = _min, ["max"] = _max, ["chill"] = _chill };
    }

    /// <summary>args: enabled, target, min, max, chill (cualquiera). Encenderlo pone el modo Manual del firmware.</summary>
    public static async Task<JsonObject> SetAsync(JsonObject args)
    {
        bool wasEnabled, enabled, chillChanged;
        lock (Lock)
        {
            wasEnabled = _enabled;
            var oldTarget = _target; var oldChill = _chill;
            if (args["target"] is JsonNode t) _target = Math.Clamp((int)t.GetValue<double>(), 20, 144);
            if (args["min"] is JsonNode mn) _min = Math.Clamp((int)mn.GetValue<double>(), 5, 30);
            if (args["max"] is JsonNode mx) _max = Math.Clamp((int)mx.GetValue<double>(), 5, 30);
            if (_min > _max) (_min, _max) = (_max, _min);
            if (args["chill"] is JsonNode c) _chill = c.GetValue<bool>();
            if (args["enabled"] is JsonNode e) _enabled = e.GetValue<bool>();
            enabled = _enabled;
            chillChanged = oldTarget != _target || oldChill != _chill;
            Save();
        }

        if (enabled && !wasEnabled)
        {
            // Se empieza a mitad del margen: arrancar en el máximo disparaba el ventilador al entrar al juego
            int start;
            lock (Lock) start = (_min + _max + 1) / 2;
            await ApplyWattsAsync(start);
            ApplyChill();
        }
        else if (!enabled && wasEnabled) ReleaseChill();
        else if (enabled && chillChanged) ApplyChill();
        return Get();
    }

    private static void Load()
    {
        try
        {
            if (JsonNode.Parse(File.ReadAllText(FilePath)) is JsonObject o)
            {
                _enabled = o["enabled"]?.GetValue<bool>() ?? false;
                _target = o["target"]?.GetValue<int>() ?? _target;
                _min = o["min"]?.GetValue<int>() ?? _min;
                _max = o["max"]?.GetValue<int>() ?? _max;
                _chill = o["chill"]?.GetValue<bool>() ?? _chill;
                _chillOwned = o["chillOwned"]?.GetValue<bool>() ?? false;
            }
        }
        catch (Exception) { }
    }

    private static void Save()
    {
        try
        {
            File.WriteAllText(FilePath, new JsonObject
            {
                ["enabled"] = _enabled, ["target"] = _target, ["min"] = _min, ["max"] = _max, ["chill"] = _chill, ["chillOwned"] = _chillOwned,
            }.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception) { }
    }

    // ---------------------------------------------------------------------
    // Bucle de control (cada segundo)
    // ---------------------------------------------------------------------

    private static async Task LoopAsync()
    {
        var recent = new Queue<double>();
        var surplusSeconds = 0;
        var tick = 0;
        while (true)
        {
            await Task.Delay(1000);
            try
            {
                if (!Enabled) { recent.Clear(); surplusSeconds = 0; continue; }

                // Cada 3 s: si el modo ya no es Manual, otro lo ha cambiado (Legion L + Y, Legion Space…) y se deja
                if (++tick % 3 == 0 && (await Program.CallServiceAsync("power.get"))["mode"]?.GetValue<string>() != "custom")
                {
                    await SetAsync(new JsonObject { ["enabled"] = false });
                    lock (Lock) _status = "Desactivado: se cambió de modo";
                    continue;
                }

                int target, min, max, watts;
                lock (Lock) { target = _target; min = _min; max = _max; watts = _watts == 0 ? max : _watts; }

                var game = ForegroundTracker.Current;
                if (game == null)
                {
                    recent.Clear(); surplusSeconds = 0;
                    lock (Lock) { _fps = null; _status = "Sin juego: en espera"; }
                    if (watts != min) await ApplyWattsAsync(min);
                    continue;
                }

                // AFMF (cada 5 s): el contador ETW solo ve los fotogramas reales del juego; los de AFMF van aparte.
                // El objetivo cuenta FPS reales y el widget avisa de que en pantalla salen el doble.
                if (tick % 5 == 0)
                {
                    try
                    {
                        var amd = AmdAdlx.Get();
                        lock (Lock) _afmf = (amd["afmf"] as JsonObject)?["enabled"]?.GetValue<bool>() == true;
                        // Chill siempre en los FPS objetivo (al cargar, con otro perfil o si se tocó a mano en Pantalla)
                        bool wantChill;
                        lock (Lock) wantChill = _chill;
                        var chill = amd["chill"] as JsonObject;
                        if (wantChill && chill?["supported"]?.GetValue<bool>() == true
                            && (chill["enabled"]?.GetValue<bool>() != true || chill["minFps"]?.GetValue<int>() != target || chill["maxFps"]?.GetValue<int>() != target))
                            ApplyChill();
                    }
                    catch (Exception) { }
                }

                var fps = (await Program.CallServiceAsync("fps.get", new JsonObject { ["pid"] = game.Value.Pid }))["fps"]?.GetValue<double>();
                if (fps is null or <= 0) { lock (Lock) { _fps = null; _status = "Esperando FPS del juego"; } continue; }

                recent.Enqueue(fps.Value);
                while (recent.Count > 3) recent.Dequeue();
                var avg = recent.Average();
                lock (Lock) _fps = Math.Round(avg);

                var next = watts;
                if (avg < target * 0.80) { next = watts + 2; surplusSeconds = 0; }
                else if (avg < target * 0.93) { next = watts + 1; surplusSeconds = 0; }
                else if (avg >= target * 0.98 && ++surplusSeconds >= 4) { next = watts - 1; surplusSeconds = 0; }
                next = Math.Clamp(next, min, max);

                lock (Lock)
                    _status = next >= max && avg < target * 0.93 ? "Al máximo: no llega al objetivo"
                        : next <= min && avg >= target * 0.98 ? "Al mínimo: sobran FPS"
                        : "Ajustando";
                if (next != watts) { await ApplyWattsAsync(next); recent.Clear(); }
            }
            catch (Exception)
            {
                lock (Lock) _status = "Sin servicio";
            }
        }
    }

    private static async Task ApplyWattsAsync(int spl)
    {
        // Ráfagas y picos como en Manual automático, pero sin pasar del máximo elegido: es un tope de consumo real
        // (con ×1,25 / ×1,5 encima, un máximo de 25 W gastaba 31–38 W)
        int max;
        lock (Lock) max = _max;
        var sppt = Math.Min(Math.Min(32, max), Math.Max(spl, (int)Math.Round(spl * 1.25)));
        var fppt = Math.Min(Math.Min(41, max), Math.Max(spl, (int)Math.Round(spl * 1.5)));
        await Program.CallServiceAsync("tdp.set", new JsonObject { ["spl"] = spl, ["sppt"] = sppt, ["fppt"] = fppt });
        lock (Lock) _watts = spl;
    }

    // ---------------------------------------------------------------------
    // Radeon Chill como tope de FPS
    // ---------------------------------------------------------------------

    private static void ApplyChill()
    {
        try
        {
            bool chill; int target;
            lock (Lock) { chill = _chill; target = _target; }
            if (!chill) { ReleaseChill(); return; }
            AmdAdlx.SetChillRange(target, target);
            var info = AmdAdlx.Get()["chill"] as JsonObject;
            if (info?["enabled"]?.GetValue<bool>() != true)
            {
                AmdAdlx.Set("chill", true);
                lock (Lock) { _chillOwned = true; Save(); }
            }
        }
        catch (Exception) { /* sin AMD: AutoTDP funciona igual, sin tope de FPS */ }
    }

    private static void ReleaseChill()
    {
        try
        {
            bool owned;
            lock (Lock) owned = _chillOwned;
            if (owned) AmdAdlx.Set("chill", false);
            lock (Lock) { _chillOwned = false; Save(); }
        }
        catch (Exception) { }
    }
}
