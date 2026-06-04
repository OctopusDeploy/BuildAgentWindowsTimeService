using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting.WindowsServices;
using Microsoft.Extensions.Logging.EventLog;
using TimeService;
using TimeService.Ntp;
using TimeService.Startup;

if (args.Length == 0)
{
    return RunAsService(args);
}

switch (args[0].ToLowerInvariant())
{
    case "install":
        return ServiceInstaller.Install(args.AsSpan(1));
    case "uninstall":
        return ServiceInstaller.Uninstall(args.AsSpan(1));
    case "run":
        return RunAsConsole(args[1..]);
    case "help":
    case "--help":
    case "-h":
    case "/?":
        PrintUsage(Console.Out);
        return 0;
    default:
        Console.Error.WriteLine($"Unknown command: {args[0]}");
        PrintUsage(Console.Error);
        return 2;
}

static int RunAsService(string[] forwardedArgs)
{
    var builder = Host.CreateApplicationBuilder(forwardedArgs);
    builder.Services.AddWindowsService(options =>
    {
        options.ServiceName = ServiceDefaults.ServiceName;
    });
    // Defer SCM's SERVICE_RUNNING signal until hosted-service StartAsync has finished,
    // so the startup sequence completes before the service is reported as running.
    if (WindowsServiceHelpers.IsWindowsService())
    {
        builder.Services.Replace(
            ServiceDescriptor.Singleton<IHostLifetime, StartupAwareWindowsServiceLifetime>());
    }
    // AddWindowsService defaults the EventLog provider to Warning+; lower it so
    // Information-level drift measurements reach the Windows Event Log.
    builder.Logging.AddFilter<EventLogLoggerProvider>(null, LogLevel.Information);
    builder.Services.AddSingleton(_ => new NtpClient());
    builder.Services.AddSingleton(sp => new StartupSequence(
        sp.GetRequiredService<ILogger<StartupSequence>>(),
        sp.GetRequiredService<NtpClient>(),
        RegistrySettings.ReadMonitorOnly(ServiceDefaults.ServiceName)));
    builder.Services.AddHostedService<Worker>();
    builder.Build().Run();
    return 0;
}

static int RunAsConsole(string[] forwardedArgs)
{
    var builder = Host.CreateApplicationBuilder(forwardedArgs);
    builder.Services.AddSingleton(_ => new NtpClient());
    builder.Services.AddSingleton(sp => new StartupSequence(
        sp.GetRequiredService<ILogger<StartupSequence>>(),
        sp.GetRequiredService<NtpClient>(),
        RegistrySettings.ReadMonitorOnly(ServiceDefaults.ServiceName)));
    builder.Services.AddHostedService<Worker>();
    builder.Build().Run();
    return 0;
}

static void PrintUsage(TextWriter writer)
{
    writer.WriteLine("OctopusTimeService");
    writer.WriteLine();
    writer.WriteLine("Usage:");
    writer.WriteLine("  OctopusTimeService                    Run as Windows service (when launched by SCM).");
    writer.WriteLine("  OctopusTimeService run                Run the worker in console mode.");
    writer.WriteLine("  OctopusTimeService install [flags]    Register as a Windows service.");
    writer.WriteLine("                                          --serviceName <name>      (default: OctopusTimeService)");
    writer.WriteLine("                                          --executable <path>       (default: current exe)");
    writer.WriteLine("                                          --dependent <name>        target service to make depend on us");
    writer.WriteLine("                                                                    (may be specified multiple times)");
    writer.WriteLine("                                          --ntpCheckInterval <sec>  NTP drift-check interval (default: 30)");
    writer.WriteLine("                                          --monitorOnly             skip the startup resync/lockdown");
    writer.WriteLine("                                                                    sequence; just measure and log");
    writer.WriteLine("  OctopusTimeService uninstall [flags]  Unregister the Windows service.");
    writer.WriteLine("                                          --serviceName <name>      (default: OctopusTimeService)");
    writer.WriteLine("                                          --dependent <name>        target service to strip us from");
    writer.WriteLine("                                                                    (may be specified multiple times;");
    writer.WriteLine("                                                                    if omitted, recovered from registry)");
}
