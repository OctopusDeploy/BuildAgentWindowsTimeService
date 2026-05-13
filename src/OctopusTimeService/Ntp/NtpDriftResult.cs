namespace TimeService.Ntp;

/// <summary>
/// Result of an NTP drift measurement.
/// Drift is positive when the server is ahead of the local clock (local clock is slow).
/// MarginOfError bounds the uncertainty of Drift due to network round-trip asymmetry.
/// </summary>
public readonly record struct NtpDriftResult(TimeSpan Drift, TimeSpan MarginOfError);
