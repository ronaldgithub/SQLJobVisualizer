namespace SQLJobVisualizer.Models;

public sealed class JobExecution
{
    public required JobRow Row   { get; init; }
    public int      RowIndex     { get; init; }
    public DateTime StartTime    { get; init; }
    public DateTime EndTime      { get; init; }
    public bool     Success      { get; init; }
    public bool     IsRunning    { get; init; }
    public TimeSpan              Duration => EndTime - StartTime;
    public List<SessionProgress> Progress { get; init; } = [];
}
