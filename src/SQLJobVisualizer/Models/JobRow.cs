namespace SQLJobVisualizer.Models;

public sealed record JobRow(string ServerName, string JobLabel)
{
    public string DisplayLabel => $"{ShortJobLabel}  {ServerName}";

    private string ShortJobLabel => JobLabel switch
    {
        "DatabaseBackup - FULL" => "Backup FULL",
        "DatabaseBackup - DIFF" => "Backup DIFF",
        "DatabaseBackup - LOG"  => "Backup LOG",
        "IndexOptimize - ALL"   => "IndexOptimize",
        "IntegrityCheck - ALL"  => "IntegrityCheck",
        _                       => JobLabel
    };
}
