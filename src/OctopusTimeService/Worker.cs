using TimeService.Ntp;
using TimeService.Startup;

namespace TimeService;

public class Worker(
    ILogger<Worker> logger,
    NtpClient ntpClient,
    StartupSequence startupSequence) : BackgroundService
{
    private static readonly TimeSpan MeasurementInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan StartupBudget = TimeSpan.FromMinutes(3);

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("{ServiceName} starting (NTP server: {Server}, interval: {Interval})",
            ServiceDefaults.ServiceName, ntpClient.Server, MeasurementInterval);

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
            logger.LogError(ex,
                "Startup sequence did not complete cleanly within {Budget}; continuing service start regardless.",
                StartupBudget);
        }

        await base.StartAsync(cancellationToken);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("{ServiceName} stopping", ServiceDefaults.ServiceName);
        await base.StopAsync(cancellationToken);
        logger.LogInformation("{ServiceName} stopped", ServiceDefaults.ServiceName);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Startup ran during StartAsync and logged a fresh drift measurement; wait one full
        // interval before the next so we don't double-measure right after boot.
        using var timer = new PeriodicTimer(MeasurementInterval);
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
            logger.LogInformation(
                "Clock drift against {Server}: {Drift} (±{Margin})",
                ntpClient.Server, result.Drift, result.MarginOfError);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to measure clock drift against {Server}", ntpClient.Server);
        }
    }
}
