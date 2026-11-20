using System.Text.Json;
using QuotaWidget.Model;

namespace QuotaWidget.Providers.ClaudeCode;

public static class ClaudeStreamParser
{
    public static QuotaSnapshot? Parse(string output, DateTimeOffset now, string providerId = "claude", string label = "Claude")
    {
        if (string.IsNullOrWhiteSpace(output))
            return null;

        foreach (var raw in output.Split('\n'))
        {
            var line = raw.Trim();
            if (!line.StartsWith('{') || !line.Contains("\"rate_limit_event\""))
                continue;

            try
            {
                using var doc = JsonDocument.Parse(line);
                if (!doc.RootElement.TryGetProperty("rate_limit_info", out var info) ||
                    !info.TryGetProperty("unifiedWindows", out var uw) || uw.ValueKind != JsonValueKind.Object)
                    continue;

                var windows = new List<QuotaWindow>();
                AddWindow(uw, "five_hour", "5h", windows);
                AddWindow(uw, "seven_day", "Week", windows);
                if (windows.Count == 0)
                    continue;

                return new QuotaSnapshot(providerId, label, now, new[] { new QuotaFamily("Claude", windows) });
            }
            catch (JsonException)
            {
                // Not a valid JSON line; keep scanning
            }
        }

        return null;
    }

    private static void AddWindow(JsonElement uw, string propertyName, string windowName, List<QuotaWindow> windows)
    {
        if (!uw.TryGetProperty(propertyName, out var w) || w.ValueKind != JsonValueKind.Object)
            return;

        if (!w.TryGetProperty("utilization", out var u) || !u.TryGetDouble(out double utilization))
            return;

        DateTimeOffset? resetsAt = null;
        if (w.TryGetProperty("resetsAt", out var r) && r.TryGetInt64(out long resetUnix))
        {
            resetsAt = DateTimeOffset.FromUnixTimeSeconds(resetUnix);
        }

        double usedPercent = Math.Clamp(utilization * 100.0, 0.0, 100.0);
        windows.Add(new QuotaWindow(windowName, usedPercent, resetsAt, isEstimated: false));
    }
}
