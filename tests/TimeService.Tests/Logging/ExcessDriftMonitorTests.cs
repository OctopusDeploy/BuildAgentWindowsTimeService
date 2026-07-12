using Microsoft.Extensions.Logging;
using TimeService.Logging;

namespace TimeService.Tests.Logging;

public class ExcessDriftMonitorTests
{
    private const int ExcessDriftEventId = 1500;
    private static readonly TimeSpan Threshold = TimeSpan.FromSeconds(60);

    // Excess = strictly more than 60s away, positive or negative.
    private static readonly TimeSpan Excess = TimeSpan.FromSeconds(61);
    private static readonly TimeSpan ExcessNegative = TimeSpan.FromSeconds(-61);
    private static readonly TimeSpan Good = TimeSpan.FromSeconds(10);

    [Fact]
    public void Does_not_trip_before_three_consecutive_excess_measurements()
    {
        var (monitor, logger, dir) = NewMonitor();

        monitor.Record(Excess);
        monitor.Record(Excess);

        Assert.Equal(0, logger.CountOf(ExcessDriftEventId));
        Assert.False(MarkerExists(dir));
    }

    [Fact]
    public void Trips_on_third_consecutive_excess_measurement()
    {
        var (monitor, logger, dir) = NewMonitor();

        monitor.Record(Excess);
        monitor.Record(Excess);
        monitor.Record(Excess);

        Assert.Equal(1, logger.CountOf(ExcessDriftEventId));
        Assert.True(MarkerExists(dir));
    }

    [Fact]
    public void A_good_measurement_resets_the_streak()
    {
        var (monitor, logger, dir) = NewMonitor();

        // Two excess, then a good one resets, so the following two are not enough.
        monitor.Record(Excess);
        monitor.Record(Excess);
        monitor.Record(Good);
        monitor.Record(Excess);
        monitor.Record(Excess);

        Assert.Equal(0, logger.CountOf(ExcessDriftEventId));
        Assert.False(MarkerExists(dir));

        // A third consecutive excess after the reset finally trips it.
        monitor.Record(Excess);

        Assert.Equal(1, logger.CountOf(ExcessDriftEventId));
        Assert.True(MarkerExists(dir));
    }

    [Fact]
    public void Negative_drift_counts_as_excess()
    {
        var (monitor, logger, dir) = NewMonitor();

        monitor.Record(ExcessNegative);
        monitor.Record(ExcessNegative);
        monitor.Record(ExcessNegative);

        Assert.Equal(1, logger.CountOf(ExcessDriftEventId));
        Assert.True(MarkerExists(dir));
    }

    [Fact]
    public void Drift_exactly_at_the_threshold_is_not_excess()
    {
        var (monitor, logger, dir) = NewMonitor();

        // Exactly 60s is "more than 60s away"? No — the rule is strictly greater than 60s.
        monitor.Record(Threshold);
        monitor.Record(Threshold);
        monitor.Record(Threshold);

        Assert.Equal(0, logger.CountOf(ExcessDriftEventId));
        Assert.False(MarkerExists(dir));
    }

    [Fact]
    public void Alert_fires_only_once_even_with_further_excess_measurements()
    {
        var (monitor, logger, dir) = NewMonitor();

        for (var i = 0; i < 10; i++)
            monitor.Record(Excess);

        Assert.Equal(1, logger.CountOf(ExcessDriftEventId));
        Assert.True(MarkerExists(dir));
    }

    [Fact]
    public void An_existing_marker_file_is_not_an_error()
    {
        var (monitor, logger, dir) = NewMonitor();
        Directory.CreateDirectory(dir);
        var markerPath = Path.Combine(dir, ExcessDriftMonitor.MarkerFileName);
        File.WriteAllText(markerPath, "left over from a prior run");

        monitor.Record(Excess);
        monitor.Record(Excess);
        monitor.Record(Excess);

        Assert.Equal(1, logger.CountOf(ExcessDriftEventId));
        Assert.True(File.Exists(markerPath));
    }

    [Fact]
    public void Marker_file_is_created_empty()
    {
        var (monitor, _, dir) = NewMonitor();

        monitor.Record(Excess);
        monitor.Record(Excess);
        monitor.Record(Excess);

        var markerPath = Path.Combine(dir, ExcessDriftMonitor.MarkerFileName);
        Assert.True(File.Exists(markerPath));
        Assert.Equal(0, new FileInfo(markerPath).Length);
    }

    private static (ExcessDriftMonitor Monitor, RecordingLogger Logger, string Directory) NewMonitor()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"octopus-timeservice-tests-{Guid.NewGuid():N}");
        var logger = new RecordingLogger();
        var monitor = new ExcessDriftMonitor(
            logger, dir, Threshold, ExcessDriftMonitor.DefaultConsecutiveThreshold);
        return (monitor, logger, dir);
    }

    private static bool MarkerExists(string directory) =>
        File.Exists(Path.Combine(directory, ExcessDriftMonitor.MarkerFileName));

    /// <summary>Minimal <see cref="ILogger{T}"/> that counts how many times each EventId was logged.</summary>
    private sealed class RecordingLogger : ILogger<ExcessDriftMonitor>
    {
        private readonly Dictionary<int, int> countsByEventId = new();

        public int CountOf(int eventId) => countsByEventId.GetValueOrDefault(eventId);

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            countsByEventId[eventId.Id] = CountOf(eventId.Id) + 1;
        }

        public bool IsEnabled(LogLevel logLevel) => true;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    }
}
