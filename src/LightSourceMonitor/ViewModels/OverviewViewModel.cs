using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LightSourceMonitor.Helpers;
using LightSourceMonitor.Models;
using LightSourceMonitor.Services.Acquisition;
using LightSourceMonitor.Services.Alarm;
using LightSourceMonitor.Services.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Serilog;
using System.Windows.Threading;

namespace LightSourceMonitor.ViewModels;

public partial class ChannelCardViewModel : ObservableObject
{
    public int ChannelId { get; set; }
    [ObservableProperty] private string _channelName = "";
    [ObservableProperty] private string _deviceSn = "";
    [ObservableProperty] private double _currentValue;
    [ObservableProperty] private string _unit = "dBm";
    [ObservableProperty] private string _statusColor = "#616161";
    [ObservableProperty] private double _specValue;
    [ObservableProperty] private double _delta;
    [ObservableProperty] private double _maxValue = double.MinValue;
    [ObservableProperty] private double _minValue = double.MaxValue;
    [ObservableProperty] private string _lastUpdate = "--";
    [ObservableProperty] private bool _isOnline = false;
    [ObservableProperty] private string _displayValue = "---";

    public void SetOffline()
    {
        IsOnline = false;
        StatusColor = "#616161";
        DisplayValue = "---";
    }

    public void UpdateWithMeasurement(double power, double alarmDelta)
    {
        IsOnline = true;
        CurrentValue = power;
        DisplayValue = power.ToString("F2");

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

public partial class WavelengthRowViewModel : ObservableObject
{
    [ObservableProperty] private int _channelIndex;
    [ObservableProperty] private string _wavelengthDisplay = "---";
    [ObservableProperty] private string _powerDisplay = "---";
    [ObservableProperty] private string _lastUpdate = "--";
    [ObservableProperty] private bool _isOnline;
}

public partial class WbaMetricViewModel : ObservableObject
{
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _unit = "";
    [ObservableProperty] private string _displayValue = "---";
}

public partial class DeviceGroupViewModel : ObservableObject
{
    [ObservableProperty] private string _deviceName = "";
    [ObservableProperty] private string _deviceSn = "";
    [ObservableProperty] private bool _isOnline;
    public ObservableCollection<ChannelCardViewModel> Channels { get; } = new();
}

public partial class WbaDeviceGroupViewModel : ObservableObject
{
    [ObservableProperty] private string _deviceName = "";
    [ObservableProperty] private string _deviceSn = "";
    [ObservableProperty] private bool _isOnline;
    [ObservableProperty] private bool _hasData;
    [ObservableProperty] private string _lastUpdate = "--";
    [ObservableProperty] private string _pressureDisplay = "---";
    public ObservableCollection<WbaMetricViewModel> Temperatures { get; } = new();
    public ObservableCollection<WbaMetricViewModel> Voltages { get; } = new();
}

public partial class OverviewViewModel : ObservableObject, IDisposable
{
    private bool _disposed;
    private readonly IServiceProvider _services;
    private readonly IChannelCatalog _channelCatalog;
    private readonly IAcquisitionService _acquisitionService;
    private readonly IAlarmService _alarmService;
    private readonly DriverSettings _driverSettings;
    private readonly Dictionary<int, (ChannelCardViewModel card, double alarmDelta)> _channelMap = new();
    private readonly Dictionary<string, DeviceGroupViewModel> _deviceMap = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, WbaDeviceGroupViewModel> _wbaDeviceMap = new(StringComparer.OrdinalIgnoreCase);
    private readonly DispatcherTimer _kpiRefreshTimer;
    private bool _kpiRefreshPending;

    [ObservableProperty] private int _totalChannels;
    [ObservableProperty] private int _normalCount;
    [ObservableProperty] private int _warningCount;
    [ObservableProperty] private int _offlineCount;
    [ObservableProperty] private string _wavelengthTableSubtitle = "—";

    public ObservableCollection<DeviceGroupViewModel> DeviceGroups { get; } = new();
    public ObservableCollection<WavelengthRowViewModel> WavelengthRows { get; } = new();
    public ObservableCollection<WbaDeviceGroupViewModel> WbaDeviceGroups { get; } = new();
    public ObservableCollection<AlarmItemViewModel> RecentAlarms { get; } = new();

    public OverviewViewModel(IServiceProvider services, IChannelCatalog channelCatalog, IAcquisitionService acquisitionService, IAlarmService alarmService, IOptions<DriverSettings> driverOptions)
    {
        _services = services;
        _channelCatalog = channelCatalog;
        _acquisitionService = acquisitionService;
        _alarmService = alarmService;
        _driverSettings = driverOptions.Value;

        _acquisitionService.DataAcquired += OnDataAcquired;
        _acquisitionService.WavelengthTableUpdated += OnWavelengthTableUpdated;
        _acquisitionService.WbaTelemetryAcquired += OnWbaTelemetryAcquired;
        _acquisitionService.AcquisitionCycleCompleted += OnAcquisitionCycleCompleted;
        _acquisitionService.PdConnectionChanged += OnPdConnectionChanged;
        _acquisitionService.PdDeviceConnectionChanged += OnPdDeviceConnectionChanged;
        _alarmService.AlarmRaised += OnAlarmRaised;

        _kpiRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _kpiRefreshTimer.Tick += (_, _) =>
        {
            if (!_kpiRefreshPending) return;
            _kpiRefreshPending = false;
            RefreshKpi();
        };
        _kpiRefreshTimer.Start();

        LoadChannelsAsync().SafeFireAndForget("OverviewViewModel.LoadChannels");
    }

    private async Task LoadChannelsAsync()
    {
        await Task.Yield();
        var channels = _channelCatalog.GetEnabledChannels();

        AsyncHelper.SafeDispatcherInvoke(() =>
        {
            // Build PD device groups
            DeviceGroups.Clear();
            _channelMap.Clear();
            _deviceMap.Clear();

            var groups = channels.GroupBy(c => c.DeviceSN);
            foreach (var g in groups)
            {
                var group = new DeviceGroupViewModel
                {
                    DeviceName = $"PD Array ({g.Key})",
                    DeviceSn = g.Key,
                    IsOnline = _acquisitionService.IsPdConnected
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
                _deviceMap[group.DeviceSn] = group;
            }

            // Build WBA device groups
            WbaDeviceGroups.Clear();
            _wbaDeviceMap.Clear();

            foreach (var wba in _driverSettings.GetEffectiveWbaDevices())
            {
                var wbaGroup = new WbaDeviceGroupViewModel
                {
                    DeviceName = $"WBA ({wba.DeviceSN})",
                    DeviceSn = wba.DeviceSN,
                    IsOnline = false
                };
                wbaGroup.Temperatures.Add(new WbaMetricViewModel { Name = "Case", Unit = "°C" });
                wbaGroup.Temperatures.Add(new WbaMetricViewModel { Name = "Switch", Unit = "°C" });
                wbaGroup.Temperatures.Add(new WbaMetricViewModel { Name = "Board", Unit = "°C" });
                wbaGroup.Temperatures.Add(new WbaMetricViewModel { Name = "Lid", Unit = "°C" });
                wbaGroup.Voltages.Add(new WbaMetricViewModel { Name = "V1", Unit = "V" });
                wbaGroup.Voltages.Add(new WbaMetricViewModel { Name = "V2", Unit = "V" });
                wbaGroup.Voltages.Add(new WbaMetricViewModel { Name = "V3", Unit = "V" });
                wbaGroup.Voltages.Add(new WbaMetricViewModel { Name = "V4", Unit = "V" });

                WbaDeviceGroups.Add(wbaGroup);
                _wbaDeviceMap[wba.DeviceSN] = wbaGroup;
            }

            RequestKpiRefresh();
        });
    }

    private void OnPdConnectionChanged(bool connected)
    {
        AsyncHelper.SafeDispatcherInvoke(() =>
        {
            foreach (var group in DeviceGroups)
            {
                group.IsOnline = connected;
                if (!connected)
                {
                    foreach (var channel in group.Channels)
                        channel.SetOffline();
                }
            }
            RequestKpiRefresh();
        });
    }

    private void OnPdDeviceConnectionChanged(IReadOnlyDictionary<string, bool> states)
    {
        AsyncHelper.SafeDispatcherInvoke(() =>
        {
            foreach (var group in DeviceGroups)
            {
                if (states.TryGetValue(group.DeviceSn, out var online))
                {
                    group.IsOnline = online;
                    if (!online)
                    {
                        foreach (var channel in group.Channels)
                            channel.SetOffline();
                    }
                }
            }
            RequestKpiRefresh();
        });
    }

    private void OnDataAcquired(Dictionary<int, MeasurementRecord> batch)
    {
        AsyncHelper.SafeDispatcherInvoke(() =>
        {
            foreach (var kvp in batch)
            {
                if (_channelMap.TryGetValue(kvp.Key, out var entry))
                {
                    entry.card.UpdateWithMeasurement(kvp.Value.Power, entry.alarmDelta);
                }
            }
            RefreshKpi();
        });
    }

    private void OnWavelengthTableUpdated(WavelengthTableSnapshot snapshot)
    {
        AsyncHelper.SafeDispatcherInvoke(() =>
        {
            WavelengthTableSubtitle = string.IsNullOrEmpty(snapshot.QueryDeviceId)
                ? "—"
                : $"QUERY {snapshot.QueryDeviceId}";

            WavelengthRows.Clear();
            foreach (var r in snapshot.Rows)
            {
                WavelengthRows.Add(new WavelengthRowViewModel
                {
                    ChannelIndex = r.ChannelIndex,
                    WavelengthDisplay = r.IsValid ? r.WavelengthNm.ToString("F3") : "---",
                    PowerDisplay = r.IsValid ? r.WmPowerDbm.ToString("F2") : "---",
                    LastUpdate = snapshot.Timestamp.ToString("HH:mm:ss"),
                    IsOnline = r.IsValid
                });
            }
        });
    }

    private void OnWbaTelemetryAcquired(IReadOnlyDictionary<string, WbaTelemetrySnapshot> snapshots)
    {
        AsyncHelper.SafeDispatcherInvoke(() =>
        {
            foreach (var kvp in snapshots)
            {
                if (!_wbaDeviceMap.TryGetValue(kvp.Key, out var wbaGroup))
                    continue;

                var t = kvp.Value;
                if (t.Temperatures.Length < 4 || t.Voltages.Length < 4)
                    continue;

                for (int i = 0; i < 4; i++)
                {
                    wbaGroup.Temperatures[i].DisplayValue = t.Temperatures[i].ToString("F2");
                    wbaGroup.Voltages[i].DisplayValue = t.Voltages[i].ToString("F3");
                }

                wbaGroup.PressureDisplay = t.AtmospherePressure.ToString("F3");
                wbaGroup.LastUpdate = t.Timestamp.ToString("HH:mm:ss");
                wbaGroup.IsOnline = true;
                wbaGroup.HasData = true;
            }
        });
    }

    private void OnAlarmRaised(AlarmEvent alarm)
    {
        AsyncHelper.SafeDispatcherInvoke(() =>
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

    private void OnAcquisitionCycleCompleted(DateTime cycleTime)
    {
        AsyncHelper.SafeDispatcherInvoke(() =>
        {
            // 更新主窗口的最后采集时间
            try
            {
                var mainVm = _services.GetRequiredService<MainViewModel>();
                mainVm.UpdateLastAcquisitionTime(cycleTime);
            }
            catch
            {
                // 日志记录可能需要添加，但不影响采集流程
            }
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
        int total = 0, normal = 0, warning = 0, offline = 0;
        foreach (var group in DeviceGroups)
        {
            foreach (var ch in group.Channels)
            {
                total++;
                if (!ch.IsOnline) offline++;
                else if (ch.StatusColor == "#00E676") normal++;
                else warning++;
            }
        }
        TotalChannels = total;
        NormalCount = normal;
        WarningCount = warning;
        OfflineCount = offline;
    }

    private void RequestKpiRefresh()
    {
        _kpiRefreshPending = true;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _acquisitionService.DataAcquired -= OnDataAcquired;
        _acquisitionService.WavelengthTableUpdated -= OnWavelengthTableUpdated;
        _acquisitionService.WbaTelemetryAcquired -= OnWbaTelemetryAcquired;
        _acquisitionService.AcquisitionCycleCompleted -= OnAcquisitionCycleCompleted;
        _acquisitionService.PdConnectionChanged -= OnPdConnectionChanged;
        _acquisitionService.PdDeviceConnectionChanged -= OnPdDeviceConnectionChanged;
        _alarmService.AlarmRaised -= OnAlarmRaised;
        _kpiRefreshTimer.Stop();
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
