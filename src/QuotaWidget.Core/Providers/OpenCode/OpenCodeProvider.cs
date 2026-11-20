using QuotaWidget.Config;
using QuotaWidget.Model;

namespace QuotaWidget.Providers.OpenCode;

public class OpenCodeProvider : IQuotaProvider
{
    public async Task<QuotaSnapshot> ReadAsync(ProviderConfig cfg, DateTimeOffset now, CancellationToken ct)
    {
        string command = cfg.Command ?? "opencode";
        string executable = PathResolver.Resolve(command);
        int resetDay = cfg.ResetDay ?? 1;

        var (_, nextReset, days) = OpenCodeParser.CalculatePeriod(now, resetDay);
        var timeout = TimeSpan.FromSeconds(cfg.TimeoutSeconds > 0 ? cfg.TimeoutSeconds.Value : 90);

        try
        {
            var result = await ProcessRunner.RunAsync(
                executable,
                new[] { "stats", "--days", days.ToString(), "--models", "--cost", "--json" },
                timeout: timeout,
                cancellationToken: ct);

            if (!result.Success && string.IsNullOrEmpty(result.Stdout))
            {
                string err = !string.IsNullOrEmpty(result.Stderr) ? result.Stderr : (result.TimedOut ? $"Timeout ({timeout.TotalSeconds:F0}s)" : $"Exit code {result.ExitCode}");
                return new QuotaSnapshot(cfg.Id, cfg.Label, now, Array.Empty<QuotaFamily>(), $"opencode: {err}");
            }

            return OpenCodeParser.Parse(result.Stdout, now, cfg.Id, cfg.Label, cfg.Budgets, nextReset);
        }
        catch (Exception ex)
        {
            return new QuotaSnapshot(cfg.Id, cfg.Label, now, Array.Empty<QuotaFamily>(), $"opencode: {ex.Message}");
        }
    }
}
