# Gather-OSLogs.ps1
#
# Collects OS-level provisioning / early-boot logs into C:\Octopus\TimeService\OSLogs
# preserving the directory structure relative to %windir%.
#
# Compatible with Windows PowerShell (Desktop) 3.0+ on Windows Server 2012, 2016, 2019, 2022.

# Use Continue globally so unexpected non-terminating errors don't halt the script.
# Catchable operations use -ErrorAction Stop locally.
$ErrorActionPreference = 'Continue'

# ---------------------------------------------------------------------------
# Configuration
# ---------------------------------------------------------------------------
$WinDir     = $env:windir                       # %windir%
$SystemRoot = $env:SystemRoot                    # %SystemRoot% -- same directory as %windir%, kept separate to mirror Microsoft's notation
$BaseDir    = 'C:\Octopus\TimeService'
$OutputRoot = Join-Path $BaseDir 'OSLogs'
$LogFile    = Join-Path $BaseDir 'oslog-copy.log'
$MaxAttempts = 3
$RetryDelaySeconds = 2

# Shared state
$script:Processed = @{}                          # de-dupe by source full path (lower-cased)
$script:Enumerated = @()                         # scratch buffer for retried enumerations
$script:Stats = @{ Copied = 0; Failed = 0; Skipped = 0 }

# ---------------------------------------------------------------------------
# Functions
# ---------------------------------------------------------------------------

function Write-Log {
    param([string]$Message)

    $timestamp = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss')
    $line = "$timestamp  $Message"
    try {
        Add-Content -LiteralPath $LogFile -Value $line -ErrorAction Stop
    } catch {
        # If we can't write to the log file, don't let it stop collection.
    }
    Write-Host $line
}

# Runs a scriptblock, retrying up to $MaxAttempts times on error.
# Returns $true on success, $false if all attempts failed.
function Invoke-WithRetry {
    param(
        [ScriptBlock]$Action,
        [string]$Description
    )

    for ($attempt = 1; $attempt -le $MaxAttempts; $attempt++) {
        try {
            & $Action
            return $true
        } catch {
            $errMessage = $_.Exception.Message
            if ($attempt -lt $MaxAttempts) {
                Write-Log ("WARN  Attempt {0}/{1} failed for {2}: {3}" -f $attempt, $MaxAttempts, $Description, $errMessage)
                Start-Sleep -Seconds $RetryDelaySeconds
            } else {
                Write-Log ("ERROR All {0} attempts failed for {1}: {2}" -f $MaxAttempts, $Description, $errMessage)
            }
        }
    }
    return $false
}

# Maps a source path under %windir% to its destination under $OutputRoot,
# preserving the structure relative to %windir%.
function Get-DestinationPath {
    param([string]$SourceFullPath)

    $base = $WinDir.TrimEnd('\')
    if ($SourceFullPath.Length -ge $base.Length -and
        $SourceFullPath.Substring(0, $base.Length).ToLower() -eq $base.ToLower()) {
        $relative = $SourceFullPath.Substring($base.Length).TrimStart('\')
    } else {
        # Fallback for anything not under %windir%: strip the drive qualifier.
        $relative = $SourceFullPath -replace '^[A-Za-z]:[\\/]', ''
    }

    if ([string]::IsNullOrEmpty($relative)) {
        return $OutputRoot
    }
    return (Join-Path $OutputRoot $relative)
}

function New-DirectoryWithRetry {
    param([string]$Path)

    Invoke-WithRetry -Description ("Create directory {0}" -f $Path) -Action {
        if (-not (Test-Path -LiteralPath $Path)) {
            New-Item -ItemType Directory -Path $Path -Force -ErrorAction Stop | Out-Null
        }
    } | Out-Null
}

# Fallback for event log files (.evtx), which are usually held open by the
# Event Log service and therefore can't be copied directly. Exports the live
# channel via wevtutil instead. Returns $true on success.
function Invoke-EvtxExportFallback {
    param([string]$SourceFile, [string]$DestFile)

    $channel = [System.IO.Path]::GetFileNameWithoutExtension($SourceFile)
    Write-Log ("INFO  Attempting wevtutil export of channel '{0}' -> {1}" -f $channel, $DestFile)

    $ok = Invoke-WithRetry -Description ("wevtutil export of channel {0}" -f $channel) -Action {
        $destParent = Split-Path -Path $DestFile -Parent
        if (-not (Test-Path -LiteralPath $destParent)) {
            New-Item -ItemType Directory -Path $destParent -Force -ErrorAction Stop | Out-Null
        }
        # /ow:true overwrites any partial file left by the failed direct copy.
        $output = & wevtutil.exe epl $channel $DestFile /ow:true 2>&1
        if ($LASTEXITCODE -ne 0) {
            throw ("wevtutil exited with code {0}: {1}" -f $LASTEXITCODE, ($output -join ' '))
        }
    }

    if ($ok) {
        Write-Log ("OK    Exported channel '{0}' -> {1} via wevtutil" -f $channel, $DestFile)
    }
    return $ok
}

# Copies a single file with retry. If the copy fails after all retries and a
# $FallbackAction scriptblock was supplied, it is invoked with ($SourceFile,
# $DestFile) and is expected to return $true/$false.
function Copy-OneFile {
    param(
        [string]$SourceFile,
        [ScriptBlock]$FallbackAction
    )

    $key = $SourceFile.ToLower()
    if ($script:Processed.ContainsKey($key)) {
        return
    }
    $script:Processed[$key] = $true

    $destFile = Get-DestinationPath $SourceFile
    $ok = Invoke-WithRetry -Description ("Copy {0}" -f $SourceFile) -Action {
        $destParent = Split-Path -Path $destFile -Parent
        if (-not (Test-Path -LiteralPath $destParent)) {
            New-Item -ItemType Directory -Path $destParent -Force -ErrorAction Stop | Out-Null
        }
        Copy-Item -LiteralPath $SourceFile -Destination $destFile -Force -ErrorAction Stop
    }

    if ($ok) {
        $script:Stats.Copied++
        Write-Log ("OK    Copied {0} -> {1}" -f $SourceFile, $destFile)
        return
    }

    # Primary copy failed after all retries. Try the fallback if one was supplied.
    if ($FallbackAction) {
        Write-Log ("INFO  Direct copy failed for {0}; invoking fallback." -f $SourceFile)
        $fallbackOk = & $FallbackAction $SourceFile $destFile
        if ($fallbackOk) {
            $script:Stats.Copied++
            return
        }
    }

    $script:Stats.Failed++
}

function Copy-Directory {
    param([string]$SourceDir)

    # Ensure the destination root for this directory exists.
    New-DirectoryWithRetry (Get-DestinationPath $SourceDir)

    # Recreate sub-directories first (preserves empty folders), with retry on enumeration.
    $script:Enumerated = @()
    $listedDirs = Invoke-WithRetry -Description ("List subdirectories of {0}" -f $SourceDir) -Action {
        $script:Enumerated = @(Get-ChildItem -LiteralPath $SourceDir -Recurse -Force -ErrorAction Stop |
            Where-Object { $_.PSIsContainer })
    }
    if ($listedDirs) {
        foreach ($dir in $script:Enumerated) {
            New-DirectoryWithRetry (Get-DestinationPath $dir.FullName)
        }
    }

    # Copy files (recursively), each with its own retry so one locked file
    # doesn't stop the rest.
    $script:Enumerated = @()
    $listedFiles = Invoke-WithRetry -Description ("List files of {0}" -f $SourceDir) -Action {
        $script:Enumerated = @(Get-ChildItem -LiteralPath $SourceDir -Recurse -Force -ErrorAction Stop |
            Where-Object { -not $_.PSIsContainer })
    }
    if (-not $listedFiles) {
        return
    }
    foreach ($file in $script:Enumerated) {
        Copy-OneFile -SourceFile $file.FullName
    }
}

function Copy-SourceItem {
    param(
        [string]$SourcePath,
        [ScriptBlock]$FallbackAction
    )

    if (-not (Test-Path -LiteralPath $SourcePath)) {
        $script:Stats.Skipped++
        Write-Log ("SKIP  Source not found: {0}" -f $SourcePath)
        return
    }

    $item = $null
    try { $item = Get-Item -LiteralPath $SourcePath -Force -ErrorAction Stop } catch { }

    if ($item -and $item.PSIsContainer) {
        Write-Log ("INFO  Processing directory: {0}" -f $SourcePath)
        Copy-Directory -SourceDir $SourcePath
    } else {
        Write-Log ("INFO  Processing file: {0}" -f $SourcePath)
        Copy-OneFile -SourceFile $SourcePath -FallbackAction $FallbackAction
    }
}

# ---------------------------------------------------------------------------
# Sources to collect (relative to %windir%).
# The two .evtx files are given a wevtutil export fallback for when the live
# Event Log service holds them open. Panther\UnattendGC lives under Panther;
# the de-dupe guard in Copy-OneFile prevents a double copy.
# ---------------------------------------------------------------------------
$EvtxFallback = {
    param($SourceFile, $DestFile)
    Invoke-EvtxExportFallback -SourceFile $SourceFile -DestFile $DestFile
}

$SourceItems = @(
    @{ Path = (Join-Path $WinDir 'System32\Sysprep\Panther') },
    @{ Path = (Join-Path $WinDir 'Panther') },
    @{ Path = (Join-Path $WinDir 'Panther\UnattendGC') },
    @{ Path = (Join-Path $WinDir 'inf\Setupapi.offline.log') },
    @{ Path = (Join-Path $WinDir 'inf\Setupapi.dev.log') },
    @{ Path = (Join-Path $WinDir 'inf\Setupapi.app.log') },
    @{ Path = (Join-Path $WinDir 'servicing\sessions\Sessions.xml') },
    @{ Path = (Join-Path $SystemRoot 'System32\Winevt\Logs\System.evtx');      Fallback = $EvtxFallback },
    @{ Path = (Join-Path $SystemRoot 'System32\Winevt\Logs\Application.evtx'); Fallback = $EvtxFallback }
)

# ---------------------------------------------------------------------------
# Main
# ---------------------------------------------------------------------------

# Bootstrap the base directory before the first log write.
if (-not (Test-Path -LiteralPath $BaseDir)) {
    New-Item -ItemType Directory -Path $BaseDir -Force | Out-Null
}

Write-Log "=========================================================="
Write-Log ("INFO  OS log collection started. windir={0}" -f $WinDir)
Write-Log ("INFO  Output: {0}" -f $OutputRoot)

New-DirectoryWithRetry $OutputRoot

foreach ($entry in $SourceItems) {
    $fallback = $null
    if ($entry.ContainsKey('Fallback')) {
        $fallback = $entry.Fallback
    }
    Copy-SourceItem -SourcePath $entry.Path -FallbackAction $fallback
}

Write-Log ("INFO  Collection finished. Copied={0} Failed={1} Skipped={2}" -f `
    $script:Stats.Copied, $script:Stats.Failed, $script:Stats.Skipped)
Write-Log "=========================================================="
