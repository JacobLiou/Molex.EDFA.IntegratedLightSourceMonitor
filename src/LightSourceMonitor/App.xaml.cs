using System.IO;
using System.Windows;
using System.Windows.Threading;
using Microsoft.EntityFrameworkCore;
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
                var dbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "monitor.db");
                services.AddDbContext<MonitorDbContext>(opts =>
                    opts.UseSqlite($"Data Source={dbPath}"),
                    ServiceLifetime.Transient);

                // Hardware drivers — swap to real drivers on production IPC:
                // services.AddSingleton<IPdArrayDriver, PdArrayDriver>();
                // services.AddSingleton<IWavelengthMeterDriver, WavelengthMeterDriver>();
                services.AddSingleton<IPdArrayDriver, SimulatedPdArrayDriver>();
                services.AddSingleton<IWavelengthMeterDriver, SimulatedWavelengthMeterDriver>();

                services.AddSingleton<IAlarmService, AlarmService>();
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

        // Database migration & seed
        using (var scope = _host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MonitorDbContext>();
            await db.Database.MigrateAsync();
            Log.Information("Database migrated successfully");

            if (!await db.LaserChannels.AnyAsync())
            {
                db.LaserChannels.AddRange(
                    new LaserChannel { ChannelIndex = 0, ChannelName = "CH1-1310", DeviceSN = "SIM-PD-001", SpecWavelength = 1310.0, SpecPowerMin = -9.5, SpecPowerMax = -7.5, AlarmDelta = 0.15 },
                    new LaserChannel { ChannelIndex = 1, ChannelName = "CH2-1310", DeviceSN = "SIM-PD-001", SpecWavelength = 1310.5, SpecPowerMin = -10.2, SpecPowerMax = -8.2, AlarmDelta = 0.15 },
                    new LaserChannel { ChannelIndex = 2, ChannelName = "CH3-1310", DeviceSN = "SIM-PD-001", SpecWavelength = 1311.0, SpecPowerMin = -11.1, SpecPowerMax = -9.1, AlarmDelta = 0.15 },
                    new LaserChannel { ChannelIndex = 3, ChannelName = "CH4-1310", DeviceSN = "SIM-PD-001", SpecWavelength = 1311.5, SpecPowerMin = -12.8, SpecPowerMax = -10.8, AlarmDelta = 0.15 },
                    new LaserChannel { ChannelIndex = 4, ChannelName = "CH5-1550", DeviceSN = "SIM-PD-001", SpecWavelength = 1550.0, SpecPowerMin = -11.5, SpecPowerMax = -9.5, AlarmDelta = 0.15 },
                    new LaserChannel { ChannelIndex = 5, ChannelName = "CH6-1550", DeviceSN = "SIM-PD-001", SpecWavelength = 1550.5, SpecPowerMin = -13.0, SpecPowerMax = -11.0, AlarmDelta = 0.15 },
                    new LaserChannel { ChannelIndex = 6, ChannelName = "CH7-1550", DeviceSN = "SIM-PD-001", SpecWavelength = 1551.0, SpecPowerMin = -14.3, SpecPowerMax = -12.3, AlarmDelta = 0.15 },
                    new LaserChannel { ChannelIndex = 7, ChannelName = "CH8-1550", DeviceSN = "SIM-PD-001", SpecWavelength = 1551.5, SpecPowerMin = -12.6, SpecPowerMax = -10.6, AlarmDelta = 0.15 }
                );
                await db.SaveChangesAsync();
                Log.Information("Seeded 8 demo LaserChannels for simulation mode");
            }
        }

        var mainWindow = _host.Services.GetRequiredService<MainWindow>();
        mainWindow.Show();

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
