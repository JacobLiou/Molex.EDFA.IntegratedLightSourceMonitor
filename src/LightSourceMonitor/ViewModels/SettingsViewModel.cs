using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LightSourceMonitor.Drivers;
using LightSourceMonitor.Helpers;
using LightSourceMonitor.Models;
using LightSourceMonitor.Services.Acquisition;
using LightSourceMonitor.Services.Channels;
using LightSourceMonitor.Services.Config;
using LightSourceMonitor.Services.Email;
using LightSourceMonitor.Services.Localization;
using LightSourceMonitor.Services.Tms;
using HandyControl.Controls;
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
    private readonly IRuntimeJsonConfigService _runtimeJsonConfig;
    private readonly ILanguageService _language;
    private readonly ILogger<SettingsViewModel> _logger;

    [ObservableProperty] private string _emailApiUrl = "";
    [ObservableProperty] private string _recipients = "";

    [ObservableProperty] private double _wmAlarmDelta = 0.05;

    [ObservableProperty] private string _tmsBaseUrl = "";
    [ObservableProperty] private string _tmsApiKey = "";
    [ObservableProperty] private int _tmsUploadIntervalSec = 300;

    [ObservableProperty] private int _samplingIntervalMs = 5000;
    [ObservableProperty] private int _wmSweepEveryN = 36;
    [ObservableProperty] private int _wbaSweepEveryN = 1;
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

    [ObservableProperty] private string _pdChannelsSummary = "";
    [ObservableProperty] private bool _pdHasDeviceTabs;
    [ObservableProperty] private bool _pdShowEmptyChannelHint;
    [ObservableProperty] private string _wbaDevicesSummary = "";
    [ObservableProperty] private string _selectedLanguage = LanguageService.ZhCn;

    private int _consecutiveEmailFailureCount;

    public ObservableCollection<LaserChannel> Channels { get; } = new();
    public ObservableCollection<PdDeviceChannelGroupViewModel> PdDeviceChannelGroups { get; } = new();
    public ObservableCollection<WbaDeviceSettings> WbaDevices { get; } = new();
    public ObservableCollection<WavelengthServiceChannelSpec> WmServiceChannelSpecs { get; } = new();

    public SettingsViewModel(IServiceProvider services, IEmailService emailService,
                             ITmsService tmsService, IChannelCatalog channelCatalog, IPdDriverManager pdDriverManager,
                             IWbaDeviceManager wbaDeviceManager,
                             IOptions<DriverSettings> driverOptions,
                             IOptions<WavelengthServiceSettings> wmServiceOptions,
                             IRuntimeJsonConfigService runtimeJsonConfig,
                             ILanguageService language,
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
        _runtimeJsonConfig = runtimeJsonConfig;
        _language = language;
        _logger = logger;
        _language.LanguageChanged += (_, _) =>
            AsyncHelper.SafeDispatcherInvoke(ApplyLocalizedLabels);
        LoadSettingsAsync().SafeFireAndForget("SettingsViewModel.LoadSettings");
    }

    private void ApplyLocalizedLabels()
    {
        try
        {
            var channels = Channels.ToList();
            PdDeviceChannelGroups.Clear();
            var deviceGroups = channels
                .GroupBy(c => c.DeviceSN?.Trim() ?? "", StringComparer.OrdinalIgnoreCase)
                .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var ordinal = 1;
            var tabNoSn = _language.GetString("Settings_PdTabHeaderNoSnFmt");
            var tabSn = _language.GetString("Settings_PdTabHeaderFmt");
            var snChFmt = _language.GetString("Settings_PdSnChannelsFmt");
            foreach (var g in deviceGroups)
            {
                var sn = g.Key;
                var header = string.IsNullOrEmpty(sn)
                    ? string.Format(tabNoSn, ordinal)
                    : string.Format(tabSn, ordinal, sn);
                var groupVm = new PdDeviceChannelGroupViewModel
                {
                    DeviceSn = sn,
                    TabHeader = header,
                    DeviceOrdinal = ordinal
                };
                foreach (var ch in g.OrderBy(c => c.ChannelIndex))
                    groupVm.Channels.Add(ch);
                groupVm.SnChannelSummary = string.Format(snChFmt, groupVm.DisplaySn, groupVm.Channels.Count);
                PdDeviceChannelGroups.Add(groupVm);
                ordinal++;
            }

            PdHasDeviceTabs = PdDeviceChannelGroups.Count > 0;
            PdShowEmptyChannelHint = !PdHasDeviceTabs;
            PdChannelsSummary = channels.Count == 0
                ? _language.GetString("Settings_PdSummaryNone")
                : string.Format(_language.GetString("Settings_PdSummaryFmt"), PdDeviceChannelGroups.Count, channels.Count);

            WbaDevicesSummary = string.Format(_language.GetString("Settings_WbaCountFmt"), WbaDevices.Count);

            DriverMode = string.Equals(_driverSettings.Mode, "Simulated", StringComparison.OrdinalIgnoreCase)
                ? _language.GetString("Settings_DriverMode_Sim")
                : _language.GetString("Settings_DriverMode_Hw");

            WmConfigXmlPath = string.IsNullOrWhiteSpace(_driverSettings.WmConfigXmlPath)
                ? _language.GetString("Settings_WmPathUnset")
                : _driverSettings.WmConfigXmlPath;
            WmQueryDeviceId = string.IsNullOrWhiteSpace(_wmServiceSettings.QueryDeviceId)
                ? _language.GetString("Settings_WmQueryAuto")
                : _wmServiceSettings.QueryDeviceId;

            var states = _pdDriverManager.ConnectionStates;
            var connectedCount = states.Count(kvp => kvp.Value);
            PdDriverStatus = states.Count == 0
                ? _language.GetString("Settings_PdNoDev")
                : string.Format(_language.GetString("Settings_PdConnFmt"), connectedCount, states.Count);

            var wbaStates = _wbaDeviceManager.ConnectionStates;
            var wbaConnectedCount = wbaStates.Count(kvp => kvp.Value);
            WbaDriverStatus = wbaStates.Count == 0
                ? _language.GetString("Settings_WbaNoDev")
                : string.Format(_language.GetString("Settings_PdConnFmt"), wbaConnectedCount, wbaStates.Count);

            var wmDriver = _services.GetRequiredService<IWavelengthMeterDriver>();
            WmDriverStatus = wmDriver.IsInitialized
                ? _language.GetString("Settings_WmInitYes")
                : _language.GetString("Settings_WmInitNo");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ApplyLocalizedLabels failed");
        }
    }

    private async Task LoadSettingsAsync()
    {
        try
        {
            var ui = await _runtimeJsonConfig.LoadUiAsync();
            SelectedLanguage = ui.Language;

            var email = await _runtimeJsonConfig.LoadEmailAsync();
            EmailApiUrl = email.ApiUrl;
            Recipients = email.Recipients;

            var tms = await _runtimeJsonConfig.LoadTmsAsync();
            TmsBaseUrl = tms.BaseUrl;
            TmsApiKey = tms.ApiKey;
            TmsUploadIntervalSec = tms.UploadIntervalSec;

            var acqConfig = await _runtimeJsonConfig.LoadAcquisitionAsync();
            SamplingIntervalMs = acqConfig.SamplingIntervalMs;
            WmSweepEveryN = acqConfig.WmSweepEveryN;
            WbaSweepEveryN = acqConfig.WbaSweepEveryN > 0 ? acqConfig.WbaSweepEveryN : 1;
            DbWriteEveryN = acqConfig.DbWriteEveryN;

            var channels = _channelCatalog.GetAllChannels()
                .OrderBy(c => c.DeviceSN, StringComparer.OrdinalIgnoreCase)
                .ThenBy(c => c.ChannelIndex)
                .ToList();
            Channels.Clear();
            foreach (var ch in channels) Channels.Add(ch);

            WbaDevices.Clear();
            foreach (var wba in _driverSettings.WbaDevices)
                WbaDevices.Add(wba);

            WmServiceMode = string.IsNullOrWhiteSpace(_wmServiceSettings.Mode)
                ? "Socket"
                : _wmServiceSettings.Mode;
            WmComPort = _wmServiceSettings.ComPort;
            WmComBaudRate = _wmServiceSettings.BaudRate;
            WmTableChannelCount = _wmServiceSettings.TableChannelCount;
            WmServiceIsSimulated = _wmServiceSettings.IsSimulated;
            WmAlarmDelta = _wmServiceSettings.DefaultWavelengthAlarmDeltaNm;

            WmServiceChannelSpecs.Clear();
            foreach (var spec in _wmServiceSettings.ChannelSpecs ?? [])
                WmServiceChannelSpecs.Add(spec);

            var acq = _services.GetRequiredService<IAcquisitionService>();
            IsAcquisitionRunning = acq.IsRunning;
            SamplingIntervalMs = acq.SamplingIntervalMs;
            WmSweepEveryN = acq.WmSweepEveryN;
            WbaSweepEveryN = acq.WbaSweepEveryN > 0 ? acq.WbaSweepEveryN : 1;
            DbWriteEveryN = acq.DbWriteEveryN;

            ApplyLocalizedLabels();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load settings");
        }
    }

    [RelayCommand]
    private async Task ApplyUiLanguageAsync()
    {
        try
        {
            _language.ApplyLanguage(SelectedLanguage);
            var ui = new UiConfig { Language = SelectedLanguage };
            await _runtimeJsonConfig.SaveUiAsync(ui);
            StatusMessage = _language.GetString("Settings_Status_Saved");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save UI language");
            StatusMessage = string.Format(_language.GetString("Settings_Status_SaveFail"), ex.Message);
        }
    }

    [RelayCommand]
    private async Task SaveSettings()
    {
        try
        {
            var email = await _runtimeJsonConfig.LoadEmailAsync();
            email.ApiUrl = EmailApiUrl.Trim();
            email.Recipients = Recipients.Trim();
            await _runtimeJsonConfig.SaveEmailAsync(email);

            var tms = await _runtimeJsonConfig.LoadTmsAsync();
            tms.BaseUrl = TmsBaseUrl.Trim();
            tms.ApiKey = TmsApiKey.Trim();
            tms.UploadIntervalSec = TmsUploadIntervalSec;
            tms.IsEnabled = !string.IsNullOrWhiteSpace(TmsBaseUrl);
            await _runtimeJsonConfig.SaveTmsAsync(tms);

            StatusMessage = _language.GetString("Settings_Status_Saved");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save settings");
            StatusMessage = string.Format(_language.GetString("Settings_Status_SaveFail"), ex.Message);
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
            EmailTestStatusText = _language.GetString("Settings_EmailSaved");
            StatusMessage = _language.GetString("Settings_EmailSaved");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save email settings");
            EmailTestStatusIcon = "✖";
            EmailTestStatusColor = "#FF1744";
            EmailTestStatusText = _language.GetString("Settings_EmailSaveFail");
            StatusMessage = string.Format(_language.GetString("Settings_Status_SaveFail"), ex.Message);
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

            StatusMessage = _language.GetString("Settings_EmailCalling");
            await _emailService.SendTestEmailAsync();
            _consecutiveEmailFailureCount = 0;
            EmailTestStatusIcon = "✔";
            EmailTestStatusColor = "#00E676";
            EmailTestStatusText = _language.GetString("Settings_EmailTestOk");
            StatusMessage = _language.GetString("Settings_EmailTestSent");
        }
        catch (Exception ex)
        {
            _consecutiveEmailFailureCount++;
            EmailTestStatusIcon = "✖";
            EmailTestStatusColor = "#FF1744";
            EmailTestStatusText = _language.GetString("Settings_EmailTestFail");
            StatusMessage = string.Format(_language.GetString("Settings_EmailSendFail"), ex.Message);

            if (_consecutiveEmailFailureCount >= 3)
            {
                Growl.WarningGlobal(_language.GetString("Growl_EmailFailRepeated"));
            }
        }
    }

    private async Task PersistEmailSettingsAsync()
    {
        var email = await _runtimeJsonConfig.LoadEmailAsync();
        email.ApiUrl = EmailApiUrl.Trim();
        email.Recipients = Recipients.Trim();
        await _runtimeJsonConfig.SaveEmailAsync(email);
    }

    private bool ValidateEmailConfig(out string error)
    {
        if (string.IsNullOrWhiteSpace(EmailApiUrl))
        {
            error = _language.GetString("Settings_Validate_ApiEmpty");
            return false;
        }

        if (!Uri.TryCreate(EmailApiUrl, UriKind.Absolute, out var apiUri)
            || (apiUri.Scheme != Uri.UriSchemeHttp && apiUri.Scheme != Uri.UriSchemeHttps))
        {
            error = _language.GetString("Settings_Validate_ApiInvalid");
            return false;
        }

        if (EmailRecipientParser.Parse(Recipients).Count == 0)
        {
            error = _language.GetString("Settings_Validate_RecipientsEmpty");
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
            StatusMessage = _language.GetString("Settings_TmsTesting");
            var ok = await _tmsService.TestConnectionAsync();
            StatusMessage = ok ? _language.GetString("Settings_TmsOk") : _language.GetString("Settings_TmsFail");
        }
        catch (Exception ex)
        {
            StatusMessage = string.Format(_language.GetString("Settings_TmsConnFail"), ex.Message);
        }
    }

    [RelayCommand]
    private async Task SaveTmsSettings()
    {
        try
        {
            var tms = await _runtimeJsonConfig.LoadTmsAsync();
            tms.BaseUrl = TmsBaseUrl.Trim();
            tms.ApiKey = TmsApiKey.Trim();
            tms.UploadIntervalSec = TmsUploadIntervalSec;
            tms.IsEnabled = !string.IsNullOrWhiteSpace(TmsBaseUrl);
            await _runtimeJsonConfig.SaveTmsAsync(tms);
            StatusMessage = _language.GetString("Settings_TmsSaved");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save TMS settings");
            StatusMessage = string.Format(_language.GetString("Settings_TmsSaveFail"), ex.Message);
        }
    }

    [RelayCommand]
    private async Task ApplyAcquisitionParams()
    {
        try
        {
            var acq = _services.GetRequiredService<IAcquisitionService>();

            SamplingIntervalMs = Math.Max(MinSamplingIntervalMs, SamplingIntervalMs);
            WmSweepEveryN = Math.Max(MinCycleEveryN, WmSweepEveryN);
            WbaSweepEveryN = Math.Max(MinCycleEveryN, WbaSweepEveryN);
            DbWriteEveryN = Math.Max(MinCycleEveryN, DbWriteEveryN);

            acq.SamplingIntervalMs = SamplingIntervalMs;
            acq.WmSweepEveryN = WmSweepEveryN;
            acq.WbaSweepEveryN = WbaSweepEveryN;
            acq.DbWriteEveryN = DbWriteEveryN;
            AcqParamsStatus = string.Format(_language.GetString("Settings_AcqApplied"),
                SamplingIntervalMs, WmSweepEveryN, WbaSweepEveryN, DbWriteEveryN);
            _logger.LogInformation(
                "Acquisition params updated: interval={Interval}ms, wmEvery={WmN}, wbaEvery={WbaN}, dbEvery={DbN}",
                SamplingIntervalMs, WmSweepEveryN, WbaSweepEveryN, DbWriteEveryN);

            await _runtimeJsonConfig.SaveAcquisitionAsync(new AcquisitionConfig
            {
                SamplingIntervalMs = SamplingIntervalMs,
                WmSweepEveryN = WmSweepEveryN,
                WbaSweepEveryN = WbaSweepEveryN,
                DbWriteEveryN = DbWriteEveryN
            });
        }
        catch (Exception ex)
        {
            AcqParamsStatus = string.Format(_language.GetString("Settings_AcqApplyFail"), ex.Message);
            _logger.LogError(ex, "Failed to apply acquisition params");
        }
    }
}
