using System.Net.Sockets;

namespace TimeService.Ntp;

/// <summary>
/// Measures clock drift between the local machine and an NTP server using SNTP/NTPv4.
/// </summary>
public sealed class NtpClient
{
    public const string DefaultServer = "time.windows.com";
    public const int NtpPort = 123;
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);

    public string Server { get; }
    public TimeSpan Timeout { get; }

    public NtpClient(string server = DefaultServer, TimeSpan? timeout = null)
    {
        if (string.IsNullOrWhiteSpace(server))
            throw new ArgumentException("Server cannot be empty.", nameof(server));
        Server = server;
        Timeout = timeout ?? DefaultTimeout;
    }

    /// <summary>
    /// Measures local clock drift against the configured NTP server.
    /// Returns the offset (server-time minus local-time) and the margin of error.
    /// </summary>
    public async Task<NtpDriftResult> MeasureDriftAsync(CancellationToken cancellationToken = default)
    {
        var request = NtpPacket.CreateClientRequest();

        using var udp = new UdpClient(AddressFamily.InterNetwork);
        udp.Connect(Server, NtpPort);

        // Record T1 as close to the wire as possible.
        var t1 = DateTime.UtcNow;
        NtpTimestamp.FromDateTime(t1).WriteBigEndian(request.AsSpan(40, 8));

        await udp.SendAsync(request, cancellationToken).ConfigureAwait(false);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(Timeout);

        UdpReceiveResult response;
        try
        {
            response = await udp.ReceiveAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"No NTP response from '{Server}' within {Timeout}.");
        }
        var t4 = DateTime.UtcNow;

        var parsed = NtpPacket.Parse(response.Buffer);
        if (parsed.Mode != NtpPacket.ModeServer)
            throw new InvalidDataException($"Expected NTP server response (mode=4), got mode={parsed.Mode}.");
        if (parsed.Stratum == 0)
            throw new InvalidDataException("NTP server returned kiss-of-death (stratum=0).");
        if (parsed.TransmitTimestamp.Seconds == 0 && parsed.TransmitTimestamp.Fraction == 0)
            throw new InvalidDataException("NTP server returned an empty transmit timestamp.");

        var t2 = parsed.ReceiveTimestamp.ToDateTime();
        var t3 = parsed.TransmitTimestamp.ToDateTime();

        return NtpDriftCalculator.Calculate(t1, t2, t3, t4);
    }
}
