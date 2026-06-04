using Microsoft.Extensions.Logging.Abstractions;
using TimeService.Logging;
using TimeService.Ntp;
using TimeService.Startup;

namespace TimeService.Tests.Startup;

public class StartupSequenceTests
{
    // CSV log pointed at a throwaway temp path so the shared measure-and-log path doesn't
    // touch the real C:\Octopus location during tests.
    private static DriftCsvLog TempCsvLog() =>
        new(NullLogger<DriftCsvLog>.Instance,
            Path.Combine(Path.GetTempPath(), $"octopus-timeservice-tests-{Guid.NewGuid():N}.csv"));

    [Fact]
    public async Task Monitor_only_takes_exactly_one_drift_measurement_and_skips_full_startup()
    {
        var ntp = new CountingNtpClient();
        var sequence = new StartupSequence(NullLogger<StartupSequence>.Instance, ntp, TempCsvLog(), monitorOnly: true);

        await sequence.RunAsync(CancellationToken.None);

        // The full startup path would invoke MeasureDriftAsync twice (pre- and post-resync).
        // Monitor-only takes exactly one baseline reading; observing 1 call therefore proves
        // the full sequence was not executed.
        Assert.Equal(1, ntp.CallCount);
    }

    [Fact]
    public async Task Monitor_only_swallows_drift_measurement_failures()
    {
        var ntp = new ThrowingNtpClient(new InvalidOperationException("boom"));
        var sequence = new StartupSequence(NullLogger<StartupSequence>.Instance, ntp, TempCsvLog(), monitorOnly: true);

        // No exception should propagate: monitor-only mode must not prevent the host coming up
        // just because a baseline drift sample failed.
        await sequence.RunAsync(CancellationToken.None);

        Assert.Equal(1, ntp.CallCount);
    }

    [Fact]
    public async Task Monitor_only_propagates_host_cancellation()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var ntp = new ThrowingNtpClient(new OperationCanceledException(cts.Token));
        var sequence = new StartupSequence(NullLogger<StartupSequence>.Instance, ntp, TempCsvLog(), monitorOnly: true);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sequence.RunAsync(cts.Token));
    }

    private class CountingNtpClient : NtpClient
    {
        public int CallCount { get; private set; }

        public override Task<NtpDriftResult> MeasureDriftAsync(CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(new NtpDriftResult(TimeSpan.Zero, TimeSpan.Zero));
        }
    }

    private class ThrowingNtpClient(Exception toThrow) : NtpClient
    {
        public int CallCount { get; private set; }

        public override Task<NtpDriftResult> MeasureDriftAsync(CancellationToken cancellationToken = default)
        {
            CallCount++;
            throw toThrow;
        }
    }
}
