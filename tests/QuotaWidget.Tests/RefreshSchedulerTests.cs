using QuotaWidget.Scheduling;

namespace QuotaWidget.Tests;

public class TestSchedulerClock : ISchedulerClock
{
    private DateTimeOffset _now;
    private readonly List<(DateTimeOffset DueTime, Action Callback, TimerHandle Handle)> _timers = new();

    public TestSchedulerClock(DateTimeOffset? initialTime = null)
    {
        _now = initialTime ?? new DateTimeOffset(2026, 11, 19, 12, 0, 0, TimeSpan.Zero);
    }

    public DateTimeOffset UtcNow => _now;

    public IDisposable ScheduleTimer(TimeSpan delay, Action callback)
    {
        var handle = new TimerHandle(this);
        var due = _now + delay;
        _timers.Add((due, callback, handle));
        return handle;
    }

    public void Advance(TimeSpan delta)
    {
        _now += delta;
        while (true)
        {
            var dueTimer = _timers
                .Where(t => !t.Handle.IsDisposed && t.DueTime <= _now)
                .OrderBy(t => t.DueTime)
                .FirstOrDefault();

            if (dueTimer.Callback == null)
                break;

            _timers.Remove(dueTimer);
            dueTimer.Callback();
        }
    }

    public void AdvanceSeconds(double seconds) => Advance(TimeSpan.FromSeconds(seconds));

    public class TimerHandle : IDisposable
    {
        private readonly TestSchedulerClock _clock;
        public bool IsDisposed { get; private set; }

        public TimerHandle(TestSchedulerClock clock) => _clock = clock;

        public void Dispose()
        {
            IsDisposed = true;
            _clock._timers.RemoveAll(t => t.Handle == this);
        }
    }
}

public class RefreshSchedulerTests
{
    [Fact]
    public void Coalescing_CombinesRequestsWithin2Seconds_IntoSingleRun()
    {
        var clock = new TestSchedulerClock();
        var executedRuns = new List<List<string>>();

        var scheduler = new RefreshScheduler(
            executeRunAsync: ids =>
            {
                executedRuns.Add(ids.ToList());
                return Task.CompletedTask;
            },
            getMinRefreshSeconds: _ => 30,
            getEnabledProviderIds: () => new[] { "p1", "p2", "p3" },
            clock: clock);

        // Request p1 at t=0
        var res1 = scheduler.RequestRefresh(new[] { "p1" });
        Assert.Equal(new[] { "p1" }, res1.Queued);
        Assert.Empty(res1.Skipped);

        // Clock advances 1 second (t=1s)
        clock.AdvanceSeconds(1.0);
        Assert.Empty(executedRuns);

        // Request p2 at t=1s
        var res2 = scheduler.RequestRefresh(new[] { "p2" });
        Assert.Equal(new[] { "p2" }, res2.Queued);

        // Advance 0.5s (t=1.5s), still coalescing
        clock.AdvanceSeconds(0.5);
        Assert.Empty(executedRuns);

        // Advance past 2s boundary (t=2.0s) -> coalesce timer fires!
        clock.AdvanceSeconds(0.5);
        Assert.Single(executedRuns);
        Assert.Equal(2, executedRuns[0].Count);
        Assert.Contains("p1", executedRuns[0]);
        Assert.Contains("p2", executedRuns[0]);
    }

    [Fact]
    public void MinInterval_SkipsProviders_WhenRefreshedRecently_UnlessForce()
    {
        var clock = new TestSchedulerClock();
        var executedRuns = new List<List<string>>();

        var scheduler = new RefreshScheduler(
            executeRunAsync: ids =>
            {
                executedRuns.Add(ids.ToList());
                return Task.CompletedTask;
            },
            getMinRefreshSeconds: id => id == "claude" ? 120 : 30,
            getEnabledProviderIds: () => new[] { "claude", "g2" },
            clock: clock);

        // First run at t=0 -> triggers at t=2
        scheduler.RequestRefresh(new[] { "claude", "g2" });
        clock.AdvanceSeconds(2.0);
        Assert.Single(executedRuns);

        // At t=10s (8s since last run): min interval for claude (120) and g2 (30) not passed
        clock.AdvanceSeconds(8.0);
        var resSkip = scheduler.RequestRefresh(new[] { "claude", "g2" }, force: false);
        Assert.Empty(resSkip.Queued);
        Assert.Equal(2, resSkip.Skipped.Count);
        Assert.All(resSkip.Skipped, s => Assert.Equal("min_interval", s.Reason));

        // Request with force: true queues both
        var resForce = scheduler.RequestRefresh(new[] { "claude" }, force: true);
        Assert.Equal(new[] { "claude" }, resForce.Queued);
        Assert.Empty(resForce.Skipped);

        clock.AdvanceSeconds(2.0);
        Assert.Equal(2, executedRuns.Count);
        Assert.Equal(new[] { "claude" }, executedRuns[1]);

        // Advance past g2 minRefreshSeconds (30s from t=2 is t=32s). Now t=40s.
        clock.AdvanceSeconds(28.0);
        var resG2 = scheduler.RequestRefresh(new[] { "g2" }, force: false);
        Assert.Equal(new[] { "g2" }, resG2.Queued);
        Assert.Empty(resG2.Skipped);
    }

    [Fact]
    public void NoParallelDuplicate_QueuesRequestDuringRun_ForRightAfter()
    {
        var clock = new TestSchedulerClock();
        var runTcs = new TaskCompletionSource();
        var executedRuns = new List<List<string>>();

        var scheduler = new RefreshScheduler(
            executeRunAsync: ids =>
            {
                executedRuns.Add(ids.ToList());
                return runTcs.Task;
            },
            getMinRefreshSeconds: _ => 10,
            getEnabledProviderIds: () => new[] { "p1" },
            clock: clock);

        // Start first run
        scheduler.RequestRefresh(new[] { "p1" });
        clock.AdvanceSeconds(2.0);
        Assert.Single(executedRuns);
        Assert.True(scheduler.IsRunning("p1"));

        // While p1 is still running, another request for p1 arrives with force
        var req2 = scheduler.RequestRefresh(new[] { "p1" }, force: true);
        Assert.Equal(new[] { "p1" }, req2.Queued);

        // Advance time to allow coalesce timer to fire
        clock.AdvanceSeconds(2.0);
        // p1 is still running in first run, so it must NOT run in parallel!
        Assert.Single(executedRuns);
        Assert.True(scheduler.IsPending("p1"));

        // Now first run finishes
        runTcs.SetResult();

        // Right after first run completes, p1 must run in second run!
        Assert.Equal(2, executedRuns.Count);
        Assert.Equal(new[] { "p1" }, executedRuns[1]);
        Assert.False(scheduler.IsRunning("p1"));
    }

    [Fact]
    public async Task WaitTask_CompletesWhenRunFinishes()
    {
        var clock = new TestSchedulerClock();
        var runTcs = new TaskCompletionSource();

        var scheduler = new RefreshScheduler(
            executeRunAsync: ids => runTcs.Task,
            getMinRefreshSeconds: _ => 30,
            getEnabledProviderIds: () => new[] { "p1" },
            clock: clock);

        var req = scheduler.RequestRefresh(new[] { "p1" }, wait: true);
        Assert.False(req.WaitTask.IsCompleted);

        clock.AdvanceSeconds(2.0);
        Assert.False(req.WaitTask.IsCompleted);

        runTcs.SetResult();
        await req.WaitTask;
        Assert.True(req.WaitTask.IsCompleted);
    }
}
