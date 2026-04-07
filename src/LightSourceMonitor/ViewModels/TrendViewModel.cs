using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LightSourceMonitor.Helpers;
using LightSourceMonitor.Models;
using LightSourceMonitor.Services.Channels;
using LightSourceMonitor.Services.Trend;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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
    private readonly DriverSettings _driverSettings;
    private readonly WavelengthServiceSettings _wmSettings;
    private readonly ILogger<TrendViewModel> _logger;
    private bool _isLoading;

    [ObservableProperty] private ObservableCollection<ISeries> _series = new();
    [ObservableProperty] private Axis[] _xAxes = Array.Empty<Axis>();
    [ObservableProperty] private Axis[] _yAxes = Array.Empty<Axis>();
    [ObservableProperty] private string _selectedTimeRange = "最近1小时";
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private int _dataPointCount;
    [ObservableProperty] private int _selectedTrendSubTabIndex;
    [ObservableProperty] private bool _wmShowDeviation = true;
    [ObservableProperty] private ObservableCollection<WmTrendGroupViewModel> _wmTrendGroups = new();

    public string[] TimeRangeOptions { get; } =
        { "最近1小时", "最近24小时", "最近7天", "最近30天" };

    public TrendViewModel(
        ITrendService trendService,
        IChannelCatalog channelCatalog,
        IOptions<DriverSettings> driverOptions,
        IOptions<WavelengthServiceSettings> wmOptions,
        ILogger<TrendViewModel> logger)
    {
        _trendService = trendService;
        _channelCatalog = channelCatalog;
        _driverSettings = driverOptions.Value;
        _wmSettings = wmOptions.Value;
        _logger = logger;
        InitializePdAxes();
        LoadDataAsync().SafeFireAndForget("TrendViewModel.InitialLoad");
    }

    private void InitializePdAxes()
    {
        XAxes = new[] { CreateTimeAxis() };
        YAxes = new[]
        {
            new Axis
            {
                Name = "功率 (dBm)",
                NamePaint = new SolidColorPaint(SKColor.Parse("#9898B0")),
                LabelsPaint = new SolidColorPaint(SKColor.Parse("#9898B0")),
                SeparatorsPaint = new SolidColorPaint(SKColor.Parse("#2D2D4A")) { StrokeThickness = 1 },
                Labeler = value => $"{value:F1}"
            }
        };
    }

    private static Axis CreateTimeAxis() =>
        new()
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
            }
        };

    partial void OnSelectedTimeRangeChanged(string value)
    {
        LoadDataAsync().SafeFireAndForget("TrendVM.TimeRangeChanged");
    }

    partial void OnSelectedTrendSubTabIndexChanged(int value)
    {
        LoadDataAsync().SafeFireAndForget("TrendVM.SubTabChanged");
    }

    partial void OnWmShowDeviationChanged(bool value)
    {
        if (SelectedTrendSubTabIndex == 1)
            LoadDataAsync().SafeFireAndForget("TrendVM.WmDeviationChanged");
    }

    [RelayCommand]
    public async Task LoadDataAsync()
    {
        if (_isLoading) return;
        _isLoading = true;

        try
        {
            StatusText = "加载中...";
            switch (SelectedTrendSubTabIndex)
            {
                case 0:
                    await LoadPdTrendAsync();
                    break;
                case 1:
                    await LoadWmTrendAsync();
                    break;
                default:
                    DisposeWmGroups();
                    WmTrendGroups.Clear();
                    ClearPdSeries();
                    DataPointCount = 0;
                    StatusText = "WBA 趋势预留 — 后续版本实现";
                    break;
            }
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

    private async Task LoadPdTrendAsync()
    {
        DisposeWmGroups();
        WmTrendGroups.Clear();
        InitializePdAxes();

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

        ReplacePdSeries(newSeries);
        DataPointCount = totalPoints;
        StatusText = totalPoints > 0
            ? $"PD — 共 {channels.Count} 通道, {totalPoints} 数据点"
            : "PD — 暂无数据 — 请等待采集后刷新";
    }

    private async Task LoadWmTrendAsync()
    {
        ClearPdSeries();
        DisposeWmGroups();
        WmTrendGroups.Clear();

        var (from, to) = GetTimeRange();
        var queryId = ResolveWmQueryDeviceId();
        var n = Math.Clamp(_wmSettings.TableChannelCount, 1, 64);
        var indices = Enumerable.Range(0, n).ToList();

        var wmByChannel = await _trendService.GetWmTrendByChannelsAsync(queryId, indices, from, to);
        if (wmByChannel.Count == 0)
        {
            DataPointCount = 0;
            StatusText =
                "WM — 暂无落库数据。波长计表格读数在每次 WM 扫描时写入数据库，请确认采集已运行并已执行过 WM 扫描。";
            return;
        }

        var groups = new Dictionary<string, List<(int Idx, IReadOnlyList<WmMeasurementRecord> Data)>>();
        foreach (var (idx, list) in wmByChannel.OrderBy(kv => kv.Key))
        {
            var key = ResolveWmGroupTitle(idx);
            if (!groups.TryGetValue(key, out var bucket))
            {
                bucket = new List<(int, IReadOnlyList<WmMeasurementRecord>)>();
                groups[key] = bucket;
            }

            bucket.Add((idx, list));
        }

        var orderedKeys = groups.Keys
            .OrderBy(k => k.StartsWith("未配置", StringComparison.Ordinal) ? 1 : 0)
            .ThenBy(k => k, StringComparer.Ordinal);

        var totalPoints = 0;
        var colorIdx = 0;

        foreach (var key in orderedKeys)
        {
            var bucket = groups[key];
            var groupVm = new WmTrendGroupViewModel { Title = key };
            var yValues = new List<double>();

            foreach (var (idx, data) in bucket)
            {
                var spec = _wmSettings.ResolveChannelSpec(idx);
                var name = !string.IsNullOrWhiteSpace(spec?.ChannelName)
                    ? spec!.ChannelName
                    : $"路{idx}";

                var points = new List<DateTimePoint>(data.Count);
                foreach (var r in data)
                {
                    var y = ComputeWmY(r, idx);
                    points.Add(new DateTimePoint(r.Timestamp, y));
                    yValues.Add(y);
                }

                totalPoints += points.Count;

                var color = ChannelColors[colorIdx % ChannelColors.Length];
                colorIdx++;
                groupVm.Series.Add(new LineSeries<DateTimePoint>
                {
                    Name = name,
                    Values = points,
                    Stroke = new SolidColorPaint(color) { StrokeThickness = 2 },
                    Fill = null,
                    GeometrySize = 0,
                    LineSmoothness = 0.3
                });
            }

            var (yMin, yMax) = GetPaddedYRange(yValues);
            var yName = WmShowDeviation ? "相对标称 (nm)" : "波长 (nm)";
            groupVm.XAxes = new[] { CreateTimeAxis() };
            groupVm.YAxes = new[]
            {
                new Axis
                {
                    Name = yName,
                    NamePaint = new SolidColorPaint(SKColor.Parse("#9898B0")),
                    LabelsPaint = new SolidColorPaint(SKColor.Parse("#9898B0")),
                    SeparatorsPaint = new SolidColorPaint(SKColor.Parse("#2D2D4A")) { StrokeThickness = 1 },
                    Labeler = value => WmShowDeviation ? $"{value:F4}" : $"{value:F3}",
                    MinLimit = yMin,
                    MaxLimit = yMax
                }
            };

            WmTrendGroups.Add(groupVm);
        }

        DataPointCount = totalPoints;
        StatusText = $"WM — 设备 {queryId}, {wmByChannel.Count} 路有数据, {totalPoints} 数据点";
    }

    private double ComputeWmY(WmMeasurementRecord r, int channelIndex)
    {
        var spec = _wmSettings.ResolveChannelSpec(channelIndex);
        if (WmShowDeviation && spec is { SpecWavelengthNm: > 0 })
            return r.WavelengthNm - spec.SpecWavelengthNm;
        return r.WavelengthNm;
    }

    private string ResolveWmGroupTitle(int channelIndex)
    {
        var spec = _wmSettings.ResolveChannelSpec(channelIndex);
        if (spec is { SpecWavelengthNm: > 0 })
        {
            var rounded = Math.Round(spec.SpecWavelengthNm, MidpointRounding.AwayFromZero);
            return $"标称波段 ~{rounded:F0} nm";
        }

        return "未配置标称 / 其他";
    }

    private string ResolveWmQueryDeviceId()
    {
        var effective = _driverSettings.GetEffectiveDevices();
        return string.IsNullOrWhiteSpace(_wmSettings.QueryDeviceId)
            ? effective.FirstOrDefault()?.DeviceSN ?? "WM"
            : _wmSettings.QueryDeviceId.Trim();
    }

    private static (double Min, double Max) GetPaddedYRange(IReadOnlyList<double> values)
    {
        var list = values.Where(v => !double.IsNaN(v) && !double.IsInfinity(v)).ToList();
        if (list.Count == 0)
            return (-1, 1);

        var min = list.Min();
        var max = list.Max();
        if (Math.Abs(max - min) < 1e-12)
            return (min - 0.02, max + 0.02);

        var pad = (max - min) * 0.12;
        return (min - pad, max + pad);
    }

    private void ClearPdSeries()
    {
        foreach (var s in Series)
        {
            if (s is IDisposable d)
                d.Dispose();
        }

        Series.Clear();
    }

    private void ReplacePdSeries(ObservableCollection<ISeries> newSeries)
    {
        foreach (var s in Series)
        {
            if (s is IDisposable d)
                d.Dispose();
        }

        Series.Clear();
        Series = newSeries;
    }

    private void DisposeWmGroups()
    {
        foreach (var g in WmTrendGroups)
        {
            foreach (var s in g.Series)
            {
                if (s is IDisposable d)
                    d.Dispose();
            }
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
            if (SelectedTrendSubTabIndex == 2)
            {
                StatusText = "WBA 趋势未实现，无需导出";
                return;
            }

            if (SelectedTrendSubTabIndex == 0)
            {
                await ExportPdCsvAsync();
                return;
            }

            await ExportWmCsvAsync();
        }
        catch (Exception ex)
        {
            StatusText = $"CSV 导出失败: {ex.Message}";
        }
    }

    private async Task ExportPdCsvAsync()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "CSV Files|*.csv",
            DefaultExt = ".csv",
            FileName = $"trend_pd_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
        };

        if (dialog.ShowDialog() != true) return;

        var (from, to) = GetTimeRange();
        var channels = _channelCatalog.GetEnabledChannels();

        var allRecords = new List<(string chName, MeasurementRecord rec)>();
        foreach (var ch in channels)
        {
            var data = await _trendService.GetTrendDataAsync(ch.Id, from, to, maxPoints: 500_000);
            foreach (var r in data)
                allRecords.Add((ch.ChannelName, r));
        }

        allRecords.Sort((a, b) => a.rec.Timestamp.CompareTo(b.rec.Timestamp));

        await using var writer = new StreamWriter(dialog.FileName, false, System.Text.Encoding.UTF8);
        await writer.WriteLineAsync("时间,通道,功率(dBm),波长(标称,nm)");
        foreach (var (chName, rec) in allRecords)
        {
            await writer.WriteLineAsync(
                $"{rec.Timestamp:yyyy-MM-dd HH:mm:ss.fff},{chName},{rec.Power:F3},{rec.Wavelength:F4}");
        }

        StatusText = $"已导出 PD {allRecords.Count} 条到 {new FileInfo(dialog.FileName).Name}";
    }

    private async Task ExportWmCsvAsync()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "CSV Files|*.csv",
            DefaultExt = ".csv",
            FileName = $"trend_wm_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
        };

        if (dialog.ShowDialog() != true) return;

        var (from, to) = GetTimeRange();
        var queryId = ResolveWmQueryDeviceId();
        var n = Math.Clamp(_wmSettings.TableChannelCount, 1, 64);
        var indices = Enumerable.Range(0, n).ToList();

        var wmByChannel =
            await _trendService.GetWmTrendByChannelsAsync(queryId, indices, from, to, maxPointsPerSeries: 500_000);

        var rows = new List<(DateTime ts, string line)>();
        foreach (var (idx, list) in wmByChannel)
        {
            var spec = _wmSettings.ResolveChannelSpec(idx);
            var specNm = spec is { SpecWavelengthNm: > 0 } ? spec.SpecWavelengthNm : double.NaN;
            foreach (var r in list)
            {
                var delta = !double.IsNaN(specNm) ? r.WavelengthNm - specNm : double.NaN;
                var deltaStr = double.IsNaN(delta) ? "" : delta.ToString("F4");
                var line =
                    $"{r.Timestamp:yyyy-MM-dd HH:mm:ss.fff},{queryId},{idx},{r.WavelengthNm:F4},{deltaStr},{r.WmPowerDbm?.ToString("F3") ?? ""},{r.IsValid}";
                rows.Add((r.Timestamp, line));
            }
        }

        rows.Sort((a, b) => a.ts.CompareTo(b.ts));

        await using var writer = new StreamWriter(dialog.FileName, false, System.Text.Encoding.UTF8);
        await writer.WriteLineAsync("时间,QueryDeviceId,ChannelIndex,波长_nm,相对标称_nm,WM功率_dBm,IsValid");
        foreach (var (_, line) in rows)
            await writer.WriteLineAsync(line);

        StatusText = $"已导出 WM {rows.Count} 条到 {new FileInfo(dialog.FileName).Name}";
    }

    public static SKColor GetChannelColor(int index) => ChannelColors[index % ChannelColors.Length];
}
