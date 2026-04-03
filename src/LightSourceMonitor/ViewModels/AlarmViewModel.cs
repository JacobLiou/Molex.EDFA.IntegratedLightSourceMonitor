using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LightSourceMonitor.Data;
using LightSourceMonitor.Helpers;
using LightSourceMonitor.Models;
using LightSourceMonitor.Services.Alarm;
using LightSourceMonitor.Services.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace LightSourceMonitor.ViewModels;

public partial class AlarmRecordViewModel : ObservableObject
{
    public long Id { get; set; }
    [ObservableProperty] private DateTime _occurredAt;
    [ObservableProperty] private string _level = "严重";
    [ObservableProperty] private string _levelColor = "#FF1744";
    [ObservableProperty] private string _channelName = "";
    [ObservableProperty] private double _measuredValue;
    [ObservableProperty] private double _specValue;
    [ObservableProperty] private double _delta;
    [ObservableProperty] private bool _emailSent;
}

public partial class AlarmViewModel : ObservableObject, IDisposable
{
    private readonly IServiceProvider _services;
    private readonly IAlarmService _alarmService;
    private readonly IChannelCatalog _channelCatalog;
    private bool _disposed;

    [ObservableProperty] private ObservableCollection<AlarmRecordViewModel> _alarms = new();
    [ObservableProperty] private string _selectedLevel = "全部";
    [ObservableProperty] private string _selectedTimeRange = "最近24小时";
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private bool _isMuted;
    [ObservableProperty] private int _totalAlarmCount;

    public string[] LevelOptions { get; } = { "全部", "严重", "警告" };
    public string[] TimeRangeOptions { get; } = { "最近1小时", "最近24小时", "最近7天", "全部" };

    public AlarmViewModel(IServiceProvider services, IAlarmService alarmService, IChannelCatalog channelCatalog)
    {
        _services = services;
        _alarmService = alarmService;
        _channelCatalog = channelCatalog;

        _alarmService.AlarmRaised += OnAlarmRaised;
        LoadAlarmsAsync().SafeFireAndForget("AlarmViewModel.LoadAlarms");
    }

    partial void OnSelectedLevelChanged(string value) => LoadAlarmsAsync().SafeFireAndForget("AlarmVM.FilterLevel");
    partial void OnSelectedTimeRangeChanged(string value) => LoadAlarmsAsync().SafeFireAndForget("AlarmVM.FilterTime");
    partial void OnSearchTextChanged(string value) => LoadAlarmsAsync().SafeFireAndForget("AlarmVM.FilterSearch");

    private async Task LoadAlarmsAsync()
    {
        try
        {
            using var scope = _services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<MonitorDbContext>();

            IQueryable<AlarmEvent> query = db.AlarmEvents.AsQueryable();

            var cutoff = SelectedTimeRange switch
            {
                "最近1小时" => DateTime.Now.AddHours(-1),
                "最近24小时" => DateTime.Now.AddHours(-24),
                "最近7天" => DateTime.Now.AddDays(-7),
                _ => (DateTime?)null
            };
            if (cutoff.HasValue)
                query = query.Where(a => a.OccurredAt >= cutoff.Value);

            if (SelectedLevel == "严重")
                query = query.Where(a => a.Level == AlarmLevel.Critical);
            else if (SelectedLevel == "警告")
                query = query.Where(a => a.Level == AlarmLevel.Warning);

            var alarmList = await query.OrderByDescending(a => a.OccurredAt).Take(500).ToListAsync();

            var records = alarmList.Select(a => ToViewModel(a)).ToList();

            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                var search = SearchText.Trim();
                records = records.Where(r =>
                    r.ChannelName.Contains(search, StringComparison.OrdinalIgnoreCase)).ToList();
            }

            AsyncHelper.SafeDispatcherInvoke(() =>
            {
                Alarms.Clear();
                foreach (var r in records)
                    Alarms.Add(r);
                TotalAlarmCount = Alarms.Count;
            });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to load alarm records");
        }
    }

    private void OnAlarmRaised(AlarmEvent alarm)
    {
        AsyncHelper.SafeDispatcherInvoke(() =>
        {
            var vm = ToViewModel(alarm);
            Alarms.Insert(0, vm);
            while (Alarms.Count > 500)
                Alarms.RemoveAt(Alarms.Count - 1);
            TotalAlarmCount = Alarms.Count;
        });
    }

    private AlarmRecordViewModel ToViewModel(AlarmEvent a)
    {
        string channelDisplay;
        if (WmAlarmChannelIds.IsWavelengthServiceAlarm(a.ChannelId)
            && WmAlarmChannelIds.TryDecode(a.ChannelId, out var wmIdx))
        {
            channelDisplay = $"WM波长服务-路{wmIdx}";
        }
        else
        {
            var ch = _channelCatalog.GetById(a.ChannelId);
            channelDisplay = ch != null
                ? $"{ch.DeviceSN}-{ch.ChannelName}"
                : "未知通道";
        }

        return new AlarmRecordViewModel
        {
            Id = a.Id,
            OccurredAt = a.OccurredAt,
            Level = a.Level == AlarmLevel.Critical ? "严重" : "警告",
            LevelColor = a.Level == AlarmLevel.Critical ? "#FF1744" : "#FFAB00",
            ChannelName = channelDisplay,
            MeasuredValue = a.MeasuredValue,
            SpecValue = a.SpecValue,
            Delta = a.Delta,
            EmailSent = a.EmailSent
        };
    }

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

    [RelayCommand]
    private async Task Refresh()
    {
        await LoadAlarmsAsync();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _alarmService.AlarmRaised -= OnAlarmRaised;
    }
}
