# SQL Job Visualizer

A dark-mode desktop application for visualizing SQL Server Agent maintenance job history across multiple servers. Designed to make it immediately obvious whether jobs ran successfully, how long they took, and whether jobs on different servers overlap.

## TODO

- [ ] **Failed job error message** — fetch the error text from `sysjobhistory` step rows (`step_id > 0`) and show it in the tooltip for failed bars
- [ ] **Estimated time to completion** — for running jobs, calculate `elapsed / percent_complete × (100 - percent_complete)` and show `ETA: ~X min` in the tooltip
- [ ] **Duration variance highlighting** — compare current run duration against historical average; colour a running bar amber if it exceeds 1.5× the average
- [ ] **Missed job indicator** — show a faint grey cell when a job ran yesterday but not today (past its expected window)
- [ ] **Database list in tooltip** — for backup jobs, parse the database name from `sysjobhistory` step messages and list them in the completed-job tooltip
- [ ] **Backup coverage tab** — third tab with one row per database: last FULL backup age, last LOG backup age, highlighted red when older than a configurable threshold

## Features

- **Week Overview** — 7-day × 24-hour grid showing every maintenance job execution per server. Green = success, red = failed, orange = currently running. Navigate with Prev/Next week buttons; auto-scrolls to the current day.
- **Day Detail** — Pick any date with a calendar picker; see proportional job bars for the full 24-hour period with exact start/end times on hover. Auto-refreshes every 30 seconds when viewing today.
- **Multiple servers in parallel** — Queries all configured servers simultaneously. If one server is unreachable the others still load; a colour-coded dot in the toolbar shows each server's status.
- **Hover tooltips** — Every cell and bar shows server name, job type, start time, end time, and duration. For a **running job** the tooltip also shows live progress: spid, database name, command, and percent complete.
- **Config file** — Servers and job definitions are read from `config.json` next to the exe. No recompile needed to add servers or jobs.

## Jobs Visualized

Out of the box the app is configured for [Ola Hallengren's](https://ola.hallengren.com/) maintenance jobs, matched by SQL Agent job name via LIKE patterns:

| Row label | SQL Agent job name pattern |
|---|---|
| Backup FULL | `DatabaseBackup%FULL%` |
| Backup DIFF | `DatabaseBackup%DIFF%` |
| Backup LOG | `DatabaseBackup%LOG%` |
| IndexOptimize | `IndexOptimize%` |
| IntegrityCheck | `DatabaseIntegrityCheck%` |

Any SQL Server Agent job can be added by editing `config.json`.

## Requirements

- Windows (.NET 8, Windows Integrated Security)
- .NET 8 Runtime (or SDK to build from source)
- SQL Server with SQL Server Agent enabled
- Login used by the app requires:
  - `db_datareader` on `msdb` (for job history and activity)
  - `VIEW SERVER STATE` (for live progress via `sys.dm_exec_requests`)

## Getting Started

1. **Clone** the repository.

2. **Build and run:**

   ```bash
   dotnet run --project src/SQLJobVisualizer
   ```

   On first run `config.json` is created next to the exe with default servers `sql1`–`sql5` and the five Ola Hallengren job patterns.

3. **Configure** — open `config.json` and update `servers` and `jobs` to match your environment:

   ```json
   {
     "servers": ["myserver1", "myserver2"],
     "jobs": [
       { "label": "DatabaseBackup - FULL", "sqlPattern": "DatabaseBackup%FULL%", "shortLabel": "Backup FULL" },
       { "label": "DatabaseBackup - LOG",  "sqlPattern": "DatabaseBackup%LOG%",  "shortLabel": "Backup LOG"  }
     ]
   }
   ```

   - `servers` — hostnames or IP addresses; Windows auth is used, no credentials in the file.
   - `sqlPattern` — SQL `LIKE` pattern matched against `msdb.dbo.sysjobs.name`.
   - `label` — display name used in tooltips.
   - `shortLabel` — abbreviated name shown in the row label panel.

4. Restart the app after editing `config.json`.

## Solution Structure

```
SQLJobVisualizer.sln
src/
  SQLJobVisualizer/
    Models/           AppConfig, CommandLogEntry, JobRow, JobSlot, JobExecution, SessionProgress
    Services/         ConfigLoader, ServerList, JobParser, CommandLogService
    Controls/         WeekMatrixControl, DayDetailControl
    Themes/           DarkTheme.axaml
config.json           Created on first run; edit to configure servers and jobs
```

## Technology

| | |
|---|---|
| UI framework | [Avalonia UI](https://avaloniaui.net/) 12.0.1 |
| Target framework | .NET 8 / Windows |
| SQL client | Microsoft.Data.SqlClient 5.2.2 |
| Architecture | Code-behind, no MVVM framework |
| Theme | Fluent Dark + custom resource dictionary |
