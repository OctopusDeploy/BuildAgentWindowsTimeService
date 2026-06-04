using Microsoft.Win32;

namespace TimeService;

/// <summary>
/// Read/write of configuration values stored under the service's registry key
/// (HKLM\SYSTEM\CurrentControlSet\Services\&lt;serviceName&gt;).
/// </summary>
internal static class RegistrySettings
{
    private const string ServicesRegistryRoot = @"SYSTEM\CurrentControlSet\Services";

    public const string DependentsValueName = "Dependents";
    public const string NtpCheckIntervalSecondsValueName = "NtpCheckIntervalSeconds";
    public const string MonitorOnlyValueName = "MonitorOnly";
    public const string LogFolderValueName = "LogFolder";

    public const int DefaultNtpCheckIntervalSeconds = 30;
    public const string DefaultLogFolder = @"C:\Octopus\TimeService";

    private static string KeyPath(string serviceName) => $@"{ServicesRegistryRoot}\{serviceName}";

    public static void WriteDependents(string serviceName, IReadOnlyList<string> dependents)
    {
        using var key = Registry.LocalMachine.OpenSubKey(KeyPath(serviceName), writable: true)
            ?? throw new InvalidOperationException(
                $@"Registry key HKLM\{KeyPath(serviceName)} does not exist; service was not created.");
        key.SetValue(DependentsValueName, string.Join(",", dependents), RegistryValueKind.String);
    }

    public static IReadOnlyList<string> ReadDependents(string serviceName)
    {
        using var key = Registry.LocalMachine.OpenSubKey(KeyPath(serviceName));
        if (key is null) return Array.Empty<string>();
        if (key.GetValue(DependentsValueName) is not string csv || string.IsNullOrWhiteSpace(csv))
            return Array.Empty<string>();
        return csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    public static void WriteNtpCheckIntervalSeconds(string serviceName, int seconds)
    {
        using var key = Registry.LocalMachine.OpenSubKey(KeyPath(serviceName), writable: true)
            ?? throw new InvalidOperationException(
                $@"Registry key HKLM\{KeyPath(serviceName)} does not exist; service was not created.");
        key.SetValue(NtpCheckIntervalSecondsValueName, seconds, RegistryValueKind.DWord);
    }

    public static int ReadNtpCheckIntervalSeconds(string serviceName)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(KeyPath(serviceName));
            if (key?.GetValue(NtpCheckIntervalSecondsValueName) is int seconds && seconds > 0)
                return seconds;
        }
        catch
        {
            // Fall through to default.
        }
        return DefaultNtpCheckIntervalSeconds;
    }

    public static void WriteMonitorOnly(string serviceName, bool enabled)
    {
        using var key = Registry.LocalMachine.OpenSubKey(KeyPath(serviceName), writable: true)
            ?? throw new InvalidOperationException(
                $@"Registry key HKLM\{KeyPath(serviceName)} does not exist; service was not created.");
        key.SetValue(MonitorOnlyValueName, enabled ? 1 : 0, RegistryValueKind.DWord);
    }

    public static bool ReadMonitorOnly(string serviceName)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(KeyPath(serviceName));
            if (key?.GetValue(MonitorOnlyValueName) is int v)
                return v != 0;
        }
        catch
        {
            // Fall through to default.
        }
        return false;
    }

    public static void WriteLogFolder(string serviceName, string folder)
    {
        using var key = Registry.LocalMachine.OpenSubKey(KeyPath(serviceName), writable: true)
            ?? throw new InvalidOperationException(
                $@"Registry key HKLM\{KeyPath(serviceName)} does not exist; service was not created.");
        key.SetValue(LogFolderValueName, folder, RegistryValueKind.String);
    }

    public static string ReadLogFolder(string serviceName)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(KeyPath(serviceName));
            if (key?.GetValue(LogFolderValueName) is string folder && !string.IsNullOrWhiteSpace(folder))
                return folder;
        }
        catch
        {
            // Fall through to default.
        }
        return DefaultLogFolder;
    }

    /// <summary>
    /// Removes the service's registry key after sc.exe delete, so any custom values
    /// (Dependents, NtpCheckIntervalSeconds, LogFolder) we wrote are not left behind. No-op if missing.
    /// </summary>
    public static void DeleteServiceKey(string serviceName)
    {
        try
        {
            Registry.LocalMachine.DeleteSubKeyTree(KeyPath(serviceName), throwOnMissingSubKey: false);
        }
        catch (ArgumentException)
        {
            // DeleteSubKeyTree throws ArgumentException on some frameworks when the subkey
            // doesn't exist, even with throwOnMissingSubKey: false. Treat as no-op.
        }
    }
}
