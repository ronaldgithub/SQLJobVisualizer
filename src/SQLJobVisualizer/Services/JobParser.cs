using System.Text.RegularExpressions;

namespace SQLJobVisualizer.Services;

public static partial class JobParser
{
    [GeneratedRegex(@"@BackupType\s*=\s*'(\w+)'", RegexOptions.IgnoreCase)]
    private static partial Regex BackupTypeRegex();

    // Matches both exact Ola Hallengren job names ("DatabaseIntegrityCheck") and
    // scoped variants ("DatabaseIntegrityCheck - ALL_DATABASES", etc.) used on some servers.
    public static string? ParseJobLabel(string commandType, string command)
    {
        var ct = commandType;

        if (ct.StartsWith("DatabaseBackup", StringComparison.OrdinalIgnoreCase))
        {
            if (ct.Contains("FULL", StringComparison.OrdinalIgnoreCase)) return "DatabaseBackup - FULL";
            if (ct.Contains("DIFF", StringComparison.OrdinalIgnoreCase)) return "DatabaseBackup - DIFF";
            if (ct.Contains("LOG",  StringComparison.OrdinalIgnoreCase)) return "DatabaseBackup - LOG";
            // CommandLog fallback: parse @BackupType from the Command column
            return ParseBackupLabel(command);
        }

        if (ct.StartsWith("IndexOptimize",          StringComparison.OrdinalIgnoreCase)) return "IndexOptimize - ALL";
        if (ct.StartsWith("DatabaseIntegrityCheck", StringComparison.OrdinalIgnoreCase)) return "IntegrityCheck - ALL";

        return null;
    }

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
