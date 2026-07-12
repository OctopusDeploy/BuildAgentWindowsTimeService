namespace TimeService.Logging;

/// <summary>
/// Watches successive NTP drift measurements and raises a one-time alert when the clock has been
/// excessively wrong for several consecutive measurements. "Excessive" means the absolute drift
/// (server ahead of, or behind, the local clock) exceeds <paramref name="threshold"/>; the alert
/// fires once the streak of excessive measurements reaches <paramref name="consecutiveThreshold"/>
/// in a row. A single within-threshold measurement resets the streak.
///
/// <para>When tripped it logs <see cref="Log.ExcessDriftDetected"/> (EventId 1500) and drops an
/// empty EXCESS_DRIFT marker file into the log folder. The alert fires at most once per process
/// lifetime. State is held in memory, which suits this long-running service. Thread-safe.</para>
/// </summary>
public sealed class ExcessDriftMonitor
{
    public const string MarkerFileName = "EXCESS_DRIFT";
    public const int DefaultConsecutiveThreshold = 3;
    public static readonly TimeSpan DefaultThreshold = TimeSpan.FromSeconds(60);

    private readonly ILogger<ExcessDriftMonitor> logger;
    private readonly string directory;
    private readonly TimeSpan threshold;
    private readonly int consecutiveThreshold;
    private readonly Lock gate = new();

    private int consecutiveExcessCount;
    private bool alerted;

    public ExcessDriftMonitor(ILogger<ExcessDriftMonitor> logger, string directory)
        : this(logger, directory, DefaultThreshold, DefaultConsecutiveThreshold)
    {
    }

    public ExcessDriftMonitor(
        ILogger<ExcessDriftMonitor> logger,
        string directory,
        TimeSpan threshold,
        int consecutiveThreshold)
    {
        this.logger = logger;
        this.directory = directory;
        this.threshold = threshold;
        this.consecutiveThreshold = consecutiveThreshold;
    }

    /// <summary>
    /// Records one drift measurement, updating the consecutive-excess streak and firing the
    /// one-time alert if the streak reaches the configured threshold. Drift may be positive or
    /// negative; only its magnitude relative to the threshold matters.
    /// </summary>
    public void Record(TimeSpan drift)
    {
        lock (gate)
        {
            var excess = drift.Duration() > threshold;
            consecutiveExcessCount = excess ? consecutiveExcessCount + 1 : 0;

            if (alerted || consecutiveExcessCount < consecutiveThreshold)
                return;

            alerted = true;
            Log.ExcessDriftDetected(logger);
            WriteMarkerFile();
        }
    }

    private void WriteMarkerFile()
    {
        var path = Path.Combine(directory, MarkerFileName);
        try
        {
            Directory.CreateDirectory(directory);
            // Empty placeholder. OpenOrCreate leaves any existing marker untouched — a marker
            // already on disk (e.g. left by a prior run) is expected, not an error.
            using var _ = File.Open(path, FileMode.OpenOrCreate, FileAccess.Write, FileShare.ReadWrite);
        }
        catch (Exception ex)
        {
            Log.ExcessDriftMarkerWriteFailed(logger, path, ex);
        }
    }
}
