using System.Diagnostics.CodeAnalysis;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ThreeByThree.Centar.Scoreboard.Application;
using ThreeByThree.Centar.Scoreboard.Application.Display;
using ThreeByThree.Centar.Scoreboard.Application.Operations;
using ThreeByThree.Centar.Scoreboard.Application.Overlay;
using ThreeByThree.Centar.Scoreboard.Application.Persistence;
using ThreeByThree.Centar.Scoreboard.Application.Settings;
using ThreeByThree.Centar.Scoreboard.Application.Tournaments;
using ThreeByThree.Centar.Scoreboard.Domain;
using ThreeByThree.Centar.Scoreboard.Infrastructure.Diagnostics;
using ThreeByThree.Centar.Scoreboard.Infrastructure.Operations;
using ThreeByThree.Centar.Scoreboard.Infrastructure.Overlay;
using ThreeByThree.Centar.Scoreboard.Infrastructure.Persistence;
using ThreeByThree.Centar.Scoreboard.Infrastructure.Settings;
using ThreeByThree.Centar.Scoreboard.Infrastructure.Windows;
using ThreeByThree.Centar.Scoreboard.Wpf.Services;
using ThreeByThree.Centar.Scoreboard.Wpf.ViewModels;
using ThreeByThree.Centar.Scoreboard.Wpf.Views;

namespace ThreeByThree.Centar.Scoreboard.Wpf;

[SuppressMessage(
    "Design",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "WPF Application owns and releases the mutex in OnExit.")]
public partial class App : System.Windows.Application
{
    private const string ProductName = "3x3 Centar Scoreboard";
    private const int MinimumWindowsBuild = 14393;
    private const string SingleInstanceName =
        @"Local\ThreeByThree.Centar.Scoreboard.9D8A3F59-0BE6-4B4B-B2E6-95F66E31A5E4";
    private IMatchPersistenceService? persistence;
    private IMatchOperationalService? operations;
    private ILocalOverlayServer? overlayServer;
    private IAppLog? appLog;
    private Mutex? singleInstanceMutex;
    private bool ownsSingleInstanceMutex;

    private readonly IHost _host = Host.CreateDefaultBuilder()
        .ConfigureServices(services =>
        {
            services.AddSingleton<TimeProvider>(TimeProvider.System);
            services.AddSingleton<MatchEngine>();
            services.AddSingleton<MatchSession>();
            services.AddSingleton<MatchPresentationTicker>();
            services.AddSingleton<IMonitorService, MonitorService>();
            services.AddSingleton(GameStoragePaths.ForCurrentUser());
            services.AddSingleton<IGameStore, JsonGameStore>();
            services.AddSingleton<ITournamentStore, JsonTournamentStore>();
            services.AddSingleton<IMatchPersistenceService, MatchPersistenceService>();
            services.AddSingleton<ISettingsStore, JsonSettingsStore>();
            services.AddSingleton<IAppSettingsService, AppSettingsService>();
            services.AddSingleton<IAudioService, AudioService>();
            services.AddSingleton<IPowerManagementService, PowerManagementService>();
            services.AddSingleton<IAppLog, FileAppLog>();
            services.AddSingleton<IMatchOperationalService, MatchOperationalService>();
            services.AddSingleton<ILocalOverlayServer, LocalOverlayServer>();
            services.AddSingleton<IControllerDialogService, ControllerDialogService>();
            services.AddSingleton<ControllerViewModel>();
            services.AddSingleton<ScoreboardViewModel>();
            services.AddSingleton<ControllerWindow>();
            services.AddSingleton<ScoreboardWindow>();
        })
        .Build();

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, MinimumWindowsBuild))
        {
            MessageBox.Show(
                "3x3 Centar Scoreboard requires 64-bit Windows 10 " +
                "version 1607 or later.",
                ProductName,
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown();
            return;
        }

        singleInstanceMutex = new Mutex(
            initiallyOwned: true,
            SingleInstanceName,
            out var createdNew);
        if (!createdNew)
        {
            MessageBox.Show(
                $"{ProductName} is already running.",
                ProductName,
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            Shutdown();
            return;
        }

        ownsSingleInstanceMutex = true;
        await _host.StartAsync();

        var settings = _host.Services.GetRequiredService<IAppSettingsService>();
        await settings.InitializeAsync();
        appLog = _host.Services.GetRequiredService<IAppLog>();
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        operations = _host.Services.GetRequiredService<IMatchOperationalService>();
        operations.Start();

        persistence = _host.Services.GetRequiredService<IMatchPersistenceService>();
        await RecoverActiveGameAsync(persistence);
        persistence.Start();

        overlayServer = _host.Services.GetRequiredService<ILocalOverlayServer>();
        overlayServer.Start();

        var controller = _host.Services.GetRequiredService<ControllerWindow>();
        MainWindow = controller;
        controller.Show();

        var scoreboard = _host.Services.GetRequiredService<ScoreboardWindow>();
        scoreboard.Show();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        DispatcherUnhandledException -= OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException -= OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;

        if (overlayServer is not null)
        {
            await overlayServer.StopAsync();
        }

        if (persistence is not null)
        {
            await persistence.StopAsync();
        }

        if (operations is not null)
        {
            await operations.StopAsync();
        }

        await _host.StopAsync(TimeSpan.FromSeconds(3));
        _host.Dispose();
        if (ownsSingleInstanceMutex)
        {
            singleInstanceMutex?.ReleaseMutex();
            ownsSingleInstanceMutex = false;
        }

        singleInstanceMutex?.Dispose();
        singleInstanceMutex = null;
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(
        object sender,
        DispatcherUnhandledExceptionEventArgs e)
    {
        appLog?.LogError("Unhandled UI exception.", e.Exception);
        MessageBox.Show(
            "A fatal error occurred. The active game recovery file has been preserved.\n\n" +
            e.Exception.Message,
            ProductName,
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }

    private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            appLog?.LogError("Unhandled application exception.", exception);
        }
    }

    private void OnUnobservedTaskException(
        object? sender,
        UnobservedTaskExceptionEventArgs e)
    {
        appLog?.LogError("Unobserved background task exception.", e.Exception);
        e.SetObserved();
    }

    private static async Task RecoverActiveGameAsync(IMatchPersistenceService persistenceService)
    {
        var document = await persistenceService.LoadRecoveryAsync();
        if (document is null)
        {
            return;
        }

        var snapshot = document.Snapshot;
        var gameClock = snapshot.Stage == Domain.Models.MatchStage.Overtime
            ? "OT"
            : ClockDisplayFormatter.FormatGameClock(
                snapshot.GameClock.Remaining,
                snapshot.Rules.GameClockTenthsThreshold);
        var shotClock = ClockDisplayFormatter.FormatShotClock(
            snapshot.ShotClock.Remaining,
            snapshot.Rules.ShotClockTenthsThreshold);
        var prompt =
            $"Recovered unfinished game\n\n" +
            $"{snapshot.Home.Name} {snapshot.Home.Score} – {snapshot.Away.Score} {snapshot.Away.Name}\n" +
            $"Game clock: {gameClock}\n" +
            $"Shot clock: {shotClock}\n\n" +
            "Choose Yes to open it paused, or No to leave it in Saved Games.";
        var choice = MessageBox.Show(
            prompt,
            "Recover unfinished game",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            MessageBoxResult.Yes);

        if (choice == MessageBoxResult.Yes)
        {
            var result = persistenceService.Recover(document);
            if (!result.IsAccepted)
            {
                MessageBox.Show(
                    result.Message,
                    "Recovery failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }

            return;
        }

    }
}
