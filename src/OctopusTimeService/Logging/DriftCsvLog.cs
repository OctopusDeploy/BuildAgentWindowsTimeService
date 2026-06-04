using System.Globalization;

namespace TimeService.Logging;

/// <summary>
/// Appends NTP drift measurements to a CSV file on disk, alongside the Windows Event Log.
/// Columns: LocalTime, NtpTime (ISO 8601, UTC), Drift, MarginOfError (TimeSpan round-trip format).
/// Write failures are logged and swallowed so a locked/unwritable file never disrupts measurement.
/// </summary>
public sealed class DriftCsvLog(ILogger<DriftCsvLog> logger, string filePath = DriftCsvLog.DefaultPath)
{
    public const string DefaultPath = @"C:\Octopus\TimeService\ntp-drift.csv";
    private const string Header = "LocalTime,NtpTime,Drift,MarginOfError";

    private readonly Lock gate = new();

    /// <summary>
    /// Appends one measurement row. <paramref name="localTimeUtc"/> and <paramref name="ntpTimeUtc"/>
    /// must be UTC (Kind=Utc) so the ISO 8601 output carries the trailing 'Z'.
    /// </summary>
    public void Append(DateTime localTimeUtc, DateTime ntpTimeUtc, TimeSpan drift, TimeSpan marginOfError)
    {
        var inv = CultureInfo.InvariantCulture;
        var line = string.Join(',',
            localTimeUtc.ToString("O", inv),
            ntpTimeUtc.ToString("O", inv),
            drift.ToString(null, inv),
            marginOfError.ToString(null, inv));

        try
        {
            lock (gate)
            {
                var dir = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                var needsHeader = !File.Exists(filePath) || new FileInfo(filePath).Length == 0;
                using var writer = new StreamWriter(filePath, append: true);
                if (needsHeader)
                    writer.WriteLine(Header);
                writer.WriteLine(line);
            }
        }
        catch (Exception ex)
        {
            Log.DriftCsvWriteFailed(logger, filePath, ex);
        }
    }
}
