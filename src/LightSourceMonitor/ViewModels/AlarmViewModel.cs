using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LightSourceMonitor.Data;
using LightSourceMonitor.Helpers;
using LightSourceMonitor.Models;
using LightSourceMonitor.Services.Alarm;
using LightSourceMonitor.Services.Channels;
using LightSourceMonitor.Services.Localization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace LightSourceMonitor.ViewModels;

public partial class AlarmRecordViewModel : ObservableObject
{
    public long Id { get; set; }
    [ObservableProperty] private DateTime _occurredAt;
    [ObservableProperty] private string _level = "";
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
    private readonly ILanguageService _language;
    private bool _disposed;

    [ObservableProperty] private ObservableCollection<AlarmRecordViewModel> _alarms = new();
    [ObservableProperty] private string _selectedLevel = UiResourceKeys.AlarmLevelAll;
    [ObservableProperty] private string _selectedTimeRange = UiResourceKeys.TimeRange24H;
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private bool _isMuted;
    [ObservableProperty] private int _totalAlarmCount;
    [ObservableProperty] private string _alarmTotalSummary = "";

    public string[] LevelFilterKeys { get; } =
        [UiResourceKeys.AlarmLevelAll, UiResourceKeys.AlarmLevelCritical, UiResourceKeys.AlarmLevelWarning];

    public string[] AlarmTimeRangeKeys { get; } =
    [
        UiResourceKeys.TimeRange1H,
        UiResourceKeys.TimeRange24H,
        UiResourceKeys.TimeRange7D,
        UiResourceKeys.TimeRangeAll
    ];

    public AlarmViewModel(IServiceProvider services, IAlarmService alarmService, IChannelCatalog channelCatalog,
        ILanguageService language)
    {
        _services = services;
        _alarmService = alarmService;
        _channelCatalog = channelCatalog;
        _language = language;

        _alarmService.AlarmRaised += OnAlarmRaised;
        _language.LanguageChanged += (_, _) =>
        {
            AsyncHelper.SafeDispatcherInvoke(RefreshAlarmTotalSummary);
            LoadAlarmsAsync().SafeFireAndForget("AlarmVM.LangChanged");
        };
        LoadAlarmsAsync().SafeFireAndForget("AlarmViewModel.LoadAlarms");
        RefreshAlarmTotalSummary();
    }

    partial void OnSelectedLevelChanged(string value) => LoadAlarmsAsync().SafeFireAndForget("AlarmVM.FilterLevel");
    partial void OnSelectedTimeRangeChanged(string value) => LoadAlarmsAsync().SafeFireAndForget("AlarmVM.FilterTime");
    partial void OnSearchTextChanged(string value) => LoadAlarmsAsync().SafeFireAndForget("AlarmVM.FilterSearch");

    partial void OnTotalAlarmCountChanged(int value) => RefreshAlarmTotalSummary();

    private void RefreshAlarmTotalSummary()
    {
        AlarmTotalSummary = string.Format(_language.GetString("Alarm_TotalCount"), TotalAlarmCount);
    }

    private async Task LoadAlarmsAsync()
    {
        try
        {
            using var scope = _services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<MonitorDbContext>();

            IQueryable<AlarmEvent> query = db.AlarmEvents.AsQueryable();

            var cutoff = SelectedTimeRange switch
            {
                UiResourceKeys.TimeRange1H => DateTime.Now.AddHours(-1),
                UiResourceKeys.TimeRange24H => DateTime.Now.AddHours(-24),
                UiResourceKeys.TimeRange7D => DateTime.Now.AddDays(-7),
                UiResourceKeys.TimeRangeAll => (DateTime?)null,
                _ => DateTime.Now.AddHours(-24)
            };
            if (cutoff.HasValue)
                query = query.Where(a => a.OccurredAt >= cutoff.Value);

            if (SelectedLevel == UiResourceKeys.AlarmLevelCritical)
                query = query.Where(a => a.Level == AlarmLevel.Critical);
            else if (SelectedLevel == UiResourceKeys.AlarmLevelWarning)
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
            channelDisplay = string.Format(_language.GetString("Alarm_Channel_WmFmt"), wmIdx);
        }
        else
        {
            var ch = _channelCatalog.GetById(a.ChannelId);
            channelDisplay = ch != null
                ? $"{ch.DeviceSN}-{ch.ChannelName}"
                : _language.GetString("Alarm_Channel_Unknown");
        }

        return new AlarmRecordViewModel
        {
            Id = a.Id,
            OccurredAt = a.OccurredAt,
            Level = a.Level == AlarmLevel.Critical
                ? _language.GetString("Alarm_Level_Critical")
                : _language.GetString("Alarm_Level_Warning"),
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
        SelectedLevel = UiResourceKeys.AlarmLevelAll;
        SelectedTimeRange = UiResourceKeys.TimeRange24H;
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
