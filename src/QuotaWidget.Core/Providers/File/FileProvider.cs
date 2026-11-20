using System.IO;
using QuotaWidget.Config;
using QuotaWidget.Model;

namespace QuotaWidget.Providers.File;

public class FileProvider : IQuotaProvider
{
    public async Task<QuotaSnapshot> ReadAsync(ProviderConfig cfg, DateTimeOffset now, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(cfg.Path))
        {
            return new QuotaSnapshot(cfg.Id, cfg.Label, now, Array.Empty<QuotaFamily>(), "file: missing path");
        }

        string path = Environment.ExpandEnvironmentVariables(cfg.Path);
        if (!System.IO.File.Exists(path))
        {
            return new QuotaSnapshot(cfg.Id, cfg.Label, now, Array.Empty<QuotaFamily>(), $"file: not found ({path})");
        }

        try
        {
            string json = await System.IO.File.ReadAllTextAsync(path, ct);
            DateTimeOffset readAt = System.IO.File.GetLastWriteTimeUtc(path);

            var snapshot = GenericQuotaParser.Parse(json, readAt, cfg.Id, cfg.Label);

            int maxAge = cfg.MaxAgeMinutes > 0 ? cfg.MaxAgeMinutes.Value : 60;
            if ((now - readAt).TotalMinutes > maxAge)
            {
                // Older than maxAgeMinutes -> mark windows as estimated
                foreach (var family in snapshot.Families)
                {
                    foreach (var window in family.Windows)
                    {
                        window.IsEstimated = true;
                    }
                }
            }

            return snapshot;
        }
        catch (Exception ex)
        {
            return new QuotaSnapshot(cfg.Id, cfg.Label, now, Array.Empty<QuotaFamily>(), $"file: {ex.Message}");
        }
    }
}
