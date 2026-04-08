using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LightSourceMonitor.Helpers;
using LightSourceMonitor.Services.Channels;
using LightSourceMonitor.Services.Localization;
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
    private readonly ILanguageService _language;
    private readonly ILogger<TrendViewModel> _logger;
    private bool _isLoading;

    [ObservableProperty] private ObservableCollection<ISeries> _series = new();
    [ObservableProperty] private Axis[] _xAxes = Array.Empty<Axis>();
    [ObservableProperty] private Axis[] _yAxes = Array.Empty<Axis>();
    [ObservableProperty] private string _selectedTimeRange = UiResourceKeys.TimeRange24H;
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private int _dataPointCount;
    [ObservableProperty] private string _dataPointSummary = "";
    [ObservableProperty] private bool _isTrendDataLoading;

    public string[] TimeRangeKeys { get; } =
    [
        UiResourceKeys.TimeRange1H,
        UiResourceKeys.TimeRange24H,
        UiResourceKeys.TimeRange7D,
        UiResourceKeys.TimeRange30D
    ];

    public TrendViewModel(ITrendService trendService, IChannelCatalog channelCatalog, ILanguageService language,
        ILogger<TrendViewModel> logger)
    {
        _trendService = trendService;
        _channelCatalog = channelCatalog;
        _language = language;
        _logger = logger;
        InitializeAxes();
        RefreshDataPointSummary();
        _language.LanguageChanged += (_, _) =>
            AsyncHelper.SafeDispatcherInvoke(() =>
            {
                InitializeAxes();
                RefreshDataPointSummary();
            });
        LoadDataAsync().SafeFireAndForget("TrendViewModel.InitialLoad");
    }

    partial void OnDataPointCountChanged(int value) => RefreshDataPointSummary();

    private void RefreshDataPointSummary()
    {
        DataPointSummary = string.Format(_language.GetString("Trend_DataPoints"), DataPointCount);
    }

    private void InitializeAxes()
    {
        XAxes =
        [
            new Axis
            {
                Name = _language.GetString("Trend_Axis_Time"),
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
        ];

        YAxes =
        [
            new Axis
            {
                Name = _language.GetString("Trend_Axis_Power"),
                NamePaint = new SolidColorPaint(SKColor.Parse("#9898B0")),
                LabelsPaint = new SolidColorPaint(SKColor.Parse("#9898B0")),
                SeparatorsPaint = new SolidColorPaint(SKColor.Parse("#2D2D4A")) { StrokeThickness = 1 },
                Labeler = value => $"{value:F1}",
            }
        ];
    }

    partial void OnSelectedTimeRangeChanged(string value)
    {
        LoadDataAsync().SafeFireAndForget("TrendVM.TimeRangeChanged");
    }

    [RelayCommand]
    public async Task LoadDataAsync()
    {
        if (_isLoading) return;
        _isLoading = true;
        IsTrendDataLoading = true;

        try
        {
            StatusText = _language.GetString("Trend_Loading");
            var (from, to) = GetTimeRange();

            var channels = _channelCatalog.GetEnabledChannels();

            var newSeries = new ObservableCollection<ISeries>();
            var colorIdx = 0;
            var totalPoints = 0;

            foreach (var channel in channels)
            {
                var data = await _trendService.GetTrendDataAsync(channel.Id, from, to);
                if (data.Count == 0) continue;

                var values = data.Select(r => new DateTimePoint(r.Timestamp, r.Power)).ToArray();

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
                ? string.Format(_language.GetString("Trend_StatusOk"), channels.Count, totalPoints)
                : _language.GetString("Trend_StatusEmpty");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load trend data");
            StatusText = string.Format(_language.GetString("Trend_StatusFail"), ex.Message);
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
            UiResourceKeys.TimeRange1H => to.AddHours(-1),
            UiResourceKeys.TimeRange24H => to.AddDays(-1),
            UiResourceKeys.TimeRange7D => to.AddDays(-7),
            UiResourceKeys.TimeRange30D => to.AddDays(-30),
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
                FileName = $"trend_power_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
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
            writer.WriteLine(_language.GetString("Trend_CsvHeader"));
            foreach (var (chName, rec) in allRecords)
            {
                writer.WriteLine($"{rec.Timestamp:yyyy-MM-dd HH:mm:ss.fff},{chName},{rec.Power:F3}");
            }

            var fileInfo = new FileInfo(dialog.FileName);
            StatusText = string.Format(_language.GetString("Trend_ExportCsvOk"), allRecords.Count, fileInfo.Name);
        }
        catch (Exception ex)
        {
            StatusText = string.Format(_language.GetString("Trend_ExportCsvFail"), ex.Message);
        }
    }

    public static SKColor GetChannelColor(int index) => ChannelColors[index % ChannelColors.Length];
}
