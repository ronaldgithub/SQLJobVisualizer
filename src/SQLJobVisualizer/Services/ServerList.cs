using SQLJobVisualizer.Models;

namespace SQLJobVisualizer.Services;

public static class ServerList
{
    private static readonly AppConfig _config = ConfigLoader.Load();

    public static string[]    ServerNames { get; } = _config.Servers;
    public static JobConfig[] Jobs        { get; } = _config.Jobs;
    public static string[]    JobLabels   { get; } = [.. _config.Jobs.Select(j => j.Label)];

    public static readonly IReadOnlyList<JobRow> AllRows =
        (from job    in _config.Jobs
         from server in _config.Servers
         let  sl     = string.IsNullOrEmpty(job.ShortLabel) ? job.Label : job.ShortLabel
         select new JobRow(server, job.Label, sl)).ToList();

    public static int GetRowIndex(string serverName, string jobLabel)
    {
        int jobIdx = Array.IndexOf(JobLabels,   jobLabel);
        int srvIdx = Array.IndexOf(ServerNames, serverName);
        if (jobIdx < 0 || srvIdx < 0) return -1;
        return jobIdx * ServerNames.Length + srvIdx;
    }

    public static string GetConnectionString(string serverName) =>
        $"Server={serverName};Database=master;Integrated Security=True;" +
        $"Connect Timeout=15;Application Name=SQLJobVisualizer;TrustServerCertificate=True;";
}
