namespace SQLJobVisualizer.Models;

public sealed class JobSlot
{
    public required JobRow Row       { get; init; }
    public int      RowIndex         { get; init; }
    public int      HourOffset       { get; init; }
    public bool     Success          { get; init; }
    public DateTime  StartTime       { get; init; }
    public DateTime? EndTime         { get; init; }
}
