using System.Globalization;
using System.IO;
using System.Windows;
using ArduSim.App.Services;
using ArduSim.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;

namespace ArduSim.App;

public partial class App : Application
{
    private IHost? _host;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        string logs = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ArduSim", "logs");

        HostApplicationBuilder builder = Host.CreateApplicationBuilder(e.Args);
        builder.Services.AddSerilog(config => config
            .MinimumLevel.Information()
            .WriteTo.File(
                Path.Combine(logs, "app-.log"),
                formatProvider: CultureInfo.InvariantCulture,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14));
        builder.Services.AddSingleton(new AppEnvironment(inProcess: e.Args.Contains("--inproc")));
        builder.Services.AddSingleton<FirmwareBuildService>();
        builder.Services.AddSingleton<UserState>();
            builder.Services.AddSingleton<ITonePlayer, WaveOutTonePlayer>();
        builder.Services.AddSingleton<MainViewModel>();
        builder.Services.AddSingleton<MainWindow>();

        _host = builder.Build();
        _host.Start();
        ILogger<App> logger = _host.Services.GetRequiredService<ILogger<App>>();
        MainViewModel viewModel = _host.Services.GetRequiredService<MainViewModel>();
        DispatcherUnhandledException += (_, failure) =>
        {
            // The app stays up with the unsaved work, and says what happened.
            LogUnhandled(logger, failure.Exception);
            viewModel.ShowInternalError(failure.Exception.Message);
            failure.Handled = true;
        };
        Theme.ApplyTextScale(Theme.SystemTextScale());
        Microsoft.Win32.SystemEvents.UserPreferenceChanged += (_, _) => Dispatcher.Invoke(() => Theme.ApplyTextScale(Theme.SystemTextScale()));
        _host.Services.GetRequiredService<MainWindow>().Show();
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "An exception reached the dispatcher")]
    private static partial void LogUnhandled(Microsoft.Extensions.Logging.ILogger logger, Exception exception);

    protected override void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            _host.StopAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            _host.Dispose();
        }

        base.OnExit(e);
    }
}
