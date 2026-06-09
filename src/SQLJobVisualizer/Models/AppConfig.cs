namespace SQLJobVisualizer.Models;

public sealed class AppConfig
{
    public string[]    Servers { get; set; } = [];
    public JobConfig[] Jobs    { get; set; } = [];
}

public sealed class JobConfig
{
    public string Label      { get; set; } = "";
    public string SqlPattern { get; set; } = "";
    public string ShortLabel { get; set; } = "";
}
