using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LightSourceMonitor.Helpers;
using LightSourceMonitor.Services.Channels;
using LightSourceMonitor.Services.Trend;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace LightSourceMonitor.ViewModels;

public partial class TrendViewModel : ObservableObject
{
    private static readonly SKColor[] ChannelColors =
    {
        SKColor.Parse("#29B6F6"), SKColor.Parse("#66BB6A"),
        SKColor.Parse("#FFA726"), SKColor.Parse("#EF5350"),
        SKColor.Parse("#AB47BC"), SKColor.Parse("#26C6DA"),
        SKColor.Parse("#D4E157"), SKColor.Parse("#EC407A")
    };

    private readonly ITrendService _trendService;
    private readonly IChannelCatalog _channelCatalog;
    private readonly ILogger<TrendViewModel> _logger;
    private bool _isLoading;

    [ObservableProperty] private ObservableCollection<ISeries> _series = new();
    [ObservableProperty] private Axis[] _xAxes = Array.Empty<Axis>();
    [ObservableProperty] private Axis[] _yAxes = Array.Empty<Axis>();
    [ObservableProperty] private string _selectedTimeRange = "最近1小时";
    [ObservableProperty] private bool _showPower = true;
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private int _dataPointCount;

    public string[] TimeRangeOptions { get; } =
        { "最近1小时", "最近24小时", "最近7天", "最近30天" };

    public TrendViewModel(ITrendService trendService, IChannelCatalog channelCatalog, ILogger<TrendViewModel> logger)
    {
        _trendService = trendService;
        _channelCatalog = channelCatalog;
        _logger = logger;
        InitializeAxes();
        LoadDataAsync().SafeFireAndForget("TrendViewModel.InitialLoad");
    }

    private void InitializeAxes()
    {
        bool isPower = ShowPower;

        XAxes = new Axis[]
        {
            new Axis
            {
                Name = "时间",
                NamePaint = new SolidColorPaint(SKColor.Parse("#9898B0")),
                LabelsPaint = new SolidColorPaint(SKColor.Parse("#9898B0")),
                SeparatorsPaint = new SolidColorPaint(SKColor.Parse("#2D2D4A")) { StrokeThickness = 1 },
                Labeler = value => {
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
                Name = isPower ? "功率 (dBm)" : "波长 (nm)",
                NamePaint = new SolidColorPaint(SKColor.Parse("#9898B0")),
                LabelsPaint = new SolidColorPaint(SKColor.Parse("#9898B0")),
                SeparatorsPaint = new SolidColorPaint(SKColor.Parse("#2D2D4A")) { StrokeThickness = 1 },
                Labeler = value => isPower ? $"{value:F1}" : $"{value:F2}",
            }
        };
    }

    partial void OnSelectedTimeRangeChanged(string value)
    {
        LoadDataAsync().SafeFireAndForget("TrendVM.TimeRangeChanged");
    }

    partial void OnShowPowerChanged(bool value)
    {
        InitializeAxes();
        ReloadData().SafeFireAndForget("TrendVM.DataTypeChanged");
    }

    private async Task ReloadData()
    {
        _isLoading = false;
        await LoadDataAsync();
    }

    [RelayCommand]
    public async Task LoadDataAsync()
    {
        if (_isLoading) return;
        _isLoading = true;

        try
        {
            StatusText = "加载中...";
            var (from, to) = GetTimeRange();

            var channels = _channelCatalog.GetEnabledChannels();

            var newSeries = new ObservableCollection<ISeries>();
            int colorIdx = 0;
            int totalPoints = 0;

            foreach (var channel in channels)
            {
                var data = await _trendService.GetTrendDataAsync(channel.Id, from, to);
                if (data.Count == 0) continue;

                var values = data.Select(r => new DateTimePoint(r.Timestamp,
                    ShowPower ? r.Power : r.Wavelength)).ToArray();

                totalPoints += values.Length;

                var color = ChannelColors[colorIdx % ChannelColors.Length];
                newSeries.Add(new LineSeries<DateTimePoint>
                {
                    Name = channel.ChannelName,
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
                ? $"共 {channels.Count} 通道, {totalPoints} 数据点"
                : "暂无数据 — 请等待采集系统产生数据后刷新";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load trend data");
            StatusText = $"加载失败: {ex.Message}";
        }
        finally
        {
            _isLoading = false;
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
            var dataType = ShowPower ? "功率" : "波长";
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "CSV Files|*.csv",
                DefaultExt = ".csv",
                FileName = $"trend_{dataType}_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
            };

            if (dialog.ShowDialog() != true) return;

            var (from, to) = GetTimeRange();
            var channels = _channelCatalog.GetEnabledChannels();

            var allRecords = new List<(string chName, Models.MeasurementRecord rec)>();
            foreach (var ch in channels)
            {
                var data = await _trendService.GetTrendDataAsync(ch.Id, from, to);
                foreach (var r in data)
                    allRecords.Add((ch.ChannelName, r));
            }

            allRecords.Sort((a, b) => a.rec.Timestamp.CompareTo(b.rec.Timestamp));

            using var writer = new StreamWriter(dialog.FileName, false, System.Text.Encoding.UTF8);
            writer.WriteLine("时间,通道,功率(dBm),波长(nm)");
            foreach (var (chName, rec) in allRecords)
            {
                writer.WriteLine($"{rec.Timestamp:yyyy-MM-dd HH:mm:ss.fff},{chName},{rec.Power:F3},{rec.Wavelength:F4}");
            }

            var fileInfo = new System.IO.FileInfo(dialog.FileName);
            StatusText = $"已导出 {allRecords.Count} 条记录到 {fileInfo.Name}";
        }
        catch (Exception ex)
        {
            StatusText = $"CSV 导出失败: {ex.Message}";
        }
    }

    public static SKColor GetChannelColor(int index) => ChannelColors[index % ChannelColors.Length];
}
