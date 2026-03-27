using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LightSourceMonitor.Models;

namespace LightSourceMonitor.ViewModels;

public partial class ChannelCardViewModel : ObservableObject
{
    [ObservableProperty] private string _channelName = "";
    [ObservableProperty] private string _deviceSn = "";
    [ObservableProperty] private double _currentValue;
    [ObservableProperty] private string _unit = "dBm";
    [ObservableProperty] private string _statusColor = "#00E676";
    [ObservableProperty] private double _specValue;
    [ObservableProperty] private double _delta;
    [ObservableProperty] private double _maxValue;
    [ObservableProperty] private double _minValue;
    [ObservableProperty] private string _lastUpdate = "--";
    [ObservableProperty] private ObservableCollection<double> _sparklineValues = new();

    public void UpdateStatus(double alarmDelta)
    {
        Delta = Math.Abs(MaxValue - MinValue);
        if (Delta > alarmDelta)
            StatusColor = "#FF1744";
        else if (Delta > alarmDelta * 0.8)
            StatusColor = "#FFAB00";
        else
            StatusColor = "#00E676";
        LastUpdate = DateTime.Now.ToString("HH:mm:ss");
    }
}

public partial class DeviceGroupViewModel : ObservableObject
{
    [ObservableProperty] private string _deviceName = "";
    [ObservableProperty] private string _deviceSn = "";
    [ObservableProperty] private bool _isOnline;
    public ObservableCollection<ChannelCardViewModel> Channels { get; } = new();
}

public partial class OverviewViewModel : ObservableObject
{
    [ObservableProperty] private int _totalChannels;
    [ObservableProperty] private int _normalCount;
    [ObservableProperty] private int _warningCount;
    [ObservableProperty] private int _offlineCount;

    public ObservableCollection<DeviceGroupViewModel> DeviceGroups { get; } = new();
    public ObservableCollection<AlarmItemViewModel> RecentAlarms { get; } = new();

    public void RefreshKpi()
    {
        int total = 0, normal = 0, warning = 0;
        foreach (var group in DeviceGroups)
        {
            foreach (var ch in group.Channels)
            {
                total++;
                if (ch.StatusColor == "#00E676") normal++;
                else warning++;
            }
        }
        TotalChannels = total;
        NormalCount = normal;
        WarningCount = warning;
        OfflineCount = total - normal - warning;
    }
}

public partial class AlarmItemViewModel : ObservableObject
{
    [ObservableProperty] private DateTime _timestamp;
    [ObservableProperty] private string _channelName = "";
    [ObservableProperty] private string _message = "";
    [ObservableProperty] private string _levelColor = "#FF1744";
    [ObservableProperty] private string _level = "严重";
}
