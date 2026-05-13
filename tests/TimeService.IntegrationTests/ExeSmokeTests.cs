using System.Diagnostics;

namespace TimeService.IntegrationTests;

[Collection(nameof(PublishedExeCollection))]
public class ExeSmokeTests(PublishedExeFixture fixture)
{
    [Fact]
    public async Task Exe_runs_as_console_when_not_started_by_scm_and_emits_worker_log()
    {
        var psi = new ProcessStartInfo
        {
            FileName = fixture.ExePath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

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
}
