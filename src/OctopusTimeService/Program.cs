using Microsoft.Extensions.Hosting.WindowsServices;
using TimeService;

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
    builder.Services.AddHostedService<Worker>();
    builder.Build().Run();
    return 0;
}

static int RunAsConsole(string[] forwardedArgs)
{
    var builder = Host.CreateApplicationBuilder(forwardedArgs);
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
    writer.WriteLine("                                          --serviceName <name>  (default: OctopusTimeService)");
    writer.WriteLine("                                          --executable <path>   (default: current exe)");
    writer.WriteLine("  OctopusTimeService uninstall [flags]  Unregister the Windows service.");
    writer.WriteLine("                                          --serviceName <name>  (default: OctopusTimeService)");
}
