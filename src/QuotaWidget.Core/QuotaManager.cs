using QuotaWidget.Config;
using QuotaWidget.Export;
using QuotaWidget.Model;
using QuotaWidget.Persistence;
using QuotaWidget.Providers;
using QuotaWidget.Scheduling;

namespace QuotaWidget;

public class QuotaManager
{
    private WidgetConfig _config;
    private readonly SnapshotManager _snapshotManager;
    private readonly Dictionary<string, QuotaSnapshot> _snapshots = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, QuotaSnapshot> _lastGoodSnapshots = new(StringComparer.OrdinalIgnoreCase);
    private readonly System.Timers.Timer _refreshTimer;
    private readonly RefreshScheduler _scheduler;
    private bool _isRefreshing;
    private readonly object _lock = new();

    public event Action? QuotasUpdated;

    public RefreshScheduler Scheduler => _scheduler;

    public QuotaManager(WidgetConfig config, SnapshotManager? snapshotManager = null, ISchedulerClock? clock = null)
    {
        _config = config;
        _snapshotManager = snapshotManager ?? new SnapshotManager();

        _scheduler = new RefreshScheduler(
            executeRunAsync: RefreshProvidersAsync,
            getMinRefreshSeconds: id =>
            {
                var p = _config.Providers.FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
                return p?.GetEffectiveMinRefreshSeconds() ?? 30;
            },
            getEnabledProviderIds: () => _config.Providers.Where(p => p.Enabled).Select(p => p.Id).ToList(),
            clock: clock
        );

        // Load saved snapshots
        var loaded = _snapshotManager.Load();
        foreach (var kvp in loaded)
        {
            _lastGoodSnapshots[kvp.Key] = kvp.Value;
            _snapshots[kvp.Key] = kvp.Value;
            _scheduler.RecordCompleted(kvp.Key, kvp.Value.ReadAt);
        }

        // Initialize slots for all configured providers
        foreach (var p in _config.Providers)
        {
            if (!_snapshots.ContainsKey(p.Id))
            {
                _snapshots[p.Id] = new QuotaSnapshot(p.Id, p.Label, DateTimeOffset.Now, Array.Empty<QuotaFamily>());
            }
        }

        // Export once at startup from the persisted snapshot
        ExportCurrentQuotas();

        int intervalMs = Math.Max(1, _config.RefreshMinutes) * 60 * 1000;
        _refreshTimer = new System.Timers.Timer(intervalMs)
        {
            AutoReset = true
        };
        _refreshTimer.Elapsed += (_, _) => _ = RefreshAsync();
        _refreshTimer.Start();
    }

    public void UpdateConfig(WidgetConfig newConfig)
    {
        lock (_lock)
        {
            _config = newConfig;

            foreach (var p in _config.Providers)
            {
                if (!_snapshots.ContainsKey(p.Id))
                {
                    _snapshots[p.Id] = new QuotaSnapshot(p.Id, p.Label, DateTimeOffset.Now, Array.Empty<QuotaFamily>());
                }
            }

            int intervalMs = Math.Max(1, _config.RefreshMinutes) * 60 * 1000;
            _refreshTimer.Interval = intervalMs;

            ExportCurrentQuotas();
        }
    }

    public IReadOnlyList<QuotaSnapshot> GetSnapshotsInConfigOrder()
    {
        lock (_lock)
        {
            var list = new List<QuotaSnapshot>();
            foreach (var p in _config.Providers)
            {
                if (_snapshots.TryGetValue(p.Id, out var snapshot))
                {
                    snapshot.Label = p.Label;
                    list.Add(snapshot);
                }
                else
                {
                    list.Add(new QuotaSnapshot(p.Id, p.Label, DateTimeOffset.Now, Array.Empty<QuotaFamily>()));
                }
            }
            return list;
        }
    }

    public QuotaExport GetCurrentExport(DateTimeOffset? now = null)
    {
        lock (_lock)
        {
            return QuotaExporter.Build(_config, _snapshots, now ?? DateTimeOffset.Now);
        }
    }

    public void ExportCurrentQuotas(DateTimeOffset? now = null)
    {
        try
        {
            QuotaExport export;
            string exportPath;
            lock (_lock)
            {
                export = QuotaExporter.Build(_config, _snapshots, now ?? DateTimeOffset.Now);
                exportPath = QuotaExporter.ResolveExportPath(_config);
            }
            QuotaExporter.SaveToFile(export, exportPath);
        }
        catch
        {
            // Do not crash
        }
    }

    public async Task RefreshAsync()
    {
        if (_isRefreshing)
            return;

        _isRefreshing = true;
        try
        {
            var now = DateTimeOffset.Now;
            List<ProviderConfig> enabledProviders;
            lock (_lock)
            {
                enabledProviders = _config.Providers.Where(p => p.Enabled).ToList();
            }

            var tasks = enabledProviders.Select(p => RunProviderAsync(p, now)).ToList();
            var results = await Task.WhenAll(tasks);

            ProcessResults(results, now);
        }
        catch
        {
            // Do not crash
        }
        finally
        {
            _isRefreshing = false;
        }
    }

    public async Task RefreshProvidersAsync(IReadOnlyList<string> providerIds)
    {
        var now = DateTimeOffset.Now;
        List<ProviderConfig> targets;
        lock (_lock)
        {
            targets = _config.Providers
                .Where(p => p.Enabled && providerIds.Contains(p.Id, StringComparer.OrdinalIgnoreCase))
                .ToList();
        }

        if (targets.Count == 0)
            return;

        var tasks = targets.Select(p => RunProviderAsync(p, now)).ToList();
        var results = await Task.WhenAll(tasks);

        ProcessResults(results, now);
    }

    private void ProcessResults(QuotaSnapshot[] results, DateTimeOffset now)
    {
        bool anyGood = false;
        lock (_lock)
        {
            foreach (var result in results)
            {
                _scheduler.RecordCompleted(result.ProviderId, now);

                if (result.HasError)
                {
                    if (_lastGoodSnapshots.TryGetValue(result.ProviderId, out var lastGood) && lastGood.Families.Count > 0)
                    {
                        var updated = new QuotaSnapshot(
                            result.ProviderId,
                            result.Label,
                            lastGood.ReadAt,
                            lastGood.Families,
                            error: result.Error);
                        _snapshots[result.ProviderId] = updated;
                    }
                    else
                    {
                        _snapshots[result.ProviderId] = result;
                    }
                }
                else
                {
                    _snapshots[result.ProviderId] = result;
                    _lastGoodSnapshots[result.ProviderId] = result;
                    anyGood = true;
                }
            }

            if (anyGood)
            {
                var toPersist = _lastGoodSnapshots.Values
                    .Where(s => s.Families.Count > 0)
                    .Select(s => new QuotaSnapshot(s.ProviderId, s.Label, s.ReadAt, s.Families, null))
                    .ToList();
                _snapshotManager.Save(toPersist);
            }

            ExportCurrentQuotas(now);
        }

        QuotasUpdated?.Invoke();
    }

    private static async Task<QuotaSnapshot> RunProviderAsync(ProviderConfig cfg, DateTimeOffset now)
    {
        if (!string.IsNullOrEmpty(cfg.ValidationError))
        {
            return new QuotaSnapshot(cfg.Id, cfg.Label, now, Array.Empty<QuotaFamily>(), cfg.ValidationError);
        }

        var provider = ProviderRegistry.Create(cfg.Type);
        if (provider == null)
        {
            return new QuotaSnapshot(cfg.Id, cfg.Label, now, Array.Empty<QuotaFamily>(), $"Unknown provider type: {cfg.Type}");
        }

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Max(5, (cfg.TimeoutSeconds ?? 90) + 5)));
            return await provider.ReadAsync(cfg, now, cts.Token);
        }
        catch (Exception ex)
        {
            return new QuotaSnapshot(cfg.Id, cfg.Label, now, Array.Empty<QuotaFamily>(), ex.Message);
        }
    }
}
