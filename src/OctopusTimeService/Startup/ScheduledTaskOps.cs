using Octopus.Shellfish;
using TimeService.Logging;

namespace TimeService.Startup;

/// <summary>
/// Thin async wrapper over schtasks.exe for disabling scheduled tasks.
/// Defensive: missing tasks and schtasks failures are logged and swallowed, never thrown.
/// </summary>
internal sealed class ScheduledTaskOps(ILogger logger)
{
    public async Task DisableAsync(string taskPath, CancellationToken cancellationToken)
    {
        Log.DisablingScheduledTask(logger, taskPath);
        try
        {
            var result = await new ShellCommand("schtasks.exe")
                .WithArguments(["/Change", "/TN", taskPath, "/DISABLE"])
                .WithStdOutTarget(line => Log.SchtasksStdOut(logger, line))
                .WithStdErrTarget(line => Log.SchtasksStdErr(logger, line))
                .ExecuteAsync(cancellationToken);

            if (result.ExitCode == 0)
                Log.ScheduledTaskDisabled(logger, taskPath);
            else
                Log.ScheduledTaskDisableFailed(logger, taskPath, result.ExitCode);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Log.ScheduledTaskDisableError(logger, taskPath, ex);
        }
    }
}
