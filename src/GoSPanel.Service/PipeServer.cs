using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json.Nodes;

namespace GoSPanel.Service;

public sealed class PipeServer(CommandRouter router, ILogger<PipeServer> log) : BackgroundService
{
    public const string PipeName = "GoSPanel";

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        log.LogInformation("Escuchando en \\\\.\\pipe\\{Pipe}", PipeName);
        while (!ct.IsCancellationRequested)
        {
            var pipe = CreatePipe();
            try
            {
                await pipe.WaitForConnectionAsync(ct);
            }
            catch (OperationCanceledException)
            {
                await pipe.DisposeAsync();
                break;
            }
            _ = Task.Run(() => ServeAsync(pipe, ct), ct);
        }
    }

    private static NamedPipeServerStream CreatePipe()
    {
        // Solo SYSTEM, administradores y el usuario con sesión interactiva (el puente del widget).
        var security = new PipeSecurity();
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.InteractiveSid, null),
            PipeAccessRights.ReadWrite, AccessControlType.Allow));

        return NamedPipeServerStreamAcl.Create(PipeName, PipeDirection.InOut,
            NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous, 0, 0, security);
    }

    private async Task ServeAsync(NamedPipeServerStream pipe, CancellationToken ct)
    {
        await using var _ = pipe;
        using var reader = new StreamReader(pipe, Encoding.UTF8, false, 4096, leaveOpen: true);
        using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, leaveOpen: true) { AutoFlush = true };
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(ct);
                if (line is null) break;

                JsonObject response;
                try
                {
                    response = JsonNode.Parse(line) is JsonObject req
                        ? router.Handle(req)
                        : CommandRouter.Error("Petición no válida");
                }
                catch (Exception ex)
                {
                    response = CommandRouter.Error(ex.Message);
                }
                await writer.WriteLineAsync(response.ToJsonString());
            }
        }
        catch (IOException) { /* cliente desconectado */ }
        catch (OperationCanceledException) { }
    }
}
