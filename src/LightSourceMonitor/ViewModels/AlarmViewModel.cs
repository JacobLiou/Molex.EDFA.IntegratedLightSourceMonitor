using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LightSourceMonitor.Data;
using LightSourceMonitor.Models;
using LightSourceMonitor.Services.Alarm;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

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

public partial class AlarmViewModel : ObservableObject
{
    private readonly IServiceProvider _services;
    private readonly IAlarmService _alarmService;
    private Dictionary<int, string> _channelNames = new();

    [ObservableProperty] private ObservableCollection<AlarmRecordViewModel> _alarms = new();
    [ObservableProperty] private string _selectedLevel = "全部";
    [ObservableProperty] private string _selectedTimeRange = "最近24小时";
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private bool _isMuted;
    [ObservableProperty] private int _totalAlarmCount;

    public string[] LevelOptions { get; } = { "全部", "严重", "警告" };
    public string[] TimeRangeOptions { get; } = { "最近1小时", "最近24小时", "最近7天", "全部" };

    public AlarmViewModel(IServiceProvider services, IAlarmService alarmService)
    {
        _services = services;
        _alarmService = alarmService;

        _alarmService.AlarmRaised += OnAlarmRaised;
        _ = LoadAlarmsAsync();
    }

    partial void OnSelectedLevelChanged(string value) => _ = LoadAlarmsAsync();
    partial void OnSelectedTimeRangeChanged(string value) => _ = LoadAlarmsAsync();
    partial void OnSearchTextChanged(string value) => _ = LoadAlarmsAsync();

    private async Task LoadAlarmsAsync()
    {
        try
        {
            using var scope = _services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<MonitorDbContext>();

            var channels = await db.LaserChannels.ToListAsync();
            _channelNames = channels.ToDictionary(c => c.Id, c => c.ChannelName);

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

            Application.Current.Dispatcher.Invoke(() =>
            {
                Alarms.Clear();
                foreach (var r in records)
                    Alarms.Add(r);
                TotalAlarmCount = Alarms.Count;
            });
        }
        catch (Exception)
        {
            // silently fail on load
        }
    }

    private void OnAlarmRaised(AlarmEvent alarm)
    {
        Application.Current.Dispatcher.Invoke(() =>
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
        string chName = _channelNames.TryGetValue(a.ChannelId, out var name) ? name : $"CH{a.ChannelId}";
        return new AlarmRecordViewModel
        {
            Id = a.Id,
            OccurredAt = a.OccurredAt,
            Level = a.Level == AlarmLevel.Critical ? "严重" : "警告",
            LevelColor = a.Level == AlarmLevel.Critical ? "#FF1744" : "#FFAB00",
            ChannelName = chName,
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
}
