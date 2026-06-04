using Octopus.Shellfish;
using TimeService.Logging;
using TimeService.Ntp;

namespace TimeService.Startup;

/// <summary>
/// Runs once at service start: measures drift, brings the OS time services up,
/// forces a Windows time resync, then locks the OS time services back down so
/// only this service is responsible for clock observation going forward.
/// In monitorOnly mode the resync/lockdown work is skipped and only a baseline
/// drift sample is taken.
/// </summary>
public sealed class StartupSequence(
    ILogger<StartupSequence> logger,
    NtpClient ntpClient,
    DriftCsvLog driftCsvLog,
    bool monitorOnly)
{
    private const string WindowsTimeService = "W32Time";
    private const string HyperVTimeSyncService = "vmictimesync";

    private static readonly TimeSpan StartupBudget = TimeSpan.FromMinutes(3);

    private static readonly string[] TimeSyncScheduledTasks =
    [
        @"\Microsoft\Windows\Time Synchronization\ForceSynchronizeTime",
        @"\Microsoft\Windows\Time Synchronization\SynchronizeTime",
    ];

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        using var startupCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        startupCts.CancelAfter(StartupBudget);

        try
        {
            Log.StartupBeginning(logger);

            Action<ILogger> logEvt = monitorOnly ? Log.MonitorOnlyMode : Log.StartupBeginning;
            logEvt(logger);

            await Worker.MeasureAndLogDriftAsync(
                logger, ntpClient, driftCsvLog, monitorOnly ? "monitor-only" : "pre-resync", cancellationToken);

            // Any operation which modifies the state of the system should be after this line.
            if (monitorOnly) return;

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

            // Disable the Windows Time Synchronization scheduled tasks so they don't relaunch
            // a sync behind our back. Best-effort: missing tasks and schtasks failures are logged
            // by ScheduledTaskOps and swallowed.
            var schTaskOps = new ScheduledTaskOps(logger);
            foreach (var taskPath in TimeSyncScheduledTasks)
            {
                await schTaskOps.DisableAsync(taskPath, cancellationToken);
            }

            // Lock both back down. Best-effort: log failures, don't throw.
            await ops.StopAndDisableAsync(WindowsTimeService, cancellationToken);
            if (hyperVRunning || WindowsServiceOps.ServiceExists(HyperVTimeSyncService))
            {
                await ops.StopAndDisableAsync(HyperVTimeSyncService, cancellationToken);
            }

            await Worker.MeasureAndLogDriftAsync(logger, ntpClient, driftCsvLog, "post-resync", cancellationToken);

            Log.StartupComplete(logger);
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
