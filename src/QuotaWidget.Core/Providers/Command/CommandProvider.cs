using QuotaWidget.Config;
using QuotaWidget.Model;

namespace QuotaWidget.Providers.Command;

public class CommandProvider : IQuotaProvider
{
    public async Task<QuotaSnapshot> ReadAsync(ProviderConfig cfg, DateTimeOffset now, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(cfg.Command))
        {
            return new QuotaSnapshot(cfg.Id, cfg.Label, now, Array.Empty<QuotaFamily>(), "command: missing command path");
        }

        string executable = PathResolver.Resolve(cfg.Command);
        var timeout = TimeSpan.FromSeconds(cfg.TimeoutSeconds > 0 ? cfg.TimeoutSeconds.Value : 90);

        try
        {
            var result = await ProcessRunner.RunAsync(
                executable,
                (IEnumerable<string>?)cfg.Args ?? Array.Empty<string>(),
                timeout: timeout,
                cancellationToken: ct);

            if (!result.Success && string.IsNullOrEmpty(result.Stdout))
            {
                string err = !string.IsNullOrEmpty(result.Stderr) ? result.Stderr : (result.TimedOut ? $"Timeout ({timeout.TotalSeconds:F0}s)" : $"Exit code {result.ExitCode}");
                return new QuotaSnapshot(cfg.Id, cfg.Label, now, Array.Empty<QuotaFamily>(), $"command: {err}");
            }

            return GenericQuotaParser.Parse(result.Stdout, now, cfg.Id, cfg.Label);
        }
        catch (Exception ex)
        {
            return new QuotaSnapshot(cfg.Id, cfg.Label, now, Array.Empty<QuotaFamily>(), $"command: {ex.Message}");
        }
    }
}
