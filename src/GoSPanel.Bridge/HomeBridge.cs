using System.Diagnostics;
using System.IO.Pipes;
using System.Text.Json.Nodes;

namespace GoSPanel.Bridge;

/// <summary>
/// Canal con GoPilot Home (el launcher a pantalla completa, repo dlavia/GoPilotHome). Tubería
/// «GoSPanel.Home.&lt;sesión&gt;», una línea JSON por mensaje:
/// · petición { id, cmd, args } → respuesta { id, ok, data | error }, con las mismas órdenes que el widget;
/// · la línea «subscribe» deja la conexión abierta para recibir avisos, p. ej. { evt: "legion", button: "L" }.
/// Mientras haya un launcher suscrito, Legion L / Legion R abren su menú en vez de la app de Xbox o Game Bar.
/// </summary>
internal static class HomeBridge
{
    public static readonly string PipeName = $"GoSPanel.Home.{Process.GetCurrentProcess().SessionId}";
    private static readonly object Lock = new();
    private static readonly List<StreamWriter> Subscribers = new();

    public static void Start() => _ = Task.Run(AcceptLoopAsync);

    private static async Task AcceptLoopAsync()
    {
        while (true)
        {
            try
            {
                var pipe = new NamedPipeServerStream(PipeName, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                await pipe.WaitForConnectionAsync();
                _ = Task.Run(() => ServeAsync(pipe));
            }
            catch (Exception)
            {
                await Task.Delay(1000);
            }
        }
    }

    private static async Task ServeAsync(NamedPipeServerStream pipe)
    {
        var reader = new StreamReader(pipe);
        var writer = new StreamWriter(pipe) { AutoFlush = true };
        var subscribed = false;
        try
        {
            while (pipe.IsConnected)
            {
                var line = await reader.ReadLineAsync();
                if (line == null) break;
                if (line == "subscribe")
                {
                    subscribed = true;
                    lock (Lock) Subscribers.Add(writer);
                    continue;
                }
                var request = JsonNode.Parse(line) as JsonObject ?? new JsonObject();
                var id = request["id"]?.DeepClone();
                request.Remove("id");
                var response = JsonNode.Parse(await Program.HandleAsync(request.ToJsonString())) as JsonObject ?? new JsonObject();
                response["id"] = id;
                lock (writer) writer.WriteLine(response.ToJsonString());
            }
        }
        catch (Exception) { /* el launcher se cerró */ }
        finally
        {
            if (subscribed) lock (Lock) Subscribers.Remove(writer);
            pipe.Dispose();
        }
    }

    /// <summary>Avisa a los launchers suscritos. Devuelve false si no hay ninguno (para usar la acción de siempre).</summary>
    public static bool Notify(JsonObject message)
    {
        List<StreamWriter> subscribers;
        lock (Lock) subscribers = Subscribers.ToList();
        var line = message.ToJsonString();
        var delivered = false;
        foreach (var writer in subscribers)
        {
            try
            {
                lock (writer) writer.WriteLine(line);
                delivered = true;
            }
            catch (Exception)
            {
                lock (Lock) Subscribers.Remove(writer);
            }
        }
        return delivered;
    }
}
