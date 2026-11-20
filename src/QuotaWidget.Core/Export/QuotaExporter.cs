using System.IO;
using System.Text.Json;
using QuotaWidget.Config;
using QuotaWidget.Model;

namespace QuotaWidget.Export;

public static class QuotaExporter
{
    public const string MutexName = @"Local\QuotaWidget.Export";

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        // keep "+" in time offsets readable for people and scripts (this output is never HTML)
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static string DefaultExportPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "QuotaWidget",
        "quotas.json");

    public static string ResolveExportPath(WidgetConfig? config = null)
    {
        if (!string.IsNullOrWhiteSpace(config?.ExportPath))
            return Environment.ExpandEnvironmentVariables(config.ExportPath);

        return DefaultExportPath;
    }

    public static QuotaExport Build(
        WidgetConfig config,
        IReadOnlyDictionary<string, QuotaSnapshot> snapshots,
        DateTimeOffset now)
    {
        var export = new QuotaExport
        {
            Version = 1,
            GeneratedAt = FormatIso(now),
            Providers = new List<QuotaExportProvider>()
        };

        var enabledProviders = config.Providers.Where(p => p.Enabled).ToList();

        foreach (var p in enabledProviders)
        {
            bool hasValidationError = !string.IsNullOrEmpty(p.ValidationError);
            snapshots.TryGetValue(p.Id, out var snapshot);
            snapshot ??= new QuotaSnapshot(p.Id, p.Label, now, Array.Empty<QuotaFamily>());

            var readAt = snapshot.ReadAt == default ? now : snapshot.ReadAt;
            bool stale = hasValidationError || QuotaLogic.IsStale(snapshot, now, config.RefreshMinutes, p.MainFamily);
            string? error = hasValidationError ? p.ValidationError : snapshot.Error;

            QuotaExportMainWindow? main = null;
            var families = new List<QuotaExportFamily>();

            if (!hasValidationError)
            {
                var thick = QuotaLogic.GetThickWindow(snapshot, p.MainFamily);
                var mainFam = QuotaLogic.GetMainFamily(snapshot, p.MainFamily);
                if (thick != null && mainFam != null)
                {
                    main = new QuotaExportMainWindow
                    {
                        Family = mainFam.Name,
                        Window = thick.Name,
                        UsedPercent = Math.Round(thick.UsedPercent, 1),
                        ResetsAt = thick.ResetsAt.HasValue ? FormatIso(thick.ResetsAt.Value) : null
                    };
                }

                foreach (var fam in snapshot.Families)
                {
                    var famExport = new QuotaExportFamily
                    {
                        Name = fam.Name,
                        Windows = fam.Windows.Select(w => new QuotaExportWindow
                        {
                            Name = w.Name,
                            UsedPercent = Math.Round(w.UsedPercent, 1),
                            ResetsAt = w.ResetsAt.HasValue ? FormatIso(w.ResetsAt.Value) : null,
                            Estimated = w.IsEstimated
                        }).ToList()
                    };
                    families.Add(famExport);
                }
            }

            var exportProvider = new QuotaExportProvider
            {
                Id = p.Id,
                Type = p.Type,
                Label = p.Label,
                ReadAt = FormatIso(readAt),
                Stale = stale,
                Error = error,
                Workers = p.Workers != null ? new List<string>(p.Workers) : new List<string>(),
                Main = main,
                Families = families,
                WorkerFamilies = p.WorkerFamilies != null ? new Dictionary<string, string>(p.WorkerFamilies, StringComparer.OrdinalIgnoreCase) : null
            };

            export.Providers.Add(exportProvider);
        }

        return export;
    }

    public static void SaveToFile(QuotaExport export, string? destinationPath = null)
    {
        string path = destinationPath ?? DefaultExportPath;
        string? dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        string tmpPath = path + ".tmp";
        string json = JsonSerializer.Serialize(export, JsonOptions);

        using var mutex = new Mutex(false, MutexName);
        bool acquired = false;
        try
        {
            try
            {
                acquired = mutex.WaitOne(TimeSpan.FromSeconds(5));
            }
            catch (AbandonedMutexException)
            {
                acquired = true;
            }

            File.WriteAllText(tmpPath, json);

            if (File.Exists(path))
            {
                try
                {
                    File.Replace(tmpPath, path, null);
                }
                catch
                {
                    File.Move(tmpPath, path, overwrite: true);
                }
            }
            else
            {
                File.Move(tmpPath, path);
            }
        }
        finally
        {
            if (acquired)
            {
                try
                {
                    mutex.ReleaseMutex();
                }
                catch
                {
                    // Ignore release errors
                }
            }

            try
            {
                if (File.Exists(tmpPath))
                    File.Delete(tmpPath);
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }

    public static QuotaExport? LoadFromFile(string? filePath = null)
    {
        string path = filePath ?? DefaultExportPath;
        if (!File.Exists(path))
            return null;

        try
        {
            string json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<QuotaExport>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private static string FormatIso(DateTimeOffset dto)
    {
        return dto.ToString("yyyy-MM-ddTHH:mm:sszzz");
    }
}
