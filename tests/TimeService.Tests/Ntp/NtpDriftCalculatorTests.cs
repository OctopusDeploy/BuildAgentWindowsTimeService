using TimeService.Ntp;

namespace TimeService.Tests.Ntp;

public class NtpDriftCalculatorTests
{
    private static readonly DateTime Base =
        new(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Identical_timestamps_yield_zero_drift_and_zero_margin()
    {
        var result = NtpDriftCalculator.Calculate(Base, Base, Base, Base);
        Assert.Equal(TimeSpan.Zero, result.Drift);
        Assert.Equal(TimeSpan.Zero, result.MarginOfError);
    }

    [Fact]
    public void Perfectly_synchronized_clock_with_symmetric_latency_yields_zero_drift()
    {
        // Clocks agree; 100ms one-way latency in each direction.
        var t1 = Base;
        var t2 = Base + TimeSpan.FromMilliseconds(100);
        var t3 = t2 + TimeSpan.FromMilliseconds(50);     // server processing time
        var t4 = t3 + TimeSpan.FromMilliseconds(100);

        var result = NtpDriftCalculator.Calculate(t1, t2, t3, t4);

        Assert.Equal(TimeSpan.Zero, result.Drift);
        // Round trip = 200ms (just the two 100ms legs; server processing is excluded).
        Assert.Equal(TimeSpan.FromMilliseconds(100), result.MarginOfError);
    }

    [Fact]
    public void Local_clock_slow_by_one_second_yields_positive_drift_of_one_second()
    {
        // Server is 1 second ahead of local. Latency symmetric 50ms each way.
        const int oneWayMs = 50;
        var localT1 = Base;
        var serverReceive = Base + TimeSpan.FromSeconds(1) + TimeSpan.FromMilliseconds(oneWayMs);
        var serverTransmit = serverReceive + TimeSpan.FromMilliseconds(10);
        var localT4 = localT1 + TimeSpan.FromMilliseconds(oneWayMs) + TimeSpan.FromMilliseconds(10) + TimeSpan.FromMilliseconds(oneWayMs);

        var result = NtpDriftCalculator.Calculate(localT1, serverReceive, serverTransmit, localT4);

        Assert.Equal(TimeSpan.FromSeconds(1), result.Drift);
        Assert.Equal(TimeSpan.FromMilliseconds(oneWayMs), result.MarginOfError);
    }

    [Fact]
    public void Local_clock_fast_yields_negative_drift()
    {
        // Server is 500ms behind local clock (local is ahead).
        var t1 = Base;
        var t2 = Base - TimeSpan.FromMilliseconds(500) + TimeSpan.FromMilliseconds(20);
        var t3 = t2 + TimeSpan.FromMilliseconds(5);
        var t4 = t1 + TimeSpan.FromMilliseconds(45);

        var result = NtpDriftCalculator.Calculate(t1, t2, t3, t4);

        Assert.Equal(TimeSpan.FromMilliseconds(-500), result.Drift);
    }

    [Fact]
    public void Margin_is_half_the_round_trip_delay()
    {
        // Build a request/response where T4 - T1 = 200ms and T3 - T2 = 0
        // → delay = 200ms → margin = 100ms.
        var t1 = Base;
        var t2 = Base + TimeSpan.FromMilliseconds(100);
        var t3 = t2;
        var t4 = t1 + TimeSpan.FromMilliseconds(200);

        var result = NtpDriftCalculator.Calculate(t1, t2, t3, t4);

        Assert.Equal(TimeSpan.FromMilliseconds(100), result.MarginOfError);
    }

    [Fact]
    public void Margin_is_non_negative_even_when_individual_diffs_are_negative()
    {
        // Pathological ordering: would otherwise yield negative delay arithmetic.
        var t1 = Base;
        var t2 = Base - TimeSpan.FromSeconds(10);
        var t3 = t2 + TimeSpan.FromMilliseconds(1);
        var t4 = t1 + TimeSpan.FromMilliseconds(50);

        var result = NtpDriftCalculator.Calculate(t1, t2, t3, t4);

        Assert.True(result.MarginOfError >= TimeSpan.Zero);
    }
}
