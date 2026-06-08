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

    private static async Task<(List<CommandLogEntry>, IReadOnlyList<string>)>
        QueryRangeAsync(int fromDate, int toDate, CancellationToken ct)
    {
        var failed = new ConcurrentBag<string>();
        var tasks  = ServerList.ServerNames
            .Select(s => QueryServerAsync(s, fromDate, toDate, failed, ct))
            .ToArray();
        var results = await Task.WhenAll(tasks);
        return (results.SelectMany(r => r).ToList(), failed.ToList());
    }

    private static async Task<List<CommandLogEntry>> QueryServerAsync(
        string serverName, int fromDate, int toDate,
        ConcurrentBag<string> failed, CancellationToken ct)
    {
        var list = new List<CommandLogEntry>();
        try
        {
            await using var conn = new SqlConnection(ServerList.GetConnectionString(serverName));
            await conn.OpenAsync(ct);

            await using var cmd = conn.CreateCommand();
            cmd.CommandText    = SqlQuery;
            cmd.CommandTimeout = 30;
            cmd.Parameters.AddWithValue("@FromDate", fromDate);
            cmd.Parameters.AddWithValue("@ToDate",   toDate);

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                list.Add(new CommandLogEntry
                {
                    ServerName  = serverName,
                    CommandType = reader.GetString(0),   // agent job name
                    Command     = "",
                    StartTime   = reader.GetDateTime(2),
                    EndTime     = reader.IsDBNull(3) ? null : reader.GetDateTime(3),
                    ErrorNumber = reader.IsDBNull(4) ? null : reader.GetInt32(4),
                });
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            failed.Add(serverName);
            await Console.Error.WriteLineAsync($"[{serverName}] {ex.Message}");
        }
        return list;
    }

    // Queries msdb.dbo.sysjobhistory (SQL Server Agent Job History).
    // run_date / run_time / run_duration are stored as HHMMSS integers;
    // the DATEADD expressions convert them to proper datetime values server-side.
    private const string SqlQuery = """
        SELECT
            j.name                                                     AS CommandType,
            ''                                                         AS Command,
            DATEADD(second,
                  h.run_time  % 100
                + (h.run_time  / 100 % 100) * 60
                + (h.run_time  / 10000)     * 3600,
                CONVERT(date, CONVERT(char(8), h.run_date))
            )                                                          AS StartTime,
            DATEADD(second,
                  h.run_duration % 100
                + (h.run_duration / 100 % 100) * 60
                + (h.run_duration / 10000)     * 3600,
                DATEADD(second,
                      h.run_time  % 100
                    + (h.run_time  / 100 % 100) * 60
                    + (h.run_time  / 10000)     * 3600,
                    CONVERT(date, CONVERT(char(8), h.run_date))
                )
            )                                                          AS EndTime,
            CASE h.run_status WHEN 1 THEN NULL ELSE h.run_status END   AS ErrorNumber
        FROM msdb.dbo.sysjobhistory h
            INNER JOIN msdb.dbo.sysjobs j ON h.job_id = j.job_id
        WHERE h.step_id = 0
          AND h.run_date BETWEEN @FromDate AND @ToDate
          AND j.name IN (
              N'DatabaseBackup - FULL',
              N'DatabaseBackup - DIFF',
              N'DatabaseBackup - LOG',
              N'IndexOptimize',
              N'DatabaseIntegrityCheck'
          )
        ORDER BY h.run_date, h.run_time;
        """;
}
