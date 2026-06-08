using System.Text.RegularExpressions;

namespace SQLJobVisualizer.Services;

public static partial class JobParser
{
    [GeneratedRegex(@"@BackupType\s*=\s*'(\w+)'", RegexOptions.IgnoreCase)]
    private static partial Regex BackupTypeRegex();

    public static string? ParseJobLabel(string commandType, string command) =>
        commandType switch
        {
            "DatabaseBackup"          => ParseBackupLabel(command),
            "IndexOptimize"           => "IndexOptimize - ALL",
            "DatabaseIntegrityCheck"  => "IntegrityCheck - ALL",
            _                         => null
        };

    private static string? ParseBackupLabel(string command)
    {
        var m = BackupTypeRegex().Match(command);
        if (!m.Success) return null;
        return m.Groups[1].Value.ToUpperInvariant() switch
        {
            "FULL" => "DatabaseBackup - FULL",
            "DIFF" => "DatabaseBackup - DIFF",
            "LOG"  => "DatabaseBackup - LOG",
            _      => null
        };
    }
}
