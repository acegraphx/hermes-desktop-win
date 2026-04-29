using System.IO;
using System.Windows;
using HermesDesktop.Helpers;
using HermesDesktop.Services;
using HermesDesktop.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;

namespace HermesDesktop;

public partial class App : Application
{
    private readonly IHost _host;

    public App()
    {
        AppPaths.EnsureDirectories();

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.File(
                Path.Combine(AppPaths.LogsDirectory, "hermes-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7)
            .CreateLogger();

        _host = Host.CreateDefaultBuilder()
            .ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddSerilog();
            })
            .ConfigureServices((_, services) =>
            {
                services.AddSingleton<SshConnectionPool>();
                services.AddSingleton<ISshTransport, SshTransport>();
                services.AddSingleton<IRemoteScriptExecutor, RemotePythonScriptExecutor>();
                services.AddSingleton<IConnectionStore, ConnectionStore>();
                services.AddSingleton<IRemoteHermesService, RemoteHermesService>();
                services.AddSingleton<IFileEditorService, FileEditorService>();
                services.AddSingleton<ISessionBrowserService, SessionBrowserService>();
                services.AddSingleton<IUsageBrowserService, UsageBrowserService>();
                services.AddSingleton<ISkillBrowserService, SkillBrowserService>();
                services.AddSingleton<ICronBrowserService, CronBrowserService>();
                services.AddSingleton<SshConfigParser>();

                services.AddSingleton<MainViewModel>();
                services.AddTransient<ConnectionManagerViewModel>();
                services.AddTransient<OverviewViewModel>();
                services.AddTransient<FileEditorViewModel>();
                services.AddTransient<SessionBrowserViewModel>();
                services.AddTransient<UsageBrowserViewModel>();
                services.AddTransient<SkillBrowserViewModel>();
                services.AddTransient<CronJobsViewModel>();
                services.AddSingleton<TerminalViewModel>();

                services.AddSingleton<MainWindow>();
            })
            .Build();
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        ThemeManager.ApplySystemTheme();

        if (!await WebView2Bootstrapper.EnsureInstalledAsync())
        {
            Shutdown();
            return;
        }

        await _host.StartAsync();

        var mainVm = _host.Services.GetRequiredService<MainViewModel>();
        await mainVm.InitializeAsync();

        var mainWindow = _host.Services.GetRequiredService<MainWindow>();
        mainWindow.DataContext = mainVm;
        mainWindow.Show();

        base.OnStartup(e);
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        var pool = _host.Services.GetRequiredService<SshConnectionPool>();
        pool.Dispose();
        await _host.StopAsync();
        Log.CloseAndFlush();
        base.OnExit(e);
    }
}
