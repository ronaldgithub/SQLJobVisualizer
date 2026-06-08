namespace SQLJobVisualizer.Models;

public sealed class CommandLogEntry
{
    public string ServerName  { get; init; } = "";
    public string CommandType { get; init; } = "";
    public string Command     { get; init; } = "";
    public DateTime  StartTime   { get; init; }
    public DateTime? EndTime     { get; init; }
    public int?      ErrorNumber { get; init; }
}
