using System.IO;
using System.Text.Json;
using QuotaWidget.Config;
using QuotaWidget.Export;
using QuotaWidget.Ipc;
using QuotaWidget.Model;
using QuotaWidget.Persistence;
using QuotaWidget.Providers;
using QuotaWidget.Selection;

namespace QuotaWidget.Cli;

public static class CliRunner
{
    public static async Task<int> RunAsync(string[] args, TextWriter? stdout = null, TextWriter? stderr = null)
    {
        stdout ??= Console.Out;
        stderr ??= Console.Error;

        var options = CliOptions.Parse(args);

        if (!string.IsNullOrEmpty(options.Error))
        {
            await stderr.WriteLineAsync(options.Error);
            return 1;
        }

        switch (options.Command)
        {
            case CliCommand.Help:
                PrintUsage(stdout);
                return 0;

            case CliCommand.Status:
                return await HandleStatusAsync(stdout, stderr);

            case CliCommand.Json:
                return await HandleJsonAsync(stdout, stderr);

            case CliCommand.Refresh:
                return await HandleRefreshAsync(options, stdout, stderr);

            case CliCommand.Pick:
                return await HandlePickAsync(options, stdout, stderr);

            default:
                await stderr.WriteLineAsync($"Unknown command");
                return 1;
        }
    }

    private static void PrintUsage(TextWriter stdout)
    {
        stdout.WriteLine("Usage: qw <command> [options]");
        stdout.WriteLine();
        stdout.WriteLine("Commands:");
        stdout.WriteLine("  json                      print quotas.json (if missing: read all providers once, then print)");
        stdout.WriteLine("  refresh [ids...] [--wait] [--force] [--timeout <s>]");
        stdout.WriteLine("                            trigger quota refresh (via widget pipe if running, else direct)");
        stdout.WriteLine("  pick <worker> [--max <percent>] [--json]");
        stdout.WriteLine("                            pick provider with lowest usage matching worker tag");
        stdout.WriteLine("  status                    is the widget running? (ping)");
    }

    private static async Task<int> HandleStatusAsync(TextWriter stdout, TextWriter stderr)
    {
        try
        {
            var res = await PipeClient.SendRequestAsync(new PipeRequest { Cmd = "ping" }, connectTimeoutMs: 1000);
            if (res.Ok)
            {
                await stdout.WriteLineAsync($"running (v{res.Version ?? "1.0.0"})");
                return 0;
            }

            await stderr.WriteLineAsync(res.Error ?? "Widget is not running");
            return 1;
        }
        catch (Exception ex)
        {
            await stderr.WriteLineAsync($"Widget is not running: {ex.Message}");
            return 1;
        }
    }

    private static async Task<int> HandleJsonAsync(TextWriter stdout, TextWriter stderr)
    {
        var config = ConfigStore.Load();
        string exportPath = QuotaExporter.ResolveExportPath(config);

        if (File.Exists(exportPath))
        {
            try
            {
                string json = await File.ReadAllTextAsync(exportPath);
                await stdout.WriteLineAsync(json);
                return 0;
            }
            catch (Exception ex)
            {
                await stderr.WriteLineAsync($"Failed to read quotas.json: {ex.Message}");
                return 1;
            }
        }

        // If missing: read all providers once, then print
        try
        {
            var snapshotManager = new SnapshotManager();
            var qm = new QuotaManager(config, snapshotManager);
            await qm.RefreshAsync();

            if (File.Exists(exportPath))
            {
                string json = await File.ReadAllTextAsync(exportPath);
                await stdout.WriteLineAsync(json);
                return 0;
            }

            await stderr.WriteLineAsync("quotas.json not found and could not be generated.");
            return 1;
        }
        catch (Exception ex)
        {
            await stderr.WriteLineAsync($"Failed to generate quotas.json: {ex.Message}");
            return 1;
        }
    }

    private static async Task<int> HandleRefreshAsync(CliOptions options, TextWriter stdout, TextWriter stderr)
    {
        // 1. Widget running: forward the request to it.
        if (!options.Standalone)
        {
            try
            {
                int responseMs = options.Wait ? (options.TimeoutSeconds ?? 120) * 1000 : 2000;
                var request = new PipeRequest
                {
                    Cmd = "refresh",
                    Ids = options.Ids,
                    Wait = options.Wait,
                    Force = options.Force
                };

                var res = await PipeClient.SendRequestAsync(request, responseTimeoutMs: responseMs);
                if (!res.Ok)
                {
                    await stderr.WriteLineAsync(res.Error ?? "Refresh failed");
                    return 1;
                }
                if (options.Wait && res.Quotas != null)
                    await stdout.WriteLineAsync(JsonSerializer.Serialize(res.Quotas, QuotaExporter.JsonOptions));
                return 0;
            }
            catch (WidgetNotRunningException)
            {
                // fall through to the standalone read below
            }
            catch (Exception ex)
            {
                // Connected but the reply failed or timed out: the widget is still doing the work,
                // so do NOT read the providers a second time here.
                await stderr.WriteLineAsync($"Widget did not answer in time: {ex.Message}");
                return 1;
            }
        }

        // 2. Widget not running and the caller does not want to wait (hooks): hand the read to a
        //    detached copy of this CLI and return at once, so a harness hook never blocks.
        if (!options.Wait && !options.Standalone && Environment.ProcessPath is { } self)
        {
            var psi = new System.Diagnostics.ProcessStartInfo(self)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden,
            };
            psi.ArgumentList.Add("refresh");
            foreach (var id in options.Ids) psi.ArgumentList.Add(id);
            psi.ArgumentList.Add("--standalone");
            if (options.Force) psi.ArgumentList.Add("--force");
            System.Diagnostics.Process.Start(psi)?.Dispose();
            return 0;
        }

        // 3. Read the providers in this process.
        try
        {
            var config = ConfigStore.Load();
            string exportPath = QuotaExporter.ResolveExportPath(config);
            var existingExport = QuotaExporter.LoadFromFile(exportPath);
            var exportMap = existingExport?.Providers?.ToDictionary(p => p.Id, p => p, StringComparer.OrdinalIgnoreCase);

            var targets = (options.Ids.Count > 0
                ? config.Providers.Where(p => p.Enabled && options.Ids.Contains(p.Id, StringComparer.OrdinalIgnoreCase))
                : config.Providers.Where(p => p.Enabled)).ToList();

            var now = DateTimeOffset.Now;
            var providersToRun = new List<ProviderConfig>();

            foreach (var p in targets)
            {
                int minSec = p.GetEffectiveMinRefreshSeconds();
                bool withinMin = false;
                if (!options.Force && exportMap != null && exportMap.TryGetValue(p.Id, out var ep))
                {
                    if (DateTimeOffset.TryParse(ep.ReadAt, out var readAt))
                    {
                        if ((now - readAt).TotalSeconds < minSec)
                        {
                            withinMin = true;
                        }
                    }
                }

                if (!withinMin)
                {
                    providersToRun.Add(p);
                }
            }

            if (providersToRun.Count > 0)
            {
                var snapshotManager = new SnapshotManager();
                var loadedSnapshots = snapshotManager.Load();

                var tasks = providersToRun.Select(p => RunProviderAsync(p, now)).ToList();
                var results = await Task.WhenAll(tasks);

                bool anyGood = false;
                foreach (var result in results)
                {
                    if (result.HasError)
                    {
                        if (loadedSnapshots.TryGetValue(result.ProviderId, out var lastGood) && lastGood.Families.Count > 0)
                        {
                            loadedSnapshots[result.ProviderId] = new QuotaSnapshot(
                                result.ProviderId,
                                result.Label,
                                lastGood.ReadAt,
                                lastGood.Families,
                                error: result.Error);
                        }
                        else
                        {
                            loadedSnapshots[result.ProviderId] = result;
                        }
                    }
                    else
                    {
                        loadedSnapshots[result.ProviderId] = result;
                        anyGood = true;
                    }
                }

                if (anyGood)
                {
                    var toPersist = loadedSnapshots.Values
                        .Where(s => s.Families.Count > 0)
                        .Select(s => new QuotaSnapshot(s.ProviderId, s.Label, s.ReadAt, s.Families, null))
                        .ToList();
                    snapshotManager.Save(toPersist);
                }

                var updatedExport = QuotaExporter.Build(config, loadedSnapshots, now);
                QuotaExporter.SaveToFile(updatedExport, exportPath);
            }

            if (options.Wait)
            {
                var current = QuotaExporter.LoadFromFile(exportPath);
                if (current != null)
                    await stdout.WriteLineAsync(JsonSerializer.Serialize(current, QuotaExporter.JsonOptions));
            }
            return 0;
        }
        catch (Exception ex)
        {
            await stderr.WriteLineAsync($"Standalone refresh failed: {ex.Message}");
            return 1;
        }
    }

    private static async Task<int> HandlePickAsync(CliOptions options, TextWriter stdout, TextWriter stderr)
    {
        var config = ConfigStore.Load();
        string exportPath = QuotaExporter.ResolveExportPath(config);
        var export = QuotaExporter.LoadFromFile(exportPath);

        if (export == null)
        {
            // Missing: read all providers once, then retry
            try
            {
                var snapshotManager = new SnapshotManager();
                var qm = new QuotaManager(config, snapshotManager);
                await qm.RefreshAsync();
                export = QuotaExporter.LoadFromFile(exportPath);
            }
            catch
            {
                // Fallback attempt failed
            }
        }

        if (export == null)
        {
            await stderr.WriteLineAsync("quotas.json not found and could not be generated.");
            return 1;
        }

        var configOrder = config.Providers.Select(p => p.Id).ToList();
        var pickResult = WorkerPicker.Pick(options.Worker!, export, options.MaxPercent, configOrder);

        if (pickResult == null)
        {
            await stderr.WriteLineAsync($"No qualifying provider found for worker '{options.Worker}'.");
            return 3;
        }

        if (options.Json)
        {
            string json = JsonSerializer.Serialize(pickResult, new JsonSerializerOptions
            {
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            });
            await stdout.WriteLineAsync(json);
            return 0;
        }

        await stdout.WriteLineAsync(pickResult.Id);
        return 0;
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
