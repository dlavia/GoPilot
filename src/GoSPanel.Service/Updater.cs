using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Security;
using System.Text.Json.Nodes;

namespace GoSPanel.Service;

/// <summary>
/// Actualizaciones desde las versiones publicadas en GitHub (Releases de dlavia/GoPilot) y desinstalación.
/// El servicio solo descarga y descomprime; la instalación la hace apply-update.ps1 (junto al servicio) en una
/// tarea programada como SYSTEM, porque tiene que parar y sustituir este mismo servicio. Ese script comprueba que
/// el paquete y el servicio nuevos van firmados con el certificado de GoPilot antes de instalar nada.
/// </summary>
internal static class Updater
{
    private const string Repo = "dlavia/GoPilot";
    private static readonly TimeSpan CacheFor = TimeSpan.FromHours(1);
    private static readonly HttpClient Http = CreateClient();
    private static readonly object Sync = new();

    private static JsonObject? _release;          // {version, notes, url, size}
    private static DateTime _checkedAt;
    private static string _state = "idle";        // idle | downloading | installing | error
    private static int _progress;
    private static string? _error;

    private static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "GoSPanel", "update");
    private static string ServiceFolder => AppContext.BaseDirectory;

    /// <summary>Última versión publicada (guardada una hora salvo que se fuerce).</summary>
    public static async Task<JsonObject> CheckAsync(bool force)
    {
        if (!force && _release != null && DateTime.UtcNow - _checkedAt < CacheFor) return Status();

        // latest.json se publica como archivo de cada versión (release.ps1). Se descarga en lugar de usar la API de
        // GitHub porque esta solo admite 60 consultas por hora e IP, y con IP compartida (VPN, operador) se agotan.
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var response = await Http.GetAsync(NoCache($"https://github.com/{Repo}/releases/latest/download/latest.json"), timeout.Token);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            lock (Sync) { _release = null; _checkedAt = DateTime.UtcNow; }   // aún no hay ninguna versión publicada
            return Status();
        }
        response.EnsureSuccessStatusCode();
        var json = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        var version = (string?)json["version"] ?? throw new InvalidOperationException("latest.json sin versión");
        lock (Sync)
        {
            _release = new JsonObject
            {
                ["version"] = version,
                ["notes"] = (string?)json["notes"] ?? "",
                ["url"] = $"https://github.com/{Repo}/releases/download/v{version}/GoPilot-{version}.zip",
                ["size"] = (long?)json["size"] ?? 0,
            };
            _checkedAt = DateTime.UtcNow;
        }
        return Status();
    }

    public static JsonObject Status()
    {
        lock (Sync)
        {
            return new JsonObject
            {
                ["state"] = _state,
                ["progress"] = _progress,
                ["error"] = _error,
                ["checkedAt"] = _checkedAt == default ? null : _checkedAt.ToString("o"),
                ["latest"] = _release?.DeepClone(),
                ["last"] = LastResult(),
            };
        }
    }

    /// <summary>Empieza a descargar e instalar la última versión; el avance se consulta con Status().</summary>
    public static JsonObject Install()
    {
        JsonObject release;
        lock (Sync)
        {
            if (_state is "downloading" or "installing") return Status();
            release = _release?.DeepClone() as JsonObject ?? throw new InvalidOperationException("No hay ninguna versión nueva");
            _state = "downloading";
            _progress = 0;
            _error = null;
        }
        _ = Task.Run(() => RunAsync(release));
        return Status();
    }

    private static async Task RunAsync(JsonObject release)
    {
        try
        {
            var version = (string)release["version"]!;
            PrepareFolder();
            var zip = Path.Combine(Folder, $"GoPilot-{version}.zip");
            var target = Path.Combine(Folder, version);

            // GitHub a veces responde 502/503 o corta la descarga: se reintenta antes de dar error.
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    await DownloadAsync((string)release["url"]!, zip, (long)release["size"]!);
                    break;
                }
                catch (Exception) when (attempt < 3)
                {
                    await Task.Delay(TimeSpan.FromSeconds(3 * attempt));
                }
            }

            if (Directory.Exists(target)) Directory.Delete(target, true);
            ZipFile.ExtractToDirectory(zip, target);
            File.Delete(zip);
            lock (Sync) { _state = "installing"; _progress = 100; }

            // Desde aquí sigue el script: parará este servicio, así que no se espera su resultado.
            var script = Path.Combine(ServiceFolder, "apply-update.ps1");
            RunAsSystem("GoSPanel-Update", script, $"-Source \"{target}\"");
        }
        catch (Exception ex)
        {
            lock (Sync) { _state = "error"; _error = ex.Message; }
        }
    }

    private static async Task DownloadAsync(string url, string zip, long size)
    {
        lock (Sync) _progress = 0;
        using var response = await Http.GetAsync(NoCache(url), HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength ?? size;
        await using var source = await response.Content.ReadAsStreamAsync();
        await using var file = File.Create(zip);
        var buffer = new byte[81920];
        long done = 0;
        int read;
        while ((read = await source.ReadAsync(buffer)) > 0)
        {
            await file.WriteAsync(buffer.AsMemory(0, read));
            done += read;
            if (total > 0) lock (Sync) _progress = (int)(done * 100 / total);
        }
        if (total > 0 && done != total) throw new IOException("Descarga incompleta");
    }

    /// <summary>
    /// Desinstala GoPilot: el widget, el servicio, sus datos y el certificado. Lo hace uninstall.ps1 como SYSTEM
    /// desde una copia temporal, porque borra la carpeta del servicio.
    /// </summary>
    public static JsonObject Uninstall()
    {
        var copy = Path.Combine(Path.GetTempPath(), "gopilot-uninstall.ps1");
        File.Copy(Path.Combine(ServiceFolder, "uninstall.ps1"), copy, true);
        RunAsSystem("GoSPanel-Uninstall", copy, "");
        return new JsonObject { ["started"] = true };
    }

    /// <summary>Resultado de la última instalación (lo escribe apply-update.ps1 en status.json).</summary>
    private static JsonNode? LastResult()
    {
        try { return JsonNode.Parse(File.ReadAllText(Path.Combine(Folder, "status.json"))); }
        catch (Exception) { return null; }
    }

    /// <summary>
    /// Carpeta de descargas solo modificable por SYSTEM y administradores: lo que hay dentro se instala con
    /// permisos de SYSTEM, así que ningún usuario normal debe poder cambiarlo.
    /// </summary>
    private static void PrepareFolder()
    {
        Directory.CreateDirectory(Folder);
        Run("icacls.exe", $"\"{Folder}\" /inheritance:r /grant:r *S-1-5-18:(OI)(CI)F *S-1-5-32-544:(OI)(CI)F *S-1-5-32-545:(OI)(CI)RX");
        foreach (var dir in Directory.GetDirectories(Folder)) Directory.Delete(dir, true);
    }

    private static void RunAsSystem(string taskName, string script, string arguments)
    {
        var xml = $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <Principals>
                <Principal id="Author">
                  <UserId>S-1-5-18</UserId>
                  <RunLevel>HighestAvailable</RunLevel>
                </Principal>
              </Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <ExecutionTimeLimit>PT30M</ExecutionTimeLimit>
              </Settings>
              <Actions Context="Author">
                <Exec>
                  <Command>powershell.exe</Command>
                  <Arguments>{SecurityElement.Escape($"-NoProfile -ExecutionPolicy Bypass -File \"{script}\" {arguments}")}</Arguments>
                </Exec>
              </Actions>
            </Task>
            """;
        var file = Path.Combine(Path.GetTempPath(), $"{taskName}.xml");
        File.WriteAllText(file, xml, System.Text.Encoding.Unicode);
        try
        {
            Run("schtasks.exe", $"/Create /TN \"{taskName}\" /XML \"{file}\" /F", check: true);
            Run("schtasks.exe", $"/Run /TN \"{taskName}\"", check: true);
        }
        finally
        {
            File.Delete(file);
        }
    }

    private static void Run(string file, string args, bool check = false)
    {
        using var p = Process.Start(new ProcessStartInfo(file, args)
        {
            CreateNoWindow = true, UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true,
        })!;
        p.WaitForExit(15000);
        if (check && p.ExitCode != 0) throw new InvalidOperationException($"{file}: {p.StandardError.ReadToEnd().Trim()}");
    }

    /// <summary>
    /// La caché de GitHub llegó a guardar un error 502 para la dirección de un zip y seguía devolviéndolo; con un
    /// parámetro distinto en cada petición siempre se pide de nuevo (GitHub lo ignora al redirigir al archivo).
    /// </summary>
    private static string NoCache(string url) => $"{url}?t={DateTime.UtcNow.Ticks}";

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("GoPilot", "1.0"));
        return client;
    }
}
