using System.Globalization;
using System.Text.Json;
using QuotaWidget.Model;

namespace QuotaWidget.Providers;

public static class GenericQuotaParser
{
    public static QuotaSnapshot Parse(string output, DateTimeOffset now, string providerId, string label)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return new QuotaSnapshot(providerId, label, now, Array.Empty<QuotaFamily>(), "Empty output");
        }

        try
        {
            using var doc = JsonDocument.Parse(output);
            var root = doc.RootElement;

            string? error = null;
            if (root.TryGetProperty("error", out var errElem) && errElem.ValueKind == JsonValueKind.String)
            {
                error = errElem.GetString();
                if (string.IsNullOrWhiteSpace(error))
                    error = null;
            }

            var families = new List<QuotaFamily>();
            if (root.TryGetProperty("families", out var famElem) && famElem.ValueKind == JsonValueKind.Array)
            {
                foreach (var fItem in famElem.EnumerateArray())
                {
                    if (fItem.ValueKind != JsonValueKind.Object)
                        continue;

                    string famName = string.Empty;
                    if (fItem.TryGetProperty("name", out var fnElem) && fnElem.ValueKind == JsonValueKind.String)
                    {
                        famName = fnElem.GetString() ?? string.Empty;
                    }

                    var windows = new List<QuotaWindow>();
                    if (fItem.TryGetProperty("windows", out var winElem) && winElem.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var wItem in winElem.EnumerateArray())
                        {
                            if (wItem.ValueKind != JsonValueKind.Object)
                                continue;

                            if (!wItem.TryGetProperty("used_percent", out var upElem) || !upElem.TryGetDouble(out double usedPercent))
                            {
                                // used_percent is required
                                continue;
                            }

                            string winName = string.Empty;
                            if (wItem.TryGetProperty("name", out var wnElem) && wnElem.ValueKind == JsonValueKind.String)
                            {
                                winName = wnElem.GetString() ?? string.Empty;
                            }

                            DateTimeOffset? resetsAt = null;
                            if (wItem.TryGetProperty("resets_at", out var raElem))
                            {
                                if (raElem.ValueKind == JsonValueKind.String &&
                                    DateTimeOffset.TryParse(raElem.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsedReset))
                                {
                                    resetsAt = parsedReset;
                                }
                            }

                            bool isEstimated = false;
                            if (wItem.TryGetProperty("estimated", out var estElem) &&
                                (estElem.ValueKind == JsonValueKind.True || estElem.ValueKind == JsonValueKind.False))
                            {
                                isEstimated = estElem.GetBoolean();
                            }

                            windows.Add(new QuotaWindow(winName, usedPercent, resetsAt, isEstimated));
                        }
                    }

                    families.Add(new QuotaFamily(famName, windows));
                }
            }

            return new QuotaSnapshot(providerId, label, now, families, error);
        }
        catch (JsonException)
        {
            string preview = output.Trim();
            if (preview.Length > 120)
                preview = preview[..120];

            return new QuotaSnapshot(providerId, label, now, Array.Empty<QuotaFamily>(), $"Invalid JSON: {preview}");
        }
    }
}
