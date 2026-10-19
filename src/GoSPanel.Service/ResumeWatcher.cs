using System.Runtime.InteropServices;
using System.Text.Json.Nodes;

namespace GoSPanel.Service;

/// <summary>
/// Detecta la vuelta de la suspensión comparando el reloj normal con el tiempo «no sesgado» (que no avanza
/// mientras el equipo duerme) y reaplica el modo, el TDP y el CPU Boost guardados.
/// </summary>
public sealed class ResumeWatcher(CommandRouter router, ILogger<ResumeWatcher> log) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan SleepThreshold = TimeSpan.FromSeconds(10);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var lastWall = DateTime.UtcNow;
        var lastAwake = AwakeTime();
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(Interval, ct);
            var wall = DateTime.UtcNow;
            var awake = AwakeTime();
            var slept = (wall - lastWall) - (awake - lastAwake);
            (lastWall, lastAwake) = (wall, awake);

            if (slept < SleepThreshold) continue;
            if (!SavedState.ReapplyOnResume)
            {
                Journal($"Reanudado tras {slept.TotalMinutes:0.0} min; reaplicar desactivado");
                continue;
            }
            log.LogInformation("Reanudado tras {Seconds:0} s de suspensión; reaplicando ajustes", slept.TotalSeconds);
            await Task.Delay(TimeSpan.FromSeconds(3), ct);   // dejar que el firmware termine de despertar
            var before = router.Handle(new JsonObject { ["cmd"] = "power.get" })["data"]?["mode"];
            Reapply();
            var after = router.Handle(new JsonObject { ["cmd"] = "power.get" })["data"]?["mode"];
            Journal($"Reanudado tras {slept.TotalMinutes:0.0} min; modo al despertar: {before}, tras reaplicar: {after}");
        }
    }

    private void Reapply()
    {
        var state = SavedState.Load();
        var mode = state["mode"]?.GetValue<string>();
        if (mode is not null)
            Run(new JsonObject { ["cmd"] = "power.set", ["args"] = new JsonObject { ["mode"] = mode } });
        if (mode == "custom" && state["tdp"] is JsonObject tdp)
            Run(new JsonObject { ["cmd"] = "tdp.set", ["args"] = tdp.DeepClone() });
        if (state["boost"] is JsonNode boost)
            Run(new JsonObject { ["cmd"] = "boost.set", ["args"] = new JsonObject { ["enabled"] = boost.GetValue<bool>() } });
        if (state["fullFan"] is JsonNode fan && fan.GetValue<bool>())
            Run(new JsonObject { ["cmd"] = "fan.set", ["args"] = new JsonObject { ["enabled"] = true } });
    }

    private void Run(JsonObject request)
    {
        var response = router.Handle(request);
        if (response["ok"]?.GetValue<bool>() != true)
            log.LogWarning("No se pudo reaplicar {Cmd}: {Error}", request["cmd"], response["error"]);
    }

    /// <summary>Diario legible en %ProgramData%\GoSPanel\resume.log (las reanudaciones no salen en el visor de eventos).</summary>
    private static void Journal(string line)
    {
        try
        {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "GoSPanel", "resume.log");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {line}{Environment.NewLine}");
        }
        catch (Exception) { /* solo diagnóstico */ }
    }

    private static TimeSpan AwakeTime()
    {
        QueryUnbiasedInterruptTime(out var ticks100ns);
        return TimeSpan.FromTicks((long)ticks100ns);
    }

    [DllImport("kernel32.dll")]
    private static extern bool QueryUnbiasedInterruptTime(out ulong unbiasedTime);
}
