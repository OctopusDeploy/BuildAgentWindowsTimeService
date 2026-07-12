#!/usr/bin/env pwsh
param(
    [string] $LogFolder = 'C:\Octopus\TimeService',
    [double] $DurationHours = 12,
    [double] $SleepMinutes = 30
)

$ErrorActionPreference = 'Stop'

$markerFileName = 'EXCESS_DRIFT'
$markerPath = Join-Path $LogFolder $markerFileName

function Write-Log {
    param([string] $Message)
    $stamp = (Get-Date).ToString('yyyy-MM-ddTHH:mm:ssK')
    Write-Host "[$stamp] $Message"
    # TeamCity service message: surfaces the line prominently in the build log.
    $escaped = $Message -replace "\|", "||" -replace "'", "|'" -replace "\[", "|[" -replace "\]", "|]" -replace "\r", "" -replace "\n", "|n"
    Write-Host "##teamcity[message text='$escaped' status='WARNING']"
}

Write-Log "Checking for excess-drift marker at '$markerPath'."

if (-not (Test-Path -LiteralPath $markerPath -PathType Leaf)) {
    Write-Log "No EXCESS_DRIFT marker found. Clock drift is within tolerance; nothing to do."
    exit 0
}

$deadline = (Get-Date).AddHours($DurationHours)
$sleep = [TimeSpan]::FromMinutes($SleepMinutes)

Write-Log "EXCESS_DRIFT marker found. Holding for $DurationHours hour(s), logging every $SleepMinutes minute(s). Will finish at $($deadline.ToString('yyyy-MM-ddTHH:mm:ssK'))."

while ($true) {
    $remaining = $deadline - (Get-Date)
    if ($remaining -le [TimeSpan]::Zero) { break }

    Write-Log "Excess clock drift detected by OctopusTimeService (marker: '$markerPath'). $([int]$remaining.TotalMinutes) minute(s) remaining."

    # Don't overshoot the 12-hour deadline on the final iteration.
    $thisSleep = if ($remaining -lt $sleep) { $remaining } else { $sleep }
    if ($thisSleep -gt [TimeSpan]::Zero) {
        Start-Sleep -Seconds ([int]$thisSleep.TotalSeconds)
    }
}

Write-Log "Excess-drift hold complete after $DurationHours hour(s)."
exit 0
