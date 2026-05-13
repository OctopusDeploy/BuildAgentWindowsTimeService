using TimeService.Ntp;

namespace TimeService.Tests.Ntp;

public class NtpPacketTests
{
    [Fact]
    public void CreateClientRequest_is_48_bytes()
    {
        var packet = NtpPacket.CreateClientRequest();
        Assert.Equal(48, packet.Length);
    }

    [Fact]
    public void CreateClientRequest_sets_VN_4_and_Mode_3()
    {
        var packet = NtpPacket.CreateClientRequest();

        // Byte 0 layout: LI(2) | VN(3) | Mode(3)
        Assert.Equal(0x23, packet[0]); // 0b00_100_011
        Assert.Equal(0, (packet[0] >> 6) & 0x3);   // LI
        Assert.Equal(4, (packet[0] >> 3) & 0x7);   // VN
        Assert.Equal(3, packet[0] & 0x7);          // Mode
    }

    [Fact]
    public void CreateClientRequest_has_zeros_in_all_other_bytes()
    {
        var packet = NtpPacket.CreateClientRequest();
        for (var i = 1; i < packet.Length; i++)
        {
            Assert.Equal(0, packet[i]);
        }
    }

    [Fact]
    public void Parse_rejects_short_buffer()
    {
        var shortBuffer = new byte[47];
        Assert.Throws<ArgumentException>(() => NtpPacket.Parse(shortBuffer));
    }

    [Fact]
    public void Parse_extracts_header_fields_from_synthetic_response()
    {
        var bytes = new byte[48];
        // LI=0, VN=4, Mode=4 (server) → 0b00_100_100 = 0x24
        bytes[0] = 0x24;
        bytes[1] = 2;        // stratum
        bytes[2] = 6;        // poll (signed)
        bytes[3] = unchecked((byte)-20); // precision

        var parsed = NtpPacket.Parse(bytes);

        Assert.Equal(0, parsed.LeapIndicator);
        Assert.Equal(4, parsed.Version);
        Assert.Equal(4, parsed.Mode);
        Assert.Equal(2, parsed.Stratum);
        Assert.Equal(6, parsed.Poll);
        Assert.Equal(-20, parsed.Precision);
    }

    [Fact]
    public void Parse_extracts_all_four_timestamps_at_correct_offsets()
    {
        var bytes = new byte[48];

        var refTs   = new NtpTimestamp(0x11111111, 0x22222222);
        var origTs  = new NtpTimestamp(0x33333333, 0x44444444);
        var recvTs  = new NtpTimestamp(0x55555555, 0x66666666);
        var xmitTs  = new NtpTimestamp(0x77777777, 0x88888888);

        refTs.WriteBigEndian(bytes.AsSpan(16, 8));
        origTs.WriteBigEndian(bytes.AsSpan(24, 8));
        recvTs.WriteBigEndian(bytes.AsSpan(32, 8));
        xmitTs.WriteBigEndian(bytes.AsSpan(40, 8));

        var parsed = NtpPacket.Parse(bytes);

        Assert.Equal(refTs, parsed.ReferenceTimestamp);
        Assert.Equal(origTs, parsed.OriginateTimestamp);
        Assert.Equal(recvTs, parsed.ReceiveTimestamp);
        Assert.Equal(xmitTs, parsed.TransmitTimestamp);
    }
}
