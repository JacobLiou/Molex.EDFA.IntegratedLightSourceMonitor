using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LightSourceMonitor.Helpers;
using LightSourceMonitor.Models;
using LightSourceMonitor.Services.Localization;
using LightSourceMonitor.Services.Trend;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace LightSourceMonitor.ViewModels;

public partial class WbaTrendViewModel : ObservableObject
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
    private readonly ILogger<WbaTrendViewModel> _logger;
    private bool _isLoading;

    [ObservableProperty] private ObservableCollection<ISeries> _series = new();
    [ObservableProperty] private Axis[] _xAxes = Array.Empty<Axis>();
    [ObservableProperty] private Axis[] _yAxes = Array.Empty<Axis>();
    [ObservableProperty] private string _selectedTimeRange = UiResourceKeys.TimeRange24H;
    [ObservableProperty] private WbaTrendMetric _selectedMetric = WbaTrendMetric.Pressure;
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private int _dataPointCount;
    [ObservableProperty] private string _dataPointSummary = "";
    [ObservableProperty] private bool _isTrendDataLoading;

    public ObservableCollection<WbaMetricUiOption> MetricOptions { get; } = new(
    [
        new WbaMetricUiOption(WbaTrendMetric.Pressure, "WbaTrend_Metric_Pressure"),
        new WbaMetricUiOption(WbaTrendMetric.TempAvg, "WbaTrend_Metric_TempAvg"),
        new WbaMetricUiOption(WbaTrendMetric.Temp0, "WbaTrend_Metric_T0"),
        new WbaMetricUiOption(WbaTrendMetric.Temp1, "WbaTrend_Metric_T1"),
        new WbaMetricUiOption(WbaTrendMetric.Temp2, "WbaTrend_Metric_T2"),
        new WbaMetricUiOption(WbaTrendMetric.Temp3, "WbaTrend_Metric_T3"),
        new WbaMetricUiOption(WbaTrendMetric.Volt0, "WbaTrend_Metric_V0"),
        new WbaMetricUiOption(WbaTrendMetric.Volt1, "WbaTrend_Metric_V1"),
        new WbaMetricUiOption(WbaTrendMetric.Volt2, "WbaTrend_Metric_V2"),
        new WbaMetricUiOption(WbaTrendMetric.Volt3, "WbaTrend_Metric_V3")
    ]);

    public string[] TimeRangeKeys { get; } =
    [
        UiResourceKeys.TimeRange1H,
        UiResourceKeys.TimeRange24H,
        UiResourceKeys.TimeRange7D,
        UiResourceKeys.TimeRange30D
    ];

    public WbaTrendViewModel(ITrendService trendService, ILanguageService language, ILogger<WbaTrendViewModel> logger)
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
        LoadDataAsync().SafeFireAndForget("WbaTrendViewModel.InitialLoad");
    }

    partial void OnDataPointCountChanged(int value) => RefreshDataPointSummary();

    partial void OnSelectedMetricChanged(WbaTrendMetric value)
    {
        InitializeAxes();
        LoadDataAsync().SafeFireAndForget("WbaTrendVM.MetricChanged");
    }

    private void RefreshDataPointSummary()
    {
        DataPointSummary = string.Format(_language.GetString("Trend_DataPoints"), DataPointCount);
    }

    private string YAxisNameResourceKey => SelectedMetric switch
    {
        WbaTrendMetric.Pressure => "WbaTrend_Axis_Pressure",
        WbaTrendMetric.TempAvg or WbaTrendMetric.Temp0 or WbaTrendMetric.Temp1 or WbaTrendMetric.Temp2
            or WbaTrendMetric.Temp3 => "WbaTrend_Axis_Temperature",
        _ => "WbaTrend_Axis_Voltage"
    };

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
                Name = _language.GetString(YAxisNameResourceKey),
                NamePaint = new SolidColorPaint(SKColor.Parse("#9898B0")),
                LabelsPaint = new SolidColorPaint(SKColor.Parse("#9898B0")),
                SeparatorsPaint = new SolidColorPaint(SKColor.Parse("#2D2D4A")) { StrokeThickness = 1 },
                Labeler = value => $"{value:F3}",
            }
        ];
    }

    partial void OnSelectedTimeRangeChanged(string value)
    {
        LoadDataAsync().SafeFireAndForget("WbaTrendVM.TimeRangeChanged");
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

            var seriesData = await _trendService.GetWbaTrendAsync(from, to, SelectedMetric);
            var newSeries = new ObservableCollection<ISeries>();
            var colorIdx = 0;
            var totalPoints = 0;

            foreach (var ch in seriesData)
            {
                if (ch.Points.Count == 0) continue;

                var values = ch.Points.Select(p => new DateTimePoint(p.Timestamp, p.Value)).ToArray();
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
                ? string.Format(_language.GetString("WbaTrend_StatusOk"), newSeries.Count, totalPoints)
                : _language.GetString("WbaTrend_StatusEmpty");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load WBA trend data");
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
                FileName = $"trend_wba_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
            };

            if (dialog.ShowDialog() != true) return;

            var (from, to) = GetTimeRange();
            var rows = await _trendService.GetWbaTelemetryRecordsAsync(from, to);
            if (rows.Count == 0)
            {
                StatusText = _language.GetString("WbaTrend_StatusEmpty");
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine(_language.GetString("WbaTrend_CsvHeader"));
            foreach (var r in rows)
            {
                sb.Append(r.Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture));
                sb.Append(',');
                sb.Append(EscapeCsvField(r.DeviceSN));
                sb.Append(',');
                sb.Append(r.AtmospherePressure.ToString(CultureInfo.InvariantCulture));
                sb.Append(',');
                sb.Append(EscapeCsvField(r.TemperaturesJson));
                sb.Append(',');
                sb.Append(EscapeCsvField(r.VoltagesJson));
                sb.AppendLine();
            }

            await File.WriteAllTextAsync(dialog.FileName, sb.ToString(), Encoding.UTF8);
            StatusText = string.Format(_language.GetString("WbaTrend_ExportOk"), rows.Count, Path.GetFileName(dialog.FileName));
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
