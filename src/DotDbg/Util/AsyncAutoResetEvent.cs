namespace DotDbg.Util;

/// <summary>
/// Auto-reset event that stores signals and completes waiters.
/// Supports multiple producers and consumers, and cancels waiters whose
/// cancellation token is triggered.
/// </summary>
public sealed class AsyncAutoResetEvent<T>
{
    private sealed class Waiter
    {
        internal TaskCompletionSource<T> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal LinkedListNode<Waiter>? Node { get; set; }
    }

    private sealed record CancellationState(
        AsyncAutoResetEvent<T> Event,
        Waiter Waiter,
        CancellationToken Token
    );

    private readonly object _lock = new();
    private readonly Queue<T> _signals = new();
    private readonly LinkedList<Waiter> _waiters = new();

    /// <summary>
    /// Returns a task that completes when the event is set.
    /// If a signal is already available, the task completes immediately.
    /// </summary>
    public Task<T> WaitAsync(CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
            return Task.FromCanceled<T>(cancellationToken);

        Waiter waiter;
        lock (_lock)
        {
            if (_signals.TryDequeue(out var signal))
                return Task.FromResult(signal);

            waiter = new Waiter();
            waiter.Node = _waiters.AddLast(waiter);
        }

        if (cancellationToken.CanBeCanceled)
        {
            var registration = cancellationToken.Register(
                static state =>
                {
                    var cancellation = (CancellationState)state!;
                    cancellation.Event.CancelWaiter(cancellation.Waiter, cancellation.Token);
                },
                new CancellationState(this, waiter, cancellationToken)
            );
            _ = waiter.Completion.Task.ContinueWith(
                static (_, state) => ((CancellationTokenRegistration)state!).Dispose(),
                registration,
                CancellationToken.None,
                TaskContinuationOptions.None,
                TaskScheduler.Default
            );
        }

        return waiter.Completion.Task;
    }

    private void CancelWaiter(Waiter waiter, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            if (waiter.Node is null)
                return;
            _waiters.Remove(waiter.Node);
            waiter.Node = null;
            waiter.Completion.TrySetCanceled(cancellationToken);
        }
    }

    /// <summary>
    /// Signals the event. If a waiter is present it is completed immediately;
    /// otherwise the signal is stored for the next waiter.
    /// Canceled or already-completed waiters are skipped.
    /// </summary>
    public void Set(T value)
    {
        lock (_lock)
        {
            while (_waiters.First is { } node)
            {
                _waiters.RemoveFirst();
                node.Value.Node = null;
                if (node.Value.Completion.TrySetResult(value))
                    return;
            }

            _signals.Enqueue(value);
        }
    }

    /// <summary>
    /// Signals a waiter if one is present. Returns true if a waiter was
    /// completed; otherwise returns false and does not store the signal.
    /// </summary>
    public bool TrySet(T value)
    {
        lock (_lock)
        {
            while (_waiters.First is { } node)
            {
                _waiters.RemoveFirst();
                node.Value.Node = null;
                if (node.Value.Completion.TrySetResult(value))
                    return true;
            }

            return false;
        }
    }

    /// <summary>
    /// Clears any stored signals and cancels pending waiters.
    /// </summary>
    public void Reset()
    {
        lock (_lock)
        {
            _signals.Clear();
            while (_waiters.First is { } node)
            {
                _waiters.RemoveFirst();
                node.Value.Node = null;
                node.Value.Completion.TrySetCanceled();
            }
        }
    }
}
