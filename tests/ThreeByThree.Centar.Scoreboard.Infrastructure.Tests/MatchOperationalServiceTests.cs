using ThreeByThree.Centar.Scoreboard.Application;
using ThreeByThree.Centar.Scoreboard.Application.Operations;
using ThreeByThree.Centar.Scoreboard.Application.Settings;
using ThreeByThree.Centar.Scoreboard.Domain.Commands;
using ThreeByThree.Centar.Scoreboard.Domain.Models;
using ThreeByThree.Centar.Scoreboard.Infrastructure.Operations;

namespace ThreeByThree.Centar.Scoreboard.Infrastructure.Tests;

[TestClass]
public sealed class MatchOperationalServiceTests
{
    [TestMethod]
    public async Task MatchLifecycle_ControlsPowerAndRoutesBuzzerEvents()
    {
        using var session = new MatchSession(new MatchEngine(), TimeProvider.System);
        var audio = new RecordingAudioService();
        var power = new RecordingPowerService();
        var settings = new StubSettingsService();
        var log = new RecordingLog();
        var operations = new MatchOperationalService(
            session,
            audio,
            power,
            settings,
            log);
        operations.Start();

        var created = session.Execute(
            new CreateGameCommand(
                new MatchMetadata(),
                MatchRules.Fiba3x3,
                "Home",
                "Away",
                "#FFFFFF",
                "#FF5252"));
        var buzzer = session.Execute(new TriggerBuzzerCommand());
        var ended = session.Execute(new EndGameCommand());
        await operations.StopAsync();

        Assert.IsTrue(created.IsAccepted);
        Assert.IsTrue(buzzer.IsAccepted);
        Assert.IsTrue(ended.IsAccepted);
        CollectionAssert.Contains(power.States, true);
        Assert.IsFalse(power.States[^1]);
        CollectionAssert.AreEqual(
            new[] { BuzzerKind.Manual },
            audio.PlayedBuzzers);
        Assert.IsTrue(log.Messages.Any(message => message.Contains("BuzzerTriggeredEvent")));
        Assert.IsTrue(log.WasStopped);
    }

    [TestMethod]
    public async Task RequestedShotClockWarning_RoutesToAudioExactlyOnce()
    {
        using var session = new MatchSession(new MatchEngine(), TimeProvider.System);
        var audio = new RecordingAudioService();
        var power = new RecordingPowerService();
        var settings = new StubSettingsService();
        var log = new RecordingLog();
        var operations = new MatchOperationalService(
            session,
            audio,
            power,
            settings,
            log);
        operations.Start();
        session.Execute(
            new CreateGameCommand(
                new MatchMetadata(),
                MatchRules.Fiba3x3,
                "Home",
                "Away",
                "#FFFFFF",
                "#FF5252"));

        var result = session.Execute(
            new TriggerBuzzerCommand(
                CommandSource.ClockScheduler,
                BuzzerKind.ShotClockWarning));
        await operations.StopAsync();

        Assert.IsTrue(result.IsAccepted);
        Assert.HasCount(1, audio.PlayedBuzzers);
        Assert.AreEqual(BuzzerKind.ShotClockWarning, audio.PlayedBuzzers[0]);
        Assert.HasCount(
            1,
            log.Messages
                .Where(message => message.Contains("BuzzerTriggeredEvent"))
                .ToArray());
    }

    private sealed class RecordingAudioService : IAudioService
    {
        public List<BuzzerKind> PlayedBuzzers { get; } = [];

        public void ApplySettings(AppSettings settings)
        {
        }

        public void Play(BuzzerKind buzzer) => PlayedBuzzers.Add(buzzer);

        public void Test(int volumePercent)
        {
        }
    }

    private sealed class RecordingPowerService : IPowerManagementService
    {
        public List<bool> States { get; } = [];

        public void SetGameActive(bool isActive) => States.Add(isActive);
    }

    private sealed class StubSettingsService : IAppSettingsService
    {
        public event EventHandler? SettingsChanged;

        public AppSettings Current { get; private set; } = new();

        public Task InitializeAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task UpdateAsync(
            AppSettings settings,
            CancellationToken cancellationToken = default)
        {
            Current = settings;
            SettingsChanged?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingLog : IAppLog
    {
        public string LogDirectory => string.Empty;

        public List<string> Messages { get; } = [];

        public bool WasStopped { get; private set; }

        public void Information(string message) => Messages.Add(message);

        public void LogError(string message, Exception exception) =>
            Messages.Add($"{message}: {exception.Message}");

        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            WasStopped = true;
            return Task.CompletedTask;
        }
    }
}
