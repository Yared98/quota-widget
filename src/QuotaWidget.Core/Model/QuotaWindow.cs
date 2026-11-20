namespace QuotaWidget.Model;

public class QuotaWindow
{
    private double _usedPercent;

    public string Name { get; set; } = string.Empty;

    public double UsedPercent
    {
        get => _usedPercent;
        set => _usedPercent = Math.Clamp(value, 0.0, 100.0);
    }

    public DateTimeOffset? ResetsAt { get; set; }
    public bool IsEstimated { get; set; }

    public QuotaWindow() { }

    public QuotaWindow(string name, double usedPercent, DateTimeOffset? resetsAt = null, bool isEstimated = false)
    {
        Name = name;
        UsedPercent = usedPercent;
        ResetsAt = resetsAt;
        IsEstimated = isEstimated;
    }
}
