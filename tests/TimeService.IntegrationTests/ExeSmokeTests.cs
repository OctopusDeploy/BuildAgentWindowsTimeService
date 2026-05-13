using System.Diagnostics;

namespace TimeService.IntegrationTests;

[Collection(nameof(PublishedExeCollection))]
public class ExeSmokeTests(PublishedExeFixture fixture)
{
    [Fact]
    public Task Worker_runs_and_emits_log_when_invoked_with_no_args()
        => AssertExeEmitsWorkerLog([]);

    [Fact]
    public Task Worker_runs_and_emits_log_when_invoked_with_run_command()
        => AssertExeEmitsWorkerLog(["run"]);

    private async Task AssertExeEmitsWorkerLog(string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fixture.ExePath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start exe");

        var sawWorkerLog = false;
        proc.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null && e.Data.Contains("Worker running at"))
                sawWorkerLog = true;
        };
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            while (!sawWorkerLog && !cts.IsCancellationRequested && !proc.HasExited)
            {
                await Task.Delay(100, cts.Token);
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (!proc.HasExited)
            {
                proc.Kill(entireProcessTree: true);
                await proc.WaitForExitAsync();
            }
        }

        Assert.True(sawWorkerLog, "Expected 'Worker running at' in stdout but never saw it.");
    }

    [Theory]
    [InlineData("help")]
    [InlineData("--help")]
    [InlineData("-h")]
    public void Help_command_prints_usage_and_exits_zero(string helpArg)
    {
        var (exit, stdout, _) = RunExeToCompletion([helpArg]);

        Assert.Equal(0, exit);
        Assert.Contains("Usage:", stdout);
        Assert.Contains("run", stdout);
        Assert.Contains("install", stdout);
        Assert.Contains("uninstall", stdout);
    }

    [Fact]
    public void Unknown_command_prints_usage_and_exits_nonzero()
    {
        var (exit, _, stderr) = RunExeToCompletion(["bogus"]);

        Assert.NotEqual(0, exit);
        Assert.Contains("Unknown command: bogus", stderr);
        Assert.Contains("Usage:", stderr);
    }

    private (int ExitCode, string StdOut, string StdErr) RunExeToCompletion(string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fixture.ExePath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start exe");

        var stdout = proc.StandardOutput.ReadToEnd();
        var stderr = proc.StandardError.ReadToEnd();
        proc.WaitForExit();
        return (proc.ExitCode, stdout, stderr);
    }
}
