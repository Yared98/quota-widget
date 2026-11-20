namespace QuotaWidget.Scheduling;

public record SkippedProvider(string Id, string Reason);

public class RefreshRequestResult
{
    public bool Ok { get; set; } = true;
    public IReadOnlyList<string> Queued { get; set; }
    public IReadOnlyList<SkippedProvider> Skipped { get; set; }
    public Task WaitTask { get; set; }

    public RefreshRequestResult(IReadOnlyList<string> queued, IReadOnlyList<SkippedProvider> skipped, Task waitTask)
    {
        Queued = queued;
        Skipped = skipped;
        WaitTask = waitTask;
    }
}

public class RefreshScheduler
{
    private readonly Func<IReadOnlyList<string>, Task> _executeRunAsync;
    private readonly Func<string, int> _getMinRefreshSeconds;
    private readonly Func<IReadOnlyList<string>> _getEnabledProviderIds;
    private readonly ISchedulerClock _clock;
    private readonly object _lock = new();

    private readonly Dictionary<string, DateTimeOffset> _lastCompletedAt = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _currentlyRunning = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _pending = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<Waiter> _waiters = new();
    private IDisposable? _coalesceTimer;

    private sealed class Waiter
    {
        public HashSet<string> RemainingIds { get; }
        public TaskCompletionSource Tcs { get; }

        public Waiter(HashSet<string> remainingIds, TaskCompletionSource tcs)
        {
            RemainingIds = remainingIds;
            Tcs = tcs;
        }
    }

    public RefreshScheduler(
        Func<IReadOnlyList<string>, Task> executeRunAsync,
        Func<string, int> getMinRefreshSeconds,
        Func<IReadOnlyList<string>> getEnabledProviderIds,
        ISchedulerClock? clock = null)
    {
        _executeRunAsync = executeRunAsync;
        _getMinRefreshSeconds = getMinRefreshSeconds;
        _getEnabledProviderIds = getEnabledProviderIds;
        _clock = clock ?? SystemSchedulerClock.Instance;
    }

    public void RecordCompleted(string providerId, DateTimeOffset? completedAt = null)
    {
        lock (_lock)
        {
            _lastCompletedAt[providerId] = completedAt ?? _clock.UtcNow;
        }
    }

    public DateTimeOffset? GetLastCompleted(string providerId)
    {
        lock (_lock)
        {
            return _lastCompletedAt.TryGetValue(providerId, out var dt) ? dt : null;
        }
    }

    public bool IsRunning(string providerId)
    {
        lock (_lock)
        {
            return _currentlyRunning.Contains(providerId);
        }
    }

    public bool IsPending(string providerId)
    {
        lock (_lock)
        {
            return _pending.Contains(providerId);
        }
    }

    public RefreshRequestResult RequestRefresh(
        IReadOnlyList<string>? requestedIds = null,
        bool force = false,
        bool wait = false)
    {
        lock (_lock)
        {
            var targetIds = requestedIds != null && requestedIds.Count > 0
                ? requestedIds
                : _getEnabledProviderIds();

            var queued = new List<string>();
            var skipped = new List<SkippedProvider>();
            var now = _clock.UtcNow;

            foreach (var id in targetIds)
            {
                int minInterval = _getMinRefreshSeconds(id);
                if (!force && _lastCompletedAt.TryGetValue(id, out var lastTime) && (now - lastTime).TotalSeconds < minInterval)
                {
                    skipped.Add(new SkippedProvider(id, "min_interval"));
                    continue;
                }

                queued.Add(id);
                _pending.Add(id);
            }

            if (queued.Count > 0)
            {
                _coalesceTimer ??= _clock.ScheduleTimer(TimeSpan.FromSeconds(2), OnCoalesceTimerFired);
            }

            Task waitTask = Task.CompletedTask;
            if (wait && queued.Count > 0)
            {
                var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _waiters.Add(new Waiter(new HashSet<string>(queued, StringComparer.OrdinalIgnoreCase), tcs));
                waitTask = tcs.Task;
            }

            return new RefreshRequestResult(queued, skipped, waitTask);
        }
    }

    private void OnCoalesceTimerFired()
    {
        List<string> toRun = new();
        lock (_lock)
        {
            _coalesceTimer = null;

            foreach (var pendingId in _pending.ToList())
            {
                if (!_currentlyRunning.Contains(pendingId))
                {
                    toRun.Add(pendingId);
                    _pending.Remove(pendingId);
                    _currentlyRunning.Add(pendingId);
                }
            }
        }

        if (toRun.Count > 0)
        {
            _ = RunBatchAsync(toRun);
        }
    }

    private async Task RunBatchAsync(List<string> ids)
    {
        try
        {
            await _executeRunAsync(ids);
        }
        catch
        {
            // Runner captures errors in snapshot
        }
        finally
        {
            List<Waiter> completedWaiters = new();
            List<string> nextBatchToRun = new();

            lock (_lock)
            {
                var now = _clock.UtcNow;
                foreach (var id in ids)
                {
                    _lastCompletedAt[id] = now;
                    _currentlyRunning.Remove(id);
                }

                for (int i = _waiters.Count - 1; i >= 0; i--)
                {
                    var waiter = _waiters[i];
                    waiter.RemainingIds.ExceptWith(ids);
                    if (waiter.RemainingIds.Count == 0)
                    {
                        completedWaiters.Add(waiter);
                        _waiters.RemoveAt(i);
                    }
                }

                foreach (var pendingId in _pending.ToList())
                {
                    if (!_currentlyRunning.Contains(pendingId))
                    {
                        nextBatchToRun.Add(pendingId);
                        _pending.Remove(pendingId);
                        _currentlyRunning.Add(pendingId);
                    }
                }
            }

            foreach (var w in completedWaiters)
            {
                w.Tcs.TrySetResult();
            }

            if (nextBatchToRun.Count > 0)
            {
                _ = RunBatchAsync(nextBatchToRun);
            }
        }
    }
}
