using System.ServiceProcess;
using Microsoft.Win32;
using Octopus.Shellfish;

namespace TimeService.Startup;

/// <summary>
/// Thin async wrapper over <see cref="ServiceController"/> plus sc.exe for the few
/// operations the SCM API doesn't expose (changing start type).
/// All methods are tolerant — failures are surfaced via return value, not exceptions.
/// </summary>
internal sealed class WindowsServiceOps(ILogger logger)
{
    private static readonly TimeSpan StatusPollInterval = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan StatusTimeout = TimeSpan.FromSeconds(30);

    public static bool ServiceExists(string serviceName)
    {
        using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{serviceName}");
        return key is not null;
    }

    /// <summary>
    /// Ensures a service is in the Running state. Handles disabled→manual, start-pending wait,
    /// stopped→start. Returns true if the service is running at the end of the call.
    /// </summary>
    public async Task<bool> EnsureRunningAsync(string serviceName, CancellationToken cancellationToken)
    {
        try
        {
            using var sc = new ServiceController(serviceName);
            sc.Refresh();

            if (sc.StartType == ServiceStartMode.Disabled)
            {
                logger.LogInformation("Service '{Service}' is Disabled; switching to Manual", serviceName);
                if (!await SetStartTypeAsync(serviceName, "demand", cancellationToken))
                    return false;
                sc.Refresh();
            }

            switch (sc.Status)
            {
                case ServiceControllerStatus.Running:
                    logger.LogInformation("Service '{Service}' is already running", serviceName);
                    return true;

                case ServiceControllerStatus.StartPending:
                    logger.LogInformation("Service '{Service}' is starting; waiting for Running", serviceName);
                    return await WaitForStatusAsync(sc, ServiceControllerStatus.Running, cancellationToken);

                case ServiceControllerStatus.Stopped:
                    logger.LogInformation("Starting service '{Service}'", serviceName);
                    sc.Start();
                    return await WaitForStatusAsync(sc, ServiceControllerStatus.Running, cancellationToken);

                default:
                    logger.LogInformation(
                        "Service '{Service}' is in transient state {Status}; waiting then re-evaluating",
                        serviceName, sc.Status);
                    // Wait for things to settle, then try again on a refreshed view.
                    await Task.Delay(StatusPollInterval, cancellationToken);
                    return await EnsureRunningAsync(serviceName, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to ensure '{Service}' is running", serviceName);
            return false;
        }
    }

    /// <summary>
    /// Stops a service (if running) and sets its start type to Disabled.
    /// No-op if the service does not exist. Returns true on success.
    /// </summary>
    public async Task<bool> StopAndDisableAsync(string serviceName, CancellationToken cancellationToken)
    {
        if (!ServiceExists(serviceName))
            return true;

        try
        {
            using (var sc = new ServiceController(serviceName))
            {
                sc.Refresh();
                if (sc.Status != ServiceControllerStatus.Stopped && sc.Status != ServiceControllerStatus.StopPending)
                {
                    logger.LogInformation("Stopping service '{Service}'", serviceName);
                    if (sc.CanStop) sc.Stop();
                    if (!await WaitForStatusAsync(sc, ServiceControllerStatus.Stopped, cancellationToken))
                        return false;
                }
            }

            logger.LogInformation("Disabling service '{Service}'", serviceName);
            return await SetStartTypeAsync(serviceName, "disabled", cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to stop/disable '{Service}'", serviceName);
            return false;
        }
    }

    private async Task<bool> WaitForStatusAsync(
        ServiceController sc,
        ServiceControllerStatus target,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + StatusTimeout;
        while (DateTime.UtcNow < deadline)
        {
            sc.Refresh();
            if (sc.Status == target) return true;
            await Task.Delay(StatusPollInterval, cancellationToken);
        }
        logger.LogError("Service '{Service}' did not reach {Target} within {Timeout}",
            sc.ServiceName, target, StatusTimeout);
        return false;
    }

    private async Task<bool> SetStartTypeAsync(string serviceName, string scStartMode, CancellationToken cancellationToken)
    {
        var result = await new ShellCommand("sc.exe")
            .WithArguments(["config", serviceName, "start=", scStartMode])
            .WithStdOutTarget(line => logger.LogDebug("[sc] {Line}", line))
            .WithStdErrTarget(line => logger.LogWarning("[sc] {Line}", line))
            .ExecuteAsync(cancellationToken);

        if (result.ExitCode != 0)
        {
            logger.LogError("sc config '{Service}' start= {Mode} failed (exit {ExitCode})",
                serviceName, scStartMode, result.ExitCode);
            return false;
        }
        return true;
    }
}
