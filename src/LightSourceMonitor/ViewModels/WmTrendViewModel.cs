using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LightSourceMonitor.Helpers;
using LightSourceMonitor.Services.Trend;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace LightSourceMonitor.ViewModels;

public partial class WmTrendViewModel : ObservableObject
{
    private static readonly SKColor[] ChannelColors =
    {
        SKColor.Parse("#29B6F6"), SKColor.Parse("#66BB6A"),
        SKColor.Parse("#FFA726"), SKColor.Parse("#EF5350"),
        SKColor.Parse("#AB47BC"), SKColor.Parse("#26C6DA"),
        SKColor.Parse("#D4E157"), SKColor.Parse("#EC407A")
    };

    private readonly ITrendService _trendService;
    private readonly ILogger<WmTrendViewModel> _logger;
    private bool _isLoading;

    [ObservableProperty] private ObservableCollection<ISeries> _series = new();
    [ObservableProperty] private Axis[] _xAxes = Array.Empty<Axis>();
    [ObservableProperty] private Axis[] _yAxes = Array.Empty<Axis>();
    [ObservableProperty] private string _selectedTimeRange = "最近1小时";
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private int _dataPointCount;
    [ObservableProperty] private bool _isTrendDataLoading;

    public string[] TimeRangeOptions { get; } =
        { "最近1小时", "最近24小时", "最近7天", "最近30天" };

    public WmTrendViewModel(ITrendService trendService, ILogger<WmTrendViewModel> logger)
    {
        _trendService = trendService;
        _logger = logger;
        InitializeAxes();
        LoadDataAsync().SafeFireAndForget("WmTrendViewModel.InitialLoad");
    }

    private void InitializeAxes()
    {
        XAxes = new Axis[]
        {
            new Axis
            {
                Name = "时间",
                NamePaint = new SolidColorPaint(SKColor.Parse("#9898B0")),
                LabelsPaint = new SolidColorPaint(SKColor.Parse("#9898B0")),
                SeparatorsPaint = new SolidColorPaint(SKColor.Parse("#2D2D4A")) { StrokeThickness = 1 },
                Labeler = value =>
                {
                    var ticks = (long)value;
                    if (ticks < DateTime.MinValue.Ticks || ticks > DateTime.MaxValue.Ticks)
                        return string.Empty;
                    return new DateTime(ticks).ToString("MM/dd HH:mm");
                },
            }
        };

        YAxes = new Axis[]
        {
            new Axis
            {
                Name = "波长 (nm)",
                NamePaint = new SolidColorPaint(SKColor.Parse("#9898B0")),
                LabelsPaint = new SolidColorPaint(SKColor.Parse("#9898B0")),
                SeparatorsPaint = new SolidColorPaint(SKColor.Parse("#2D2D4A")) { StrokeThickness = 1 },
                Labeler = value => $"{value:F3}",
            }
        };
    }

    partial void OnSelectedTimeRangeChanged(string value)
    {
        LoadDataAsync().SafeFireAndForget("WmTrendVM.TimeRangeChanged");
    }

    [RelayCommand]
    public async Task LoadDataAsync()
    {
        if (_isLoading) return;
        _isLoading = true;
        IsTrendDataLoading = true;

        try
        {
            StatusText = "加载中...";
            var (from, to) = GetTimeRange();

            var seriesData = await _trendService.GetWmWavelengthTrendAsync(from, to);
            var newSeries = new ObservableCollection<ISeries>();
            var colorIdx = 0;
            var totalPoints = 0;

            foreach (var ch in seriesData)
            {
                if (ch.Points.Count == 0) continue;

                var values = ch.Points.Select(p => new DateTimePoint(p.Timestamp, p.WavelengthNm)).ToArray();
                totalPoints += values.Length;

                var color = ChannelColors[colorIdx % ChannelColors.Length];
                newSeries.Add(new LineSeries<DateTimePoint>
                {
                    Name = ch.SeriesName,
                    Values = values,
                    Stroke = new SolidColorPaint(color) { StrokeThickness = 2 },
                    Fill = null,
                    GeometrySize = 0,
                    LineSmoothness = 0.3
                });
                colorIdx++;
            }

            foreach (var s in Series)
                if (s is IDisposable d) d.Dispose();
            Series.Clear();

            Series = newSeries;
            DataPointCount = totalPoints;
            StatusText = totalPoints > 0
                ? $"共 {newSeries.Count} 路, {totalPoints} 数据点"
                : "暂无数据 — 请等待采集写入波长计快照后刷新";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load WM trend data");
            StatusText = $"加载失败: {ex.Message}";
        }
        finally
        {
            _isLoading = false;
            IsTrendDataLoading = false;
        }
    }

    private (DateTime from, DateTime to) GetTimeRange()
    {
        var to = DateTime.Now;
        var from = SelectedTimeRange switch
        {
            "最近1小时" => to.AddHours(-1),
            "最近24小时" => to.AddDays(-1),
            "最近7天" => to.AddDays(-7),
            "最近30天" => to.AddDays(-30),
            _ => to.AddDays(-1)
        };
        return (from, to);
    }

    [RelayCommand]
    private async Task ExportCsv()
    {
        try
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "CSV Files|*.csv",
                DefaultExt = ".csv",
                FileName = $"trend_wm_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
            };

            if (dialog.ShowDialog() != true) return;

            var (from, to) = GetTimeRange();
            var rows = await _trendService.GetWavelengthMeterSnapshotsAsync(from, to);
            if (rows.Count == 0)
            {
                StatusText = "所选时间范围内无波长计快照";
                return;
            }

            var maxCh = 0;
            foreach (var r in rows)
            {
                maxCh = Math.Max(maxCh, WmSnapshotCsvCodec.ParseWavelengthSegments(r.OneTimeValues).Count);
                maxCh = Math.Max(maxCh, WmSnapshotCsvCodec.ParsePowerSegments(r.PowerValues).Count);
            }

            var sb = new StringBuilder();
            sb.Append("时间,设备");
            for (var i = 0; i < maxCh; i++)
                sb.Append($",路{i}波长(nm),路{i}功率(dBm)");
            sb.AppendLine();

            foreach (var r in rows)
            {
                var wl = WmSnapshotCsvCodec.ParseWavelengthSegments(r.OneTimeValues);
                var pw = WmSnapshotCsvCodec.ParsePowerSegments(r.PowerValues);
                sb.Append(r.Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff"));
                sb.Append(',');
                sb.Append(EscapeCsvField(r.QueryDeviceId));
                for (var i = 0; i < maxCh; i++)
                {
                    sb.Append(',');
                    sb.Append(wl.Count > i && wl[i] is double wv ? wv.ToString(CultureInfo.InvariantCulture) : "");
                    sb.Append(',');
                    sb.Append(pw.Count > i && pw[i] is double pv ? pv.ToString(CultureInfo.InvariantCulture) : "");
                }

                sb.AppendLine();
            }

            await File.WriteAllTextAsync(dialog.FileName, sb.ToString(), Encoding.UTF8);
            StatusText = $"已导出 {rows.Count} 条快照到 {System.IO.Path.GetFileName(dialog.FileName)}";
        }
        catch (Exception ex)
        {
            StatusText = $"CSV 导出失败: {ex.Message}";
        }
    }

    private static string EscapeCsvField(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        if (s.Contains(',') || s.Contains('"') || s.Contains('\n'))
            return $"\"{s.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
        return s;
    }

    public static SKColor GetChannelColor(int index) => ChannelColors[index % ChannelColors.Length];
}
