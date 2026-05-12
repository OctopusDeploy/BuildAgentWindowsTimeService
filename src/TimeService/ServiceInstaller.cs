using System.Diagnostics;

namespace TimeService;

internal static class ServiceInstaller
{
    public static int Install(ReadOnlySpan<string> args)
    {
        var serviceName = ServiceDefaults.ServiceName;
        string? executablePath = null;

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
