namespace QuotaWidget.Scheduling;

public interface ISchedulerClock
{
    DateTimeOffset UtcNow { get; }
    IDisposable ScheduleTimer(TimeSpan delay, Action callback);
}

public class SystemSchedulerClock : ISchedulerClock
{
    public static readonly SystemSchedulerClock Instance = new();

    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

    public IDisposable ScheduleTimer(TimeSpan delay, Action callback)
    {
        var timer = new System.Threading.Timer(_ => callback(), null, delay, Timeout.InfiniteTimeSpan);
        return new TimerSubscription(timer);
    }

    private sealed class TimerSubscription : IDisposable
    {
        private readonly System.Threading.Timer _timer;

        public TimerSubscription(System.Threading.Timer timer) => _timer = timer;

        public void Dispose() => _timer.Dispose();
    }
}
