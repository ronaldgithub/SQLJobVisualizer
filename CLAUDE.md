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
  Services/           -- SQL access and parsing logic
  Controls/           -- Avalonia UserControls (week view, day view)
  Themes/             -- DarkTheme.axaml resource dictionary
  App.axaml           -- RequestedThemeVariant="Dark", merges DarkTheme.axaml
  MainWindow.axaml    -- TabControl with WeekMatrixControl and DayDetailControl
```

## Key Architecture Decisions

- **Avalonia 12.0.1**, .NET 8, no MVVM framework — code-behind only.
- **No ORM** — raw `Microsoft.Data.SqlClient` (`SqlConnection` / `SqlCommand` / `ExecuteReaderAsync`).
- **Canvas-based rendering** — both views build `Rectangle` objects in code-behind and add them to a `Canvas`. This enables `ToolTip.SetTip` on individual cells/bars without extra infrastructure.
- **Parallel server queries** — `Task.WhenAll` across all 5 servers; per-server failures are non-fatal (console error + red dot in toolbar).

## Servers & Connection Strings

Hardcoded in `Services/ServerList.cs`. To add or rename servers, change the `ServerNames` array. The connection string uses Windows Integrated Security and targets the `master` database (where Ola Hallengren installs CommandLog by default).

## Row Ordering

25 rows total: job type is the **outer** group, server is **inner**.

```
DatabaseBackup - FULL  sql1
DatabaseBackup - FULL  sql2
...
DatabaseBackup - DIFF  sql1
...
IntegrityCheck - ALL   sql5
```

`ServerList.GetRowIndex(serverName, jobLabel)` converts a (server, job) pair to a 0-based row index. Row index drives both `Canvas.SetTop` positioning and the label panel order — keep them in sync.

## Data Source

Queries `msdb.dbo.sysjobhistory` (SQL Server Agent Job History) joined to `msdb.dbo.sysjobs`. This is universally available on any SQL Server running Agent jobs, with no additional tools required.

| sysjobhistory column | Notes |
|---|---|
| `j.name` (job name) | Matched directly to job label in `JobParser` |
| `h.run_date` | `int` `YYYYMMDD`; passed as `@FromDate`/`@ToDate` parameter |
| `h.run_time` | `int` `HHMMSS`; converted to `datetime` via `DATEADD` in SQL |
| `h.run_duration` | `int` `HHMMSS`; added to start time → `EndTime` |
| `h.run_status` | `1` = success → `ErrorNumber NULL`; other = failed → `ErrorNumber = run_status` |
| `h.step_id = 0` | Job-level outcome row only (not individual step rows) |

Agent job names matched (`sysjobs.name`):
`DatabaseBackup - FULL`, `DatabaseBackup - DIFF`, `DatabaseBackup - LOG`, `IndexOptimize`, `DatabaseIntegrityCheck`

`JobParser` also handles the legacy `dbo.CommandLog` `CommandType` values as a fallback.

## Week View Canvas Geometry

- `CellW = 12 px` per hour, `CellH = 24 px` per row, `HeaderH = 42 px`
- Canvas size: `2016 × 642 px` (168 cols × 25 rows + header)
- A multi-hour job creates one `JobSlot` per covered hour so overlaps render correctly
- Day separators at every 288 px (24 × 12), group separators at every 120 px (5 × 24)

## Day View Canvas Geometry

- `CanvasW = 1440 px` (1 px per minute), `HeaderH = 30 px`, `RowH = 28 px`
- Bar x-position = `StartTime.TimeOfDay.TotalMinutes`, minimum bar width = 3 px

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
| `JobRunningBrush` | `#F39C12` | Reserved for still-running jobs |
