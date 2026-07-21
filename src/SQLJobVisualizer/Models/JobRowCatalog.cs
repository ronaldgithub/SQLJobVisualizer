using SQLJobVisualizer.Services;

namespace SQLJobVisualizer.Models;

public sealed class JobRowCatalog
{
    private readonly Dictionary<(string ServerName, string JobLabel), int> _rowIndexes;

    public JobRowCatalog(IReadOnlyList<JobRow> rows)
    {
        AllRows = rows;
        JobLabels = [.. rows.Select(r => r.JobLabel).Distinct()];
        _rowIndexes = rows
            .Select((row, index) => (row, index))
            .ToDictionary(x => (x.row.ServerName, x.row.JobLabel), x => x.index);
    }

    public IReadOnlyList<JobRow> AllRows { get; }

    public IReadOnlyList<string> JobLabels { get; }

    public int GetRowIndex(string serverName, string jobLabel) =>
        _rowIndexes.TryGetValue((serverName, jobLabel), out var index) ? index : -1;

    public IEnumerable<(int StartIndex, int RowCount)> GetGroups()
    {
        string? currentLabel = null;
        int startIndex = 0;
        int rowCount = 0;

        for (int i = 0; i < AllRows.Count; i++)
        {
            var label = AllRows[i].JobLabel;
            if (currentLabel is not null && label != currentLabel)
            {
                yield return (startIndex, rowCount);
                startIndex = i;
                rowCount = 0;
            }

            currentLabel = label;
            rowCount++;
        }

        if (currentLabel is not null)
            yield return (startIndex, rowCount);
    }

    public static JobRowCatalog FromConfigured() => new(ServerList.AllRows);

    public static JobRowCatalog FromEntries(
        IEnumerable<CommandLogEntry> entries,
        IEnumerable<ScheduledRun>? scheduled = null)
    {
        var discovered = new HashSet<(string ServerName, string JobLabel)>();

        foreach (var entry in entries)
        {
            var jobLabel = JobParser.ParseJobLabel(entry.CommandType, entry.Command, includeUnconfigured: true);
            if (jobLabel is not null)
                discovered.Add((entry.ServerName, jobLabel));
        }

        if (scheduled is not null)
        {
            foreach (var run in scheduled)
                discovered.Add((run.ServerName, run.JobLabel));
        }

        var configuredLabels = ServerList.Jobs.Select(j => j.Label).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var rows = new List<JobRow>(ServerList.AllRows);

        var serverOrder = ServerList.ServerNames
            .Select((server, index) => (server, index))
            .ToDictionary(x => x.server, x => x.index, StringComparer.OrdinalIgnoreCase);

        var extraRows = discovered
            .Where(r => !configuredLabels.Contains(r.JobLabel))
            .OrderBy(r => r.JobLabel, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => serverOrder.TryGetValue(r.ServerName, out var index) ? index : int.MaxValue)
            .ThenBy(r => r.ServerName, StringComparer.OrdinalIgnoreCase)
            .Select(r => new JobRow(r.ServerName, r.JobLabel, r.JobLabel));

        rows.AddRange(extraRows);
        return new JobRowCatalog(rows);
    }
}
