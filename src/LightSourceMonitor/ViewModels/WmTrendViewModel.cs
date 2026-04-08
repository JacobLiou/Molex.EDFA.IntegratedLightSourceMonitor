using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LightSourceMonitor.Helpers;
using LightSourceMonitor.Services.Localization;
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
    private readonly ILanguageService _language;
    private readonly ILogger<WmTrendViewModel> _logger;
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

    public WmTrendViewModel(ITrendService trendService, ILanguageService language, ILogger<WmTrendViewModel> logger)
    {
        _trendService = trendService;
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
        LoadDataAsync().SafeFireAndForget("WmTrendViewModel.InitialLoad");
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
                Name = _language.GetString("WmTrend_Axis_Wl"),
                NamePaint = new SolidColorPaint(SKColor.Parse("#9898B0")),
                LabelsPaint = new SolidColorPaint(SKColor.Parse("#9898B0")),
                SeparatorsPaint = new SolidColorPaint(SKColor.Parse("#2D2D4A")) { StrokeThickness = 1 },
                Labeler = value => $"{value:F3}",
            }
        ];
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
            StatusText = _language.GetString("Trend_Loading");
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
                ? string.Format(_language.GetString("WmTrend_StatusOk"), newSeries.Count, totalPoints)
                : _language.GetString("WmTrend_StatusEmpty");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load WM trend data");
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
                FileName = $"trend_wm_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
            };

            if (dialog.ShowDialog() != true) return;

            var (from, to) = GetTimeRange();
            var rows = await _trendService.GetWavelengthMeterSnapshotsAsync(from, to);
            if (rows.Count == 0)
            {
                StatusText = _language.GetString("WmTrend_NoSnapshots");
                return;
            }

            var maxCh = 0;
            foreach (var r in rows)
            {
                maxCh = Math.Max(maxCh, WmSnapshotCsvCodec.ParseWavelengthSegments(r.OneTimeValues).Count);
                maxCh = Math.Max(maxCh, WmSnapshotCsvCodec.ParsePowerSegments(r.PowerValues).Count);
            }

            var sb = new StringBuilder();
            sb.Append(_language.GetString("WmTrend_CsvHeaderBase"));
            for (var i = 0; i < maxCh; i++)
                sb.Append(string.Format(_language.GetString("WmTrend_CsvChCols"), i));
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
            StatusText = string.Format(_language.GetString("WmTrend_ExportOk"), rows.Count, Path.GetFileName(dialog.FileName));
        }
        catch (Exception ex)
        {
            StatusText = string.Format(_language.GetString("Trend_ExportCsvFail"), ex.Message);
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
