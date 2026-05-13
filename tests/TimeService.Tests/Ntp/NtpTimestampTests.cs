using TimeService.Ntp;

namespace TimeService.Tests.Ntp;

public class NtpTimestampTests
{
    [Fact]
    public void Epoch_is_1900_01_01_UTC()
    {
        Assert.Equal(new DateTime(1900, 1, 1, 0, 0, 0, DateTimeKind.Utc), NtpTimestamp.Epoch);
    }

    [Fact]
    public void FromDateTime_at_epoch_yields_zero()
    {
        var ts = NtpTimestamp.FromDateTime(NtpTimestamp.Epoch);
        Assert.Equal(0u, ts.Seconds);
        Assert.Equal(0u, ts.Fraction);
    }

    [Fact]
    public void Unix_epoch_has_known_NTP_seconds()
    {
        // RFC 5905: 70 years + 17 leap days between 1900-01-01 and 1970-01-01.
        var unixEpoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var ts = NtpTimestamp.FromDateTime(unixEpoch);

        Assert.Equal(2208988800u, ts.Seconds);
        Assert.Equal(0u, ts.Fraction);
    }

    [Fact]
    public void FromDateTime_rejects_non_utc()
    {
        var local = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Local);
        Assert.Throws<ArgumentException>(() => NtpTimestamp.FromDateTime(local));
    }

    [Fact]
    public void FromDateTime_rejects_pre_epoch()
    {
        var preEpoch = new DateTime(1899, 12, 31, 23, 59, 59, DateTimeKind.Utc);
        Assert.Throws<ArgumentOutOfRangeException>(() => NtpTimestamp.FromDateTime(preEpoch));
    }

    [Fact]
    public void Roundtrip_DateTime_preserves_value_to_tick_precision()
    {
        var original = new DateTime(2024, 6, 15, 12, 34, 56, 789, DateTimeKind.Utc).AddTicks(1234);

        var ts = NtpTimestamp.FromDateTime(original);
        var roundTripped = ts.ToDateTime();

        // NTP fractional resolution (~233 ps) is far finer than DateTime tick (100 ns),
        // but the int division does floor — allow ±1 tick.
        var diff = Math.Abs((original - roundTripped).Ticks);
        Assert.True(diff <= 1, $"Round-trip drift was {diff} ticks");
    }

    [Fact]
    public void Half_second_is_exactly_top_bit_of_fraction()
    {
        var halfPastEpoch = NtpTimestamp.Epoch.AddTicks(TimeSpan.TicksPerSecond / 2);
        var ts = NtpTimestamp.FromDateTime(halfPastEpoch);

        Assert.Equal(0u, ts.Seconds);
        Assert.Equal(0x80000000u, ts.Fraction);
    }

    [Fact]
    public void WriteBigEndian_then_ReadBigEndian_round_trips()
    {
        var original = new NtpTimestamp(0xDEADBEEF, 0x12345678);
        Span<byte> buf = stackalloc byte[8];
        original.WriteBigEndian(buf);

        // Verify byte order explicitly.
        Assert.Equal(new byte[] { 0xDE, 0xAD, 0xBE, 0xEF, 0x12, 0x34, 0x56, 0x78 }, buf.ToArray());

        var read = NtpTimestamp.ReadBigEndian(buf);
        Assert.Equal(original, read);
    }
}
