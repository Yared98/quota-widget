using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using QuotaWidget.Export;

namespace QuotaWidget.Selection;

public class WorkerPickResult
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("label")]
    public string Label { get; set; } = string.Empty;

    [JsonPropertyName("used_percent")]
    public double UsedPercent { get; set; }

    [JsonPropertyName("window")]
    public string Window { get; set; } = string.Empty;

    [JsonPropertyName("resets_at")]
    public string? ResetsAt { get; set; }

    [JsonPropertyName("stale")]
    public bool Stale { get; set; }
}

public static class WorkerPicker
{
    public static bool MatchesWildcard(string pattern, string input)
    {
        if (string.Equals(pattern, input, StringComparison.OrdinalIgnoreCase))
            return true;

        if (pattern.Contains('*'))
        {
            string regexPattern = "^" + Regex.Escape(pattern).Replace("\\*", ".*") + "$";
            if (Regex.IsMatch(input, regexPattern, RegexOptions.IgnoreCase))
                return true;
        }

        if (input.Contains('*'))
        {
            string regexPattern = "^" + Regex.Escape(input).Replace("\\*", ".*") + "$";
            if (Regex.IsMatch(pattern, regexPattern, RegexOptions.IgnoreCase))
                return true;
        }

        return false;
    }

    public static WorkerPickResult? Pick(
        string workerQuery,
        QuotaExport export,
        double maxPercent = 95.0,
        IReadOnlyList<string>? configOrder = null)
    {
        if (string.IsNullOrWhiteSpace(workerQuery) || export.Providers == null || export.Providers.Count == 0)
            return null;

        var orderMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (configOrder != null)
        {
            for (int i = 0; i < configOrder.Count; i++)
                orderMap[configOrder[i]] = i;
        }
        else
        {
            for (int i = 0; i < export.Providers.Count; i++)
                orderMap[export.Providers[i].Id] = i;
        }

        var candidates = new List<(QuotaExportProvider Provider, double LimitingUsed, QuotaExportWindow LimitingWindow, int OrderIndex)>();

        foreach (var p in export.Providers)
        {
            // Errors excluded
            if (!string.IsNullOrWhiteSpace(p.Error))
                continue;

            // Check if provider workers match workerQuery
            if (p.Workers == null || !p.Workers.Any(w => MatchesWildcard(w, workerQuery)))
                continue;

            // Determine limiting family
            string? limitingFamilyName = null;
            if (p.WorkerFamilies != null)
            {
                foreach (var kvp in p.WorkerFamilies)
                {
                    if (MatchesWildcard(kvp.Key, workerQuery))
                    {
                        limitingFamilyName = kvp.Value;
                        break;
                    }
                }
            }

            if (string.IsNullOrEmpty(limitingFamilyName))
            {
                limitingFamilyName = p.Main?.Family ?? p.Families?.FirstOrDefault()?.Name;
            }

            if (string.IsNullOrEmpty(limitingFamilyName) || p.Families == null)
                continue;

            var family = p.Families.FirstOrDefault(f =>
                string.Equals(f.Name, limitingFamilyName, StringComparison.OrdinalIgnoreCase));

            if (family == null || family.Windows == null || family.Windows.Count == 0)
                continue;

            // Disqualify if ANY window in limiting family >= maxPercent
            if (family.Windows.Any(w => w.UsedPercent >= maxPercent))
                continue;

            // Limiting window is the highest used % in that family
            var limitingWindow = family.Windows.OrderByDescending(w => w.UsedPercent).First();
            double used = limitingWindow.UsedPercent;

            int orderIndex = orderMap.TryGetValue(p.Id, out int idx) ? idx : int.MaxValue;
            candidates.Add((p, used, limitingWindow, orderIndex));
        }

        if (candidates.Count == 0)
            return null;

        // Ties: lower used % first, then config order
        var winner = candidates
            .OrderBy(c => c.LimitingUsed)
            .ThenBy(c => c.OrderIndex)
            .First();

        return new WorkerPickResult
        {
            Id = winner.Provider.Id,
            Label = winner.Provider.Label,
            UsedPercent = Math.Round(winner.LimitingUsed, 1),
            Window = winner.LimitingWindow.Name,
            ResetsAt = winner.LimitingWindow.ResetsAt,
            Stale = winner.Provider.Stale
        };
    }
}
