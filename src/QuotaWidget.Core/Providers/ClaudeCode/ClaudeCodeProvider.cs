using System.IO;
using QuotaWidget.Config;
using QuotaWidget.Model;

namespace QuotaWidget.Providers.ClaudeCode;

public class ClaudeCodeProvider : IQuotaProvider
{
    private static readonly string[] CliArgs =
    {
        "-p", "ok", "--model", "haiku", "--output-format", "stream-json", "--verbose", "--max-turns", "1",
        "--system-prompt", "Reply ok.", "--tools", "", "--strict-mcp-config", "--setting-sources", "",
        "--no-session-persistence"
    };

    public async Task<QuotaSnapshot> ReadAsync(ProviderConfig cfg, DateTimeOffset now, CancellationToken ct)
    {
        string command = cfg.Command ?? "claude";
        string executable = PathResolver.Resolve(command);
        var timeout = TimeSpan.FromSeconds(cfg.TimeoutSeconds > 0 ? cfg.TimeoutSeconds.Value : 90);

        try
        {
            var result = await ProcessRunner.RunAsync(
                executable,
                CliArgs,
                workingDirectory: Path.GetTempPath(),
                timeout: timeout,
                cancellationToken: ct);

            if (!result.Success && string.IsNullOrEmpty(result.Stdout))
            {
                string err = !string.IsNullOrEmpty(result.Stderr) ? result.Stderr : (result.TimedOut ? $"Timeout ({timeout.TotalSeconds:F0}s)" : $"Exit code {result.ExitCode}");
                return new QuotaSnapshot(cfg.Id, cfg.Label, now, Array.Empty<QuotaFamily>(), $"claude: {err}");
            }

            var snapshot = ClaudeStreamParser.Parse(result.Stdout, now, cfg.Id, cfg.Label);
            if (snapshot != null)
                return snapshot;

            string preview = result.Stdout.Trim();
            if (preview.Length > 120) preview = preview[..120];
            return new QuotaSnapshot(cfg.Id, cfg.Label, now, Array.Empty<QuotaFamily>(), $"claude: no rate limit info found ({preview})");
        }
        catch (Exception ex)
        {
            return new QuotaSnapshot(cfg.Id, cfg.Label, now, Array.Empty<QuotaFamily>(), $"claude: {ex.Message}");
        }
    }
}
