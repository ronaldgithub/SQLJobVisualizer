using System.Text.Json;
using SQLJobVisualizer.Models;

namespace SQLJobVisualizer.Services;

public static class ConfigLoader
{
    public static readonly string ConfigPath =
        Path.Combine(AppContext.BaseDirectory, "config.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented        = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static AppConfig Load()
    {
        if (!File.Exists(ConfigPath))
        {
            var defaults = CreateDefaults();
            Save(defaults);
            return defaults;
        }
        try
        {
            var json = File.ReadAllText(ConfigPath);
            return JsonSerializer.Deserialize<AppConfig>(json, JsonOptions) ?? CreateDefaults();
        }
        catch
        {
            return CreateDefaults();
        }
    }

    private static void Save(AppConfig config) =>
        File.WriteAllText(ConfigPath, JsonSerializer.Serialize(config, JsonOptions));

    private static AppConfig CreateDefaults() => new()
    {
        Servers = ["sql1", "sql2", "sql3", "sql4", "sql5"],
        Jobs =
        [
            new() { Label = "DatabaseBackup - FULL", SqlPattern = "DatabaseBackup%FULL%",     ShortLabel = "Backup FULL"    },
            new() { Label = "DatabaseBackup - DIFF", SqlPattern = "DatabaseBackup%DIFF%",     ShortLabel = "Backup DIFF"    },
            new() { Label = "DatabaseBackup - LOG",  SqlPattern = "DatabaseBackup%LOG%",      ShortLabel = "Backup LOG"     },
            new() { Label = "IndexOptimize - ALL",   SqlPattern = "IndexOptimize%",           ShortLabel = "IndexOptimize"  },
            new() { Label = "IntegrityCheck - ALL",  SqlPattern = "DatabaseIntegrityCheck%",  ShortLabel = "IntegrityCheck" },
        ]
    };
}
