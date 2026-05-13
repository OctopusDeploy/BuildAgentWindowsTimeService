namespace TimeService.Ntp;

/// <summary>
/// NTP v4 packet (RFC 5905 §7.3). Only the fields we need are exposed.
/// </summary>
internal static class NtpPacket
{
    public const int Size = 48;
    public const byte ModeClient = 3;
    public const byte ModeServer = 4;
    public const byte Version = 4;

    /// <summary>Build a 48-byte client request packet (LI=0, VN=4, Mode=3, rest zero).</summary>
    public static byte[] CreateClientRequest()
    {
        var buffer = new byte[Size];
        // First byte: LI(2)<<6 | VN(3)<<3 | Mode(3)
        buffer[0] = (byte)((Version << 3) | ModeClient);
        return buffer;
    }

    public static Parsed Parse(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < Size)
            throw new ArgumentException($"NTP packet must be at least {Size} bytes (got {bytes.Length}).", nameof(bytes));

        var b0 = bytes[0];
        return new Parsed
        {
            LeapIndicator = (byte)((b0 >> 6) & 0x03),
            Version = (byte)((b0 >> 3) & 0x07),
            Mode = (byte)(b0 & 0x07),
            Stratum = bytes[1],
            Poll = (sbyte)bytes[2],
            Precision = (sbyte)bytes[3],
            ReferenceTimestamp = NtpTimestamp.ReadBigEndian(bytes.Slice(16, 8)),
            OriginateTimestamp = NtpTimestamp.ReadBigEndian(bytes.Slice(24, 8)),
            ReceiveTimestamp = NtpTimestamp.ReadBigEndian(bytes.Slice(32, 8)),
            TransmitTimestamp = NtpTimestamp.ReadBigEndian(bytes.Slice(40, 8)),
        };
    }

    public readonly struct Parsed
    {
        public byte LeapIndicator { get; init; }
        public byte Version { get; init; }
        public byte Mode { get; init; }
        public byte Stratum { get; init; }
        public sbyte Poll { get; init; }
        public sbyte Precision { get; init; }
        public NtpTimestamp ReferenceTimestamp { get; init; }
        public NtpTimestamp OriginateTimestamp { get; init; }
        public NtpTimestamp ReceiveTimestamp { get; init; }
        public NtpTimestamp TransmitTimestamp { get; init; }
    }
}
