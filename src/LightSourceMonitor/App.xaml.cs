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
        }

        var mainWindow = _host.Services.GetRequiredService<MainWindow>();
        mainWindow.Show();

        await _host.StartAsync();
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
