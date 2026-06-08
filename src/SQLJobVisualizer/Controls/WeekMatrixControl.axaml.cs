using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using SQLJobVisualizer.Models;
using SQLJobVisualizer.Services;

namespace SQLJobVisualizer.Controls;

public partial class WeekMatrixControl : UserControl
{
    private const int CellW   = 12;
    private const int CellH   = 24;
    private const int HeaderH = 42;

    private DateTime _weekStart;
    private readonly CommandLogService _service = new();
    private CancellationTokenSource? _cts;

    public WeekMatrixControl()
    {
        InitializeComponent();
        _weekStart = GetWeekMonday(DateTime.Today);
        UpdateWeekLabel();
        PopulateLabelPanel();
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        PrevWeekButton.Click += (_, _) => NavigateWeek(-1);
        NextWeekButton.Click += (_, _) => NavigateWeek(1);
        RefreshButton.Click  += (_, _) => _ = LoadAsync();
        _ = LoadAsync();
    }

    private void NavigateWeek(int delta)
    {
        _weekStart = _weekStart.AddDays(delta * 7);
        UpdateWeekLabel();
        _ = LoadAsync();
    }

    private void UpdateWeekLabel() =>
        WeekLabel.Text = $"{_weekStart:dd MMM} – {_weekStart.AddDays(6):dd MMM yyyy}";

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
                Height          = CellH,
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
                Text              = row.ShortJobLabel,
                VerticalAlignment = VerticalAlignment.Center,
                FontSize          = 10,
                Foreground        = new SolidColorBrush(isGroupStart
                    ? Color.Parse("#D0D2D8")
                    : Color.Parse("#8A8FA0")),
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
            var (entries, failedServers) = await _service.LoadWeekAsync(_weekStart, ct);
            if (ct.IsCancellationRequested) return;

            var slots = BuildSlots(entries, _weekStart);
            RenderMatrix(slots, _weekStart);
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

    private static List<JobSlot> BuildSlots(List<CommandLogEntry> entries, DateTime weekStart)
    {
        var slots = new List<JobSlot>();
        foreach (var entry in entries)
        {
            var jobLabel = JobParser.ParseJobLabel(entry.CommandType, entry.Command);
            if (jobLabel is null) continue;

            int rowIdx = ServerList.GetRowIndex(entry.ServerName, jobLabel);
            if (rowIdx < 0) continue;

            int startHour = (int)(entry.StartTime - weekStart).TotalHours;
            int endHour   = entry.EndTime.HasValue
                ? (int)Math.Ceiling((entry.EndTime.Value - weekStart).TotalHours)
                : startHour + 1;

            startHour = Math.Clamp(startHour, 0, 167);
            endHour   = Math.Clamp(endHour,   0, 167);
            bool success = !entry.ErrorNumber.HasValue;

            for (int h = startHour; h <= endHour; h++)
            {
                slots.Add(new JobSlot
                {
                    Row        = ServerList.AllRows[rowIdx],
                    RowIndex   = rowIdx,
                    HourOffset = h,
                    Success    = success,
                    StartTime  = entry.StartTime,
                    EndTime    = entry.EndTime,
                });
            }
        }
        return slots;
    }

    private void RenderMatrix(List<JobSlot> slots, DateTime weekStart)
    {
        MatrixCanvas.Children.Clear();

        const int totalCols = 168;
        const int totalRows = 25;
        double canvasW = totalCols * CellW;   // 2016
        double canvasH = HeaderH + totalRows * CellH; // 642

        MatrixCanvas.Width  = canvasW;
        MatrixCanvas.Height = canvasH;

        // Main background
        AddRect(0, 0, canvasW, canvasH, "#1A1D23");

        // Header background
        AddRect(0, 0, canvasW, HeaderH, "#15171C");

        // Alternating group backgrounds
        string[] groupBg = ["#1A1D23", "#1D2028"];
        for (int g = 0; g < 5; g++)
            AddRect(0, HeaderH + g * 5 * CellH, canvasW, 5 * CellH, groupBg[g % 2]);

        // Day column headers and separators
        string[] dayNames = ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"];
        for (int d = 0; d < 7; d++)
        {
            double dayX = d * 24 * CellW;  // 288 px per day
            var date = weekStart.AddDays(d);

            // Day name label
            var dayTb = new TextBlock
            {
                Text          = $"{dayNames[d]} {date:dd MMM}",
                FontSize      = 11,
                FontWeight    = FontWeight.SemiBold,
                Foreground    = new SolidColorBrush(Color.Parse("#D0D2D8")),
                Width         = 24 * CellW,
                TextAlignment = TextAlignment.Center,
            };
            Canvas.SetLeft(dayTb, dayX);
            Canvas.SetTop(dayTb, 6);
            MatrixCanvas.Children.Add(dayTb);

            // 6-hour tick labels in header
            for (int h = 0; h < 24; h += 6)
            {
                var htb = new TextBlock
                {
                    Text       = $"{h:00}",
                    FontSize   = 9,
                    Foreground = new SolidColorBrush(Color.Parse("#6B7280")),
                };
                Canvas.SetLeft(htb, dayX + h * CellW + 1);
                Canvas.SetTop(htb, HeaderH - 16);
                MatrixCanvas.Children.Add(htb);
            }

            // Day separator
            if (d > 0)
                AddRect(dayX, 0, 1, canvasH, "#3A3D45");
        }

        // Header bottom border
        AddRect(0, HeaderH - 1, canvasW, 1, "#3A3D45");

        // Job-group separators (every 5 rows)
        for (int g = 1; g < 5; g++)
            AddRect(0, HeaderH + g * 5 * CellH, canvasW, 2, "#3A3D45");

        // Faint 6-hour grid lines inside days
        for (int h = 6; h < totalCols; h += 6)
        {
            if (h % 24 == 0) continue; // already drawn as day separator
            AddRect(h * CellW, HeaderH, 1, totalRows * CellH, "#252830");
        }

        // Job slot rectangles
        foreach (var slot in slots)
        {
            var color = !slot.EndTime.HasValue ? "#F39C12"
                : slot.Success                 ? "#2ECC71"
                :                                "#E74C3C";
            var rect  = new Rectangle
            {
                Width   = CellW - 1,
                Height  = CellH - 2,
                Fill    = new SolidColorBrush(Color.Parse(color)),
                Opacity = 0.88,
            };
            Canvas.SetLeft(rect, slot.HourOffset * CellW + 0.5);
            Canvas.SetTop(rect, HeaderH + slot.RowIndex * CellH + 1);
            ToolTip.SetTip(rect, BuildSlotTooltip(slot));
            MatrixCanvas.Children.Add(rect);
        }

        if (slots.Count == 0)
        {
            var msg = new TextBlock
            {
                Text              = "No job data found for this week",
                Foreground        = new SolidColorBrush(Color.Parse("#6B7280")),
                FontSize          = 13,
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            Canvas.SetLeft(msg, canvasW / 2 - 130);
            Canvas.SetTop(msg, HeaderH + (totalRows * CellH) / 2.0 - 10);
            MatrixCanvas.Children.Add(msg);
        }

        ScrollToCurrentDay();
    }

    private void ScrollToCurrentDay()
    {
        int dayOffset = (int)(DateTime.Today - _weekStart).TotalDays;
        if (dayOffset < 0 || dayOffset > 6) return;

        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            double x = Math.Max(0, dayOffset * 24 * CellW - 50);
            MatrixScroll.Offset = new Vector(x, 0);
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
        MatrixCanvas.Children.Add(rect);
    }

    private static string BuildSlotTooltip(JobSlot slot)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine(slot.Row.JobLabel);
        sb.AppendLine(slot.Row.ServerName);
        sb.AppendLine($"Start:    {slot.StartTime:ddd dd-MM HH:mm}");
        if (slot.EndTime.HasValue)
        {
            sb.AppendLine($"End:      {slot.EndTime.Value:HH:mm}");
            sb.AppendLine($"Duration: {FormatDuration(slot.EndTime.Value - slot.StartTime)}");
        }
        else
        {
            sb.AppendLine("End:      still running");
        }
        if (!slot.Success) sb.AppendLine("⚠ FAILED");
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

    private static DateTime GetWeekMonday(DateTime date)
    {
        int diff = ((int)date.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
        return date.Date.AddDays(-diff);
    }

    private static string FormatDuration(TimeSpan ts)
    {
        if (ts.TotalHours >= 1)   return $"{(int)ts.TotalHours}h {ts.Minutes:00}m";
        if (ts.TotalMinutes >= 1) return $"{ts.Minutes}m {ts.Seconds:00}s";
        return $"{ts.Seconds}s";
    }
}
