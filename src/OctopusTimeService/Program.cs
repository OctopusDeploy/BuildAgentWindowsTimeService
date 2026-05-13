using Microsoft.Extensions.Hosting.WindowsServices;
using TimeService;

if (args.Length > 0)
{
    switch (args[0].ToLowerInvariant())
    {
        case "install":
            return ServiceInstaller.Install(args.AsSpan(1));
        case "uninstall":
            return ServiceInstaller.Uninstall(args.AsSpan(1));
    }
}

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddWindowsService(options =>
{
    options.ServiceName = ServiceDefaults.ServiceName;
});
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
return 0;
