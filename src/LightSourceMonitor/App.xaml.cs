using System.IO;
using System.Threading.Channels;
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

        DispatcherUnhandledException += (_, args) =>
        {
            Log.Error(args.Exception, "Unhandled UI exception");
            MessageBox.Show($"发生未处理的错误:\n{args.Exception.Message}", "错误",
                MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
                Log.Fatal(ex, "Fatal domain exception");
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error(args.Exception, "Unobserved task exception");
            args.SetObserved();
        };

        LiveCharts.Configure(config => config
            .AddSkiaSharp()
            .AddDarkTheme());

        _host = Host.CreateDefaultBuilder()
            .UseSerilog((context, services, configuration) => configuration
                .MinimumLevel.Information()
                .WriteTo.Console()
                .WriteTo.File(
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs", "log-.txt"),
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 30))
            .ConfigureServices((context, services) =>
            {
                var dbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "monitor.db");
                services.AddDbContext<MonitorDbContext>(opts =>
                    opts.UseSqlite($"Data Source={dbPath}"),
                    ServiceLifetime.Transient);

                services.AddSingleton(Channel.CreateUnbounded<MeasurementRecord>(
                    new UnboundedChannelOptions { SingleReader = false }));

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

                services.AddSingleton<MainViewModel>();
                services.AddTransient<OverviewViewModel>();
                services.AddTransient<TrendViewModel>();
                services.AddTransient<AlarmViewModel>();
                services.AddTransient<SettingsViewModel>();

                services.AddSingleton<MainWindow>();
            })
            .Build();

        using (var scope = _host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MonitorDbContext>();
            await db.Database.MigrateAsync();

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
        acq.DataAcquired += _ => Current.Dispatcher.Invoke(() => mainVm.UpdateLastAcquisitionTime());
        await acq.StartAsync();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_host != null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }
        base.OnExit(e);
    }
}
