using System.Text.Json.Serialization;
using QuotaWidget.Export;
using QuotaWidget.Scheduling;

namespace QuotaWidget.Ipc;

public static class PipeProtocol
{
    public const int MaxMessageBytes = 4096;

    public static string GetPipeName() => $"QuotaWidget.{Environment.UserName}";
}

public class PipeRequest
{
    [JsonPropertyName("cmd")]
    public string Cmd { get; set; } = string.Empty;

    [JsonPropertyName("ids")]
    public List<string>? Ids { get; set; }

    [JsonPropertyName("wait")]
    public bool Wait { get; set; }

    [JsonPropertyName("force")]
    public bool Force { get; set; }
}

public class PipeResponse
{
    [JsonPropertyName("ok")]
    public bool Ok { get; set; }

    [JsonPropertyName("version")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Version { get; set; }

    [JsonPropertyName("queued")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Queued { get; set; }

    [JsonPropertyName("skipped")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<SkippedProvider>? Skipped { get; set; }

    [JsonPropertyName("quotas")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public QuotaExport? Quotas { get; set; }

    [JsonPropertyName("error")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Error { get; set; }
}
