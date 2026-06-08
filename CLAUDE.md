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

Queries `dbo.CommandLog` (Ola Hallengren standard schema):

| Column | Used for |
|---|---|
| `CommandType` | Job type: `DatabaseBackup`, `IndexOptimize`, `DatabaseIntegrityCheck` |
| `Command` | Parsed by `JobParser` regex to extract `@BackupType='FULL\|DIFF\|LOG'` |
| `StartTime` | Position on the time axis |
| `EndTime` | Duration; `NULL` = still running (skipped in day view) |
| `ErrorNumber` | `NULL` = success (green), any value = failed (red) |

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
