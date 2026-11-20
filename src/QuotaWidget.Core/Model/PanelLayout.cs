namespace QuotaWidget.Model;

public enum ProviderLayoutMode
{
    Compact,
    Single,
    Grouped
}

public static class PanelLayout
{
    public static ProviderLayoutMode GetLayoutMode(QuotaSnapshot snapshot)
    {
        if (snapshot.Families.Count <= 1)
            return ProviderLayoutMode.Single;

        if (snapshot.Families.All(f => f.Windows.Count == 1))
            return ProviderLayoutMode.Compact;

        return ProviderLayoutMode.Grouped;
    }

    public static string ShortenFamilyName(string? familyName)
    {
        if (string.IsNullOrWhiteSpace(familyName))
            return string.Empty;

        var trimmed = familyName.Trim();

        if (trimmed.Contains("Claude", StringComparison.OrdinalIgnoreCase) &&
            trimmed.Contains("GPT", StringComparison.OrdinalIgnoreCase))
        {
            return "Cl/GPT";
        }

        var parts = trimmed.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return string.Empty;

        var firstWord = parts[0];
        return firstWord.Length <= 7 ? firstWord : firstWord[..7];
    }

    public static bool IsExhausted(QuotaSnapshot snapshot)
    {
        if (snapshot.Families.Count == 0)
            return false;

        var windows = snapshot.Families.SelectMany(f => f.Windows).ToList();
        return windows.Count > 0 && windows.All(w => w.UsedPercent >= 100.0);
    }

    public static string GetStatusDotColor(IEnumerable<QuotaSnapshot> snapshots, DateTimeOffset now, int refreshMinutes)
    {
        var list = snapshots.ToList();
        if (list.Count == 0)
            return ColorThresholds.GreenHex;

        if (list.Any(s => s.HasError))
            return ColorThresholds.RedHex;

        if (list.Any(s => QuotaLogic.IsOld(s, now, refreshMinutes)))
            return ColorThresholds.AmberHex;

        return ColorThresholds.GreenHex;
    }
}
