using System.Collections.Concurrent;
using Microsoft.Data.SqlClient;
using SQLJobVisualizer.Models;

namespace SQLJobVisualizer.Services;

public sealed class CommandLogService
{
    public async Task<(List<CommandLogEntry> Entries, IReadOnlyList<string> FailedServers)>
        LoadWeekAsync(DateTime weekStart, bool includeAllJobs = false, CancellationToken ct = default) =>
        await QueryRangeAsync(
            int.Parse(weekStart.ToString("yyyyMMdd")),
            int.Parse(weekStart.AddDays(6).ToString("yyyyMMdd")),
            includeAllJobs,
            ct);

    public async Task<(List<CommandLogEntry> Entries, IReadOnlyList<string> FailedServers)>
        LoadDayAsync(DateTime day, bool includeAllJobs = false, CancellationToken ct = default) =>
        await QueryRangeAsync(
            int.Parse(day.Date.ToString("yyyyMMdd")),
            int.Parse(day.Date.ToString("yyyyMMdd")),
            includeAllJobs,
            ct);

    public async Task RescheduleAsync(string serverName, int scheduleId, TimeSpan newTime,
                                      CancellationToken ct = default)
    {
        int timeVal = newTime.Hours * 10000 + newTime.Minutes * 100;
        await using var conn = new SqlConnection(ServerList.GetConnectionString(serverName));
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText    = "msdb.dbo.sp_update_schedule";
        cmd.CommandType    = System.Data.CommandType.StoredProcedure;
        cmd.CommandTimeout = 15;
        cmd.Parameters.AddWithValue("@schedule_id",       scheduleId);
        cmd.Parameters.AddWithValue("@active_start_time", timeVal);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<List<ScheduledRun>> LoadScheduledAsync(
        DateTime day, bool includeAllJobs = false, CancellationToken ct = default)
    {
        var tasks   = ServerList.ServerNames
            .Select(s => QueryScheduledAsync(s, day, includeAllJobs, ct))
            .ToArray();
        var results = await Task.WhenAll(tasks);
        return results.SelectMany(r => r).ToList();
    }

    private static async Task<List<ScheduledRun>> QueryScheduledAsync(
        string serverName, DateTime day, bool includeAllJobs, CancellationToken ct)
    {
        var list = new List<ScheduledRun>();
        try
        {
            await using var conn = new SqlConnection(ServerList.GetConnectionString(serverName));
            await conn.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandTimeout = 10;
            cmd.Parameters.AddWithValue("@Day", day.Date);
            cmd.CommandText = BuildScheduledQuery(cmd, includeAllJobs);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var jobName       = reader.GetString(0);
                var scheduledTime = reader.GetDateTime(1);
                var scheduleId    = reader.IsDBNull(2) ? 0 : reader.GetInt32(2);
                var jobLabel      = JobParser.ParseJobLabel(jobName, "", includeAllJobs);
                if (jobLabel is null) continue;
                list.Add(new ScheduledRun
                {
                    ServerName    = serverName,
                    JobLabel      = jobLabel,
                    RowIndex      = -1,
                    ScheduledTime = scheduledTime,
                    ScheduleId    = scheduleId,
                });
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await Console.Error.WriteLineAsync($"[scheduled:{serverName}] {ex.Message}");
        }
        return list;
    }

    private static string BuildScheduledQuery(SqlCommand cmd, bool includeAllJobs)
    {
        var filter = BuildJobFilter(cmd, includeAllJobs);
        return $"""
            SELECT j.name,
                   ja.next_scheduled_run_date,
                   (SELECT TOP 1 s.schedule_id
                    FROM msdb.dbo.sysjobschedules js2
                        INNER JOIN msdb.dbo.sysschedules s ON js2.schedule_id = s.schedule_id
                    WHERE js2.job_id = j.job_id
                      AND s.enabled = 1
                    ORDER BY ABS(CAST(s.active_start_time AS bigint) -
                                 CAST(DATEPART(hour,   ja.next_scheduled_run_date) * 10000
                                    + DATEPART(minute, ja.next_scheduled_run_date) * 100
                                    + DATEPART(second, ja.next_scheduled_run_date) AS bigint))
                   ) AS schedule_id
            FROM msdb.dbo.sysjobactivity ja
                INNER JOIN msdb.dbo.sysjobs j ON ja.job_id = j.job_id
            WHERE ja.session_id = (SELECT MAX(session_id) FROM msdb.dbo.syssessions)
              AND ja.next_scheduled_run_date IS NOT NULL
              AND CAST(ja.next_scheduled_run_date AS date) = @Day
              AND {filter}
            ORDER BY ja.next_scheduled_run_date;
            """;
    }

    public async Task<(List<CommandLogEntry> Entries, IReadOnlyList<string> FailedServers)>
        LoadRunningAsync(bool includeAllJobs = false, CancellationToken ct = default)
    {
        var failed  = new ConcurrentBag<(string Server, string Error)>();
        var tasks   = ServerList.ServerNames
            .Select(s => QueryRunningAsync(s, includeAllJobs, failed, ct))
            .ToArray();
        var results = await Task.WhenAll(tasks);
        return (results.SelectMany(r => r).ToList(),
                failed.Select(f => $"{f.Server}|{f.Error}").ToList());
    }

    private static async Task<List<CommandLogEntry>> QueryRunningAsync(
        string serverName, bool includeAllJobs,
        ConcurrentBag<(string, string)> failed, CancellationToken ct)
    {
        var list = new List<CommandLogEntry>();
        try
        {
            await using var conn = new SqlConnection(ServerList.GetConnectionString(serverName));
            await conn.OpenAsync(ct);

            await using var cmd = conn.CreateCommand();
            cmd.CommandTimeout = 10;
            cmd.CommandText    = BuildRunningQuery(cmd, includeAllJobs);
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
               OR r.command = N'REBUILD INDEX'
               OR r.command = N'UPDATE STATISTICS')
        ORDER BY r.session_id;
        """;

    private static async Task<(List<CommandLogEntry>, IReadOnlyList<string>)>
        QueryRangeAsync(int fromDate, int toDate, bool includeAllJobs, CancellationToken ct)
    {
        var failed = new ConcurrentBag<(string Server, string Error)>();
        var tasks  = ServerList.ServerNames
            .Select(s => QueryServerAsync(s, fromDate, toDate, includeAllJobs, failed, ct))
            .ToArray();
        var results = await Task.WhenAll(tasks);
        return (results.SelectMany(r => r).ToList(),
                failed.Select(f => $"{f.Server}|{f.Error}").ToList());
    }

    private static async Task<List<CommandLogEntry>> QueryServerAsync(
        string serverName, int fromDate, int toDate,
        bool includeAllJobs, ConcurrentBag<(string, string)> failed, CancellationToken ct)
    {
        var list = new List<CommandLogEntry>();
        try
        {
            await using var conn = new SqlConnection(ServerList.GetConnectionString(serverName));
            await conn.OpenAsync(ct);

            await using var cmd = conn.CreateCommand();
            cmd.CommandTimeout = 30;
            cmd.CommandText    = BuildRangedQuery(cmd, includeAllJobs);
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

    public async Task<IReadOnlyDictionary<(string Server, string JobLabel), string>>
        LoadJobStepCommandsAsync(bool includeAllJobs = false, CancellationToken ct = default)
    {
        var tasks = ServerList.ServerNames
            .Select(s => QueryJobStepsAsync(s, includeAllJobs, ct))
            .ToArray();
        var results = await Task.WhenAll(tasks);
        return results
            .SelectMany(r => r)
            .ToDictionary(r => (r.Server, r.JobLabel), r => r.Command);
    }

    private static async Task<List<(string Server, string JobLabel, string Command)>> QueryJobStepsAsync(
        string serverName, bool includeAllJobs, CancellationToken ct)
    {
        var list = new List<(string, string, string)>();
        try
        {
            await using var conn = new SqlConnection(ServerList.GetConnectionString(serverName));
            await conn.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandTimeout = 10;
            cmd.CommandText    = BuildJobStepQuery(cmd, includeAllJobs);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var jobName  = reader.GetString(0);
                var stepCmd  = reader.GetString(1);
                var jobLabel = JobParser.ParseJobLabel(jobName, "", includeAllJobs);
                if (jobLabel is not null)
                    list.Add((serverName, jobLabel, stepCmd));
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await Console.Error.WriteLineAsync($"[steps:{serverName}] {ex.Message}");
        }
        return list;
    }

    private static string BuildJobStepQuery(SqlCommand cmd, bool includeAllJobs)
    {
        var filter = BuildJobFilter(cmd, includeAllJobs);
        return $"""
            SELECT j.name, js.command
            FROM msdb.dbo.sysjobs j
                INNER JOIN msdb.dbo.sysjobsteps js ON j.job_id = js.job_id
            WHERE js.step_id = 1
              AND {filter}
            """;
    }

    private static string BuildJobFilter(SqlCommand cmd, bool includeAllJobs)
    {
        if (includeAllJobs) return "1=1";

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

    private static string BuildRangedQuery(SqlCommand cmd, bool includeAllJobs)
    {
        var filter = BuildJobFilter(cmd, includeAllJobs);
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

    private static string BuildRunningQuery(SqlCommand cmd, bool includeAllJobs)
    {
        var filter = BuildJobFilter(cmd, includeAllJobs);
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
