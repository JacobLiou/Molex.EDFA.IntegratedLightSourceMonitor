using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace LightSourceMonitor.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IServiceProvider _services;
    private readonly DispatcherTimer _uptimeTimer;
    private DateTime _startTime;

    [ObservableProperty] private object? _currentPage;
    [ObservableProperty] private int _selectedNavIndex;
    [ObservableProperty] private string _uptimeText = "00:00:00";
    [ObservableProperty] private string _globalStatus = "正常";
    [ObservableProperty] private string _globalStatusColor = "#00E676";
    [ObservableProperty] private string _lastAcquisitionTime = "--";

    public MainViewModel(IServiceProvider services)
    {
        _services = services;
        _startTime = DateTime.Now;

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

    public void NavigateTo(int index)
    {
        CurrentPage = index switch
        {
            0 => _services.GetService(typeof(OverviewViewModel)),
            1 => _services.GetService(typeof(TrendViewModel)),
            2 => _services.GetService(typeof(AlarmViewModel)),
            3 => _services.GetService(typeof(SettingsViewModel)),
            _ => CurrentPage
        };
    }

    public void UpdateGlobalStatus(string status, string color)
    {
        GlobalStatus = status;
        GlobalStatusColor = color;
    }

    public void UpdateLastAcquisitionTime()
    {
        LastAcquisitionTime = DateTime.Now.ToString("HH:mm:ss");
    }
}
