using System.Reflection;
using DotDbg.Util;
using Xunit;

namespace DotDbg.Tests;

public class AsyncAutoResetEventTests
{
    [Fact]
    public async Task TimedOutWaitersLeaveTheQueueBeforeTheNextSignal()
    {
        var signal = new AsyncAutoResetEvent<int>();
        for (var i = 0; i < 256; i++)
        {
            using var cancellation = new CancellationTokenSource();
            var wait = signal.WaitAsync(cancellation.Token);
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => wait);
        }

        Assert.Equal(0, PendingWaiterCount(signal));
        var next = signal.WaitAsync();
        signal.Set(42);
        Assert.Equal(42, await next);
    }

    [Fact]
    public async Task ResetCancelsPendingWaitersAndClearsStoredSignals()
    {
        var signal = new AsyncAutoResetEvent<int>();
        var waiting = signal.WaitAsync();
        signal.Reset();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
        Assert.Equal(0, PendingWaiterCount(signal));

        signal.Set(1);
        signal.Reset();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            signal.WaitAsync(cancellation.Token)
        );
        Assert.Equal(0, PendingWaiterCount(signal));
    }

    private static int PendingWaiterCount<T>(AsyncAutoResetEvent<T> signal)
    {
        var waiters = typeof(AsyncAutoResetEvent<T>)
            .GetField("_waiters", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(signal)!;
        return (int)waiters.GetType().GetProperty("Count")!.GetValue(waiters)!;
    }
}
