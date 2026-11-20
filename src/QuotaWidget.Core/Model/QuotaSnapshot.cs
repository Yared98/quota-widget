namespace QuotaWidget.Model;

public class QuotaSnapshot
{
    public string ProviderId { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public DateTimeOffset ReadAt { get; set; }
    public List<QuotaFamily> Families { get; set; } = new();
    public string? Error { get; set; }

    public QuotaSnapshot() { }

    public QuotaSnapshot(string providerId, string label, DateTimeOffset readAt, IEnumerable<QuotaFamily>? families = null, string? error = null)
    {
        ProviderId = providerId;
        Label = label;
        ReadAt = readAt;
        if (families != null)
        {
            Families = families.ToList();
        }
        Error = error;
    }

    public bool HasError => !string.IsNullOrEmpty(Error);
}
