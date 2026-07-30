using System.Collections.Concurrent;
using ThreeByThree.Centar.Scoreboard.Application.Operations;
using ThreeByThree.Centar.Scoreboard.Domain.Models;
using ThreeByThree.Centar.Scoreboard.Infrastructure.Overlay;

namespace ThreeByThree.Centar.Scoreboard.Infrastructure.Tests.Overlay;

[TestClass]
public sealed class OverlayStatePublisherTests(TestContext testContext)
{
    private static readonly int[] ExpectedSerializedScores = [1, 3];

    [TestMethod]
    [Timeout(5000, CooperativeCancellation = true)]
    public async Task TryPublish_BlockedSerializer_ReturnsAndCoalescesToLatestState()
    {
        using var releaseSerializer = new ManualResetEventSlim();
        var serializationStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var latestPublished = new TaskCompletionSource<string>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var serializedScores = new ConcurrentQueue<int>();
        var log = new RecordingLog();
        var publisher = new OverlayStatePublisher(
            log,
            json =>
            {
                if (json == "score:3")
                {
                    latestPublished.TrySetResult(json);
                }
            },
            snapshot =>
            {
                var score = snapshot.Home.Score;
                serializedScores.Enqueue(score);
                if (score == 1)
                {
                    serializationStarted.TrySetResult();
                    releaseSerializer.Wait();
                }

                return $"score:{score}";
            });
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            testContext.CancellationToken);
        var worker = Task.Run(
            () => publisher.RunAsync(cancellation.Token),
            CancellationToken.None);

        try
        {
            Assert.IsTrue(publisher.TryPublish(CreateSnapshot(1)));
            await serializationStarted.Task.WaitAsync(testContext.CancellationToken);

            var publishLatest = Task.Run(
                () =>
                {
                    Assert.IsTrue(publisher.TryPublish(CreateSnapshot(2)));
                    Assert.IsTrue(publisher.TryPublish(CreateSnapshot(3)));
                },
                CancellationToken.None);
            await publishLatest.WaitAsync(
                TimeSpan.FromSeconds(1),
                testContext.CancellationToken);

            releaseSerializer.Set();
            var published = await latestPublished.Task.WaitAsync(
                testContext.CancellationToken);

            Assert.AreEqual("score:3", published);
            Assert.AreEqual("score:3", publisher.CurrentJson);
            CollectionAssert.AreEqual(
                ExpectedSerializedScores,
                serializedScores.ToArray());
            Assert.IsEmpty(log.Errors);
        }
        finally
        {
            releaseSerializer.Set();
            publisher.Complete();
            await cancellation.CancelAsync();
            await worker.WaitAsync(TimeSpan.FromSeconds(1), CancellationToken.None);
        }
    }

    [TestMethod]
    [Timeout(5000, CooperativeCancellation = true)]
    public async Task RunAsync_SerializationFailure_LogsAndPublishesNextState()
    {
        var firstAttempted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var recoveredPublication = new TaskCompletionSource<string>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var log = new RecordingLog();
        var attempt = 0;
        var publisher = new OverlayStatePublisher(
            log,
            json => recoveredPublication.TrySetResult(json),
            snapshot =>
            {
                if (Interlocked.Increment(ref attempt) == 1)
                {
                    firstAttempted.TrySetResult();
                    throw new NotSupportedException("Controlled serialization failure.");
                }

                return $"score:{snapshot.Home.Score}";
            });
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            testContext.CancellationToken);
        var worker = Task.Run(
            () => publisher.RunAsync(cancellation.Token),
            CancellationToken.None);

        try
        {
            Assert.IsTrue(publisher.TryPublish(CreateSnapshot(1)));
            await firstAttempted.Task.WaitAsync(testContext.CancellationToken);
            Assert.IsTrue(publisher.TryPublish(CreateSnapshot(2)));

            var published = await recoveredPublication.Task.WaitAsync(
                testContext.CancellationToken);

            Assert.AreEqual("score:2", published);
            Assert.AreEqual("score:2", publisher.CurrentJson);
            var error = Assert.ContainsSingle(log.Errors);
            Assert.Contains("Match operation is unaffected.", error);
            Assert.Contains("Controlled serialization failure.", error);
        }
        finally
        {
            publisher.Complete();
            await cancellation.CancelAsync();
            await worker.WaitAsync(TimeSpan.FromSeconds(1), CancellationToken.None);
        }
    }

    private static MatchState CreateSnapshot(int homeScore) =>
        MatchState.Empty with
        {
            Home = new TeamState
            {
                Name = "Home",
                Score = homeScore,
            },
        };

    private sealed class RecordingLog : IAppLog
    {
        public string LogDirectory => string.Empty;

        public ConcurrentQueue<string> Errors { get; } = new();

        public void Information(string message)
        {
        }

        public void LogError(string message, Exception exception) =>
            Errors.Enqueue($"{message} {exception.Message}");

        public Task StopAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
