using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LightSourceMonitor.Data;
using LightSourceMonitor.Drivers;
using LightSourceMonitor.Helpers;
using LightSourceMonitor.Models;
using LightSourceMonitor.Services.Acquisition;
using LightSourceMonitor.Services.Channels;
using LightSourceMonitor.Services.Email;
using LightSourceMonitor.Services.Tms;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LightSourceMonitor.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private const int MinSamplingIntervalMs = 100;
    private const int MinCycleEveryN = 1;

    private readonly IServiceProvider _services;
    private readonly IEmailService _emailService;
    private readonly ITmsService _tmsService;
    private readonly IChannelCatalog _channelCatalog;
    private readonly IPdDriverManager _pdDriverManager;
    private readonly DriverSettings _driverSettings;
    private readonly ILogger<SettingsViewModel> _logger;

    [ObservableProperty] private string _smtpServer = "";
    [ObservableProperty] private int _smtpPort = 587;
    [ObservableProperty] private string _smtpUsername = "";
    [ObservableProperty] private string _smtpPassword = "";
    [ObservableProperty] private string _fromAddress = "";
    [ObservableProperty] private string _recipients = "";

    [ObservableProperty] private double _pdAlarmDelta = 0.15;
    [ObservableProperty] private double _wmAlarmDelta = 0.05;

    [ObservableProperty] private string _tmsBaseUrl = "";
    [ObservableProperty] private string _tmsApiKey = "";
    [ObservableProperty] private int _tmsUploadIntervalSec = 300;

    [ObservableProperty] private int _samplingIntervalMs = 5000;
    [ObservableProperty] private int _wmSweepEveryN = 10;
    [ObservableProperty] private int _dbWriteEveryN = 10;

    [ObservableProperty] private string _statusMessage = "";
    [ObservableProperty] private string _acqParamsStatus = "";
    [ObservableProperty] private string _driverMode = "";
    [ObservableProperty] private string _pdDriverStatus = "";
    [ObservableProperty] private string _wmDriverStatus = "";
    [ObservableProperty] private bool _isAcquisitionRunning;

    public ObservableCollection<LaserChannel> Channels { get; } = new();

    public SettingsViewModel(IServiceProvider services, IEmailService emailService,
                             ITmsService tmsService, IChannelCatalog channelCatalog, IPdDriverManager pdDriverManager,
                             IOptions<DriverSettings> driverOptions,
                             ILogger<SettingsViewModel> logger)
    {
        _services = services;
        _emailService = emailService;
        _tmsService = tmsService;
        _channelCatalog = channelCatalog;
        _pdDriverManager = pdDriverManager;
        _driverSettings = driverOptions.Value;
        _logger = logger;
        LoadSettingsAsync().SafeFireAndForget("SettingsViewModel.LoadSettings");
    }

    private async Task LoadSettingsAsync()
    {
        try
        {
            using var scope = _services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<MonitorDbContext>();

            var email = await db.EmailConfigs.FirstOrDefaultAsync();
            if (email != null)
            {
                SmtpServer = email.SmtpServer;
                SmtpPort = email.SmtpPort;
                SmtpUsername = email.Username;
                FromAddress = email.FromAddress;
                Recipients = email.Recipients;
            }

            var tms = await db.TmsConfigs.FirstOrDefaultAsync();
            if (tms != null)
            {
                TmsBaseUrl = tms.BaseUrl;
                TmsApiKey = tms.ApiKey;
                TmsUploadIntervalSec = tms.UploadIntervalSec;
            }

            var channels = _channelCatalog.GetAllChannels()
                .OrderBy(c => c.DeviceSN, StringComparer.OrdinalIgnoreCase)
                .ThenBy(c => c.ChannelIndex)
                .ToList();
            Channels.Clear();
            foreach (var ch in channels) Channels.Add(ch);

            var wmDriver = _services.GetRequiredService<IWavelengthMeterDriver>();
            DriverMode = string.Equals(_driverSettings.Mode, "Simulated", StringComparison.OrdinalIgnoreCase)
                ? "模拟模式 (Simulated)"
                : "硬件模式 (Hardware)";

            var states = _pdDriverManager.ConnectionStates;
            int connectedCount = states.Count(kvp => kvp.Value);
            PdDriverStatus = states.Count == 0
                ? "未配置PD设备"
                : $"{connectedCount}/{states.Count} 已连接";
            WmDriverStatus = wmDriver.IsInitialized ? "已初始化" : "未初始化";

            var acq = _services.GetRequiredService<IAcquisitionService>();
            IsAcquisitionRunning = acq.IsRunning;
            SamplingIntervalMs = acq.SamplingIntervalMs;
            WmSweepEveryN = acq.WmSweepEveryN;
            DbWriteEveryN = acq.DbWriteEveryN;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load settings");
        }
    }

    [RelayCommand]
    private async Task SaveSettings()
    {
        try
        {
            using var scope = _services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<MonitorDbContext>();

            var email = await db.EmailConfigs.FirstOrDefaultAsync() ?? new EmailConfig();
            email.SmtpServer = SmtpServer;
            email.SmtpPort = SmtpPort;
            email.Username = SmtpUsername;
            email.FromAddress = FromAddress;
            email.Recipients = Recipients;
            if (email.Id == 0) db.EmailConfigs.Add(email);

            var tms = await db.TmsConfigs.FirstOrDefaultAsync() ?? new TmsConfig();
            tms.BaseUrl = TmsBaseUrl;
            tms.ApiKey = TmsApiKey;
            tms.UploadIntervalSec = TmsUploadIntervalSec;
            tms.IsEnabled = !string.IsNullOrWhiteSpace(TmsBaseUrl);
            if (tms.Id == 0) db.TmsConfigs.Add(tms);

            await db.SaveChangesAsync();
            StatusMessage = "设置已保存";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save settings");
            StatusMessage = "保存失败: " + ex.Message;
        }
    }

    [RelayCommand]
    private async Task TestSmtp()
    {
        try
        {
            StatusMessage = "正在发送测试邮件...";
            await _emailService.SendTestEmailAsync();
            StatusMessage = "测试邮件已发送";
        }
        catch (Exception ex)
        {
            StatusMessage = "发送失败: " + ex.Message;
        }
    }

    [RelayCommand]
    private async Task TestTmsConnection()
    {
        try
        {
            StatusMessage = "正在测试TMS连接...";
            var ok = await _tmsService.TestConnectionAsync();
            StatusMessage = ok ? "TMS连接成功" : "TMS连接失败";
        }
        catch (Exception ex)
        {
            StatusMessage = "连接失败: " + ex.Message;
        }
    }

    [RelayCommand]
    private Task ApplyAcquisitionParams()
    {
        try
        {
            var acq = _services.GetRequiredService<IAcquisitionService>();

            // Prevent invalid values from breaking acquisition loop modulo operations.
            SamplingIntervalMs = Math.Max(MinSamplingIntervalMs, SamplingIntervalMs);
            WmSweepEveryN = Math.Max(MinCycleEveryN, WmSweepEveryN);
            DbWriteEveryN = Math.Max(MinCycleEveryN, DbWriteEveryN);

            acq.SamplingIntervalMs = SamplingIntervalMs;
            acq.WmSweepEveryN = WmSweepEveryN;
            acq.DbWriteEveryN = DbWriteEveryN;
            AcqParamsStatus = $"已应用 — 间隔{SamplingIntervalMs}ms, WM每{WmSweepEveryN}次, DB每{DbWriteEveryN}次";
            _logger.LogInformation("Acquisition params updated: interval={Interval}ms, wmEvery={WmN}, dbEvery={DbN}",
                SamplingIntervalMs, WmSweepEveryN, DbWriteEveryN);
        }
        catch (Exception ex)
        {
            AcqParamsStatus = "应用失败: " + ex.Message;
            _logger.LogError(ex, "Failed to apply acquisition params");
        }
        return Task.CompletedTask;
    }
}
