namespace QuotaWidget.Config;

public class BudgetConfig
{
    public string Name { get; set; } = string.Empty;
    public List<string> Models { get; set; } = new();
    /// <summary>Monthly limit in USD (OpenCode's cost estimate). 0 = not used.</summary>
    public double Usd { get; set; }
    /// <summary>Monthly limit in requests (OpenCode "steps"). 0 = not used. Subscription models report cost 0, so use this.</summary>
    public double Requests { get; set; }
    /// <summary>Monthly limit in tokens (input + output + reasoning). 0 = not used.</summary>
    public double Tokens { get; set; }

    public BudgetConfig() { }

    public BudgetConfig(string name, IEnumerable<string> models, double usd)
    {
        Name = name;
        Models = models.ToList();
        Usd = usd;
    }
}
