# SQL Job Visualizer

A dark-mode desktop application for visualizing [Ola Hallengren](https://ola.hallengren.com/) SQL Server maintenance job history across multiple servers. Designed to make it immediately obvious whether jobs ran successfully, how long they took, and whether jobs on different servers overlap.

![Dark mode Avalonia app showing a 7-day job matrix]

## Features

- **Week Overview** — 7-day × 24-hour grid showing every maintenance job execution per server. Green = success, red = failed. Overlapping jobs across servers appear in adjacent rows, making scheduling conflicts visible at a glance.
- **Day Detail** — Pick any date with a calendar picker; see proportional job bars for the full 24-hour period with exact start/end times on hover.
- **5 servers in parallel** — Queries sql1–sql5 simultaneously. If one server is unreachable the others still load; a colour-coded dot in the toolbar shows each server's status.
- **Hover tooltips** — Every cell and bar shows server name, job type, start time, end time, and duration.

## Jobs Visualized

| Row label | Ola Hallengren job |
|---|---|
| Backup FULL | `DatabaseBackup` `@BackupType='FULL'` |
| Backup DIFF | `DatabaseBackup` `@BackupType='DIFF'` |
| Backup LOG | `DatabaseBackup` `@BackupType='LOG'` |
| IndexOptimize | `IndexOptimize` |
| IntegrityCheck | `DatabaseIntegrityCheck` |

Each job type has one row per server (5 servers × 5 job types = 25 rows total).

## Requirements

- Windows (Windows auth to SQL Server)
- .NET 8 SDK
- SQL Server with [Ola Hallengren's maintenance solution](https://ola.hallengren.com/sql-server-index-and-statistics-maintenance.html) installed (`dbo.CommandLog` table in `master`)

## Getting Started

1. **Clone** the repository.

2. **Configure servers** — open `src/SQLJobVisualizer/Services/ServerList.cs` and update `ServerNames` with your actual server names/addresses:

   ```csharp
   public static readonly string[] ServerNames =
       ["sql1", "sql2", "sql3", "sql4", "sql5"];
   ```

   The connection string uses Windows Integrated Security and connects to `master`. Adjust `GetConnectionString` if your CommandLog is in a different database or you need SQL auth.

3. **Build and run:**

   ```bash
   dotnet run --project src/SQLJobVisualizer
   ```

## Solution Structure

```
SQLJobVisualizer.sln
src/
  SQLJobVisualizer/
    Models/           CommandLogEntry, JobRow, JobSlot, JobExecution
    Services/         ServerList, JobParser, CommandLogService
    Controls/         WeekMatrixControl, DayDetailControl
    Themes/           DarkTheme.axaml
```

## Technology

| | |
|---|---|
| UI framework | [Avalonia UI](https://avaloniaui.net/) 12.0.1 |
| Target framework | .NET 8 / Windows |
| SQL client | Microsoft.Data.SqlClient 5.2.2 |
| Architecture | Code-behind, no MVVM framework |
| Theme | Fluent Dark + custom resource dictionary |
