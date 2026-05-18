# OctopusTimeService

A diagnostic Windows service for troubleshooting clock drift on TeamCity build
agents. It runs in the background, periodically measures how far the local
clock has drifted from an NTP reference, and writes the result to the Windows
Event Log so the data can be correlated against build failures and other
agent symptoms.

## What it does

When the service starts, it runs a one-time startup sequence:

1. Measures clock drift against the NTP server ("pre-resync" baseline).
2. Brings up `W32Time` (and `vmictimesync` on Hyper-V guests) if they are
   disabled or stopped.
3. Runs `w32tm /resync /force` to give Windows one chance to correct the
   clock.
4. Disables the `\Microsoft\Windows\Time Synchronization\ForceSynchronizeTime`
   and `\Microsoft\Windows\Time Synchronization\SynchronizeTime` scheduled
   tasks so they cannot quietly re-sync behind us.
5. Stops and disables `W32Time` and `vmictimesync` so nothing else touches
   the clock while we are observing it.
6. Measures drift again ("post-resync") to record the result.

Once the startup sequence finishes, the service enters its steady-state
loop: measure drift, log it, wait, repeat. The interval is configurable
(see [Configuration](#configuration)); the default is 30 seconds.

The NTP server is `time.windows.com`, queried over UDP/123.

## Logs

All output goes to the **Windows Event Log** under the source name matching
the service name. Each log entry has a stable Event ID; the ranges are:

| Range     | Component         |
| --------- | ----------------- |
| 1000-1099 | Worker (steady-state drift measurements, service lifecycle) |
| 2000-2099 | StartupSequence (startup measurements, w32tm resync)        |
| 3000-3099 | WindowsServiceOps (start/stop/disable of `W32Time` etc.)    |
| 4000-4099 | ScheduledTaskOps (disabling scheduled tasks)                |

The steady-state drift measurement is Event ID **1005**; a failed
measurement is **1006**.

## Installation

The service ships as a single self-contained NativeAOT executable
(`OctopusTimeService.exe`). No .NET runtime is required on the target
machine.

Open an **elevated** command prompt and run:

```
OctopusTimeService.exe install --dependent MSSQLSERVER --dependent TCBuildAgent
```

This registers the service with SCM under the default name
`OctopusTimeService`, sets it to start automatically at boot, and writes
its configuration into the registry.

To remove it:

```
OctopusTimeService.exe uninstall
```

This stops the service, deregisters it from SCM, and clears the service's
registry key. It does **not** re-enable `W32Time` or the scheduled tasks
the startup sequence disabled — if you want those back, re-enable them
manually.

### Install flags

| Flag                       | Default              | Purpose |
| -------------------------- | -------------------- | ------- |
| `--serviceName <name>`     | `OctopusTimeService` | Override the SCM service name. Mostly useful for testing alongside an existing install. |
| `--executable <path>`      | current exe          | Path SCM should record as the service binary. |
| `--dependent <name>`       | (none)               | Name of an existing service that should depend on this one. May be specified multiple times. The named service is modified so SCM will start this service before it. |
| `--ntpCheckInterval <sec>` | `30`                 | How often the steady-state loop measures drift, in seconds. |
| `--monitorOnly`            | off                  | Switch flag. When set, the service skips the startup resync/lockdown sequence; it just takes one drift reading at start and then enters the steady-state measurement loop. Use this when you want to observe an agent without touching `W32Time`, `vmictimesync`, or the time-sync scheduled tasks. |

### Uninstall flags

| Flag                   | Default              | Purpose |
| ---------------------- | -------------------- | ------- |
| `--serviceName <name>` | `OctopusTimeService` | Service to uninstall. |
| `--dependent <name>`   | (recovered from registry) | Strip this service from the named target's dependency list before deleting. If omitted, the list recorded at install time is read back from the registry. |

### Running in console mode

For local diagnosis, the worker can be run as a console app without
installing it as a service:

```
OctopusTimeService.exe run
```

Drift measurements are written to stdout instead of the Event Log. Use
Ctrl-C to stop.

## Configuration

Install-time settings are persisted under

```
HKLM\SYSTEM\CurrentControlSet\Services\<serviceName>
```

| Value                     | Type      | Meaning |
| ------------------------- | --------- | ------- |
| `Dependents`              | REG_SZ    | Comma-separated list of services that depend on this one; written by `install --dependent ...` and read back by `uninstall` when `--dependent` is not passed. |
| `NtpCheckIntervalSeconds` | REG_DWORD | Drift-check interval in seconds. The service reads this on startup; missing or zero/negative values fall back to 30 seconds. |
| `MonitorOnly`             | REG_DWORD | `1` if `--monitorOnly` was passed at install, `0` otherwise. When `1`, the worker skips the startup sequence and just observes. |

The whole subkey is deleted on uninstall.

## Build

The project targets **.NET 10** on Windows and publishes as NativeAOT for
`win-x64`. Building requires the .NET 10 SDK and the Visual Studio
Build Tools (the AOT toolchain shells out to `vswhere.exe` to locate the
Windows SDK).

```
dotnet publish src\OctopusTimeService\OctopusTimeService.csproj -c Release -r win-x64
```

The output exe lands in
`src\OctopusTimeService\bin\Release\net10.0-windows\win-x64\publish\`.

## Tests

- `tests\TimeService.Tests` — unit tests for NTP packet parsing and drift
  calculation.
- `tests\TimeService.IntegrationTests` — exe-level tests, including
  install/uninstall round-trips. Tests that touch SCM are marked
  `[SkippableFact]` and skip automatically when the test runner is not
  elevated.

```
dotnet test
```
