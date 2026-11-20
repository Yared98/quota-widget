namespace QuotaWidget.Model;

public class QuotaFamily
{
    public string Name { get; set; } = string.Empty;
    public List<QuotaWindow> Windows { get; set; } = new();

    public QuotaFamily() { }

    public QuotaFamily(string name, IEnumerable<QuotaWindow>? windows = null)
    {
        Name = name;
        if (windows != null)
        {
            Windows = windows.ToList();
        }
    }
}
