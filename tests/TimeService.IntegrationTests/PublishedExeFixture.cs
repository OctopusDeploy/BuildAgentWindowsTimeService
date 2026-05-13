using System.Text;
using Octopus.Shellfish;

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

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        var command = new ShellCommand("dotnet")
            .WithArguments(["publish", projectPath, "-c", "Release", "-r", "win-x64"])
            .WithStdOutTarget(stdout)
            .WithStdErrTarget(stderr);

        // The AOT toolchain shells out to vswhere.exe to locate the Windows SDK.
        // Shellfish merges these on top of the parent process's environment.
        var vswhereDir = @"C:\Program Files (x86)\Microsoft Visual Studio\Installer";
        if (Directory.Exists(vswhereDir))
        {
            var currentPath = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            command = command.WithEnvironmentVariables(new Dictionary<string, string>
            {
                ["PATH"] = vswhereDir + Path.PathSeparator + currentPath,
            });
        }

        var result = await command.ExecuteAsync();

        if (result.ExitCode != 0)
            throw new InvalidOperationException(
                $"dotnet publish failed (exit {result.ExitCode}).\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}");

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
