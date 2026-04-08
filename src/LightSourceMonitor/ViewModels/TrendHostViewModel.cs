using CommunityToolkit.Mvvm.ComponentModel;

namespace LightSourceMonitor.ViewModels;

public partial class TrendHostViewModel : ObservableObject
{
    public TrendViewModel PdTrend { get; }
    public WmTrendViewModel WmTrend { get; }

    /// <summary>0 = PD 功率（默认）, 1 = 波长计。用 Visibility 切换子页，避免 TabControl 卸载 LiveCharts 触发其 Dispose 空引用。</summary>
    [ObservableProperty] private int _activeTrendTab;

    public TrendHostViewModel(TrendViewModel pdTrend, WmTrendViewModel wmTrend)
    {
        PdTrend = pdTrend;
        WmTrend = wmTrend;
        ActiveTrendTab = 0;
    }

    public bool IsPdTrendTabActive => ActiveTrendTab == 0;
    public bool IsWmTrendTabActive => ActiveTrendTab == 1;

    partial void OnActiveTrendTabChanged(int value)
    {
        OnPropertyChanged(nameof(IsPdTrendTabActive));
        OnPropertyChanged(nameof(IsWmTrendTabActive));
    }
}
