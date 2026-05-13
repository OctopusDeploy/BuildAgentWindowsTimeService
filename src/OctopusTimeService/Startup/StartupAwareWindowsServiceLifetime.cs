using System.Runtime.Versioning;
using Microsoft.Extensions.Hosting.WindowsServices;
using Microsoft.Extensions.Options;

namespace TimeService.Startup;

/// <summary>
/// Variant of <see cref="WindowsServiceLifetime"/> that doesn't report SERVICE_RUNNING
/// back to the SCM until all hosted services have completed their <c>StartAsync</c>.
/// The base implementation signals SCM as soon as <c>OnStart</c> returns, which races
/// with the host's hosted-service startup loop.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class StartupAwareWindowsServiceLifetime : WindowsServiceLifetime
{
    // SCM's default start timeout is ~30s. Our startup sequence can run multiple service
    // status waits (each up to 30s) plus a w32tm resync, so we ask SCM for more headroom.
    private static readonly TimeSpan StartupHint = TimeSpan.FromMinutes(5);

    private readonly IHostApplicationLifetime _applicationLifetime;

    public StartupAwareWindowsServiceLifetime(
        IHostEnvironment environment,
        IHostApplicationLifetime applicationLifetime,
        ILoggerFactory loggerFactory,
        IOptions<HostOptions> hostOptions,
        IOptions<WindowsServiceLifetimeOptions> serviceOptions)
        : base(environment, applicationLifetime, loggerFactory, hostOptions, serviceOptions)
    {
        _applicationLifetime = applicationLifetime;
    }

    protected override void OnStart(string[] args)
    {
        // Lets WaitForStartAsync return so Host.StartAsync can run hosted services.
        base.OnStart(args);

        // Tell SCM not to time us out while hosted-service startup runs.
        RequestAdditionalTime((int)StartupHint.TotalMilliseconds);

        // Block until ApplicationStarted fires (all IHostedService.StartAsync calls have returned).
        // Returning from OnStart is what tells SCM the service has reached SERVICE_RUNNING.
        _applicationLifetime.ApplicationStarted.WaitHandle.WaitOne(StartupHint);
    }
}
