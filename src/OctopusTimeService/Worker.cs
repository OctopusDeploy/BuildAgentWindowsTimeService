using TimeService.Logging;
using TimeService.Ntp;
using TimeService.Startup;

namespace TimeService;

public class Worker(
    ILogger<Worker> logger,
    NtpClient ntpClient,
    StartupSequence startupSequence,
    DriftCsvLog driftCsvLog) : BackgroundService
{
    private readonly TimeSpan measurementInterval = TimeSpan.FromSeconds(
        RegistrySettings.ReadNtpCheckIntervalSeconds(ServiceDefaults.ServiceName));

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        Log.ServiceStarting(logger, ServiceDefaults.ServiceName, ntpClient.Server, measurementInterval);

        // Run startup inline so SCM (or the host in console mode) doesn't see us as Running
        // until the startup sequence has finished. base.StartAsync then schedules ExecuteAsync.
        await startupSequence.RunAsync(cancellationToken);

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
            await MeasureAndLogDriftAsync(logger, ntpClient, driftCsvLog, "steady-state", stoppingToken);
        }
    }

    /// <summary>
    /// Takes a single NTP drift measurement and records it to both the Windows Event Log and the
    /// on-disk CSV log. Shared by the steady-state loop and <see cref="StartupSequence"/> so every
    /// measurement, whatever its origin, lands in both sinks identically. <paramref name="phase"/>
    /// labels the measurement in the event log (e.g. "steady-state", "pre-resync", "post-resync").
    /// Measurement failures are logged and swallowed; host cancellation is propagated.
    /// </summary>
    public static async Task MeasureAndLogDriftAsync(
        ILogger logger,
        NtpClient ntpClient,
        DriftCsvLog driftCsvLog,
        string phase,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await ntpClient.MeasureDriftAsync(cancellationToken);
            var localTime = DateTime.UtcNow;
            var ntpTime = localTime + result.Drift;
            Log.ClockDrift(logger, phase, ntpClient.Server, result.Drift, result.MarginOfError);
            driftCsvLog.Append(localTime, ntpTime, result.Drift, result.MarginOfError);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Log.ClockDriftFailed(logger, phase, ntpClient.Server, ex);
        }
    }
}
