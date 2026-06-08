using SQLJobVisualizer.Models;

namespace SQLJobVisualizer.Services;

public static class ServerList
{
    public static readonly string[] ServerNames =
        ["sql1", "sql2", "sql3", "sql4", "sql5"];

    public static readonly string[] JobLabels =
    [
        "DatabaseBackup - FULL",
        "DatabaseBackup - DIFF",
        "DatabaseBackup - LOG",
        "IndexOptimize - ALL",
        "IntegrityCheck - ALL",
    ];

    public static readonly IReadOnlyList<JobRow> AllRows =
        (from job    in JobLabels
         from server in ServerNames
         select new JobRow(server, job)).ToList();

    public static int GetRowIndex(string serverName, string jobLabel)
    {
        int jobIdx = Array.IndexOf(JobLabels, jobLabel);
        int srvIdx = Array.IndexOf(ServerNames, serverName);
        if (jobIdx < 0 || srvIdx < 0) return -1;
        return jobIdx * ServerNames.Length + srvIdx;
    }

    public static string GetConnectionString(string serverName) =>
        $"Server={serverName};Database=master;Integrated Security=True;" +
        $"Connect Timeout=15;Application Name=SQLJobVisualizer;TrustServerCertificate=True;";
}
