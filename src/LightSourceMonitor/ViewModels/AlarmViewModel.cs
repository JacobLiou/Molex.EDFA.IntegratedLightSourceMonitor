using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace LightSourceMonitor.ViewModels;

public partial class AlarmRecordViewModel : ObservableObject
{
    [ObservableProperty] private DateTime _occurredAt;
    [ObservableProperty] private string _level = "严重";
    [ObservableProperty] private string _levelColor = "#FF1744";
    [ObservableProperty] private string _channelName = "";
    [ObservableProperty] private double _measuredValue;
    [ObservableProperty] private double _specValue;
    [ObservableProperty] private double _delta;
    [ObservableProperty] private bool _emailSent;
}

public partial class AlarmViewModel : ObservableObject
{
    [ObservableProperty] private ObservableCollection<AlarmRecordViewModel> _alarms = new();
    [ObservableProperty] private string _selectedLevel = "全部";
    [ObservableProperty] private string _selectedTimeRange = "最近24小时";
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private bool _isMuted;

    public string[] LevelOptions { get; } = { "全部", "严重", "警告" };
    public string[] TimeRangeOptions { get; } = { "最近1小时", "最近24小时", "最近7天", "全部" };

    [RelayCommand]
    private void ToggleMute()
    {
        IsMuted = !IsMuted;
    }

    [RelayCommand]
    private void ClearFilters()
    {
        SelectedLevel = "全部";
        SelectedTimeRange = "最近24小时";
        SearchText = "";
    }
}
