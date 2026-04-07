using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;

namespace LightSourceMonitor.ViewModels;

/// <summary>趋势页多图分组（WM 波段 / WBA 设备等）。</summary>
public partial class TrendChartGroupViewModel : ObservableObject
{
    [ObservableProperty] private string _title = "";

    [ObservableProperty] private ObservableCollection<ISeries> _series = new();

    [ObservableProperty] private Axis[] _xAxes = Array.Empty<Axis>();

    [ObservableProperty] private Axis[] _yAxes = Array.Empty<Axis>();
}
