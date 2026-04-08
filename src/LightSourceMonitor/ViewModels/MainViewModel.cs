using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using LightSourceMonitor.Models;
using LightSourceMonitor.Services.Localization;
using Microsoft.Extensions.Options;
using Serilog;

namespace LightSourceMonitor.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IServiceProvider _services;
    private readonly ILanguageService _language;
    private readonly DispatcherTimer _uptimeTimer;
    private DateTime _startTime;
    private readonly Dictionary<int, object?> _pageCache = new();

    [ObservableProperty] private object? _currentPage;
    [ObservableProperty] private int _selectedNavIndex;
    [ObservableProperty] private string _uptimeText = "00:00:00";
    [ObservableProperty] private GlobalStatusKind _globalStatusKind = GlobalStatusKind.Normal;
    [ObservableProperty] private string _globalStatusText = "";
    [ObservableProperty] private string _globalStatusColor = "#00E676";
    [ObservableProperty] private string _lastAcquisitionTime = "--";
    [ObservableProperty] private bool _isSimulationMode;

    public MainViewModel(IServiceProvider services, IOptions<DriverSettings> driverOptions, ILanguageService language)
    {
        _services = services;
        _language = language;
        _startTime = DateTime.Now;
        IsSimulationMode = string.Equals(driverOptions.Value.Mode, "Simulated", StringComparison.OrdinalIgnoreCase);

        RefreshGlobalStatusText();
        _language.LanguageChanged += (_, _) => RefreshGlobalStatusText();

        _uptimeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _uptimeTimer.Tick += (_, _) =>
        {
            var elapsed = DateTime.Now - _startTime;
            UptimeText = elapsed.Days > 0
                ? $"{elapsed.Days}d {elapsed.Hours:D2}h {elapsed.Minutes:D2}m"
                : $"{elapsed.Hours:D2}:{elapsed.Minutes:D2}:{elapsed.Seconds:D2}";
        };
        _uptimeTimer.Start();
    }

    partial void OnSelectedNavIndexChanged(int value)
    {
        NavigateTo(value);
    }

    partial void OnGlobalStatusKindChanged(GlobalStatusKind value)
    {
        RefreshGlobalStatusText();
    }

    private void RefreshGlobalStatusText()
    {
        GlobalStatusText = GlobalStatusKind == GlobalStatusKind.Alarm
            ? _language.GetString("Status_Alarm")
            : _language.GetString("Status_Normal");
    }

    public void NavigateTo(int index)
    {
        try
        {
            if (!_pageCache.TryGetValue(index, out var page) || page == null)
            {
                page = index switch
                {
                    0 => _services.GetService(typeof(OverviewViewModel)),
                    1 => _services.GetService(typeof(TrendViewModel)),
                    2 => _services.GetService(typeof(WmTrendViewModel)),
                    3 => _services.GetService(typeof(AlarmViewModel)),
                    4 => _services.GetService(typeof(SettingsViewModel)),
                    _ => CurrentPage
                };
                _pageCache[index] = page;
            }

            CurrentPage = page;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to navigate to page index {Index}", index);
        }
    }

    public void UpdateGlobalStatus(GlobalStatusKind kind, string color)
    {
        GlobalStatusKind = kind;
        GlobalStatusColor = color;
    }

    public void UpdateLastAcquisitionTime()
    {
        LastAcquisitionTime = DateTime.Now.ToString("HH:mm:ss");
    }

    public void UpdateLastAcquisitionTime(DateTime acquisitionTime)
    {
        LastAcquisitionTime = acquisitionTime.ToString("HH:mm:ss");
    }
}
