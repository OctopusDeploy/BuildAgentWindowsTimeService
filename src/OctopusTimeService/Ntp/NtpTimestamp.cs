using System.Buffers.Binary;

namespace TimeService.Ntp;

/// <summary>
/// A 64-bit NTP timestamp (RFC 5905 §6): seconds + fractional seconds since 1900-01-01 UTC.
/// Valid within NTP era 0, which ends 2036-02-07 06:28:16 UTC.
/// </summary>
internal readonly struct NtpTimestamp(uint seconds, uint fraction) : IEquatable<NtpTimestamp>
{
    private const long FractionPerSecond = 1L << 32;

    public static readonly DateTime Epoch =
        new(1900, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public uint Seconds { get; } = seconds;
    public uint Fraction { get; } = fraction;

    public static NtpTimestamp FromDateTime(DateTime utc)
    {
        if (utc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Must be UTC.", nameof(utc));
        if (utc < Epoch)
            throw new ArgumentOutOfRangeException(nameof(utc), "Predates NTP epoch (1900-01-01).");

        var ticksSinceEpoch = (utc - Epoch).Ticks;
        var seconds = ticksSinceEpoch / TimeSpan.TicksPerSecond;
        var subSecondTicks = ticksSinceEpoch % TimeSpan.TicksPerSecond;

        // fraction = subSecondTicks * 2^32 / TicksPerSecond
        var fraction = (uint)((ulong)subSecondTicks * (ulong)FractionPerSecond / (ulong)TimeSpan.TicksPerSecond);

        return new NtpTimestamp((uint)seconds, fraction);
    }

    public DateTime ToDateTime()
    {
        var secondsTicks = (long)Seconds * TimeSpan.TicksPerSecond;
        var fractionTicks = (long)((ulong)Fraction * (ulong)TimeSpan.TicksPerSecond / (ulong)FractionPerSecond);
        return Epoch.AddTicks(secondsTicks + fractionTicks);
    }

    public static NtpTimestamp ReadBigEndian(ReadOnlySpan<byte> source)
    {
        var s = BinaryPrimitives.ReadUInt32BigEndian(source);
        var f = BinaryPrimitives.ReadUInt32BigEndian(source[4..]);
        return new NtpTimestamp(s, f);
    }

    public void WriteBigEndian(Span<byte> destination)
    {
        BinaryPrimitives.WriteUInt32BigEndian(destination, Seconds);
        BinaryPrimitives.WriteUInt32BigEndian(destination[4..], Fraction);
    }

    public bool Equals(NtpTimestamp other) => Seconds == other.Seconds && Fraction == other.Fraction;
    public override bool Equals(object? obj) => obj is NtpTimestamp t && Equals(t);
    public override int GetHashCode() => HashCode.Combine(Seconds, Fraction);
    public static bool operator ==(NtpTimestamp a, NtpTimestamp b) => a.Equals(b);
    public static bool operator !=(NtpTimestamp a, NtpTimestamp b) => !a.Equals(b);
}
