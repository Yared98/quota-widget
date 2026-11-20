using System.Text.Json.Serialization;

namespace QuotaWidget.Export;

public class QuotaExport
{
    [JsonPropertyName("version")]
    public int Version { get; set; } = 1;

    [JsonPropertyName("generated_at")]
    public string GeneratedAt { get; set; } = string.Empty;

    [JsonPropertyName("providers")]
    public List<QuotaExportProvider> Providers { get; set; } = new();
}

public class QuotaExportProvider
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("label")]
    public string Label { get; set; } = string.Empty;

    [JsonPropertyName("read_at")]
    public string ReadAt { get; set; } = string.Empty;

    [JsonPropertyName("stale")]
    public bool Stale { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("workers")]
    public List<string> Workers { get; set; } = new();

    [JsonPropertyName("main")]
    public QuotaExportMainWindow? Main { get; set; }

    [JsonPropertyName("families")]
    public List<QuotaExportFamily> Families { get; set; } = new();

    [JsonPropertyName("workerFamilies")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, string>? WorkerFamilies { get; set; }
}

public class QuotaExportMainWindow
{
    [JsonPropertyName("family")]
    public string Family { get; set; } = string.Empty;

    [JsonPropertyName("window")]
    public string Window { get; set; } = string.Empty;

    [JsonPropertyName("used_percent")]
    public double UsedPercent { get; set; }

    [JsonPropertyName("resets_at")]
    public string? ResetsAt { get; set; }
}

public class QuotaExportFamily
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("windows")]
    public List<QuotaExportWindow> Windows { get; set; } = new();

    public QuotaExportFamily() { }

    public QuotaExportFamily(string name, List<QuotaExportWindow>? windows = null)
    {
        Name = name;
        if (windows != null)
            Windows = windows;
    }
}

public class QuotaExportWindow
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("used_percent")]
    public double UsedPercent { get; set; }

    [JsonPropertyName("resets_at")]
    public string? ResetsAt { get; set; }

    [JsonPropertyName("estimated")]
    public bool Estimated { get; set; }

    public QuotaExportWindow() { }

    public QuotaExportWindow(string name, double usedPercent, string? resetsAt = null, bool estimated = false)
    {
        Name = name;
        UsedPercent = usedPercent;
        ResetsAt = resetsAt;
        Estimated = estimated;
    }
}
