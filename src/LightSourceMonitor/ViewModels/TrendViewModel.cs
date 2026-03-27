using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LightSourceMonitor.Data;
using LightSourceMonitor.Services.Trend;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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
    private readonly IServiceProvider _services;

    [ObservableProperty] private ObservableCollection<ISeries> _series = new();
    [ObservableProperty] private Axis[] _xAxes = Array.Empty<Axis>();
    [ObservableProperty] private Axis[] _yAxes = Array.Empty<Axis>();
    [ObservableProperty] private string _selectedTimeRange = "最近24小时";
    [ObservableProperty] private bool _showPower = true;
    [ObservableProperty] private bool _showWavelength;

    public string[] TimeRangeOptions { get; } =
        { "最近1小时", "最近24小时", "最近7天", "最近30天" };

    public TrendViewModel(ITrendService trendService, IServiceProvider services)
    {
        _trendService = trendService;
        _services = services;
        InitializeAxes();
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
                Name = ShowPower ? "功率 (dBm)" : "波长 (nm)",
                NamePaint = new SolidColorPaint(SKColor.Parse("#9898B0")),
                LabelsPaint = new SolidColorPaint(SKColor.Parse("#9898B0")),
                SeparatorsPaint = new SolidColorPaint(SKColor.Parse("#2D2D4A")) { StrokeThickness = 1 },
            }
        };
    }

    partial void OnSelectedTimeRangeChanged(string value)
    {
        _ = LoadDataAsync();
    }

    public async Task LoadDataAsync()
    {
        var (from, to) = GetTimeRange();

        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MonitorDbContext>();
        var channels = await db.LaserChannels.Where(c => c.IsEnabled).ToListAsync();

        Series.Clear();
        int colorIdx = 0;

        foreach (var channel in channels)
        {
            var data = await _trendService.GetTrendDataAsync(channel.Id, from, to);
            if (data.Count == 0) continue;

            var values = data.Select(r => new DateTimePoint(r.Timestamp,
                ShowPower ? r.Power : r.Wavelength)).ToArray();

            var color = ChannelColors[colorIdx % ChannelColors.Length];
            Series.Add(new LineSeries<DateTimePoint>
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
    private void ExportCsv()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "CSV Files|*.csv",
            DefaultExt = ".csv",
            FileName = $"trend_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
        };

        if (dialog.ShowDialog() != true) return;

        using var writer = new StreamWriter(dialog.FileName);
        writer.WriteLine("时间,通道,值");
        foreach (var s in Series)
        {
            if (s is LineSeries<DateTimePoint> line && line.Values != null)
            {
                foreach (var pt in line.Values)
                {
                    writer.WriteLine($"{pt.DateTime:yyyy-MM-dd HH:mm:ss},{line.Name},{pt.Value:F3}");
                }
            }
        }
    }

    [RelayCommand]
    private void ExportPng()
    {
        // PNG export uses RenderTargetBitmap on the chart control
    }

    public static SKColor GetChannelColor(int index) => ChannelColors[index % ChannelColors.Length];
}
