using System.Text.RegularExpressions;

namespace SQLJobVisualizer.Services;

public static class JobParser
{
    public static string? ParseJobLabel(string jobName, string _command, bool includeUnconfigured = false)
    {
        foreach (var job in ServerList.Jobs)
        {
            if (MatchesLike(jobName, job.SqlPattern))
                return job.Label;
        }
        return includeUnconfigured ? jobName : null;
    }

    private static bool MatchesLike(string input, string pattern)
    {
        var regexPattern = "^" + Regex.Escape(pattern)
            .Replace("%", ".*")
            .Replace("_", ".") + "$";
        return Regex.IsMatch(input, regexPattern, RegexOptions.IgnoreCase);
    }
}
