# SQLJobVisualizer — Claude Code Guide

## Build & Run

```bash
dotnet build src/SQLJobVisualizer/SQLJobVisualizer.csproj
dotnet run --project src/SQLJobVisualizer
```

## Project Layout

```
src/SQLJobVisualizer/
  Models/             -- Plain data types (no dependencies)
    AppConfig.cs        -- Config POCO: Servers[], Jobs[] (JobConfig)
    CommandLogEntry.cs  -- Raw row from SQL query; Progress list attached for running jobs
    JobRow.cs           -- (ServerName, JobLabel, ShortJobLabel) record
    JobSlot.cs          -- One hour-cell in the week matrix
    JobExecution.cs     -- One bar in the day view
    SessionProgress.cs  -- spid/database/command/percent from sys.dm_exec_requests
  Services/           -- SQL access and parsing logic
    ConfigLoader.cs     -- Reads/writes config.json; creates defaults on first run
    ServerList.cs       -- Loads from ConfigLoader; exposes ServerNames, Jobs, AllRows
    JobParser.cs        -- Config-driven LIKE matching: job name → label
    CommandLogService.cs -- All SQL queries (range, running jobs, live progress)
  Controls/           -- Avalonia UserControls (week view, day view)
  Themes/             -- DarkTheme.axaml resource dictionary
  App.axaml           -- RequestedThemeVariant="Dark", merges DarkTheme.axaml
  MainWindow.axaml    -- TabControl with WeekMatrixControl and DayDetailControl
  config.json         -- Created on first run; edit to change servers and jobs
```

## Key Architecture Decisions

- **Avalonia 12.0.1**, .NET 8, no MVVM framework — code-behind only.
- **No ORM** — raw `Microsoft.Data.SqlClient` (`SqlConnection` / `SqlCommand` / `ExecuteReaderAsync`).
- **Canvas-based rendering** — both views build `Rectangle` objects in code-behind and add them to a `Canvas`. This enables `ToolTip.SetTip` on individual cells/bars without extra infrastructure.
- **Parallel server queries** — `Task.WhenAll` across all servers; per-server failures are non-fatal (console error + red dot in toolbar).
- **Config-driven** — servers and job definitions live in `config.json` next to the exe. `ServerList` and `JobParser` load from there at startup; no recompile needed to add a server or job.

## Configuration

`config.json` is created with defaults on first run at `AppContext.BaseDirectory`. Edit it to change servers or jobs:

```json
{
  "servers": ["sql1", "sql2", "sql3", "sql4", "sql5"],
  "jobs": [
    { "label": "DatabaseBackup - FULL",  "sqlPattern": "DatabaseBackup%FULL%",  "shortLabel": "Backup FULL"  },
    { "label": "DatabaseBackup - DIFF",  "sqlPattern": "DatabaseBackup%DIFF%",  "shortLabel": "Backup DIFF"  },
    { "label": "DatabaseBackup - LOG",   "sqlPattern": "DatabaseBackup%LOG%",   "shortLabel": "Backup LOG"   },
    { "label": "IndexOptimize",          "sqlPattern": "IndexOptimize%",        "shortLabel": "Index"        },
    { "label": "DatabaseIntegrityCheck", "sqlPattern": "DatabaseIntegrityCheck%","shortLabel": "Integrity"   }
  ]
}
```

`sqlPattern` is a SQL `LIKE` pattern matched against `sysjobs.name`. `label` is the canonical name used internally and in tooltips; `shortLabel` is shown in the row label panel.

## Row Ordering

Rows = jobs × servers (job type is **outer** group, server is **inner**). Order follows the `jobs` array in `config.json`.

```
DatabaseBackup - FULL  sql1
DatabaseBackup - FULL  sql2
...
DatabaseBackup - DIFF  sql1
...
DatabaseIntegrityCheck sql5
```

`ServerList.GetRowIndex(serverName, jobLabel)` converts a (server, job) pair to a 0-based row index. Row index drives both `Canvas.SetTop` positioning and the label panel order — keep them in sync.

## Data Source

Queries `msdb.dbo.sysjobhistory` (SQL Server Agent Job History) joined to `msdb.dbo.sysjobs`. Universally available on any SQL Server running Agent jobs; no additional tools required.

| sysjobhistory column | Notes |
|---|---|
| `j.name` (job name) | Matched via LIKE pattern from config (`JobParser`) |
| `h.run_date` | `int` `YYYYMMDD`; passed as `@FromDate`/`@ToDate` parameter |
| `h.run_time` | `int` `HHMMSS`; converted to `datetime` via `DATEADD` in SQL |
| `h.run_duration` | `int` `HHMMSS`; added to start time → `EndTime` |
| `h.run_status` | `1` = success → `ErrorNumber NULL`; other = failed → `ErrorNumber = run_status` |
| `h.step_id = 0` | Job-level outcome row only (not individual step rows) |

Running jobs are detected via `msdb.dbo.sysjobactivity` (`stop_execution_date IS NULL`).

## Live Progress (Running Jobs)

When a job is actively running (orange bar), the tooltip shows live session data from `sys.dm_exec_requests`:

```sql
SELECT CAST(r.session_id AS int),
       ISNULL(DB_NAME(r.database_id), N'') AS database_name,
       ISNULL(r.command, N'')              AS command,
       ISNULL(CAST(r.percent_complete AS decimal(5,1)), 0) AS percent_complete
FROM sys.dm_exec_requests  r
    INNER JOIN sys.dm_exec_sessions s ON s.session_id = r.session_id
WHERE s.program_name LIKE N'SQLAgent%'
  AND (   r.command LIKE N'BACKUP%'
       OR r.command LIKE N'DBCC%'
       OR r.command LIKE N'RESTORE VERIFYONLY%'
       OR r.command LIKE N'RESTORE HEADERONLY%'
       OR r.command = N'ALTER INDEX'
       OR r.command = N'UPDATE STATISTICS')
ORDER BY r.session_id;
```

Requires `VIEW SERVER STATE` on the monitored servers. The progress query uses a **separate connection** from the running-jobs query (connection reuse caused silent failures). `session_id` is `smallint` in the DMV — cast to `int` before reading via `GetInt32`.

## Week View Canvas Geometry

- `CellW = 12 px` per hour, `CellH = 24 px` per row, `HeaderH = 42 px`
- Canvas width: `168 × 12 = 2016 px`; height: `HeaderH + rows × CellH`
- A multi-hour job creates one `JobSlot` per covered hour so overlaps render correctly
- Day separators at every 288 px (24 × 12), group separators between job types

## Day View Canvas Geometry

- `CanvasW = 1440 px` (1 px per minute), `HeaderH = 30 px`, `RowH = 28 px`
- Bar x-position = `StartTime.TimeOfDay.TotalMinutes`, minimum bar width = 3 px

## Avalonia Pitfalls

- **`OnLoaded` fires on every tab switch** — subscribe button/timer events in the constructor, not `OnLoaded`. Only call `LoadAsync()` in `OnLoaded`.
- **Canvas vertical centering** — a `Canvas` inside a container with an explicit `Height` will be centred. Fix with `VerticalAlignment="Top"` on the Canvas.
- **Capture `_weekStart` before `await`** — navigation state can change while an async load is in flight; capture the value at the top of `LoadAsync` before any `await`.

## Theme Colors

Defined in `Themes/DarkTheme.axaml` as `DynamicResource` keys:

| Key | Hex | Used for |
|---|---|---|
| `BackgroundBrush` | `#1A1D23` | Main background |
| `BackgroundDarkBrush` | `#15171C` | Toolbar, label panel |
| `ForegroundBrush` | `#E4E6EB` | Primary text |
| `BorderBrush` | `#3A3D45` | Separators |
| `JobSuccessBrush` | `#2ECC71` | Successful job cells |
| `JobFailedBrush` | `#E74C3C` | Failed job cells |
| `JobRunningBrush` | `#F39C12` | Still-running jobs |
