using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Session;

namespace GoSPanel.Service;

/// <summary>
/// Contador de FPS al estilo PresentMon: escucha por ETW los eventos «Present» de DXGI (DirectX 10/11/12 y la
/// mayoría de juegos Vulkan/OpenGL en Windows, que presentan mediante DXGI) y de Direct3D 9, y los cuenta por
/// proceso. Necesita privilegios de administrador, por eso vive en el servicio.
/// </summary>
public sealed class FpsMonitor(ILogger<FpsMonitor> log) : BackgroundService
{
    private const string SessionName = "GoSPanel-FPS";
    private static readonly Guid DxgiProvider = new("CA11C036-0102-4A2D-A6AD-F03CFED5D3C9");
    private static readonly Guid D3D9Provider = new("783ACA0A-790E-4D7F-8451-AA850511C6B9");
    private const int DxgiPresentStart = 42, D3D9PresentStart = 1;

    private static readonly ConcurrentDictionary<int, int> Counting = new();
    private static volatile Dictionary<int, double> _fps = new();
    private static DateTime _windowStart = DateTime.UtcNow;

    /// <summary>
    /// FPS del proceso indicado durante el último segundo (null si no presenta fotogramas). Si ese proceso no
    /// presenta (navegadores, lanzadores con proceso de render aparte), se usa el proceso del mismo nombre que más presente.
    /// </summary>
    public static JsonObject Get(int? pid)
    {
        var snapshot = _fps;
        double? fps = null;
        if (pid is int p)
        {
            if (snapshot.TryGetValue(p, out var v)) fps = v;
            else
            {
                string? name = null;
                try { name = System.Diagnostics.Process.GetProcessById(p).ProcessName; } catch (Exception) { }
                if (name != null)
                    foreach (var (otherPid, otherFps) in snapshot)
                    {
                        try
                        {
                            if (System.Diagnostics.Process.GetProcessById(otherPid).ProcessName == name && otherFps > (fps ?? 0)) fps = otherFps;
                        }
                        catch (Exception) { }
                    }
            }
        }
        return new JsonObject
        {
            ["fps"] = fps is null ? null : Math.Round(fps.Value),
            ["frameTimeMs"] = fps is > 0 ? Math.Round(1000 / fps.Value, 1) : null,
        };
    }

    /// <summary>Diagnóstico: procesos que han presentado fotogramas en el último segundo.</summary>
    public static JsonObject Top()
    {
        var list = new JsonObject();
        foreach (var (pid, fps) in _fps.OrderByDescending(kv => kv.Value).Take(8))
        {
            string name;
            try { name = System.Diagnostics.Process.GetProcessById(pid).ProcessName; } catch (Exception) { name = "?"; }
            list[$"{pid} {name}"] = Math.Round(fps);
        }
        return list;
    }

    protected override Task ExecuteAsync(CancellationToken ct) => Task.Run(() => Run(ct), ct);

    private void Run(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var session = new TraceEventSession(SessionName) { StopOnDispose = true };
                using var registration = ct.Register(() => session.Dispose());
                session.EnableProvider(DxgiProvider, TraceEventLevel.Informational, ulong.MaxValue);
                session.EnableProvider(D3D9Provider, TraceEventLevel.Informational, ulong.MaxValue);

                session.Source.AllEvents += OnEvent;
                using var timer = new Timer(_ => Roll(), null, 1000, 1000);
                log.LogInformation("Contador de FPS activo (ETW)");
                session.Source.Process();   // bloquea hasta que se cierra la sesión
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                log.LogWarning(ex, "Sesión ETW de FPS interrumpida; se reintenta en 5 s");
                Thread.Sleep(5000);
            }
        }
    }

    private static void OnEvent(TraceEvent e)
    {
        var id = (int)e.ID;
        if ((e.ProviderGuid == DxgiProvider && id == DxgiPresentStart) || (e.ProviderGuid == D3D9Provider && id == D3D9PresentStart))
            Counting.AddOrUpdate(e.ProcessID, 1, (_, n) => n + 1);
    }

    private static void Roll()
    {
        var now = DateTime.UtcNow;
        var seconds = Math.Max(0.001, (now - _windowStart).TotalSeconds);
        _windowStart = now;
        // Media móvil exponencial: evita picos sueltos cuando un lote de eventos ETW llega tarde.
        var previous = _fps;
        var next = new Dictionary<int, double>();
        foreach (var pid in Counting.Keys.ToList())
        {
            if (!Counting.TryRemove(pid, out var count)) continue;
            var sample = count / seconds;
            next[pid] = previous.TryGetValue(pid, out var last) ? last * 0.6 + sample * 0.4 : sample;
        }
        _fps = next;
    }
}
