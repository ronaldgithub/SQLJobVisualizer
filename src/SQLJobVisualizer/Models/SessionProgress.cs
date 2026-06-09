namespace SQLJobVisualizer.Models;

public sealed class SessionProgress
{
    public int    SessionId       { get; init; }
    public string DatabaseName    { get; init; } = "";
    public string Command         { get; init; } = "";
    public double PercentComplete { get; init; }
}
