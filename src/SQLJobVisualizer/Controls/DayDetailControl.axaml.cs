using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Threading;
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
    private IReadOnlyDictionary<(string Server, string JobLabel), string> _stepCommands =
        new Dictionary<(string, string), string>();
    private readonly DispatcherTimer _clockTimer   = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _refreshTimer = new() { Interval = TimeSpan.FromSeconds(30) };
    private Rectangle? _timeLine;

    public DayDetailControl()
    {
        InitializeComponent();
        PopulateLabelPanel();

        DatePicker.SelectedDate        =  DateTime.Today;
        DatePicker.SelectedDateChanged += (_, _) => _ = LoadAsync();
        RefreshButton.Click            += (_, _) => _ = LoadAsync();

        _clockTimer.Tick += (_, _) => UpdateClock();
        _clockTimer.Start();
        UpdateClock();

        _refreshTimer.Tick += (_, _) =>
        {
            var day = DatePicker.SelectedDate?.Date ?? DateTime.Today;
            if (day == DateTime.Today) _ = LoadAsync();
        };
        _refreshTimer.Start();
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        _ = LoadStepCommandsAsync();
        _ = LoadAsync();
    }

    private async Task LoadStepCommandsAsync()
    {
        _stepCommands = await _service.LoadJobStepCommandsAsync();
        PopulateLabelPanel();
    }

    private void UpdateClock()
    {
        ClockText.Text = DateTime.Now.ToString("HH:mm:ss");
        if (_timeLine is not null)
            Canvas.SetLeft(_timeLine, DateTime.Now.TimeOfDay.TotalMinutes);
    }

    private const int LabelW = 200;

    private void PopulateLabelPanel()
    {
        LabelCanvas.Children.Clear();

        // Header row — same height as canvas HeaderH so rows align exactly
        var headerBorder = new Border
        {
            Width           = LabelW,
            Height          = HeaderH,
            Padding         = new Thickness(6, 0, 4, 0),
            BorderBrush     = new SolidColorBrush(Color.Parse("#3A3D45")),
            BorderThickness = new Thickness(0, 0, 0, 1),
        };
        var headerGrid = new Grid();
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(120)));
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
        headerGrid.Children.Add(MakeHeaderTb("Job Type", 0));
        headerGrid.Children.Add(MakeHeaderTb("Server",   1));
        headerBorder.Child = headerGrid;
        Canvas.SetLeft(headerBorder, 0);
        Canvas.SetTop(headerBorder, 0);
        LabelCanvas.Children.Add(headerBorder);

        // Data rows — y matches HeaderH + rowIndex * RowH exactly
        string? lastJob = null;
        for (int i = 0; i < ServerList.AllRows.Count; i++)
        {
            var row = ServerList.AllRows[i];
            bool isGroupStart = row.JobLabel != lastJob;
            lastJob = row.JobLabel;

            var border = new Border
            {
                Width           = LabelW,
                Height          = RowH,
                Padding         = new Thickness(6, 0, 4, 0),
                BorderBrush     = new SolidColorBrush(Color.Parse("#2A2D35")),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Background      = isGroupStart
                    ? new SolidColorBrush(Color.Parse("#1E2530"))
                    : new SolidColorBrush(Color.Parse("#1A1D23")),
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(120)));
            grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));

            var jobTb = new TextBlock
            {
                Text              = isGroupStart ? row.ShortJobLabel : "",
                VerticalAlignment = VerticalAlignment.Center,
                FontSize          = 10,
                Foreground        = new SolidColorBrush(Color.Parse("#D0D2D8")),
            };
            var srvTb = new TextBlock
            {
                Text              = row.ServerName,
                VerticalAlignment = VerticalAlignment.Center,
                FontSize          = 10,
                Foreground        = new SolidColorBrush(Color.Parse("#C8CAD0")),
                FontWeight        = FontWeight.SemiBold,
            };
            Grid.SetColumn(jobTb, 0);
            Grid.SetColumn(srvTb, 1);
            grid.Children.Add(jobTb);
            grid.Children.Add(srvTb);

            border.Child = grid;
            Canvas.SetLeft(border, 0);
            Canvas.SetTop(border, HeaderH + i * RowH);
            ToolTip.SetTip(border, BuildRowTooltip(row));
            LabelCanvas.Children.Add(border);
        }

        LabelCanvas.Height = HeaderH + ServerList.AllRows.Count * RowH;
    }

    private string BuildRowTooltip(JobRow row)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine(row.JobLabel);
        sb.AppendLine(row.ServerName);
        if (_stepCommands.TryGetValue((row.ServerName, row.JobLabel), out var cmd))
        {
            sb.AppendLine();
            sb.Append(cmd);
        }
        return sb.ToString().TrimEnd();
    }

    private static TextBlock MakeHeaderTb(string text, int col)
    {
        var tb = new TextBlock
        {
            Text              = text,
            VerticalAlignment = VerticalAlignment.Center,
            FontSize          = 11,
            FontWeight        = FontWeight.SemiBold,
            Foreground        = new SolidColorBrush(Color.Parse("#E4E6EB")),
        };
        Grid.SetColumn(tb, col);
        return tb;
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

            var completedTask  = _service.LoadDayAsync(day, ct);
            var runningTask    = day == DateTime.Today
                ? _service.LoadRunningAsync(ct)
                : Task.FromResult<(List<CommandLogEntry>, IReadOnlyList<string>)>(([], []));
            var scheduledTask  = _service.LoadScheduledAsync(day, ct);

            await Task.WhenAll(completedTask, runningTask, scheduledTask);
            if (ct.IsCancellationRequested) return;

            var (completed, failedCompleted) = completedTask.Result;
            var (running,   failedRunning)   = runningTask.Result;
            var scheduled                    = scheduledTask.Result;

            // Suppress only the specific completed row that matches the running job's start time
            // (to the minute), so earlier runs of the same job stay visible.
            var runningKeys = new HashSet<string>(
                running.Select(r => $"{r.ServerName}|{r.CommandType}|{r.StartTime:yyyyMMddHHmm}"));
            var merged = completed
                .Where(c => !runningKeys.Contains($"{c.ServerName}|{c.CommandType}|{c.StartTime:yyyyMMddHHmm}"))
                .Concat(running)
                .ToList();

            var failedServers = failedCompleted.Concat(failedRunning).Distinct().ToList();
            var executions    = BuildExecutions(merged, day);

            // Average historical duration per row — used to size scheduled bars
            var avgDurations = executions
                .Where(e => !e.IsRunning)
                .GroupBy(e => e.RowIndex)
                .ToDictionary(g => g.Key,
                    g => TimeSpan.FromSeconds(g.Average(e => e.Duration.TotalSeconds)));

            RenderDay(executions, scheduled, avgDurations, day);
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
            var jobLabel = JobParser.ParseJobLabel(entry.CommandType, entry.Command);
            if (jobLabel is null) continue;

            int rowIdx = ServerList.GetRowIndex(entry.ServerName, jobLabel);
            if (rowIdx < 0) continue;

            // Still-running jobs: use current time as provisional end time
            var endTime = entry.EndTime ?? DateTime.Now;

            list.Add(new JobExecution
            {
                Row       = ServerList.AllRows[rowIdx],
                RowIndex  = rowIdx,
                StartTime = entry.StartTime,
                EndTime   = endTime,
                Success   = !entry.ErrorNumber.HasValue,
                IsRunning = !entry.EndTime.HasValue,
                Progress  = entry.Progress,
            });
        }
        return list;
    }

    private void RenderDay(List<JobExecution> executions, List<ScheduledRun> scheduled,
                           Dictionary<int, TimeSpan> avgDurations, DateTime day)
    {
        DayCanvas.Children.Clear();

        int totalRows = ServerList.AllRows.Count;
        int jobCount  = ServerList.JobLabels.Length;
        int srvCount  = ServerList.ServerNames.Length;
        double canvasH = HeaderH + totalRows * RowH;

        DayCanvas.Width  = CanvasW;
        DayCanvas.Height = canvasH;

        // Background
        AddRect(0, 0, CanvasW, canvasH, "#1A1D23");

        // Header background
        AddRect(0, 0, CanvasW, HeaderH, "#15171C");

        // Alternating row group backgrounds
        string[] groupBg = ["#1A1D23", "#1D2028"];
        for (int g = 0; g < jobCount; g++)
            AddRect(0, HeaderH + g * srvCount * RowH, CanvasW, srvCount * RowH, groupBg[g % 2]);

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
        for (int g = 1; g < jobCount; g++)
            AddRect(0, HeaderH + g * srvCount * RowH, CanvasW, 2, "#3A3D45");

        // Job execution bars
        foreach (var ex in executions)
        {
            double x = ex.StartTime.TimeOfDay.TotalMinutes;
            double w = Math.Max(3, ex.Duration.TotalMinutes);
            double y = HeaderH + ex.RowIndex * RowH + 4;

            if (x >= CanvasW) continue;
            w = Math.Min(w, CanvasW - x);

            var color = ex.IsRunning ? "#F39C12"
                : ex.Success         ? "#2ECC71"
                :                      "#E74C3C";
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

        // Scheduled (next-run) hollow bars
        foreach (var sr in scheduled)
        {
            var estDuration = avgDurations.TryGetValue(sr.RowIndex, out var avg)
                ? avg : TimeSpan.FromMinutes(30);
            double x = sr.ScheduledTime.TimeOfDay.TotalMinutes;
            double w = Math.Max(4, estDuration.TotalMinutes);
            double y = HeaderH + sr.RowIndex * RowH + 4;

            if (x >= CanvasW) continue;
            w = Math.Min(w, CanvasW - x);

            var hollow = new Rectangle
            {
                Width           = w,
                Height          = RowH - 8,
                Fill            = Brushes.Transparent,
                Stroke          = new SolidColorBrush(Color.Parse("#E74C3C")),
                StrokeThickness = 1.5,
                Opacity         = 0.6,
            };
            Canvas.SetLeft(hollow, x);
            Canvas.SetTop(hollow, y);
            ToolTip.SetTip(hollow, $"{sr.JobLabel}\n{sr.ServerName}\nScheduled: {sr.ScheduledTime:HH:mm}");
            DayCanvas.Children.Add(hollow);
        }

        if (executions.Count == 0)
        {
            var msg = new TextBlock
            {
                Text                = "No job data found for this date",
                Foreground          = new SolidColorBrush(Color.Parse("#6B7280")),
                FontSize            = 13,
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            Canvas.SetLeft(msg, CanvasW / 2.0 - 130);
            Canvas.SetTop(msg, HeaderH + (totalRows * RowH) / 2.0 - 10);
            DayCanvas.Children.Add(msg);
        }

        // Current-time marker — only when viewing today; moves every second via UpdateClock
        _timeLine = null;
        if (day == DateTime.Today)
        {
            _timeLine = new Rectangle
            {
                Width   = 2,
                Height  = canvasH,
                Fill    = new SolidColorBrush(Color.Parse("#FF4444")),
                Opacity = 0.85,
            };
            Canvas.SetLeft(_timeLine, DateTime.Now.TimeOfDay.TotalMinutes);
            Canvas.SetTop(_timeLine, 0);
            DayCanvas.Children.Add(_timeLine);
        }

        ScrollToCurrentTime(day);
    }

    private void ScrollToCurrentTime(DateTime day)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            double x = day.Date == DateTime.Today
                ? Math.Max(0, DateTime.Now.Hour * 60 - 120)
                : 0;
            DayScroll.Offset = new Vector(x, 0);
        }, Avalonia.Threading.DispatcherPriority.Loaded);
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
        if (ex.IsRunning)
            sb.AppendLine("End:      still running ⏳");
        else
            sb.AppendLine($"End:      {ex.EndTime:HH:mm:ss}");
        sb.AppendLine($"Duration: {FormatDuration(ex.Duration)}");
        if (!ex.Success) sb.AppendLine("⚠ FAILED");
        if (ex.IsRunning && ex.Progress.Count > 0)
        {
            sb.AppendLine("──────────────────────────");
            foreach (var p in ex.Progress)
                sb.AppendLine($"spid {p.SessionId}  {p.DatabaseName}  {p.Command}  {p.PercentComplete:0.0}%");
        }
        return sb.ToString().TrimEnd();
    }

    private void UpdateServerStatus(IReadOnlyList<string> failedServers)
    {
        // failedServers entries are "serverName|errorMessage"
        var errors = failedServers
            .Select(s => s.Split('|', 2))
            .Where(p => p.Length == 2)
            .ToDictionary(p => p[0], p => p[1]);

        ServerStatusPanel.Children.Clear();
        foreach (var server in ServerList.ServerNames)
        {
            bool failed = errors.ContainsKey(server);
            var dot = new Border
            {
                Width             = 8,
                Height            = 8,
                CornerRadius      = new CornerRadius(4),
                Background        = new SolidColorBrush(Color.Parse(failed ? "#E74C3C" : "#2ECC71")),
                Margin            = new Thickness(0, 0, 2, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            var tip = failed ? $"{server}: {errors[server]}" : $"{server}: OK";
            ToolTip.SetTip(dot, tip);

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
