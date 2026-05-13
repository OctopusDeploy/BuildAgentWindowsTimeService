using System.Diagnostics;
using Microsoft.Win32;

namespace TimeService;

internal static class ServiceInstaller
{
    private const string ServicesRegistryRoot = @"SYSTEM\CurrentControlSet\Services";

    public static int Install(ReadOnlySpan<string> args)
    {
        var serviceName = ServiceDefaults.ServiceName;
        string? executablePath = null;
        var dependents = new List<string>();

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--serviceName":
                    if (!TryGetValue(args, ref i, out var nameValue)) return ArgError("--serviceName requires a value");
                    serviceName = nameValue;
                    break;
                case "--executable":
                case "--exe":
                    if (!TryGetValue(args, ref i, out var exeValue)) return ArgError("--executable requires a value");
                    executablePath = exeValue;
                    break;
                case "--dependent":
                    if (!TryGetValue(args, ref i, out var depValue)) return ArgError("--dependent requires a value");
                    dependents.Add(depValue);
                    break;
                default:
                    return ArgError($"Unknown argument: {args[i]}");
            }
        }

        executablePath ??= Environment.ProcessPath;
        if (string.IsNullOrEmpty(executablePath))
        {
            Console.Error.WriteLine("Could not determine executable path. Pass --executable explicitly.");
            return 1;
        }

        executablePath = Path.GetFullPath(executablePath);
        if (!File.Exists(executablePath))
        {
            Console.Error.WriteLine($"Executable not found: {executablePath}");
            return 1;
        }

        // Pre-validate dependents before we make any changes.
        foreach (var dep in dependents)
        {
            if (string.Equals(dep, serviceName, StringComparison.OrdinalIgnoreCase))
            {
                Console.Error.WriteLine($"--dependent '{dep}' cannot reference the service being installed.");
                return 2;
            }
            if (!ServiceExists(dep))
            {
                Console.Error.WriteLine($"Dependent service '{dep}' does not exist.");
                return 1;
            }
        }

        Console.WriteLine($"Installing service '{serviceName}' from '{executablePath}'");

        // The ImagePath stored in the registry must be quoted so SCM doesn't split on spaces.
        // We pass the value with embedded quote chars; ProcessStartInfo.ArgumentList escapes them.
        var quotedExePath = $"\"{executablePath}\"";
        var scArgs = new[]
        {
            "create", serviceName,
            "binPath=", quotedExePath,
            "start=", "auto",
            "DisplayName=", serviceName,
        };

        var exitCode = RunSc(scArgs);
        if (exitCode != 0)
        {
            Console.Error.WriteLine($"sc.exe create failed with exit code {exitCode}");
            return exitCode;
        }

        Console.WriteLine($"Service '{serviceName}' installed.");

        var depFailures = 0;
        foreach (var dep in dependents)
        {
            if (!AddDependency(target: dep, prerequisite: serviceName))
                depFailures++;
        }

        if (depFailures > 0)
        {
            Console.Error.WriteLine($"Service installed but {depFailures} dependency edit(s) failed.");
            return 1;
        }

        return 0;
    }

    public static int Uninstall(ReadOnlySpan<string> args)
    {
        var serviceName = ServiceDefaults.ServiceName;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--serviceName":
                    if (!TryGetValue(args, ref i, out var nameValue)) return ArgError("--serviceName requires a value");
                    serviceName = nameValue;
                    break;
                default:
                    return ArgError($"Unknown argument: {args[i]}");
            }
        }

        Console.WriteLine($"Uninstalling service '{serviceName}'");

        // Best-effort stop first; ignore failure (service may already be stopped).
        RunSc(["stop", serviceName]);

        var exitCode = RunSc(["delete", serviceName]);
        if (exitCode != 0)
        {
            Console.Error.WriteLine($"sc.exe delete failed with exit code {exitCode}");
            return exitCode;
        }

        Console.WriteLine($"Service '{serviceName}' uninstalled.");
        return 0;
    }

    private static bool ServiceExists(string serviceName)
    {
        using var key = Registry.LocalMachine.OpenSubKey($@"{ServicesRegistryRoot}\{serviceName}");
        return key is not null;
    }

    /// <summary>
    /// Adds <paramref name="prerequisite"/> to <paramref name="target"/>'s DependOnService list.
    /// Reads the existing list from the registry, merges, then writes via sc.exe so SCM picks
    /// up the change immediately (a raw registry write wouldn't notify SCM).
    /// </summary>
    private static bool AddDependency(string target, string prerequisite)
    {
        string[] existing;
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey($@"{ServicesRegistryRoot}\{target}");
            if (key is null)
            {
                Console.Error.WriteLine($"  Could not read registry for service '{target}'.");
                return false;
            }
            existing = (string[]?)key.GetValue("DependOnService") ?? Array.Empty<string>();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"  Error reading '{target}' dependencies: {ex.Message}");
            return false;
        }

        if (Array.Exists(existing, s => string.Equals(s, prerequisite, StringComparison.OrdinalIgnoreCase)))
        {
            Console.WriteLine($"  '{target}' already depends on '{prerequisite}'.");
            return true;
        }

        // sc.exe `depend=` takes services separated by '/'.
        var merged = existing.Length == 0
            ? prerequisite
            : string.Join("/", existing) + "/" + prerequisite;

        var exitCode = RunSc(["config", target, "depend=", merged]);
        if (exitCode != 0)
        {
            Console.Error.WriteLine($"  sc.exe config failed for '{target}' (exit {exitCode}).");
            return false;
        }

        Console.WriteLine($"  '{target}' now depends on '{prerequisite}'.");
        return true;
    }

    private static bool TryGetValue(ReadOnlySpan<string> args, ref int i, out string value)
    {
        if (i + 1 >= args.Length)
        {
            value = string.Empty;
            return false;
        }
        i++;
        value = args[i];
        return true;
    }

    private static int ArgError(string message)
    {
        Console.Error.WriteLine(message);
        return 2;
    }

    private static int RunSc(string[] scArgs)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "sc.exe",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var a in scArgs) psi.ArgumentList.Add(a);

        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start sc.exe");

        var stdout = proc.StandardOutput.ReadToEnd();
        var stderr = proc.StandardError.ReadToEnd();
        proc.WaitForExit();

        if (!string.IsNullOrWhiteSpace(stdout)) Console.WriteLine(stdout.TrimEnd());
        if (!string.IsNullOrWhiteSpace(stderr)) Console.Error.WriteLine(stderr.TrimEnd());

        return proc.ExitCode;
    }
}
