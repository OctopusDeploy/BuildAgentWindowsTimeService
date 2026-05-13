namespace TimeService.Ntp;

/// <summary>
/// Pure NTP offset/delay arithmetic (RFC 5905 §8). Separated from the network layer so
/// it can be unit-tested with synthetic timestamps.
/// </summary>
internal static class NtpDriftCalculator
{
    /// <param name="t1">Client send time (UTC).</param>
    /// <param name="t2">Server receive time (UTC).</param>
    /// <param name="t3">Server transmit time (UTC).</param>
    /// <param name="t4">Client receive time (UTC).</param>
    public static NtpDriftResult Calculate(DateTime t1, DateTime t2, DateTime t3, DateTime t4)
    {
        // Clock offset: theta = ((T2 - T1) + (T3 - T4)) / 2
        var offsetTicks = ((t2 - t1).Ticks + (t3 - t4).Ticks) / 2;
        var drift = TimeSpan.FromTicks(offsetTicks);

        // Round-trip delay: delta = (T4 - T1) - (T3 - T2)
        // Max error in offset, assuming symmetric path delays, is |delta| / 2.
        var delayTicks = (t4 - t1).Ticks - (t3 - t2).Ticks;
        var margin = TimeSpan.FromTicks(Math.Abs(delayTicks) / 2);

        return new NtpDriftResult(drift, margin);
    }
}
