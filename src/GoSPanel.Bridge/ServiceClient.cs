using System.IO.Pipes;
using System.Text;
using System.Text.Json.Nodes;

namespace GoSPanel.Bridge;

/// <summary>Cliente de la named pipe del servicio GoSPanel. Reconecta solo si se cae.</summary>
internal sealed class ServiceClient : IDisposable
{
    private const string PipeName = "GoSPanel";

    private readonly SemaphoreSlim _gate = new(1, 1);
    private NamedPipeClientStream? _pipe;
    private StreamReader? _reader;
    private StreamWriter? _writer;

    public async Task<string> SendAsync(string line)
    {
        await _gate.WaitAsync();
        try
        {
            for (var attempt = 0; attempt < 2; attempt++)
            {
                try
                {
                    await EnsureConnectedAsync();
                    await _writer!.WriteLineAsync(line);
                    return await _reader!.ReadLineAsync() ?? throw new IOException("Conexión cerrada");
                }
                catch (Exception ex) when (ex is IOException or TimeoutException or ObjectDisposedException)
                {
                    Reset();
                }
            }
            return new JsonObject
            {
                ["ok"] = false,
                ["error"] = "El servicio GoSPanel no está en ejecución",
            }.ToJsonString();
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task EnsureConnectedAsync()
    {
        if (_pipe is { IsConnected: true }) return;
        Reset();
        _pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await _pipe.ConnectAsync(1500);
        _reader = new StreamReader(_pipe, Encoding.UTF8, false, 4096, leaveOpen: true);
        _writer = new StreamWriter(_pipe, new UTF8Encoding(false), 4096, leaveOpen: true) { AutoFlush = true };
    }

    private void Reset()
    {
        _reader?.Dispose();
        _writer?.Dispose();
        _pipe?.Dispose();
        (_reader, _writer, _pipe) = (null, null, null);
    }

    public void Dispose() => Reset();
}
