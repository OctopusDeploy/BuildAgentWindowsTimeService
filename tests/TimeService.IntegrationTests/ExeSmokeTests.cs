using System.Diagnostics;
using System.Text;
using Octopus.Shellfish;

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
        Process? capturedProc = null;
        var sawWorkerLog = false;

        var shellTask = new ShellCommand(fixture.ExePath)
            .WithArguments(args)
            .CaptureProcess(p => capturedProc = p)
            .WithStdOutTarget(line =>
            {
                // Accept either a successful drift measurement or a failure log
                // (CI agents without outbound UDP/123 still exercise the worker code path).
                if (line.Contains("Clock drift") || line.Contains("Failed to measure clock drift"))
                    sawWorkerLog = true;
            })
            .ExecuteAsync();

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            while (!sawWorkerLog && !cts.IsCancellationRequested && capturedProc?.HasExited != true)
            {
                await Task.Delay(100, cts.Token);
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (capturedProc is { HasExited: false })
            {
                capturedProc.Kill(entireProcessTree: true);
            }
            try { await shellTask; } catch { /* exit-on-kill is expected */ }
        }

        Assert.True(sawWorkerLog, "Expected a clock-drift log line in stdout but never saw one.");
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
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        var result = new ShellCommand(fixture.ExePath)
            .WithArguments(args)
            .WithStdOutTarget(stdout)
            .WithStdErrTarget(stderr)
            .Execute();
        return (result.ExitCode, stdout.ToString(), stderr.ToString());
    }
}
