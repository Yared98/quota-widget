namespace QuotaWidget.Model;

public static class QuotaLogic
{
    public static QuotaFamily? GetMainFamily(QuotaSnapshot snapshot, string? mainFamilyConfig = null)
    {
        if (snapshot.Families.Count == 0)
            return null;

        if (!string.IsNullOrWhiteSpace(mainFamilyConfig))
        {
            var match = snapshot.Families.FirstOrDefault(f =>
                string.Equals(f.Name, mainFamilyConfig, StringComparison.OrdinalIgnoreCase));
            if (match != null)
                return match;
        }

        return snapshot.Families[0];
    }

    public static QuotaWindow? GetThickWindow(QuotaSnapshot snapshot, string? mainFamilyConfig = null)
    {
        var family = GetMainFamily(snapshot, mainFamilyConfig);
        if (family == null || family.Windows.Count == 0)
            return null;

        return family.Windows[0];
    }

    public static QuotaWindow? GetThinWindow(QuotaSnapshot snapshot, string? mainFamilyConfig = null)
    {
        var family = GetMainFamily(snapshot, mainFamilyConfig);
        if (family == null || family.Windows.Count < 2)
            return null;

        return family.Windows[1];
    }

    public static bool IsStale(QuotaSnapshot snapshot, DateTimeOffset now, int refreshMinutes, string? mainFamilyConfig = null)
    {
        var thickWindow = GetThickWindow(snapshot, mainFamilyConfig);
        if (thickWindow?.IsEstimated == true)
            return true;

        if ((now - snapshot.ReadAt).TotalMinutes > 2.0 * refreshMinutes)
            return true;

        return false;
    }

    /// <summary>Only the age of the read: older than 2 x refresh. Estimated values (e.g. OpenCode budgets)
    /// are not "old", so they must not show "read X ago" or turn the status dot amber.</summary>
    public static bool IsOld(QuotaSnapshot snapshot, DateTimeOffset now, int refreshMinutes) =>
        (now - snapshot.ReadAt).TotalMinutes > 2.0 * refreshMinutes;
}
