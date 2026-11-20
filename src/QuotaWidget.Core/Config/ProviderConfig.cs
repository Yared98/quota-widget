using System.Text.Json.Serialization;

namespace QuotaWidget.Config;

public class ProviderConfig
{
    public string Id { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public bool HideOnBar { get; set; } = false;
    public string? MainFamily { get; set; }
    public string? Tag { get; set; }

    // Provider-specific configs
    public string? Command { get; set; }
    public List<string>? Args { get; set; }
    public int? TimeoutSeconds { get; set; }
    public string? Path { get; set; }
    public int? MaxAgeMinutes { get; set; }
    public string? RunAsUser { get; set; }
    public string? ExchangeDir { get; set; }
    public int? ResetDay { get; set; }
    public List<BudgetConfig>? Budgets { get; set; }

    // Agent API additions
    public List<string>? Workers { get; set; }
    public Dictionary<string, string>? WorkerFamilies { get; set; }
    public int? MinRefreshSeconds { get; set; }

    [JsonIgnore]
    public string? ValidationError { get; set; }

    public int GetEffectiveMinRefreshSeconds()
    {
        if (MinRefreshSeconds.HasValue)
            return MinRefreshSeconds.Value;

        return string.Equals(Type, "claude-code", StringComparison.OrdinalIgnoreCase) ? 120 : 30;
    }
}
