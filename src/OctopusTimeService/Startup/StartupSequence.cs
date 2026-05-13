using Octopus.Shellfish;
using TimeService.Ntp;

namespace TimeService.Startup;

/// <summary>
/// Runs once at service start: measures drift, brings the OS time services up,
/// forces a Windows time resync, then locks the OS time services back down so
/// only this service is responsible for clock observation going forward.
/// </summary>
public sealed class StartupSequence(ILogger<StartupSequence> logger, NtpClient ntpClient)
{
    private const string WindowsTimeService = "W32Time";
    private const string HyperVTimeSyncService = "vmictimesync";

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Startup sequence beginning");

        await MeasureAndLogDriftAsync("pre-resync", cancellationToken);

        var ops = new WindowsServiceOps(logger);

        // Windows Time is required for w32tm /resync to work.
        var w32TimeRunning = await ops.EnsureRunningAsync(WindowsTimeService, cancellationToken);

        // Hyper-V time sync is only present in VMs; missing is fine, missing-and-failing is not.
        var hyperVRunning = false;
        if (WindowsServiceOps.ServiceExists(HyperVTimeSyncService))
        {
            hyperVRunning = await ops.EnsureRunningAsync(HyperVTimeSyncService, cancellationToken);
        }
        else
        {
            logger.LogWarning("Hyper-V time sync service '{Service}' is not installed; continuing without it",
                HyperVTimeSyncService);
        }

        if (w32TimeRunning)
        {
            await RunW32tmResyncAsync(cancellationToken);
        }
        else
        {
            logger.LogWarning("Skipping 'w32tm /resync /force' because '{Service}' could not be started",
                WindowsTimeService);
        }

        // Lock both back down. Best-effort: log failures, don't throw.
        await ops.StopAndDisableAsync(WindowsTimeService, cancellationToken);
        if (hyperVRunning || WindowsServiceOps.ServiceExists(HyperVTimeSyncService))
        {
            await ops.StopAndDisableAsync(HyperVTimeSyncService, cancellationToken);
        }

        await MeasureAndLogDriftAsync("post-resync", cancellationToken);

        logger.LogInformation("Startup sequence complete");
    }

    private async Task MeasureAndLogDriftAsync(string phase, CancellationToken cancellationToken)
    {
        try
        {
            var result = await ntpClient.MeasureDriftAsync(cancellationToken);
            logger.LogInformation(
                "Clock drift ({Phase}) against {Server}: {Drift} (±{Margin})",
                phase, ntpClient.Server, result.Drift, result.MarginOfError);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Failed to measure clock drift ({Phase}) against {Server}", phase, ntpClient.Server);
        }
    }

    private async Task RunW32tmResyncAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Running 'w32tm /resync /force'");
        try
        {
            var result = await new ShellCommand("w32tm")
                .WithArguments(["/resync", "/force"])
                .WithStdOutTarget(line => logger.LogInformation("[w32tm] {Line}", line))
                .WithStdErrTarget(line => logger.LogWarning("[w32tm] {Line}", line))
                .ExecuteAsync(cancellationToken);

            if (result.ExitCode == 0)
            {
                logger.LogInformation("w32tm resync completed");
            }
            else
            {
                logger.LogWarning("w32tm exited with code {ExitCode}", result.ExitCode);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to run w32tm /resync /force");
        }
    }
}
