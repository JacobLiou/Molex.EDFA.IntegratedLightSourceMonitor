using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;

namespace LightSourceMonitor.ViewModels;

public partial class WmTrendGroupViewModel : ObservableObject
{
    [ObservableProperty] private string _title = "";

    [ObservableProperty] private ObservableCollection<ISeries> _series = new();

    [ObservableProperty] private Axis[] _xAxes = Array.Empty<Axis>();

    [ObservableProperty] private Axis[] _yAxes = Array.Empty<Axis>();
}
