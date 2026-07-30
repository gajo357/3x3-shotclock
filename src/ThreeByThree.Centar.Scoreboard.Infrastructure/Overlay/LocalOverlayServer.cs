using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ThreeByThree.Centar.Scoreboard.Application;
using ThreeByThree.Centar.Scoreboard.Application.Operations;
using ThreeByThree.Centar.Scoreboard.Application.Overlay;

namespace ThreeByThree.Centar.Scoreboard.Infrastructure.Overlay;

public sealed class LocalOverlayServer : ILocalOverlayServer, IDisposable
{
    public const string OverlayUrl = "http://127.0.0.1:5050/overlay";

    private const string ListenUrl = "http://127.0.0.1:5050";
    private static readonly TimeSpan ClockPublishInterval = TimeSpan.FromMilliseconds(100);

    private readonly MatchSession session;
    private readonly IAppLog log;
    private readonly object lifecycleGate = new();
    private readonly ConcurrentDictionary<Guid, Channel<string>> clients = new();
    private readonly OverlayStatePublisher statePublisher;
    private readonly CancellationTokenSource stopping = new();

    private Task backgroundTask = Task.CompletedTask;
    private bool isStarted;
    private bool isStopped;
    private bool isDisposed;

    public LocalOverlayServer(MatchSession session, IAppLog log)
    {
        this.session = session;
        this.log = log;
        statePublisher = new OverlayStatePublisher(log, PublishToClients);
    }

    public void Start()
    {
        lock (lifecycleGate)
        {
            ObjectDisposedException.ThrowIf(isDisposed, this);
            if (isStarted)
            {
                return;
            }

            isStarted = true;
            session.EventsCommitted += OnEventsCommitted;
            backgroundTask = Task.Run(() => RunAsync(stopping.Token));
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        Task task;
        lock (lifecycleGate)
        {
            if (!isStarted || isStopped)
            {
                return;
            }

            isStopped = true;
            session.EventsCommitted -= OnEventsCommitted;
            statePublisher.Complete();
            task = backgroundTask;
        }

        await stopping.CancelAsync().ConfigureAwait(false);
        await task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (isDisposed)
        {
            return;
        }

        StopAsync().GetAwaiter().GetResult();
        isDisposed = true;
        stopping.Dispose();
        GC.SuppressFinalize(this);
    }

    private void OnEventsCommitted(object? sender, MatchEventsCommittedEventArgs e) =>
        _ = statePublisher.TryPublish(e.Snapshot);

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        var statePublisherTask = statePublisher.RunAsync(cancellationToken);
        var clockSamplerTask = SampleRunningClocksAsync(cancellationToken);
        _ = statePublisher.TryPublish(session.Snapshot);

        try
        {
            await RunWebServerAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            log.LogError(
                "Local OBS overlay server failed. Match operation is unaffected.",
                exception);
        }
        finally
        {
            await stopping.CancelAsync().ConfigureAwait(false);
            statePublisher.Complete();
            await ObserveBackgroundTasksAsync(
                    statePublisherTask,
                    clockSamplerTask,
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async Task SampleRunningClocksAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(ClockPublishInterval);

        try
        {
            while (await timer
                       .WaitForNextTickAsync(cancellationToken)
                       .ConfigureAwait(false))
            {
                var snapshot = session.Snapshot;
                if (snapshot.GameClock.IsRunning || snapshot.ShotClock.IsRunning)
                {
                    _ = statePublisher.TryPublish(snapshot);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task RunWebServerAsync(CancellationToken cancellationToken)
    {
        var builder = WebApplication.CreateSlimBuilder(
            new WebApplicationOptions
            {
                Args = [],
            });
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls(ListenUrl);

        await using var app = builder.Build();
        app.MapGet("/", () => Results.Redirect("/overlay"));
        app.MapGet(
            "/state",
            () => Results.Text(
                statePublisher.CurrentJson,
                "application/json; charset=utf-8"));
        app.MapGet(
            "/overlay",
            () => Results.Content(
                OverlayPage.Html,
                "text/html; charset=utf-8"));
        app.MapGet("/events", StreamEventsAsync);

        await app.StartAsync(cancellationToken).ConfigureAwait(false);
        log.Information($"Local OBS overlay is available at {OverlayUrl}.");

        try
        {
            await app.WaitForShutdownAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            using var shutdownTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await app.StopAsync(shutdownTimeout.Token).ConfigureAwait(false);
        }
    }

    private async Task StreamEventsAsync(HttpContext context)
    {
        context.Response.ContentType = "text/event-stream";
        context.Response.Headers.CacheControl = "no-cache";
        context.Response.Headers.Connection = "keep-alive";

        var clientId = Guid.NewGuid();
        var channel = Channel.CreateBounded<string>(
            new BoundedChannelOptions(1)
            {
                SingleReader = true,
                SingleWriter = false,
                AllowSynchronousContinuations = false,
                FullMode = BoundedChannelFullMode.DropOldest,
            });
        clients[clientId] = channel;

        try
        {
            await WriteEventAsync(
                    context,
                    statePublisher.CurrentJson,
                    context.RequestAborted)
                .ConfigureAwait(false);

            await foreach (var json in channel.Reader.ReadAllAsync(context.RequestAborted))
            {
                await WriteEventAsync(context, json, context.RequestAborted)
                    .ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
        }
        finally
        {
            if (clients.TryRemove(clientId, out var removed))
            {
                removed.Writer.TryComplete();
            }
        }
    }

    private void PublishToClients(string json)
    {
        foreach (var client in clients.Values)
        {
            _ = client.Writer.TryWrite(json);
        }
    }

    private static async Task WriteEventAsync(
        HttpContext context,
        string json,
        CancellationToken cancellationToken)
    {
        await context.Response
            .WriteAsync($"data: {json}\n\n", cancellationToken)
            .ConfigureAwait(false);
        await context.Response.Body
            .FlushAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task ObserveBackgroundTasksAsync(
        Task statePublisherTask,
        Task clockSamplerTask,
        CancellationToken cancellationToken)
    {
        try
        {
            await Task.WhenAll(statePublisherTask, clockSamplerTask).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            log.LogError(
                "An OBS overlay background worker failed. Match operation is unaffected.",
                exception);
        }
    }
}
