using System.Threading.Channels;
using PristonTale.Application.Services;
using PristonTale.Persistence.Abstractions.GameLogs;

namespace PristonTale.Application.GameLogs;

// Where the world reports the items and gold that changed hands (on the tick, so it must not block).
public interface IGameLog
{
    void ItemTransfers(IReadOnlyList<ItemTransferRecord> transfers);
}

public readonly record struct GameLogStats(long Written, long Failures, long Dropped);

// The background writer of item transfers. Rows wait in a bounded queue (Capacity); one writer inserts up to BatchSize per
// transaction and retries a refused batch (RetryBackoff). A full queue drops new rows and counts them: a log never holds
// the game. On stop, what is left is tried once, a batch at a time, and what storage refuses is counted as dropped.
public sealed class GameLogQueue(IGameLogRepository logs, TimeSpan? firstBackoff = null) : IGameLog
{
    public const int Capacity = 65536;
    private const int BatchSize = 500;

    private readonly Channel<ItemTransferRecord> _transfers = Channel.CreateBounded<ItemTransferRecord>(
        new BoundedChannelOptions(Capacity) { FullMode = BoundedChannelFullMode.Wait, SingleReader = true });
    private long _written;
    private long _failures;
    private long _dropped;

    public Action<Exception>? Failed { get; init; }

    public GameLogStats Stats => new(Interlocked.Read(ref _written), Interlocked.Read(ref _failures), Interlocked.Read(ref _dropped));

    public void ItemTransfers(IReadOnlyList<ItemTransferRecord> transfers)
    {
        foreach (var transfer in transfers)
        {
            if (!_transfers.Writer.TryWrite(transfer))
            {
                Interlocked.Increment(ref _dropped);
            }
        }
    }

    public async Task RunAsync(CancellationToken stop)
    {
        var backoff = new RetryBackoff(firstBackoff);
        var batch = new List<ItemTransferRecord>(BatchSize);
        while (!stop.IsCancellationRequested)
        {
            if (batch.Count == 0)
            {
                try
                {
                    await _transfers.Reader.WaitToReadAsync(stop);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }

            Fill(batch);
            if (await WriteAsync(batch))
            {
                batch.Clear();
                backoff.Reset();
                continue;
            }

            try
            {
                await Task.Delay(backoff.Next(), stop);
            }
            catch (OperationCanceledException)
            {
            }
        }

        _transfers.Writer.TryComplete();
        do
        {
            Fill(batch);
            if (batch.Count > 0 && !await WriteAsync(batch))
            {
                Interlocked.Add(ref _dropped, batch.Count);
            }

            batch.Clear();
        }
        while (_transfers.Reader.TryPeek(out _));
    }

    private void Fill(List<ItemTransferRecord> batch)
    {
        while (batch.Count < BatchSize && _transfers.Reader.TryRead(out var transfer))
        {
            batch.Add(transfer);
        }
    }

    private async Task<bool> WriteAsync(List<ItemTransferRecord> batch)
    {
        try
        {
            await logs.AppendItemTransfersAsync(batch, CancellationToken.None);
            Interlocked.Add(ref _written, batch.Count);
            return true;
        }
        catch (Exception exception)
        {
            Interlocked.Increment(ref _failures);
            Failed?.Invoke(exception);
            return false;
        }
    }
}
