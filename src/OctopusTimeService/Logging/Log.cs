using System.ServiceProcess;

namespace TimeService.Logging;

/// <summary>
/// Source-generated logging for the service. Each entry has a stable EventId so individual
/// events are identifiable in the Windows Event Log.
/// Ranges: 1000-1099 Worker, 2000-2099 StartupSequence, 3000-3099 WindowsServiceOps.
/// </summary>
internal static partial class Log
{
    // -------- Worker (1000-1099) --------

    [LoggerMessage(EventId = 1001, Level = LogLevel.Information,
        Message = "{ServiceName} starting (NTP server: {Server}, interval: {Interval})")]
    public static partial void ServiceStarting(ILogger logger, string serviceName, string server, TimeSpan interval);

    [LoggerMessage(EventId = 1002, Level = LogLevel.Error,
        Message = "Startup sequence did not complete cleanly within {Budget}; continuing service start regardless.")]
    public static partial void StartupSequenceFailed(ILogger logger, TimeSpan budget, Exception ex);

    [LoggerMessage(EventId = 1003, Level = LogLevel.Information,
        Message = "{ServiceName} stopping")]
    public static partial void ServiceStopping(ILogger logger, string serviceName);

    [LoggerMessage(EventId = 1004, Level = LogLevel.Information,
        Message = "{ServiceName} stopped")]
    public static partial void ServiceStopped(ILogger logger, string serviceName);

    [LoggerMessage(EventId = 1005, Level = LogLevel.Information,
        Message = "Clock drift against {Server}: {Drift} (±{Margin})")]
    public static partial void ClockDrift(ILogger logger, string server, TimeSpan drift, TimeSpan margin);

    [LoggerMessage(EventId = 1006, Level = LogLevel.Warning,
        Message = "Failed to measure clock drift against {Server}")]
    public static partial void ClockDriftFailed(ILogger logger, string server, Exception ex);

    // -------- StartupSequence (2000-2099) --------

    [LoggerMessage(EventId = 2001, Level = LogLevel.Information,
        Message = "Startup sequence beginning")]
    public static partial void StartupBeginning(ILogger logger);

    [LoggerMessage(EventId = 2002, Level = LogLevel.Warning,
        Message = "Hyper-V time sync service '{Service}' is not installed; continuing without it")]
    public static partial void HyperVTimeNotInstalled(ILogger logger, string service);

    [LoggerMessage(EventId = 2003, Level = LogLevel.Warning,
        Message = "Skipping 'w32tm /resync /force' because '{Service}' could not be started")]
    public static partial void SkippingW32tm(ILogger logger, string service);

    [LoggerMessage(EventId = 2004, Level = LogLevel.Information,
        Message = "Startup sequence complete")]
    public static partial void StartupComplete(ILogger logger);

    [LoggerMessage(EventId = 2005, Level = LogLevel.Information,
        Message = "Clock drift ({Phase}) against {Server}: {Drift} (±{Margin})")]
    public static partial void ClockDriftPhase(ILogger logger, string phase, string server, TimeSpan drift, TimeSpan margin);

    [LoggerMessage(EventId = 2006, Level = LogLevel.Warning,
        Message = "Failed to measure clock drift ({Phase}) against {Server}")]
    public static partial void ClockDriftPhaseFailed(ILogger logger, string phase, string server, Exception ex);

    [LoggerMessage(EventId = 2007, Level = LogLevel.Information,
        Message = "Running 'w32tm /resync /force'")]
    public static partial void RunningW32tm(ILogger logger);

    [LoggerMessage(EventId = 2008, Level = LogLevel.Information,
        Message = "[w32tm] {Line}")]
    public static partial void W32tmStdOut(ILogger logger, string line);

    [LoggerMessage(EventId = 2009, Level = LogLevel.Warning,
        Message = "[w32tm] {Line}")]
    public static partial void W32tmStdErr(ILogger logger, string line);

    [LoggerMessage(EventId = 2010, Level = LogLevel.Information,
        Message = "w32tm resync completed")]
    public static partial void W32tmCompleted(ILogger logger);

    [LoggerMessage(EventId = 2011, Level = LogLevel.Warning,
        Message = "w32tm exited with code {ExitCode}")]
    public static partial void W32tmExitedNonZero(ILogger logger, int exitCode);

    [LoggerMessage(EventId = 2012, Level = LogLevel.Error,
        Message = "Failed to run w32tm /resync /force")]
    public static partial void W32tmFailed(ILogger logger, Exception ex);

    // -------- WindowsServiceOps (3000-3099) --------

    [LoggerMessage(EventId = 3001, Level = LogLevel.Information,
        Message = "Service '{Service}' is Disabled; switching to Manual")]
    public static partial void EnableDisabledService(ILogger logger, string service);

    [LoggerMessage(EventId = 3002, Level = LogLevel.Information,
        Message = "Service '{Service}' is already running")]
    public static partial void ServiceAlreadyRunning(ILogger logger, string service);

    [LoggerMessage(EventId = 3003, Level = LogLevel.Information,
        Message = "Service '{Service}' is starting; waiting for Running")]
    public static partial void ServiceStartPending(ILogger logger, string service);

    [LoggerMessage(EventId = 3004, Level = LogLevel.Information,
        Message = "Starting service '{Service}'")]
    public static partial void StartingService(ILogger logger, string service);

    [LoggerMessage(EventId = 3005, Level = LogLevel.Information,
        Message = "Service '{Service}' is in transient state {Status}; waiting then re-evaluating")]
    public static partial void ServiceTransient(ILogger logger, string service, ServiceControllerStatus status);

    [LoggerMessage(EventId = 3006, Level = LogLevel.Error,
        Message = "Failed to ensure '{Service}' is running")]
    public static partial void EnsureRunningFailed(ILogger logger, string service, Exception ex);

    [LoggerMessage(EventId = 3007, Level = LogLevel.Information,
        Message = "Stopping service '{Service}'")]
    public static partial void StoppingService(ILogger logger, string service);

    [LoggerMessage(EventId = 3008, Level = LogLevel.Information,
        Message = "Disabling service '{Service}'")]
    public static partial void DisablingService(ILogger logger, string service);

    [LoggerMessage(EventId = 3009, Level = LogLevel.Error,
        Message = "Failed to stop/disable '{Service}'")]
    public static partial void StopAndDisableFailed(ILogger logger, string service, Exception ex);

    [LoggerMessage(EventId = 3010, Level = LogLevel.Error,
        Message = "Service '{Service}' did not reach {Target} within {Timeout}")]
    public static partial void ServiceStatusTimeout(ILogger logger, string service, ServiceControllerStatus target, TimeSpan timeout);

    [LoggerMessage(EventId = 3011, Level = LogLevel.Debug,
        Message = "[sc] {Line}")]
    public static partial void ScStdOut(ILogger logger, string line);

    [LoggerMessage(EventId = 3012, Level = LogLevel.Warning,
        Message = "[sc] {Line}")]
    public static partial void ScStdErr(ILogger logger, string line);

    [LoggerMessage(EventId = 3013, Level = LogLevel.Error,
        Message = "sc config '{Service}' start= {Mode} failed (exit {ExitCode})")]
    public static partial void ScConfigFailed(ILogger logger, string service, string mode, int exitCode);
}
