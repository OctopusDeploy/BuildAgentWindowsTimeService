using TimeService.Logging;
using TimeService.Ntp;
using TimeService.Startup;

namespace TimeService;

public class Worker(
    ILogger<Worker> logger,
    NtpClient ntpClient,
    StartupSequence startupSequence) : BackgroundService
{
    private static readonly TimeSpan StartupBudget = TimeSpan.FromMinutes(3);

    private readonly TimeSpan measurementInterval = TimeSpan.FromSeconds(
        RegistrySettings.ReadNtpCheckIntervalSeconds(ServiceDefaults.ServiceName));

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        Log.ServiceStarting(logger, ServiceDefaults.ServiceName, ntpClient.Server, measurementInterval);

        // Run startup inline so SCM (or the host in console mode) doesn't see us as Running
        // until the startup sequence has finished. base.StartAsync then schedules ExecuteAsync.
        //
        // A failed or hung startup sequence must not prevent the service from coming up,
        // so we cap it at StartupBudget and swallow anything that isn't a real shutdown.
        using var startupCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        startupCts.CancelAfter(StartupBudget);
        try
        {
            await startupSequence.RunAsync(startupCts.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Host is genuinely shutting down — propagate so the host can abort start.
            throw;
        }
        catch (Exception ex)
        {
            Log.StartupSequenceFailed(logger, StartupBudget, ex);
        }

        await base.StartAsync(cancellationToken);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        Log.ServiceStopping(logger, ServiceDefaults.ServiceName);
        await base.StopAsync(cancellationToken);
        Log.ServiceStopped(logger, ServiceDefaults.ServiceName);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Startup ran during StartAsync and logged a fresh drift measurement; wait one full
        // interval before the next so we don't double-measure right after boot.
        using var timer = new PeriodicTimer(measurementInterval);
        while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
        {
            await MeasureAndLogAsync(stoppingToken);
        }
    }

    private async Task MeasureAndLogAsync(CancellationToken cancellationToken)
    {
        try
        {
            var result = await ntpClient.MeasureDriftAsync(cancellationToken);
            Log.ClockDrift(logger, ntpClient.Server, result.Drift, result.MarginOfError);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Log.ClockDriftFailed(logger, ntpClient.Server, ex);
        }
    }
}
