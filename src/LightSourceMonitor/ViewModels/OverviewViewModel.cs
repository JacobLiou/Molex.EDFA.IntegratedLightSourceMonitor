using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LightSourceMonitor.Data;
using LightSourceMonitor.Models;
using LightSourceMonitor.Services.Acquisition;
using LightSourceMonitor.Services.Alarm;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LightSourceMonitor.ViewModels;

public partial class ChannelCardViewModel : ObservableObject
{
    public int ChannelId { get; set; }
    [ObservableProperty] private string _channelName = "";
    [ObservableProperty] private string _deviceSn = "";
    [ObservableProperty] private double _currentValue;
    [ObservableProperty] private string _unit = "dBm";
    [ObservableProperty] private string _statusColor = "#00E676";
    [ObservableProperty] private double _specValue;
    [ObservableProperty] private double _delta;
    [ObservableProperty] private double _maxValue = double.MinValue;
    [ObservableProperty] private double _minValue = double.MaxValue;
    [ObservableProperty] private string _lastUpdate = "--";
    [ObservableProperty] private double _wavelength;

    public void UpdateWithMeasurement(double power, double wavelength, double alarmDelta)
    {
        CurrentValue = power;
        Wavelength = wavelength;

        if (power > MaxValue) MaxValue = power;
        if (power < MinValue) MinValue = power;

        Delta = MaxValue - MinValue;
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
    private readonly IServiceProvider _services;
    private readonly IAcquisitionService _acquisitionService;
    private readonly IAlarmService _alarmService;
    private readonly Dictionary<int, (ChannelCardViewModel card, double alarmDelta)> _channelMap = new();

    [ObservableProperty] private int _totalChannels;
    [ObservableProperty] private int _normalCount;
    [ObservableProperty] private int _warningCount;
    [ObservableProperty] private int _offlineCount;

    public ObservableCollection<DeviceGroupViewModel> DeviceGroups { get; } = new();
    public ObservableCollection<AlarmItemViewModel> RecentAlarms { get; } = new();

    public OverviewViewModel(IServiceProvider services, IAcquisitionService acquisitionService, IAlarmService alarmService)
    {
        _services = services;
        _acquisitionService = acquisitionService;
        _alarmService = alarmService;

        _acquisitionService.DataAcquired += OnDataAcquired;
        _alarmService.AlarmRaised += OnAlarmRaised;

        _ = LoadChannelsAsync();
    }

    private async Task LoadChannelsAsync()
    {
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MonitorDbContext>();
        var channels = await db.LaserChannels
            .Where(c => c.IsEnabled)
            .OrderBy(c => c.ChannelIndex)
            .ToListAsync();

        Application.Current.Dispatcher.Invoke(() =>
        {
            DeviceGroups.Clear();
            _channelMap.Clear();

            var groups = channels.GroupBy(c => c.DeviceSN);
            foreach (var g in groups)
            {
                var group = new DeviceGroupViewModel
                {
                    DeviceName = $"PD Array ({g.Key})",
                    DeviceSn = g.Key,
                    IsOnline = true
                };

                foreach (var ch in g)
                {
                    var card = new ChannelCardViewModel
                    {
                        ChannelId = ch.Id,
                        ChannelName = ch.ChannelName,
                        DeviceSn = ch.DeviceSN,
                        SpecValue = (ch.SpecPowerMin + ch.SpecPowerMax) / 2.0,
                        Unit = "dBm"
                    };
                    group.Channels.Add(card);
                    _channelMap[ch.Id] = (card, ch.AlarmDelta);
                }

                DeviceGroups.Add(group);
            }

            RefreshKpi();
        });
    }

    private void OnDataAcquired(Dictionary<int, MeasurementRecord> batch)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            foreach (var kvp in batch)
            {
                if (_channelMap.TryGetValue(kvp.Key, out var entry))
                {
                    entry.card.UpdateWithMeasurement(kvp.Value.Power, kvp.Value.Wavelength, entry.alarmDelta);
                }
            }
            RefreshKpi();
        });
    }

    private void OnAlarmRaised(AlarmEvent alarm)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            string channelName = _channelMap.TryGetValue(alarm.ChannelId, out var entry) ? entry.card.ChannelName : $"CH{alarm.ChannelId}";
            var item = new AlarmItemViewModel
            {
                Timestamp = alarm.OccurredAt,
                ChannelName = channelName,
                Message = $"{alarm.AlarmType}: {alarm.MeasuredValue:F3} (spec {alarm.SpecValue:F3}, Δ{alarm.Delta:F3})",
                Level = alarm.Level == AlarmLevel.Critical ? "严重" : "警告",
                LevelColor = alarm.Level == AlarmLevel.Critical ? "#FF1744" : "#FFAB00"
            };

            RecentAlarms.Insert(0, item);
            while (RecentAlarms.Count > 20)
                RecentAlarms.RemoveAt(RecentAlarms.Count - 1);
        });
    }

    [RelayCommand]
    private void NavigateToAlarms()
    {
        var mainVm = _services.GetRequiredService<MainViewModel>();
        mainVm.SelectedNavIndex = 2;
    }

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
        OfflineCount = 0;
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
