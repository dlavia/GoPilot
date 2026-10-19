using System.Text.Json;
using System.Text.Json.Nodes;
using Windows.Storage;

namespace GoSPanel.Bridge;

/// <summary>
/// Perfiles: uno «Global» (cuando no hay juego) y uno por juego (clave: nombre del ejecutable).
/// Al detectar un juego con perfil se guarda lo que había en Global y se aplica el del juego; al cerrarlo se
/// vuelve a Global. Desde el widget se puede alternar a mano entre Global y el perfil del juego en curso.
/// Se guardan en profiles.json dentro de la carpeta de datos del paquete.
/// </summary>
internal static class ProfileManager
{
    public const string GlobalTarget = "global";

    private static readonly string FilePath = Path.Combine(ApplicationData.Current.LocalFolder.Path, "profiles.json");
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    private static JsonObject _data = Load();
    private static string _active = GlobalTarget;   // perfil aplicado ahora: "global" o el .exe del juego
    private static int _activePid;                  // juego al que corresponde el perfil activo
    private static int _globalOverridePid;          // juego para el que el usuario eligió Global a mano
    private static bool _manual;
    private static int _autoSaveTick;                    // perfil de un juego elegido a mano desde la lista (sin su juego)
    // Juegos cuyo perfil se ha borrado a mano: no se les vuelve a crear uno solo mientras siga abierto el agente.
    private static readonly HashSet<string> NoAutoProfile = new(StringComparer.OrdinalIgnoreCase);

    public static void Start()
    {
        // Perfiles creados solos para apps que no son juegos (versiones anteriores contaban Edge, por ejemplo)
        var wrong = Profiles.Select(p => p.Key).Where(ForegroundTracker.IsIgnored).ToList();
        foreach (var exe in wrong) Profiles.Remove(exe);
        if (wrong.Count > 0) Save();
        _ = Task.Run(LoopAsync);
    }

    /// <summary>¿Hay un perfil guardado para ese ejecutable («juego.exe» en minúsculas)?</summary>
    public static bool HasProfile(string exe) => Profiles.ContainsKey(exe);

    private static JsonObject Profiles => (JsonObject)(_data["profiles"] ??= new JsonObject());
    private static bool Auto => _data["auto"]?.GetValue<bool>() ?? true;
    private static bool AutoSave => _data["autosave"]?.GetValue<bool>() ?? true;

    // ---------------------------------------------------------------------
    // Comandos del widget
    // ---------------------------------------------------------------------

    public static JsonObject List()
    {
        var list = new JsonArray();
        foreach (var (exe, profile) in Profiles)
        {
            var copy = (JsonObject)profile!.DeepClone();
            copy["exe"] = exe;
            list.Add(copy);
        }
        var current = ForegroundTracker.Current;
        return new JsonObject
        {
            ["auto"] = Auto,
            ["autosave"] = AutoSave,
            ["profiles"] = list,
            ["global"] = _data["global"]?.DeepClone(),
            ["active"] = _active,
            ["manual"] = _manual,
            ["currentExe"] = current?.Exe,
            ["currentName"] = current?.Name,
            // Compatibilidad con versiones anteriores del widget.
            ["appliedExe"] = _active == GlobalTarget ? null : _active,
        };
    }

    /// <summary>Guarda los ajustes actuales en el perfil activo (Global o el del juego).</summary>
    public static async Task<JsonObject> SaveCurrentAsync()
    {
        await Gate.WaitAsync();
        try
        {
            var current = ForegroundTracker.Current;
            var profile = await CaptureAsync();
            if (_active == GlobalTarget)
            {
                _data["global"] = profile;
            }
            else
            {
                var previous = Profiles[_active] as JsonObject;
                var ownGame = current != null && current.Value.Exe == _active;
                profile["name"] = ownGame ? current!.Value.Name : previous?["name"]?.GetValue<string>() ?? _active;
                // Lossless solo se puede leer con el juego delante; si no, se conserva lo guardado.
                profile["lossless"] = ownGame
                    ? LosslessScaling.Get()["active"]?.GetValue<bool>() ?? false
                    : previous?["lossless"]?.GetValue<bool>() ?? false;
                Profiles[_active] = profile;
            }
            Save();
        }
        finally
        {
            Gate.Release();
        }
        return List();
    }

    /// <summary>
    /// Cambia a mano de perfil: «global», «game» (el del juego en curso; si no tiene, se crea con lo actual)
    /// o el .exe de cualquier perfil guardado, aunque su juego no esté abierto (para usarlo o editarlo).
    /// </summary>
    public static async Task<JsonObject> UseAsync(string target)
    {
        await Gate.WaitAsync();
        try
        {
            var current = ForegroundTracker.Current;
            if (target == GlobalTarget)
            {
                _globalOverridePid = current?.Pid ?? 0;
                _manual = false;
                await SwitchToGlobalAsync();
            }
            else if (target != "game")
            {
                var exe = target.ToLowerInvariant();
                if (Profiles[exe] is not JsonObject) throw new InvalidOperationException("Ese perfil ya no existe");
                if (current != null && current.Value.Exe == exe)
                {
                    _globalOverridePid = 0;
                    _manual = false;
                    await SwitchToGameAsync(current.Value.Pid, exe);
                }
                else
                {
                    _globalOverridePid = current?.Pid ?? 0;   // no lo pisa el juego abierto ahora
                    await SwitchToGameAsync(0, exe);
                    _manual = true;
                }
            }
            else
            {
                if (current == null) throw new InvalidOperationException("No hay ningún juego detectado");
                _globalOverridePid = 0;
                _manual = false;
                if (Profiles[current.Value.Exe] is not JsonObject)
                {
                    // Sin perfil todavía: el juego hereda lo que hay ahora y queda guardado como su perfil.
                    var profile = await CaptureAsync();
                    profile["name"] = current.Value.Name;
                    profile["lossless"] = false;
                    Profiles[current.Value.Exe] = profile;
                    Save();
                }
                await SwitchToGameAsync(current.Value.Pid, current.Value.Exe);
            }
        }
        finally
        {
            Gate.Release();
        }
        return List();
    }

    public static JsonObject Delete(string exe)
    {
        NoAutoProfile.Add(exe.ToLowerInvariant());
        Profiles.Remove(exe.ToLowerInvariant());
        Save();
        return List();
    }

    public static JsonObject SetAutoSave(bool enabled)
    {
        _data["autosave"] = enabled;
        Save();
        return List();
    }

    public static JsonObject SetAuto(bool enabled)
    {
        _data["auto"] = enabled;
        Save();
        return List();
    }

    // ---------------------------------------------------------------------
    // Aplicación automática
    // ---------------------------------------------------------------------

    private static async Task LoopAsync()
    {
        while (true)
        {
            await Task.Delay(2000);
            await Gate.WaitAsync();
            try { await TickAsync(); }
            catch (Exception) { /* servicio caído o juego cerrado a mitad: se reintenta */ }
            finally { Gate.Release(); }
        }
    }

    private static async Task TickAsync()
    {
        var current = ForegroundTracker.Current;
        // La elección manual de Global dura mientras siga abierto ese juego.
        if (_globalOverridePid != 0 && (current == null || current.Value.Pid != _globalOverridePid)) _globalOverridePid = 0;

        // Juego nuevo, sin perfil: se le crea uno con los ajustes de ahora (los de Global), como Armoury Crate. Así lo
        // que se ajuste jugando se queda en ese juego (autoguardado) y la pastilla muestra que se ha detectado.
        // Solo para juegos de una biblioteca (Steam, Epic…): uno reconocido solo por ocupar la pantalla puede no serlo.
        if (Auto && current != null && current.Value.Pid != _globalOverridePid && !_manual && ForegroundTracker.CurrentIsLibrary
            && Profiles[current.Value.Exe] is not JsonObject && !NoAutoProfile.Contains(current.Value.Exe))
        {
            var profile = await CaptureAsync();
            profile["name"] = current.Value.Name;
            profile["lossless"] = false;
            Profiles[current.Value.Exe] = profile;
            Save();
        }

        var wantsGame = Auto && current != null && current.Value.Pid != _globalOverridePid
                        && Profiles[current.Value.Exe] is JsonObject;

        // Un perfil elegido a mano se mantiene hasta que se elija otro o se abra un juego con perfil.
        if (_manual && !wantsGame) return;
        _manual = false;

        if (wantsGame && (_active != current!.Value.Exe || _activePid != current.Value.Pid))
            await SwitchToGameAsync(current.Value.Pid, current.Value.Exe);
        else if (!wantsGame && _active != GlobalTarget && (current == null || current.Value.Pid != _activePid))
            await SwitchToGlobalAsync();   // el juego se cerró o se cambió a uno sin perfil
        else if (AutoSave && ++_autoSaveTick % 3 == 0)
            await AutoSaveAsync(current);
    }

    private static async Task SwitchToGameAsync(int pid, string exe)
    {
        // Al salir de Global se guarda lo que había, para volver a ello al cerrar el juego.
        if (_active == GlobalTarget)
        {
            _data["global"] = await CaptureAsync();
            Save();
        }
        _active = exe;
        _activePid = pid;
        if (Profiles[exe] is JsonObject profile) await ApplyAsync(profile, pid);
    }

    private static async Task SwitchToGlobalAsync()
    {
        _active = GlobalTarget;
        _activePid = 0;
        if (_data["global"] is JsonObject global) await ApplyAsync(global, 0);
    }

    /// <summary>
    /// Autoguardado: si algo ha cambiado respecto al perfil activo (modo, TDP, CPU Boost, pantalla o, con su
    /// juego delante, Lossless), se guarda en ese perfil. Así no hace falta acordarse del disquete.
    /// </summary>
    private static async Task AutoSaveAsync((int Pid, string Exe, string Name)? current)
    {
        var captured = await CaptureAsync();
        var stored = _active == GlobalTarget ? _data["global"] as JsonObject : Profiles[_active] as JsonObject;
        if (stored is null) return;   // perfil aún sin crear: lo crea el disquete o la lista

        var ownGame = _active != GlobalTarget && current != null && current.Value.Exe == _active;
        if (ownGame) captured["lossless"] = LosslessScaling.Get()["active"]?.GetValue<bool>() ?? false;

        bool Same(string key) => JsonNode.DeepEquals(captured[key], stored[key]);
        var mode = captured["mode"]?.GetValue<string>();
        var changed = !Same("mode") || !Same("boost") || !Same("width") || !Same("height") || !Same("hz")
                      || (mode == "custom" && !Same("tdp")) || (ownGame && !Same("lossless"))
                      || (captured["amd"] != null && !Same("amd")) || (captured["lights"] != null && !Same("lights"))
                      || (captured["gyroMode"] != null && !Same("gyroMode"));
        if (!changed) return;

        foreach (var (key, value) in captured)
            stored[key] = value?.DeepClone();
        Save();
    }

    /// <summary>Ajustes actuales: modo, TDP de Manual, CPU Boost y pantalla.</summary>
    private static async Task<JsonObject> CaptureAsync()
    {
        var power = await Program.CallServiceAsync("power.get");
        var tdp = await Program.CallServiceAsync("tdp.get");
        var boost = await Program.CallServiceAsync("boost.get");
        var display = DisplayControl.Get()["current"]!.AsObject();
        var profile = new JsonObject
        {
            ["mode"] = power["mode"]?.GetValue<string>(),
            ["tdp"] = new JsonObject { ["spl"] = tdp["spl"]?.GetValue<double>(), ["sppt"] = tdp["sppt"]?.GetValue<double>(), ["fppt"] = tdp["fppt"]?.GetValue<double>() },
            ["boost"] = boost["enabled"]?.GetValue<bool>(),
            ["width"] = display["width"]!.GetValue<int>(),
            ["height"] = display["height"]!.GetValue<int>(),
            ["hz"] = display["hz"]!.GetValue<int>(),
        };

        // AMD (solo lo que la gráfica admite)
        try
        {
            var amd = AmdAdlx.Get();
            if (amd["available"]?.GetValue<bool>() == true)
            {
                var flags = new JsonObject();
                foreach (var f in AmdAdlx.Features)
                    if (amd[f] is JsonObject info && info["supported"]?.GetValue<bool>() == true)
                        flags[f] = info["enabled"]?.GetValue<bool>() ?? false;
                profile["amd"] = flags;
            }
        }
        catch (Exception) { }

        // Luces de los sticks
        try
        {
            var l = StickLights.Get();
            if (l["available"]?.GetValue<bool>() == true)
                profile["lights"] = new JsonObject
                {
                    ["enabled"] = l["enabled"]?.DeepClone(), ["effect"] = l["effect"]?.DeepClone(), ["color"] = l["color"]?.DeepClone(),
                    ["brightness"] = l["brightness"]?.DeepClone(), ["speed"] = l["speed"]?.DeepClone(),
                };
        }
        catch (Exception) { }

        // Modo del giroscopio (nada / ratón / stick)
        try
        {
            var adv = await StickLights.GetAdvancedAsync();
            if (adv["gyroMode"] is JsonValue g) profile["gyroMode"] = g.GetValue<int>();
        }
        catch (Exception) { }
        return profile;
    }

    private static async Task ApplyAsync(JsonObject p, int gamePid)
    {
        var mode = p["mode"]?.GetValue<string>();
        if (mode == "custom" && p["tdp"] is JsonObject tdp)
            await Program.CallServiceAsync("tdp.set", (JsonObject)tdp.DeepClone());   // tdp.set ya pone el modo Manual
        else if (mode != null)
            await Program.CallServiceAsync("power.set", new JsonObject { ["mode"] = mode });

        if (p["boost"] is JsonNode boost)
            await Program.CallServiceAsync("boost.set", new JsonObject { ["enabled"] = boost.GetValue<bool>() });

        if (p["width"] is JsonNode w && p["height"] is JsonNode h && p["hz"] is JsonNode hz)
        {
            var current = DisplayControl.Get()["current"]!.AsObject();
            if (current["width"]!.GetValue<int>() != w.GetValue<int>() || current["height"]!.GetValue<int>() != h.GetValue<int>()
                || current["hz"]!.GetValue<int>() != hz.GetValue<int>())
                DisplayControl.Set(w.GetValue<int>(), h.GetValue<int>(), hz.GetValue<int>());
        }

        if (p["amd"] is JsonObject amdWanted)
        {
            try
            {
                var amd = AmdAdlx.Get();
                foreach (var (f, v) in amdWanted)
                    if (v is JsonValue want && amd[f] is JsonObject info && info["supported"]?.GetValue<bool>() == true
                        && (info["enabled"]?.GetValue<bool>() ?? false) != want.GetValue<bool>())
                        AmdAdlx.Set(f, want.GetValue<bool>());
            }
            catch (Exception) { /* AMD opcional */ }
        }

        if (p["lights"] is JsonObject lights)
        {
            try
            {
                var current = StickLights.Get();
                if (lights.Any(kv => !JsonNode.DeepEquals(kv.Value, current[kv.Key])))
                    await StickLights.SetAsync((JsonObject)lights.DeepClone());
            }
            catch (Exception) { }
        }

        if (p["gyroMode"] is JsonValue gyro)
        {
            try
            {
                var adv = await StickLights.GetAdvancedAsync();
                if (adv["gyroMode"]?.GetValue<int>() != gyro.GetValue<int>())
                    await StickLights.SetAdvancedAsync(new JsonObject { ["gyroMode"] = gyro.GetValue<int>() });
            }
            catch (Exception) { }
        }

        if (gamePid != 0 && p["lossless"]?.GetValue<bool>() == true && LosslessScaling.Installed)
        {
            await Task.Delay(5000);   // que el juego termine de abrir su ventana
            if (ForegroundTracker.CurrentPid == gamePid) await LosslessScaling.EnsureActiveAsync(gamePid);
        }
    }

    // ---------------------------------------------------------------------
    // Archivo
    // ---------------------------------------------------------------------

    private static JsonObject Load()
    {
        try
        {
            if (File.Exists(FilePath) && JsonNode.Parse(File.ReadAllText(FilePath)) is JsonObject data) return data;
        }
        catch (Exception) { /* archivo dañado: se empieza de cero */ }
        return new JsonObject { ["auto"] = true, ["profiles"] = new JsonObject() };
    }

    private static void Save() => File.WriteAllText(FilePath, _data.ToJsonString(Indented));
}
