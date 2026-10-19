using System.Text.Json;
using System.Text.Json.Nodes;

namespace GoSPanel.Service;

/// <summary>Herramienta de consola para explorar el hardware directamente en la Legion.</summary>
internal static class Cli
{
    private static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };

    public static int Run(string[] args)
    {
        try
        {
            switch (args[0].ToLowerInvariant())
            {
                case "probe":
                    Print(LenovoWmi.Probe());
                    return 0;

                case "status":
                    SystemInfo.Status();          // primera muestra de CPU
                    Thread.Sleep(500);
                    Print(SystemInfo.Status());
                    return 0;

                case "call" when args.Length >= 3:
                    var callArgs = args.Skip(3)
                        .Select(a => a.Split('=', 2))
                        .ToDictionary(kv => kv[0], kv => (object)kv[1]);
                    var result = LenovoWmi.Call(args[1], args[2], callArgs);
                    var output = new JsonObject();
                    foreach (var (name, value) in result)
                        output[name] = value switch
                        {
                            null => null,
                            Array array => JsonValue.Create(string.Join(",", array.Cast<object>())),
                            _ => JsonValue.Create(value.ToString()),
                        };
                    Print(output);
                    return 0;

                case "cmd" when args.Length >= 2:
                    var request = JsonNode.Parse(args[1]) as JsonObject ?? throw new ArgumentException("JSON no válido");
                    Print(new CommandRouter().Handle(request));
                    return 0;

                default:
                    Console.WriteLine("""
                        Uso (como administrador):
                          GoSPanel.Service.exe probe
                          GoSPanel.Service.exe status
                          GoSPanel.Service.exe call <CLASE_LENOVO> <Método> [Param=valor ...]
                          GoSPanel.Service.exe cmd "{\"cmd\":\"tdp.get\"}"
                        Sin argumentos se ejecuta como servicio de Windows.
                        """);
                    return 2;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }

    private static void Print(JsonNode? node) => Console.WriteLine(node?.ToJsonString(Pretty) ?? "null");
}
