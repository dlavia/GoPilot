using System.IO.Pipes;
using System.Text.Json.Nodes;
using Windows.ApplicationModel;
using Windows.ApplicationModel.AppService;
using Windows.Foundation.Collections;

namespace GoSPanel.Bridge;

/// <summary>
/// Agente de la sesión del usuario. Arranca con Windows (tarea de inicio del paquete) y se queda en segundo
/// plano vigilando el juego en primer plano para aplicar perfiles. Cuando se abre el widget, este lanza una
/// copia con el argumento «relay» que solo avisa al agente para que se conecte al widget y termina.
/// Si no hay agente en marcha, esa copia pasa a ser el agente.
/// </summary>
internal static class Program
{
    private const string AppServiceName = "GoSPanel.Bridge";
    private static readonly string AgentPipe = $"GoSPanel.Agent.{System.Diagnostics.Process.GetCurrentProcess().SessionId}";
    private static readonly string[] UserSessionCommands =
        { "display.", "volume.", "foreground.", "keyboard.", "profiles.", "ls.", "amd.", "lights.", "gyro.", "overlay.", "buttons.", "controller.", "vrr.", "legionwidget." };

    private static readonly ServiceClient Service = new();
    private static readonly SemaphoreSlim ConnectGate = new(1, 1);
    private static AppServiceConnection? _connection;

    private static async Task<int> Main(string[] args)
    {
        // Diagnóstico: «GoSPanel.Bridge.exe amd-test salida.json» vuelca la lectura de ADLX (solo lectura).
        if (args.Length >= 2 && args[0] == "amd-test")
        {
            File.WriteAllText(args[1], AmdAdlx.Get().ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }
        // Diagnóstico: «GoSPanel.Bridge.exe action-test gopilot» ejecuta una acción de los botones Legion.
        if (args.Length >= 2 && args[0] == "action-test")
        {
            await LegionButtons.RunAsync(args[1]);
            return 0;
        }
        // Diagnóstico: «GoSPanel.Bridge.exe lights-test salida.txt» busca el mando y escribe la ruta HID encontrada.
        if (args.Length >= 2 && args[0] == "lights-test")
        {
            File.WriteAllText(args[1], StickLights.FindDeviceForTest() ?? "no encontrado");
            return 0;
        }

        // Diagnóstico: «GoSPanel.Bridge.exe advanced-set archivo.json salida.json» aplica ajustes avanzados desde un archivo.
        if (args.Length >= 3 && args[0] == "advanced-set")
        {
            string result;
            try { result = (await StickLights.SetAdvancedAsync((JsonObject)JsonNode.Parse(File.ReadAllText(args[1]))!)).ToJsonString(); }
            catch (Exception ex) { result = ex.ToString(); }
            File.WriteAllText(args[2], result);
            return 0;
        }

        // Diagnóstico: «GoSPanel.Bridge.exe advanced-test salida.json» lee los ajustes avanzados del mando.
        if (args.Length >= 2 && args[0] == "advanced-test")
        {
            string result;
            try { result = (await StickLights.GetAdvancedAsync()).ToJsonString() + Environment.NewLine + await StickLights.DumpAsync(); }
            catch (Exception ex) { result = ex.ToString(); }
            File.WriteAllText(args[1], result);
            return 0;
        }

        // Diagnóstico: «GoSPanel.Bridge.exe volume-test salida.txt [valor]» lee (o pone) el volumen maestro.
        if (args.Length >= 2 && args[0] == "volume-test")
        {
            string result;
            try { result = (args.Length >= 3 ? Volume.Set(int.Parse(args[2])) : Volume.Get()).ToJsonString(); }
            catch (Exception ex) { result = ex.ToString(); }
            File.WriteAllText(args[1], result);
            return 0;
        }
        // Diagnóstico: «GoSPanel.Bridge.exe hid-raw salida.txt 0F 01 [datos…]» envía una orden cruda al mando (MI_03).
        if (args.Length >= 4 && args[0] == "hid-raw")
        {
            var bytes = args.Skip(2).Select(b => Convert.ToByte(b, 16)).ToArray();
            string result;
            try { result = await StickLights.RawAsync(bytes[0], bytes[1], bytes.Skip(2).ToArray()); }
            catch (Exception ex) { result = ex.ToString(); }
            File.WriteAllText(args[1], result);
            return 0;
        }
        // Diagnóstico: «GoSPanel.Bridge.exe controller-set clave valor salida.json» cambia un ajuste del mando y lo relee.
        if (args.Length >= 4 && args[0] == "controller-set")
        {
            string result;
            try
            {
                JsonNode value = double.TryParse(args[2], out var n) ? JsonValue.Create(n) : JsonValue.Create(args[2]);
                result = (await StickLights.SetControllerAsync(new JsonObject { [args[1]] = value })).ToJsonString();
            }
            catch (Exception ex) { result = ex.ToString(); }
            File.WriteAllText(args[3], result);
            return 0;
        }

        // Diagnóstico: «GoSPanel.Bridge.exe controller-test salida.json» lee los ajustes del mando (solo lectura).
        if (args.Length >= 2 && args[0] == "controller-test")
        {
            File.WriteAllText(args[1], (await StickLights.GetControllerAsync()).ToJsonString());
            return 0;
        }

        var relay = args.Any(a => a.Equals("relay", StringComparison.OrdinalIgnoreCase));
        if (relay && await SignalAgentAsync())
            return 0;   // el agente ya estaba en marcha y se conecta él al widget

        using var single = new Mutex(true, @"Local\GoSPanel.Agent", out var isFirst);
        if (!isFirst) return 0;

        ForegroundTracker.Start();
        ProfileManager.Start();
        GameOverlay.Start();
        LegionButtons.Start();
        _ = Task.Run(StickLights.ResumeReactiveAsync);
        _ = Task.Run(ListenForSignalsAsync);
        if (relay) await ConnectToWidgetAsync();

        await Task.Delay(Timeout.Infinite);
        return 0;
    }

    // ---------------------------------------------------------------------
    // Coordinación agente ↔ copia lanzada por el widget
    // ---------------------------------------------------------------------

    private static async Task<bool> SignalAgentAsync()
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", AgentPipe, PipeDirection.Out);
            await pipe.ConnectAsync(1000);
            using var writer = new StreamWriter(pipe) { AutoFlush = true };
            await writer.WriteLineAsync("connect");
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static async Task ListenForSignalsAsync()
    {
        while (true)
        {
            try
            {
                using var pipe = new NamedPipeServerStream(AgentPipe, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                await pipe.WaitForConnectionAsync();
                using var reader = new StreamReader(pipe);
                if (await reader.ReadLineAsync() == "connect")
                    await ConnectToWidgetAsync();
            }
            catch (Exception)
            {
                await Task.Delay(500);
            }
        }
    }

    private static async Task ConnectToWidgetAsync()
    {
        await ConnectGate.WaitAsync();
        try
        {
            if (_connection != null) return;
            var connection = new AppServiceConnection
            {
                AppServiceName = AppServiceName,
                PackageFamilyName = Package.Current.Id.FamilyName,
            };
            connection.RequestReceived += OnRequestReceived;
            connection.ServiceClosed += (_, _) => _connection = null;   // el agente sigue vivo
            if (await connection.OpenAsync() == AppServiceConnectionStatus.Success)
                _connection = connection;
        }
        finally
        {
            ConnectGate.Release();
        }
    }

    // ---------------------------------------------------------------------
    // Peticiones del widget
    // ---------------------------------------------------------------------

    private static async void OnRequestReceived(AppServiceConnection sender, AppServiceRequestReceivedEventArgs e)
    {
        var deferral = e.GetDeferral();
        try
        {
            var line = e.Request.Message.TryGetValue("req", out var r) ? r as string ?? "" : "";
            var response = await HandleAsync(line);
            await e.Request.SendResponseAsync(new ValueSet { ["resp"] = response });
        }
        finally
        {
            deferral.Complete();
        }
    }

    private static async Task<string> HandleAsync(string line)
    {
        try
        {
            var request = JsonNode.Parse(line) as JsonObject ?? throw new ArgumentException("Petición no válida");
            var cmd = request["cmd"]?.GetValue<string>() ?? "";
            var args = request["args"] as JsonObject ?? new JsonObject();

            // Pantalla, audio, ventanas, teclado y perfiles pertenecen a la sesión del usuario;
            // el servicio (sesión 0) no puede tocarlos.
            if (UserSessionCommands.Any(prefix => cmd.StartsWith(prefix, StringComparison.Ordinal)))
            {
                JsonNode data = cmd switch
                {
                    "foreground.get" => ForegroundTracker.Get(),
                    "foreground.kill" => ForegroundTracker.Kill(),
                    "keyboard.toggle" => TouchKeyboard.Toggle(),
                    "volume.get" => Volume.Get(),
                    "volume.set" => Volume.Set((int)args["value"]!.GetValue<double>()),
                    "display.get" => DisplayControl.Get(),
                    "display.set" => DisplayControl.Set(
                        args["width"]!.GetValue<int>(),
                        args["height"]!.GetValue<int>(),
                        args["hz"]!.GetValue<int>()),
                    "profiles.list" => ProfileManager.List(),
                    "profiles.save" => await ProfileManager.SaveCurrentAsync(),
                    "profiles.use" => await ProfileManager.UseAsync(args["target"]!.GetValue<string>()),
                    "profiles.delete" => ProfileManager.Delete(args["exe"]!.GetValue<string>()),
                    "profiles.auto" => ProfileManager.SetAuto(args["enabled"]!.GetValue<bool>()),
                    "profiles.autosave" => ProfileManager.SetAutoSave(args["enabled"]!.GetValue<bool>()),
                    "lights.get" => StickLights.Get(),
                    "lights.set" => await StickLights.SetAsync(args),
                    "amd.get" => AmdAdlx.Get(),
                    "amd.chill" => AmdAdlx.SetChillRange((int)args["min"]!.GetValue<double>(), (int)args["max"]!.GetValue<double>()),
                    "gyro.set" => await StickLights.SetGyroAsync(args["enabled"]!.GetValue<bool>()),
                    "controller.get" => await StickLights.GetControllerAsync(),
                    "controller.set" => await StickLights.SetControllerAsync(args),
                    "controller.advanced.get" => await StickLights.GetAdvancedAsync(),
                    "controller.advanced.set" => await StickLights.SetAdvancedAsync(args),
                    "controller.input" => LegionButtons.Input(),
                    "legionwidget.get" => LegionWidget.Get(),
                    "legionwidget.set" => await LegionWidget.SetAsync(args["visible"]!.GetValue<bool>()),
                    "vrr.get" => WindowedVrr.Get(),
                    "vrr.set" => WindowedVrr.Set(args["enabled"]!.GetValue<bool>()),
                    "controller.factory" => await StickLights.FactoryResetAsync(),
                    "controller.calibrate" => await StickLights.CalibrateAsync(args["target"]!.GetValue<string>(), args["action"]!.GetValue<string>()),
                    "amd.set" => AmdAdlx.Set(args["feature"]!.GetValue<string>(), args["enabled"]!.GetValue<bool>()),
                    "buttons.get" => LegionButtons.Get(),
                    "buttons.set" => LegionButtons.Set(args),
                    "buttons.run" => await RunButtonActionAsync(args["action"]!.GetValue<string>()),
                    "overlay.get" => GameOverlay.Get(),
                    "overlay.set" => GameOverlay.Set(args),
                    "ls.get" => LosslessScaling.Get(),
                    "ls.toggle" => await LosslessScaling.ToggleAsync(),
                    _ => throw new ArgumentException($"Comando desconocido: {cmd}"),
                };
                return new JsonObject { ["ok"] = true, ["data"] = data }.ToJsonString();
            }

            return await Service.SendAsync(line);
        }
        catch (Exception ex)
        {
            return new JsonObject { ["ok"] = false, ["error"] = ex.Message }.ToJsonString();
        }
    }

    private static async Task<JsonObject> RunButtonActionAsync(string action)
    {
        await LegionButtons.RunAsync(action);
        return new JsonObject { ["done"] = true };
    }

    /// <summary>Llama al servicio y devuelve «data», o lanza una excepción con su error.</summary>
    public static async Task<JsonObject> CallServiceAsync(string cmd, JsonObject? args = null)
    {
        var request = new JsonObject { ["cmd"] = cmd };
        if (args != null) request["args"] = args;
        var response = JsonNode.Parse(await Service.SendAsync(request.ToJsonString())) as JsonObject;
        if (response?["ok"]?.GetValue<bool>() != true)
            throw new InvalidOperationException($"{cmd}: {response?["error"]}");
        return response["data"] as JsonObject ?? new JsonObject();
    }
}
