using System.ComponentModel;
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

        PdTrend.PropertyChanged += OnChildTrendPropertyChanged;
        WmTrend.PropertyChanged += OnChildTrendPropertyChanged;
    }

    /// <summary>当前选中子页是否正在拉取/绘制趋势数据（用于全页 Loading 遮罩）。</summary>
    public bool IsActiveTrendLoading =>
        ActiveTrendTab == 0 ? PdTrend.IsTrendDataLoading : WmTrend.IsTrendDataLoading;

    public bool IsPdTrendTabActive => ActiveTrendTab == 0;
    public bool IsWmTrendTabActive => ActiveTrendTab == 1;

    partial void OnActiveTrendTabChanged(int value)
    {
        OnPropertyChanged(nameof(IsPdTrendTabActive));
        OnPropertyChanged(nameof(IsWmTrendTabActive));
        OnPropertyChanged(nameof(IsActiveTrendLoading));
    }

    private void OnChildTrendPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TrendViewModel.IsTrendDataLoading))
            OnPropertyChanged(nameof(IsActiveTrendLoading));
    }
}
