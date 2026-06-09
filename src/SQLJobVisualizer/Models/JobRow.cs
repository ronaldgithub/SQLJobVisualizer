namespace SQLJobVisualizer.Models;

public sealed record JobRow(string ServerName, string JobLabel, string ShortJobLabel)
{
    public string DisplayLabel => $"{ShortJobLabel}  {ServerName}";
}
