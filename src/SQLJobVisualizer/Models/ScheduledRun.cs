namespace SQLJobVisualizer.Models;

public sealed class ScheduledRun
{
    public required string   ServerName        { get; init; }
    public required string   JobLabel          { get; init; }
    public required int      RowIndex          { get; init; }
    public required DateTime ScheduledTime     { get; init; }
    public          TimeSpan EstimatedDuration { get; init; } = TimeSpan.FromMinutes(30);
    public          int      ScheduleId        { get; init; } = 0;
}
