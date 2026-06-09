using System.Collections.Concurrent;
using Microsoft.Data.SqlClient;
using SQLJobVisualizer.Models;

namespace SQLJobVisualizer.Services;

public sealed class CommandLogService
{
    public async Task<(List<CommandLogEntry> Entries, IReadOnlyList<string> FailedServers)>
        LoadWeekAsync(DateTime weekStart, CancellationToken ct = default) =>
        await QueryRangeAsync(
            int.Parse(weekStart.ToString("yyyyMMdd")),
            int.Parse(weekStart.AddDays(6).ToString("yyyyMMdd")),
            ct);

    public async Task<(List<CommandLogEntry> Entries, IReadOnlyList<string> FailedServers)>
        LoadDayAsync(DateTime day, CancellationToken ct = default) =>
        await QueryRangeAsync(
            int.Parse(day.Date.ToString("yyyyMMdd")),
            int.Parse(day.Date.ToString("yyyyMMdd")),
            ct);

    public async Task<(List<CommandLogEntry> Entries, IReadOnlyList<string> FailedServers)>
        LoadRunningAsync(CancellationToken ct = default)
    {
        var failed  = new ConcurrentBag<(string Server, string Error)>();
        var tasks   = ServerList.ServerNames
            .Select(s => QueryRunningAsync(s, failed, ct))
            .ToArray();
        var results = await Task.WhenAll(tasks);
        return (results.SelectMany(r => r).ToList(),
                failed.Select(f => $"{f.Server}|{f.Error}").ToList());
    }

    private static async Task<List<CommandLogEntry>> QueryRunningAsync(
        string serverName, ConcurrentBag<(string, string)> failed, CancellationToken ct)
    {
        var list = new List<CommandLogEntry>();
        try
        {
            await using var conn = new SqlConnection(ServerList.GetConnectionString(serverName));
            await conn.OpenAsync(ct);

            await using var cmd = conn.CreateCommand();
            cmd.CommandTimeout = 10;
            cmd.CommandText    = BuildRunningQuery(cmd);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                list.Add(new CommandLogEntry
                {
                    ServerName  = serverName,
                    CommandType = reader.GetString(0),
                    Command     = "",
                    StartTime   = reader.GetDateTime(1),
                    EndTime     = null,
                    ErrorNumber = null,
                });
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            failed.Add((serverName, ex.Message));
        }

        if (list.Count > 0)
        {
            // Fresh connection for progress so there's no shared connection state
            var progress = await QueryProgressAsync(serverName, ct);
            foreach (var entry in list)
                entry.Progress = progress;
        }

        return list;
    }

    private static async Task<List<SessionProgress>> QueryProgressAsync(
        string serverName, CancellationToken ct)
    {
        var list = new List<SessionProgress>();
        try
        {
            await using var conn = new SqlConnection(ServerList.GetConnectionString(serverName));
            await conn.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandTimeout = 10;
            cmd.CommandText    = ProgressQuery;
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                list.Add(new SessionProgress
                {
                    SessionId       = reader.GetInt32(0),
                    DatabaseName    = reader.GetString(1),
                    Command         = reader.GetString(2),
                    PercentComplete = (double)reader.GetDecimal(3),
                });
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Surface the error in the tooltip so it's visible without a console
            list.Add(new SessionProgress { Command = $"progress error: {ex.Message}" });
        }
        return list;
    }

    private const string ProgressQuery = """
        SELECT
            CAST(r.session_id AS int),
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
        """;

    private static async Task<(List<CommandLogEntry>, IReadOnlyList<string>)>
        QueryRangeAsync(int fromDate, int toDate, CancellationToken ct)
    {
        var failed = new ConcurrentBag<(string Server, string Error)>();
        var tasks  = ServerList.ServerNames
            .Select(s => QueryServerAsync(s, fromDate, toDate, failed, ct))
            .ToArray();
        var results = await Task.WhenAll(tasks);
        return (results.SelectMany(r => r).ToList(),
                failed.Select(f => $"{f.Server}|{f.Error}").ToList());
    }

    private static async Task<List<CommandLogEntry>> QueryServerAsync(
        string serverName, int fromDate, int toDate,
        ConcurrentBag<(string, string)> failed, CancellationToken ct)
    {
        var list = new List<CommandLogEntry>();
        try
        {
            await using var conn = new SqlConnection(ServerList.GetConnectionString(serverName));
            await conn.OpenAsync(ct);

            await using var cmd = conn.CreateCommand();
            cmd.CommandTimeout = 30;
            cmd.CommandText    = BuildRangedQuery(cmd);
            cmd.Parameters.AddWithValue("@FromDate", fromDate);
            cmd.Parameters.AddWithValue("@ToDate",   toDate);

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                list.Add(new CommandLogEntry
                {
                    ServerName  = serverName,
                    CommandType = reader.GetString(0),
                    Command     = "",
                    StartTime   = reader.GetDateTime(2),
                    EndTime     = reader.IsDBNull(3) ? null : reader.GetDateTime(3),
                    ErrorNumber = reader.IsDBNull(4) ? null : reader.GetInt32(4),
                });
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            failed.Add((serverName, ex.Message));
            await Console.Error.WriteLineAsync($"[{serverName}] {ex.Message}");
        }
        return list;
    }

    private static string BuildJobFilter(SqlCommand cmd)
    {
        var jobs = ServerList.Jobs;
        if (jobs.Length == 0) return "1=0";
        var parts = new string[jobs.Length];
        for (int i = 0; i < jobs.Length; i++)
        {
            var p = $"@jp{i}";
            cmd.Parameters.AddWithValue(p, jobs[i].SqlPattern);
            parts[i] = $"j.name LIKE {p}";
        }
        return $"({string.Join(" OR ", parts)})";
    }

    private static string BuildRangedQuery(SqlCommand cmd)
    {
        var filter = BuildJobFilter(cmd);
        return $"""
            SELECT
                j.name                                                     AS CommandType,
                ''                                                         AS Command,
                DATEADD(second,
                      h.run_time  % 100
                    + (h.run_time  / 100 % 100) * 60
                    + (h.run_time  / 10000)     * 3600,
                    CONVERT(datetime, CONVERT(char(8), h.run_date))
                )                                                          AS StartTime,
                DATEADD(second,
                      h.run_duration % 100
                    + (h.run_duration / 100 % 100) * 60
                    + (h.run_duration / 10000)     * 3600,
                    DATEADD(second,
                          h.run_time  % 100
                        + (h.run_time  / 100 % 100) * 60
                        + (h.run_time  / 10000)     * 3600,
                        CONVERT(datetime, CONVERT(char(8), h.run_date))
                    )
                )                                                          AS EndTime,
                CASE h.run_status WHEN 1 THEN NULL ELSE h.run_status END   AS ErrorNumber
            FROM msdb.dbo.sysjobhistory h
                INNER JOIN msdb.dbo.sysjobs j ON h.job_id = j.job_id
            WHERE h.step_id = 0
              AND h.run_date BETWEEN @FromDate AND @ToDate
              AND {filter}
            ORDER BY h.run_date, h.run_time;
            """;
    }

    private static string BuildRunningQuery(SqlCommand cmd)
    {
        var filter = BuildJobFilter(cmd);
        return $"""
            SELECT j.name                       AS CommandType,
                   MAX(ja.start_execution_date) AS StartTime
            FROM msdb.dbo.sysjobactivity ja
                INNER JOIN msdb.dbo.sysjobs j ON ja.job_id = j.job_id
            WHERE ja.start_execution_date IS NOT NULL
              AND ja.stop_execution_date  IS NULL
              AND ja.start_execution_date >= CAST(GETDATE() AS date)
              AND {filter}
            GROUP BY j.name;
            """;
    }
}
