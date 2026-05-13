using Octopus.Shellfish;
using TimeService.Logging;
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
        Log.StartupBeginning(logger);

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
            Log.HyperVTimeNotInstalled(logger, HyperVTimeSyncService);
        }

        if (w32TimeRunning)
        {
            await RunW32tmResyncAsync(cancellationToken);
        }
        else
        {
            Log.SkippingW32tm(logger, WindowsTimeService);
        }

        // Lock both back down. Best-effort: log failures, don't throw.
        await ops.StopAndDisableAsync(WindowsTimeService, cancellationToken);
        if (hyperVRunning || WindowsServiceOps.ServiceExists(HyperVTimeSyncService))
        {
            await ops.StopAndDisableAsync(HyperVTimeSyncService, cancellationToken);
        }

        await MeasureAndLogDriftAsync("post-resync", cancellationToken);

        Log.StartupComplete(logger);
    }

    private async Task MeasureAndLogDriftAsync(string phase, CancellationToken cancellationToken)
    {
        try
        {
            var result = await ntpClient.MeasureDriftAsync(cancellationToken);
            Log.ClockDriftPhase(logger, phase, ntpClient.Server, result.Drift, result.MarginOfError);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Log.ClockDriftPhaseFailed(logger, phase, ntpClient.Server, ex);
        }
    }

    private async Task RunW32tmResyncAsync(CancellationToken cancellationToken)
    {
        Log.RunningW32tm(logger);
        try
        {
            var result = await new ShellCommand("w32tm")
                .WithArguments(["/resync", "/force"])
                .WithStdOutTarget(line => Log.W32tmStdOut(logger, line))
                .WithStdErrTarget(line => Log.W32tmStdErr(logger, line))
                .ExecuteAsync(cancellationToken);

            if (result.ExitCode == 0)
            {
                Log.W32tmCompleted(logger);
            }
            else
            {
                Log.W32tmExitedNonZero(logger, result.ExitCode);
            }
        }
        catch (Exception ex)
        {
            Log.W32tmFailed(logger, ex);
        }
    }
}
