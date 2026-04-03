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
using HandyControl.Controls;
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
    private readonly IWbaDeviceManager _wbaDeviceManager;
    private readonly DriverSettings _driverSettings;
    private readonly WavelengthServiceSettings _wmServiceSettings;
    private readonly ILogger<SettingsViewModel> _logger;

    [ObservableProperty] private string _emailApiUrl = "";
    [ObservableProperty] private string _recipients = "";

    [ObservableProperty] private double _pdAlarmDelta = 0.15;
    [ObservableProperty] private double _wmAlarmDelta = 0.05;

    [ObservableProperty] private string _tmsBaseUrl = "";
    [ObservableProperty] private string _tmsApiKey = "";
    [ObservableProperty] private int _tmsUploadIntervalSec = 300;

    [ObservableProperty] private int _samplingIntervalMs = 5000;
    [ObservableProperty] private int _wmSweepEveryN = 36;
    [ObservableProperty] private int _dbWriteEveryN = 10;

    [ObservableProperty] private string _statusMessage = "";
    [ObservableProperty] private string _acqParamsStatus = "";
    [ObservableProperty] private string _driverMode = "";
    [ObservableProperty] private string _pdDriverStatus = "";
    [ObservableProperty] private string _wbaDriverStatus = "";
    [ObservableProperty] private string _wmDriverStatus = "";
    [ObservableProperty] private bool _isAcquisitionRunning;
    [ObservableProperty] private string _wmConfigXmlPath = "";
    [ObservableProperty] private string _wmServiceMode = "";
    [ObservableProperty] private string _wmComPort = "";
    [ObservableProperty] private int _wmComBaudRate;
    [ObservableProperty] private int _wmTableChannelCount;
    [ObservableProperty] private string _wmQueryDeviceId = "";
    [ObservableProperty] private bool _wmServiceIsSimulated;
    [ObservableProperty] private string _emailTestStatusIcon = "";
    [ObservableProperty] private string _emailTestStatusColor = "#9898B0";
    [ObservableProperty] private string _emailTestStatusText = "";

    private int _consecutiveEmailFailureCount;

    public ObservableCollection<LaserChannel> Channels { get; } = new();
    public ObservableCollection<WbaDeviceSettings> WbaDevices { get; } = new();
    public ObservableCollection<WavelengthServiceChannelSpec> WmServiceChannelSpecs { get; } = new();

    public SettingsViewModel(IServiceProvider services, IEmailService emailService,
                             ITmsService tmsService, IChannelCatalog channelCatalog, IPdDriverManager pdDriverManager,
                             IWbaDeviceManager wbaDeviceManager,
                             IOptions<DriverSettings> driverOptions,
                             IOptions<WavelengthServiceSettings> wmServiceOptions,
                             ILogger<SettingsViewModel> logger)
    {
        _services = services;
        _emailService = emailService;
        _tmsService = tmsService;
        _channelCatalog = channelCatalog;
        _pdDriverManager = pdDriverManager;
        _wbaDeviceManager = wbaDeviceManager;
        _driverSettings = driverOptions.Value;
        _wmServiceSettings = wmServiceOptions.Value;
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
                EmailApiUrl = email.ApiUrl;
                Recipients = email.Recipients;
            }

            var tms = await db.TmsConfigs.FirstOrDefaultAsync();
            if (tms != null)
            {
                TmsBaseUrl = tms.BaseUrl;
                TmsApiKey = tms.ApiKey;
                TmsUploadIntervalSec = tms.UploadIntervalSec;
            }

            var acqConfig = await db.AcquisitionConfigs.FirstOrDefaultAsync();
            if (acqConfig != null)
            {
                SamplingIntervalMs = acqConfig.SamplingIntervalMs;
                WmSweepEveryN = acqConfig.WmSweepEveryN;
                DbWriteEveryN = acqConfig.DbWriteEveryN;
            }

            var channels = _channelCatalog.GetAllChannels()
                .OrderBy(c => c.DeviceSN, StringComparer.OrdinalIgnoreCase)
                .ThenBy(c => c.ChannelIndex)
                .ToList();
            Channels.Clear();
            foreach (var ch in channels) Channels.Add(ch);

            WbaDevices.Clear();
            foreach (var wba in _driverSettings.WbaDevices)
                WbaDevices.Add(wba);

            var wmDriver = _services.GetRequiredService<IWavelengthMeterDriver>();
            DriverMode = string.Equals(_driverSettings.Mode, "Simulated", StringComparison.OrdinalIgnoreCase)
                ? "模拟模式 (Simulated)"
                : "硬件模式 (Hardware)";

            WmConfigXmlPath = string.IsNullOrWhiteSpace(_driverSettings.WmConfigXmlPath)
                ? "(未配置)"
                : _driverSettings.WmConfigXmlPath;
            WmServiceMode = string.IsNullOrWhiteSpace(_wmServiceSettings.Mode)
                ? "Socket"
                : _wmServiceSettings.Mode;
            WmComPort = _wmServiceSettings.ComPort;
            WmComBaudRate = _wmServiceSettings.BaudRate;
            WmTableChannelCount = _wmServiceSettings.TableChannelCount;
            WmQueryDeviceId = string.IsNullOrWhiteSpace(_wmServiceSettings.QueryDeviceId)
                ? "(自动使用首个设备)"
                : _wmServiceSettings.QueryDeviceId;
            WmServiceIsSimulated = _wmServiceSettings.IsSimulated;
            WmAlarmDelta = _wmServiceSettings.DefaultWavelengthAlarmDeltaNm;

            WmServiceChannelSpecs.Clear();
            foreach (var spec in _wmServiceSettings.ChannelSpecs ?? [])
                WmServiceChannelSpecs.Add(spec);

            var states = _pdDriverManager.ConnectionStates;
            int connectedCount = states.Count(kvp => kvp.Value);
            PdDriverStatus = states.Count == 0
                ? "未配置PD设备"
                : $"{connectedCount}/{states.Count} 已连接";

            var wbaStates = _wbaDeviceManager.ConnectionStates;
            int wbaConnectedCount = wbaStates.Count(kvp => kvp.Value);
            WbaDriverStatus = wbaStates.Count == 0
                ? "未配置WBA设备"
                : $"{wbaConnectedCount}/{wbaStates.Count} 已连接";

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
            email.ApiUrl = EmailApiUrl;
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
    private async Task SaveEmailSettings()
    {
        if (!ValidateEmailConfig(out var error))
        {
            EmailTestStatusIcon = "✖";
            EmailTestStatusColor = "#FF1744";
            EmailTestStatusText = error;
            StatusMessage = error;
            return;
        }

        try
        {
            await PersistEmailSettingsAsync();
            EmailTestStatusIcon = "✔";
            EmailTestStatusColor = "#00E676";
            EmailTestStatusText = "邮件配置已保存";
            StatusMessage = "邮件配置已保存";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save email settings");
            EmailTestStatusIcon = "✖";
            EmailTestStatusColor = "#FF1744";
            EmailTestStatusText = "邮件配置保存失败";
            StatusMessage = "邮件配置保存失败: " + ex.Message;
        }
    }

    [RelayCommand]
    private async Task TestEmailApi()
    {
        try
        {
            if (!ValidateEmailConfig(out var error))
            {
                EmailTestStatusIcon = "✖";
                EmailTestStatusColor = "#FF1744";
                EmailTestStatusText = error;
                StatusMessage = error;
                return;
            }

            await PersistEmailSettingsAsync();

            StatusMessage = "正在调用邮件 API...";
            await _emailService.SendTestEmailAsync();
            _consecutiveEmailFailureCount = 0;
            EmailTestStatusIcon = "✔";
            EmailTestStatusColor = "#00E676";
            EmailTestStatusText = "测试请求发送成功";
            StatusMessage = "测试邮件已通过 API 发送";
        }
        catch (Exception ex)
        {
            _consecutiveEmailFailureCount++;
            EmailTestStatusIcon = "✖";
            EmailTestStatusColor = "#FF1744";
            EmailTestStatusText = "测试邮件发送失败";
            StatusMessage = "发送失败: " + ex.Message;

            if (_consecutiveEmailFailureCount >= 3)
            {
                Growl.WarningGlobal("邮件发送连续失败，请检查邮件 API 地址、收件人配置和网络连通性。");
            }
        }
    }

    private async Task PersistEmailSettingsAsync()
    {
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MonitorDbContext>();
        var email = await db.EmailConfigs.FirstOrDefaultAsync() ?? new EmailConfig();

        email.ApiUrl = EmailApiUrl.Trim();
        email.Recipients = Recipients.Trim();
        email.SmtpPort = 0;
        email.Username = string.Empty;
        email.EncryptedPassword = string.Empty;
        email.FromAddress = string.Empty;
        email.UseSsl = false;

        if (email.Id == 0)
            db.EmailConfigs.Add(email);

        await db.SaveChangesAsync();
    }

    private bool ValidateEmailConfig(out string error)
    {
        if (string.IsNullOrWhiteSpace(EmailApiUrl))
        {
            error = "请先填写邮件 API 地址";
            return false;
        }

        if (!Uri.TryCreate(EmailApiUrl, UriKind.Absolute, out var apiUri)
            || (apiUri.Scheme != Uri.UriSchemeHttp && apiUri.Scheme != Uri.UriSchemeHttps))
        {
            error = "邮件 API 地址无效";
            return false;
        }

        if (string.IsNullOrWhiteSpace(Recipients))
        {
            error = "请先填写至少一个收件人";
            return false;
        }

        error = string.Empty;
        return true;
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
    private async Task ApplyAcquisitionParams()
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

            using var scope = _services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<MonitorDbContext>();
            var acqConfig = await db.AcquisitionConfigs.FirstOrDefaultAsync() ?? new AcquisitionConfig();
            acqConfig.SamplingIntervalMs = SamplingIntervalMs;
            acqConfig.WmSweepEveryN = WmSweepEveryN;
            acqConfig.DbWriteEveryN = DbWriteEveryN;
            if (acqConfig.Id == 0) db.AcquisitionConfigs.Add(acqConfig);
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            AcqParamsStatus = "应用失败: " + ex.Message;
            _logger.LogError(ex, "Failed to apply acquisition params");
        }
    }
}
