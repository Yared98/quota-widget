namespace QuotaWidget.Config;

public class WidgetConfig
{
    public int RefreshMinutes { get; set; } = 10;
    public string Language { get; set; } = "auto";
    public int OffsetX { get; set; } = 8;
    public string? ExportPath { get; set; }
    public List<ProviderConfig> Providers { get; set; } = new();
}
