using System.Threading.Channels;
using ThreeByThree.Centar.Scoreboard.Application.Operations;
using ThreeByThree.Centar.Scoreboard.Infrastructure.Persistence;

namespace ThreeByThree.Centar.Scoreboard.Infrastructure.Diagnostics;

public sealed class FileAppLog : IAppLog
{
    private readonly TimeProvider timeProvider;
    private readonly Channel<string> entries = Channel.CreateUnbounded<string>(
        new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
        });
    private readonly Task writerTask;
    private bool isStopped;

    public FileAppLog(GameStoragePaths paths, TimeProvider timeProvider)
    {
        this.timeProvider = timeProvider;
        LogDirectory = Path.Combine(
            Directory.GetParent(paths.ActiveDirectory)?.FullName ?? paths.ActiveDirectory,
            "Logs");
        writerTask = ProcessEntriesAsync();
    }

    public string LogDirectory { get; }

    public void Information(string message) => Write("INF", message);

    public void LogError(string message, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        Write("ERR", $"{message} | {exception}");
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (isStopped)
        {
            return;
        }

        isStopped = true;
        entries.Writer.TryComplete();
        await writerTask.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private void Write(string level, string message)
    {
        if (isStopped)
        {
            return;
        }

        var timestamp = timeProvider.GetUtcNow();
        var normalized = message.ReplaceLineEndings(" ");
        _ = entries.Writer.TryWrite($"{timestamp:O} [{level}] {normalized}");
    }

    private async Task ProcessEntriesAsync()
    {
        await foreach (var entry in entries.Reader.ReadAllAsync())
        {
            try
            {
                Directory.CreateDirectory(LogDirectory);
                var fileName = $"scoreboard-{timeProvider.GetUtcNow():yyyyMMdd}.log";
                var path = Path.Combine(LogDirectory, fileName);
                await File.AppendAllTextAsync(path, entry + Environment.NewLine)
                    .ConfigureAwait(false);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                // Diagnostics must never interrupt match operation.
            }
        }
    }
}
