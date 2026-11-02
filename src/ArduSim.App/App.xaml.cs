using System.Globalization;
using System.IO;
using System.Windows;
using ArduSim.App.Services;
using ArduSim.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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
        builder.Services.AddSingleton<MainViewModel>();
        builder.Services.AddSingleton<MainWindow>();

        _host = builder.Build();
        _host.Start();
        _host.Services.GetRequiredService<MainWindow>().Show();
    }

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
