using System.Text.Json;
using System.Text.RegularExpressions;
using QuotaWidget.Config;
using QuotaWidget.Model;

namespace QuotaWidget.Providers.OpenCode;

public static class OpenCodeParser
{
    public static (DateTimeOffset periodStart, DateTimeOffset nextReset, int days) CalculatePeriod(DateTimeOffset now, int resetDay)
    {
        resetDay = Math.Clamp(resetDay, 1, 28);

        DateTimeOffset periodStart;
        DateTimeOffset nextReset;

        if (now.Day >= resetDay)
        {
            periodStart = new DateTimeOffset(now.Year, now.Month, resetDay, 0, 0, 0, now.Offset);
            nextReset = now.Month == 12
                ? new DateTimeOffset(now.Year + 1, 1, resetDay, 0, 0, 0, now.Offset)
                : new DateTimeOffset(now.Year, now.Month + 1, resetDay, 0, 0, 0, now.Offset);
        }
        else
        {
            periodStart = now.Month == 1
                ? new DateTimeOffset(now.Year - 1, 12, resetDay, 0, 0, 0, now.Offset)
                : new DateTimeOffset(now.Year, now.Month - 1, resetDay, 0, 0, 0, now.Offset);
            nextReset = new DateTimeOffset(now.Year, now.Month, resetDay, 0, 0, 0, now.Offset);
        }

        int days = (int)Math.Max(0, (now.Date - periodStart.Date).TotalDays);
        return (periodStart, nextReset, days);
    }

    public static bool MatchesPattern(string pattern, string input)
    {
        if (string.IsNullOrWhiteSpace(pattern) || string.IsNullOrWhiteSpace(input))
            return false;

        if (pattern == "*")
            return true;

        string regex = "^" + Regex.Escape(pattern).Replace("\\*", ".*") + "$";
        return Regex.IsMatch(input, regex, RegexOptions.IgnoreCase);
    }

    public static QuotaSnapshot Parse(
        string json,
        DateTimeOffset now,
        string providerId,
        string label,
        IEnumerable<BudgetConfig>? budgets,
        DateTimeOffset nextReset)
    {
        var budgetList = budgets?.ToList() ?? new List<BudgetConfig>();
        var totals = budgetList.ToDictionary(b => b, _ => new double[3]); // cost, requests, tokens

        if (string.IsNullOrWhiteSpace(json))
        {
            return new QuotaSnapshot(providerId, label, now, Array.Empty<QuotaFamily>(), "Empty output from opencode");
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            JsonElement modelsArray;
            if (root.ValueKind == JsonValueKind.Array)
            {
                modelsArray = root;
            }
            else if (root.TryGetProperty("models", out var prop) && prop.ValueKind == JsonValueKind.Array)
            {
                modelsArray = prop;
            }
            else
            {
                modelsArray = default;
            }

            if (modelsArray.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in modelsArray.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object)
                        continue;

                    double cost = Num(item, "cost");
                    double steps = Num(item, "steps");
                    double tokens = 0;
                    if (item.TryGetProperty("tokens", out var tok) && tok.ValueKind == JsonValueKind.Object)
                        tokens = Num(tok, "input") + Num(tok, "output") + Num(tok, "reasoning");

                    // Real output: { "model": { "id", "providerID" }, ... }; also accept flat id/providerID.
                    var source = item.TryGetProperty("model", out var m) && m.ValueKind == JsonValueKind.Object ? m : item;
                    string? providerIdVal = Str(source, "providerID");
                    string? idVal = Str(source, "id");
                    var candidateIds = new List<string>();
                    if (!string.IsNullOrEmpty(providerIdVal) && !string.IsNullOrEmpty(idVal))
                        candidateIds.Add($"{providerIdVal}/{idVal}");
                    if (!string.IsNullOrEmpty(idVal))
                        candidateIds.Add(idVal);
                    if (item.TryGetProperty("model", out var ms) && ms.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(ms.GetString()))
                        candidateIds.Add(ms.GetString()!);

                    foreach (var budget in budgetList)
                    {
                        bool matched = false;
                        foreach (var pattern in budget.Models)
                        {
                            foreach (var candidate in candidateIds)
                            {
                                if (MatchesPattern(pattern, candidate))
                                {
                                    matched = true;
                                    break;
                                }
                            }
                            if (matched) break;
                        }

                        if (matched)
                        {
                            var t = totals[budget];
                            t[0] += cost; t[1] += steps; t[2] += tokens;
                        }
                    }
                }
            }

            var families = new List<QuotaFamily>();
            foreach (var budget in budgetList)
            {
                var t = totals[budget];
                double usedPercent = 0.0;
                if (budget.Usd > 0) usedPercent = Math.Max(usedPercent, t[0] / budget.Usd * 100.0);
                if (budget.Requests > 0) usedPercent = Math.Max(usedPercent, t[1] / budget.Requests * 100.0);
                if (budget.Tokens > 0) usedPercent = Math.Max(usedPercent, t[2] / budget.Tokens * 100.0);
                usedPercent = Math.Clamp(usedPercent, 0.0, 100.0);

                var window = new QuotaWindow("Month", usedPercent, nextReset, isEstimated: true);
                families.Add(new QuotaFamily(budget.Name, new[] { window }));
            }

            return new QuotaSnapshot(providerId, label, now, families);
        }
        catch (JsonException)
        {
            string preview = json.Trim();
            if (preview.Length > 120) preview = preview[..120];
            return new QuotaSnapshot(providerId, label, now, Array.Empty<QuotaFamily>(), $"Invalid JSON from opencode: {preview}");
        }
    }

    private static double Num(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d) ? d : 0.0;

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
