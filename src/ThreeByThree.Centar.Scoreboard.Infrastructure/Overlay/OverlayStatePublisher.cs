using System.Text.Json;
using System.Threading.Channels;
using ThreeByThree.Centar.Scoreboard.Application.Operations;
using ThreeByThree.Centar.Scoreboard.Domain.Models;

namespace ThreeByThree.Centar.Scoreboard.Infrastructure.Overlay;

internal sealed class OverlayStatePublisher
{
    private const string InitialJson =
        """{"homeTeam":"HOME","awayTeam":"AWAY","homeScore":0,"awayScore":0,"homeFouls":0,"awayFouls":0,"gameClock":"10:00","shotClock":"12","gameClockRunning":false,"shotClockRunning":false}""";

    private static readonly JsonSerializerOptions SerializerOptions =
        new(JsonSerializerDefaults.Web);

    private readonly IAppLog log;
    private readonly Action<string> statePublished;
    private readonly Func<MatchState, string> serialize;
    private readonly Channel<MatchState> pendingStates =
        Channel.CreateBounded<MatchState>(
            new BoundedChannelOptions(1)
            {
                SingleReader = true,
                SingleWriter = false,
                AllowSynchronousContinuations = false,
                FullMode = BoundedChannelFullMode.DropOldest,
            });

    private string currentJson = InitialJson;

    public OverlayStatePublisher(IAppLog log, Action<string> statePublished)
        : this(log, statePublished, Serialize)
    {
    }

    internal OverlayStatePublisher(
        IAppLog log,
        Action<string> statePublished,
        Func<MatchState, string> serialize)
    {
        this.log = log;
        this.statePublished = statePublished;
        this.serialize = serialize;
    }

    public string CurrentJson => Volatile.Read(ref currentJson);

    public bool TryPublish(MatchState snapshot) =>
        pendingStates.Writer.TryWrite(snapshot);

    public void Complete() => pendingStates.Writer.TryComplete();

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (await pendingStates.Reader
                       .WaitToReadAsync(cancellationToken)
                       .ConfigureAwait(false))
            {
                MatchState? latest = null;
                while (pendingStates.Reader.TryRead(out var snapshot))
                {
                    latest = snapshot;
                }

                if (latest is null)
                {
                    continue;
                }

                string json;
                try
                {
                    json = serialize(latest);
                }
                catch (Exception exception) when (
                    exception is JsonException or NotSupportedException)
                {
                    log.LogError(
                        "An OBS overlay state could not be serialized. " +
                        "Match operation is unaffected.",
                        exception);
                    continue;
                }

                Volatile.Write(ref currentJson, json);
                statePublished(json);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private static string Serialize(MatchState snapshot) =>
        JsonSerializer.Serialize(
            ScoreboardOverlayState.FromSnapshot(snapshot),
            SerializerOptions);
}
