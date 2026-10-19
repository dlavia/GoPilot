using GoSPanel.Service;

// Con argumentos: modo consola para diagnóstico en la Legion (ejecutar como administrador).
//   GoSPanel.Service.exe probe
//   GoSPanel.Service.exe status
//   GoSPanel.Service.exe call LENOVO_GAMEZONE_DATA GetSmartFanMode
//   GoSPanel.Service.exe call LENOVO_OTHER_METHOD GetFeatureValue IDs=0x0101FF00
//   GoSPanel.Service.exe cmd "{\"cmd\":\"tdp.get\"}"
// Ayudante elevado de Lossless Scaling (lo lanza el propio servicio como tarea en la sesión del usuario).
if (args.Length == 2 && args[0] == "ls-hotkey" && int.TryParse(args[1], out var lsPid))
    return LosslessHelper.RunHotkey(lsPid);

if (args.Length > 0)
    return Cli.Run(args);

// Cualquier error no capturado queda en %ProgramData%\GoSPanel\service-crash.log
AppDomain.CurrentDomain.UnhandledException += (_, e) =>
{
    try
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "GoSPanel");
        Directory.CreateDirectory(dir);
        File.AppendAllText(Path.Combine(dir, "service-crash.log"), $"{DateTime.Now:s} {e.ExceptionObject}{Environment.NewLine}{Environment.NewLine}");
    }
    catch (Exception) { }
};

var builder = Host.CreateApplicationBuilder(args);
// Un fallo en una tarea de fondo (p. ej. WMI o ETW aún no listos al arrancar Windows) no debe parar el servicio.
builder.Services.Configure<HostOptions>(o => o.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.Ignore);
builder.Services.AddWindowsService(o => o.ServiceName = "GoSPanel");
builder.Services.AddSingleton<CommandRouter>();
builder.Services.AddHostedService<PipeServer>();
builder.Services.AddHostedService<ResumeWatcher>();
builder.Services.AddHostedService<ThermalModeWatcher>();
builder.Services.AddHostedService<FpsMonitor>();
builder.Build().Run();
return 0;
