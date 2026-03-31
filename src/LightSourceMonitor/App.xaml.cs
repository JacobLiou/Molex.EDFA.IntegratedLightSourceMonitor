using System.IO;
using System.Windows;
using System.Windows.Threading;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LightSourceMonitor.Data;
using LightSourceMonitor.Drivers;
using LightSourceMonitor.Models;
using LightSourceMonitor.Services.Acquisition;
using LightSourceMonitor.Services.Alarm;
using LightSourceMonitor.Services.Channels;
using LightSourceMonitor.Services.Email;
using LightSourceMonitor.Services.Retention;
using LightSourceMonitor.Services.Tms;
using LightSourceMonitor.Services.Trend;
using LightSourceMonitor.ViewModels;
using LightSourceMonitor.Views;

namespace LightSourceMonitor;

public partial class App : Application
{
    private IHost _host = null!;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // ── Global exception handlers ──────────────────────────────────
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        try
        {
            await InitializeApplicationAsync();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Application startup failed");
            MessageBox.Show($"应用程序启动失败:\n{ex.Message}\n\n详情请查看日志文件。",
                "启动错误", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private async Task InitializeApplicationAsync()
    {
        // Serilog bootstrap (before host, so early errors are captured)
        var logDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
        Directory.CreateDirectory(logDir);

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .Enrich.WithProperty("App", "LightSourceMonitor")
            .WriteTo.Console()
            .WriteTo.File(
                Path.Combine(logDir, "log-.txt"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 30,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        Log.Information("=== LightSourceMonitor starting ===");

        LiveCharts.Configure(config => config
            .AddSkiaSharp()
            .AddDarkTheme());

        _host = Host.CreateDefaultBuilder()
            .UseSerilog()
            .ConfigureServices((context, services) =>
            {
                services.Configure<DriverSettings>(context.Configuration.GetSection("Driver"));
                services.Configure<WavelengthServiceSettings>(context.Configuration.GetSection("WavelengthService"));

                var dbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "monitor.db");
                services.AddDbContext<MonitorDbContext>(opts =>
                    opts.UseSqlite($"Data Source={dbPath}"),
                    ServiceLifetime.Transient);

                var driverSettings = context.Configuration.GetSection("Driver").Get<DriverSettings>() ?? new DriverSettings();
                var validation = driverSettings.ValidateConfiguration();
                var effectiveDevices = driverSettings.GetEffectiveDevices();
                var enabledChannelCount = effectiveDevices.Sum(d => d.Channels.Count(c => c.IsEnabled));

                Log.Information("Driver config loaded: mode={Mode}, devices={DeviceCount}, enabledChannels={ChannelCount}",
                    driverSettings.Mode,
                    effectiveDevices.Count,
                    enabledChannelCount);

                foreach (var warning in validation.Warnings)
                    Log.Warning("Driver config warning: {Warning}", warning);

                foreach (var error in validation.Errors)
                    Log.Error("Driver config error: {Error}", error);

                if (!validation.IsValid)
                {
                    throw new InvalidOperationException(
                        "appsettings Driver 配置无效。请修正重复 DeviceSN、重复 ChannelIndex、空 UsbAddress 等错误后再启动。详细信息见日志。");
                }

                var useSimulated = string.Equals(driverSettings.Mode, "Simulated", StringComparison.OrdinalIgnoreCase);
                if (useSimulated)
                {
                    services.AddTransient<IPdArrayDriver, SimulatedPdArrayDriver>();
                    services.AddSingleton<IWavelengthMeterDriver, SimulatedWavelengthMeterDriver>();
                    Log.Information("Driver mode: Simulated");
                }
                else
                {
                    services.AddTransient<IPdArrayDriver, PdArrayDriver>();
                    services.AddSingleton<IWavelengthMeterDriver, WavelengthMeterDriver>();
                    Log.Information("Driver mode: Hardware");
                }

                services.AddSingleton<Func<IPdArrayDriver>>(sp => () => sp.GetRequiredService<IPdArrayDriver>());
                services.AddSingleton<IPdDriverManager, PdDriverManager>();

                services.AddSingleton<IWavelengthServiceDriver, WavelengthServiceDriver>();
                services.AddSingleton<IAlarmService, AlarmService>();
                services.AddSingleton<IChannelCatalog, ChannelCatalog>();
                services.AddSingleton<IEmailService, EmailService>();
                services.AddSingleton<IAcquisitionService, AcquisitionService>();
                services.AddSingleton<ITmsService, TmsUploadService>();
                services.AddSingleton<ITrendService, TrendService>();
                services.AddHostedService<DataRetentionService>();

                services.AddSingleton<MainViewModel>();
                services.AddTransient<OverviewViewModel>();
                services.AddTransient<TrendViewModel>();
                services.AddTransient<AlarmViewModel>();
                services.AddTransient<SettingsViewModel>();

                services.AddSingleton<MainWindow>();
            })
            .Build();

        var mainWindow = _host.Services.GetRequiredService<MainWindow>();
        mainWindow.Show();

        // Apply any pending EF Core migrations (creates / upgrades the DB schema)
        using (var scope = _host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MonitorDbContext>();
            await db.Database.MigrateAsync();
            var repaired = await LegacySchemaRepair.EnsureNoLegacyLaserChannelForeignKeysAsync(db);
            if (repaired)
                Log.Warning("Detected legacy LaserChannels FK constraints in existing DB; schema was repaired automatically.");
            Log.Information("Database migrated successfully");
        }

        await _host.StartAsync();

        var acq = _host.Services.GetRequiredService<IAcquisitionService>();
        var mainVm = _host.Services.GetRequiredService<MainViewModel>();
        acq.DataAcquired += _ =>
        {
            try
            {
                Current?.Dispatcher?.Invoke(() => mainVm.UpdateLastAcquisitionTime());
            }
            catch (Exception) { /* shutdown race */ }
        };
        await acq.StartAsync();
        Log.Information("Acquisition service started");
    }

    // ── Exception handlers ──────────────────────────────────────────

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error(e.Exception, "Unhandled UI thread exception: {Message}", e.Exception.Message);
        e.Handled = true;

        try
        {
            MessageBox.Show(
                $"发生未处理的错误:\n{e.Exception.Message}\n\n详情请查看日志文件。",
                "运行时错误", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch { /* MessageBox itself could fail during shutdown */ }
    }

    private static void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
            Log.Fatal(ex, "Fatal domain exception (IsTerminating={IsTerminating})", e.IsTerminating);
        else
            Log.Fatal("Fatal domain exception (non-Exception object): {Obj}", e.ExceptionObject);

        Log.CloseAndFlush();
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Log.Error(e.Exception, "Unobserved task exception ({Count} inner exceptions)",
            e.Exception?.InnerExceptions.Count ?? 0);
        e.SetObserved();
    }

    // ── Shutdown ────────────────────────────────────────────────────

    protected override async void OnExit(ExitEventArgs e)
    {
        Log.Information("=== LightSourceMonitor shutting down ===");

        try
        {
            if (_host != null)
            {
                var acq = _host.Services.GetService<IAcquisitionService>();
                if (acq?.IsRunning == true)
                {
                    await acq.StopAsync();
                    Log.Information("Acquisition service stopped");
                }

                await _host.StopAsync(TimeSpan.FromSeconds(5));
                _host.Dispose();
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error during shutdown");
        }
        finally
        {
            Log.CloseAndFlush();
        }

        base.OnExit(e);
    }
}
