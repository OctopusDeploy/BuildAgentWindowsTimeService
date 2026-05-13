using System.Diagnostics;

namespace TimeService.IntegrationTests;

/// <summary>
/// Publishes the main project (NativeAOT) once per test run and exposes the resulting exe path.
/// Set the env var TIMESERVICE_EXE to skip the publish and point at an existing exe.
/// </summary>
public sealed class PublishedExeFixture : IAsyncLifetime
{
    public string ExePath { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        var preBuilt = Environment.GetEnvironmentVariable("TIMESERVICE_EXE");
        if (!string.IsNullOrWhiteSpace(preBuilt) && File.Exists(preBuilt))
        {
            ExePath = preBuilt;
            return;
        }

        var projectPath = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "OctopusTimeService", "OctopusTimeService.csproj"));

        if (!File.Exists(projectPath))
            throw new FileNotFoundException($"Could not locate main project at {projectPath}");

        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        psi.ArgumentList.Add("publish");
        psi.ArgumentList.Add(projectPath);
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add("Release");
        psi.ArgumentList.Add("-r");
        psi.ArgumentList.Add("win-x64");

        // The AOT toolchain shells out to vswhere.exe to locate the Windows SDK.
        // Make sure its directory is on PATH for the child process.
        var vswhereDir = @"C:\Program Files (x86)\Microsoft Visual Studio\Installer";
        if (Directory.Exists(vswhereDir))
        {
            var currentPath = psi.Environment["PATH"] ?? string.Empty;
            psi.Environment["PATH"] = vswhereDir + Path.PathSeparator + currentPath;
        }

        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start dotnet publish");

        var stdoutTask = proc.StandardOutput.ReadToEndAsync();
        var stderrTask = proc.StandardError.ReadToEndAsync();
        await proc.WaitForExitAsync();
        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        if (proc.ExitCode != 0)
            throw new InvalidOperationException(
                $"dotnet publish failed (exit {proc.ExitCode}).\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}");

        var publishDir = Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(projectPath)!,
            "bin", "Release", "net10.0-windows", "win-x64", "publish"));
        var exe = Path.Combine(publishDir, "OctopusTimeService.exe");

        if (!File.Exists(exe))
            throw new FileNotFoundException($"Published exe not found at {exe}");

        ExePath = exe;
    }

    public Task DisposeAsync() => Task.CompletedTask;
}

[CollectionDefinition(nameof(PublishedExeCollection))]
public sealed class PublishedExeCollection : ICollectionFixture<PublishedExeFixture>
{
}
