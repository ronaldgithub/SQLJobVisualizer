using System.Text.RegularExpressions;

namespace SQLJobVisualizer.Services;

public static class JobParser
{
    public static string? ParseJobLabel(string jobName, string _command)
    {
        foreach (var job in ServerList.Jobs)
        {
            if (MatchesLike(jobName, job.SqlPattern))
                return job.Label;
        }
        return null;
    }

    private static bool MatchesLike(string input, string pattern)
    {
        var regexPattern = "^" + Regex.Escape(pattern)
            .Replace("%", ".*")
            .Replace("_", ".") + "$";
        return Regex.IsMatch(input, regexPattern, RegexOptions.IgnoreCase);
    }
}
