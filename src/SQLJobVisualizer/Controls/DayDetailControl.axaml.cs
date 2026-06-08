using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using SQLJobVisualizer.Models;
using SQLJobVisualizer.Services;

namespace SQLJobVisualizer.Controls;

public partial class DayDetailControl : UserControl
{
    private const int HeaderH = 30;
    private const int RowH    = 28;
    private const int CanvasW = 1440; // 1 px per minute × 1440 min/day

    private readonly CommandLogService _service = new();
    private CancellationTokenSource? _cts;

    public DayDetailControl()
    {
        InitializeComponent();
        PopulateLabelPanel();
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        DatePicker.SelectedDate = DateTime.Today;
        DatePicker.SelectedDateChanged += (_, _) => _ = LoadAsync();
        RefreshButton.Click += (_, _) => _ = LoadAsync();
        _ = LoadAsync();
    }

    private void PopulateLabelPanel()
    {
        LabelPanel.Children.Clear();
        string? lastJob = null;

        foreach (var row in ServerList.AllRows)
        {
            bool isGroupStart = row.JobLabel != lastJob;
            lastJob = row.JobLabel;

            var border = new Border
            {
                Height          = RowH,
                Padding         = new Thickness(6, 0, 4, 0),
                BorderBrush     = new SolidColorBrush(Color.Parse("#2A2D35")),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Background      = isGroupStart
                    ? new SolidColorBrush(Color.Parse("#1E2530"))
                    : new SolidColorBrush(Color.Parse("#1A1D23")),
            };
            border.Child = new TextBlock
            {
                Text              = row.DisplayLabel,
                VerticalAlignment = VerticalAlignment.Center,
                FontSize          = 10,
                Foreground        = new SolidColorBrush(Color.Parse("#C8CAD0")),
            };
            LabelPanel.Children.Add(border);
        }
    }

    private async Task LoadAsync()
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        RefreshButton.IsEnabled = false;
        RefreshButton.Content   = "Loading…";

        try
        {
            var day = DatePicker.SelectedDate?.Date ?? DateTime.Today;
            var (entries, failedServers) = await _service.LoadDayAsync(day, ct);
            if (ct.IsCancellationRequested) return;

            var executions = BuildExecutions(entries, day);
            RenderDay(executions);
            UpdateServerStatus(failedServers);
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (!ct.IsCancellationRequested)
            {
                RefreshButton.IsEnabled = true;
                RefreshButton.Content   = "⟳ Refresh";
            }
        }
    }

    private static List<JobExecution> BuildExecutions(List<CommandLogEntry> entries, DateTime day)
    {
        var list = new List<JobExecution>();
        foreach (var entry in entries)
        {
            if (entry.EndTime is null) continue; // skip still-running

            var jobLabel = JobParser.ParseJobLabel(entry.CommandType, entry.Command);
            if (jobLabel is null) continue;

            int rowIdx = ServerList.GetRowIndex(entry.ServerName, jobLabel);
            if (rowIdx < 0) continue;

            list.Add(new JobExecution
            {
                Row       = ServerList.AllRows[rowIdx],
                RowIndex  = rowIdx,
                StartTime = entry.StartTime,
                EndTime   = entry.EndTime.Value,
                Success   = !entry.ErrorNumber.HasValue,
            });
        }
        return list;
    }

    private void RenderDay(List<JobExecution> executions)
    {
        DayCanvas.Children.Clear();

        const int totalRows = 25;
        double canvasH = HeaderH + totalRows * RowH; // 30 + 700 = 730

        DayCanvas.Width  = CanvasW;
        DayCanvas.Height = canvasH;

        // Background
        AddRect(0, 0, CanvasW, canvasH, "#1A1D23");

        // Header background
        AddRect(0, 0, CanvasW, HeaderH, "#15171C");

        // Alternating row group backgrounds
        string[] groupBg = ["#1A1D23", "#1D2028"];
        for (int g = 0; g < 5; g++)
            AddRect(0, HeaderH + g * 5 * RowH, CanvasW, 5 * RowH, groupBg[g % 2]);

        // Hour header labels and vertical grid lines
        for (int h = 0; h <= 24; h++)
        {
            double x = h * 60.0; // 60px per hour
            if (h < 24)
            {
                var htb = new TextBlock
                {
                    Text       = $"{h:00}",
                    FontSize   = 10,
                    Foreground = new SolidColorBrush(Color.Parse("#8A8FA0")),
                };
                Canvas.SetLeft(htb, x + 2);
                Canvas.SetTop(htb, 8);
                DayCanvas.Children.Add(htb);
            }

            // Vertical grid lines
            var lineColor = h % 6 == 0 ? "#3A3D45" : "#252830";
            AddRect(x, 0, 1, canvasH, lineColor);
        }

        // Header bottom border
        AddRect(0, HeaderH - 1, CanvasW, 1, "#3A3D45");

        // Job-group separators
        for (int g = 1; g < 5; g++)
            AddRect(0, HeaderH + g * 5 * RowH, CanvasW, 2, "#3A3D45");

        // Job execution bars
        foreach (var ex in executions)
        {
            double x = ex.StartTime.TimeOfDay.TotalMinutes;
            double w = Math.Max(3, ex.Duration.TotalMinutes);
            double y = HeaderH + ex.RowIndex * RowH + 4;

            // Clamp to canvas width
            if (x >= CanvasW) continue;
            w = Math.Min(w, CanvasW - x);

            var color = ex.Success ? "#2ECC71" : "#E74C3C";
            var rect  = new Rectangle
            {
                Width   = w,
                Height  = RowH - 8,
                Fill    = new SolidColorBrush(Color.Parse(color)),
                Opacity = 0.88,
            };
            Canvas.SetLeft(rect, x);
            Canvas.SetTop(rect, y);
            ToolTip.SetTip(rect, BuildExecTooltip(ex));
            DayCanvas.Children.Add(rect);
        }
    }

    private void AddRect(double x, double y, double w, double h, string color, double opacity = 1.0)
    {
        var rect = new Rectangle
        {
            Width   = w,
            Height  = h,
            Fill    = new SolidColorBrush(Color.Parse(color)),
            Opacity = opacity,
        };
        Canvas.SetLeft(rect, x);
        Canvas.SetTop(rect, y);
        DayCanvas.Children.Add(rect);
    }

    private static string BuildExecTooltip(JobExecution ex)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine(ex.Row.JobLabel);
        sb.AppendLine(ex.Row.ServerName);
        sb.AppendLine($"Start:    {ex.StartTime:HH:mm:ss}");
        sb.AppendLine($"End:      {ex.EndTime:HH:mm:ss}");
        sb.AppendLine($"Duration: {FormatDuration(ex.Duration)}");
        if (!ex.Success) sb.AppendLine("⚠ FAILED");
        return sb.ToString().TrimEnd();
    }

    private void UpdateServerStatus(IReadOnlyList<string> failedServers)
    {
        ServerStatusPanel.Children.Clear();
        foreach (var server in ServerList.ServerNames)
        {
            bool failed = failedServers.Contains(server);
            var dot = new Border
            {
                Width             = 8,
                Height            = 8,
                CornerRadius      = new CornerRadius(4),
                Background        = new SolidColorBrush(Color.Parse(failed ? "#E74C3C" : "#2ECC71")),
                Margin            = new Thickness(0, 0, 2, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            ToolTip.SetTip(dot, failed ? $"{server}: connection failed" : $"{server}: OK");

            var lbl = new TextBlock
            {
                Text              = server,
                FontSize          = 10,
                Foreground        = new SolidColorBrush(Color.Parse("#8A8FA0")),
                VerticalAlignment = VerticalAlignment.Center,
                Margin            = new Thickness(0, 0, 8, 0),
            };

            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(dot);
            sp.Children.Add(lbl);
            ServerStatusPanel.Children.Add(sp);
        }
    }

    private static string FormatDuration(TimeSpan ts)
    {
        if (ts.TotalHours >= 1)   return $"{(int)ts.TotalHours}h {ts.Minutes:00}m";
        if (ts.TotalMinutes >= 1) return $"{ts.Minutes}m {ts.Seconds:00}s";
        return $"{ts.Seconds}s";
    }
}
