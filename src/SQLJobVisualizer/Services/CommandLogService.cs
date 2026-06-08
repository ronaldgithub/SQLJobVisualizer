using System.Collections.Concurrent;
using Microsoft.Data.SqlClient;
using SQLJobVisualizer.Models;

namespace SQLJobVisualizer.Services;

public sealed class CommandLogService
{
    public async Task<(List<CommandLogEntry> Entries, IReadOnlyList<string> FailedServers)>
        LoadWeekAsync(DateTime weekStart, CancellationToken ct = default) =>
        await QueryRangeAsync(weekStart, weekStart.AddDays(7), ct);

    public async Task<(List<CommandLogEntry> Entries, IReadOnlyList<string> FailedServers)>
        LoadDayAsync(DateTime day, CancellationToken ct = default) =>
        await QueryRangeAsync(day.Date, day.Date.AddDays(1), ct);

    private static async Task<(List<CommandLogEntry>, IReadOnlyList<string>)>
        QueryRangeAsync(DateTime from, DateTime to, CancellationToken ct)
    {
        var failed = new ConcurrentBag<string>();
        var tasks  = ServerList.ServerNames
            .Select(s => QueryServerAsync(s, from, to, failed, ct))
            .ToArray();
        var results = await Task.WhenAll(tasks);
        return (results.SelectMany(r => r).ToList(), failed.ToList());
    }

    private static async Task<List<CommandLogEntry>> QueryServerAsync(
        string serverName, DateTime from, DateTime to,
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
            cmd.Parameters.AddWithValue("@From", from);
            cmd.Parameters.AddWithValue("@To",   to);

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                list.Add(new CommandLogEntry
                {
                    ServerName  = serverName,
                    CommandType = reader.GetString(0),
                    Command     = reader.IsDBNull(1) ? "" : reader.GetString(1),
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

    private const string SqlQuery = """
        SELECT
            CommandType,
            Command,
            StartTime,
            EndTime,
            ErrorNumber
        FROM dbo.CommandLog
        WHERE StartTime >= @From
          AND StartTime <  @To
          AND CommandType IN (
              'DatabaseBackup',
              'IndexOptimize',
              'DatabaseIntegrityCheck'
          )
        ORDER BY StartTime;
        """;
}
