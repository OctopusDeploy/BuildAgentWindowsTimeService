using TimeService.Ntp;
using TimeService.Startup;

namespace TimeService;

public class Worker(
    ILogger<Worker> logger,
    NtpClient ntpClient,
    StartupSequence startupSequence) : BackgroundService
{
    private static readonly TimeSpan MeasurementInterval = TimeSpan.FromSeconds(30);

    public override Task StartAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("{ServiceName} starting (NTP server: {Server}, interval: {Interval})",
            ServiceDefaults.ServiceName, ntpClient.Server, MeasurementInterval);
        return base.StartAsync(cancellationToken);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("{ServiceName} stopping", ServiceDefaults.ServiceName);
        await base.StopAsync(cancellationToken);
        logger.LogInformation("{ServiceName} stopped", ServiceDefaults.ServiceName);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await startupSequence.RunAsync(stoppingToken);

        // Startup just logged a fresh drift measurement; wait one full interval before the next.
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
