using System.IO;
using System.Text.Json;
using QuotaWidget.Providers;

namespace QuotaWidget.Config;

public static class ConfigStore
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public static string DefaultConfigDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "QuotaWidget");

    public static string DefaultConfigPath =>
        Path.Combine(DefaultConfigDirectory, "config.json");

    public static void EnsureConfigFile(string? targetPath = null, string? exampleDir = null)
    {
        string path = targetPath ?? DefaultConfigPath;
        if (File.Exists(path))
            return;

        string dir = Path.GetDirectoryName(path) ?? string.Empty;
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        string seedDir = exampleDir ?? AppDomain.CurrentDomain.BaseDirectory;
        string exampleFile = Path.Combine(seedDir, "config.example.json");
        if (File.Exists(exampleFile))
        {
            File.Copy(exampleFile, path, overwrite: true);
        }
        else
        {
            // Fallback default
            var defaultConfig = new WidgetConfig();
            Save(defaultConfig, path);
        }
    }

    public static WidgetConfig Load(string? configPath = null)
    {
        string path = configPath ?? DefaultConfigPath;
        if (!File.Exists(path))
        {
            EnsureConfigFile(path);
        }

        if (!File.Exists(path))
        {
            return new WidgetConfig();
        }

        try
        {
            string json = File.ReadAllText(path);
            return Parse(json);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // Show the problem in the bar instead of crashing; the user fixes the file and reloads.
            return new WidgetConfig
            {
                Providers = new List<ProviderConfig>
                {
                    new() { Id = "config", Type = "file", Label = "config", ValidationError = $"config.json: {ex.Message}" }
                }
            };
        }
    }

    public static WidgetConfig Parse(string json)
    {
        var config = JsonSerializer.Deserialize<WidgetConfig>(json, JsonOptions) ?? new WidgetConfig();
        Validate(config);
        return config;
    }

    public static void Validate(WidgetConfig config)
    {
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var p in config.Providers)
        {
            if (string.IsNullOrWhiteSpace(p.Id))
            {
                p.ValidationError = "Missing required field: id";
                continue;
            }

            if (!seenIds.Add(p.Id))
            {
                p.ValidationError = $"Duplicate provider id: {p.Id}";
                continue;
            }

            if (string.IsNullOrWhiteSpace(p.Type))
            {
                p.ValidationError = "Missing required field: type";
                continue;
            }

            if (!ProviderRegistry.IsKnownType(p.Type))
            {
                p.ValidationError = $"Unknown provider type: {p.Type}";
                continue;
            }

            if (p.Type.Equals("file", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(p.Path))
            {
                p.ValidationError = "Missing required field: path";
                continue;
            }

            if (p.Type.Equals("command", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(p.Command))
            {
                p.ValidationError = "Missing required field: command";
                continue;
            }
        }
    }

    public static void Save(WidgetConfig config, string? configPath = null)
    {
        string path = configPath ?? DefaultConfigPath;
        string? dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        string json = JsonSerializer.Serialize(config, JsonOptions);
        File.WriteAllText(path, json);
    }
}
