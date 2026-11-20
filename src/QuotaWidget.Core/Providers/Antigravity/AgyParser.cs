using System.Globalization;
using QuotaWidget.Model;

namespace QuotaWidget.Providers.Antigravity;

public static class AgyParser
{
    public static QuotaSnapshot Parse(string output, DateTimeOffset now, string providerId = "gemini", string label = "Gemini")
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return new QuotaSnapshot(providerId, label, now, Array.Empty<QuotaFamily>());
        }

        var familyMap = new Dictionary<string, List<QuotaWindow>>(StringComparer.OrdinalIgnoreCase);
        var familyOrder = new List<string>();

        var lines = output.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (string.IsNullOrEmpty(line))
                continue;

            var parts = line.Split('\t');
            if (parts.Length < 4)
                continue;

            string familyName = parts[0].Trim();
            string labelText = parts[1].Trim();
            string percentStr = parts[2].Trim();
            string resetStr = parts[3].Trim();

            if (string.IsNullOrEmpty(familyName) || string.IsNullOrEmpty(labelText))
                continue;

            string windowName;
            if (labelText.Contains("Five Hour", StringComparison.OrdinalIgnoreCase))
            {
                windowName = "5h";
            }
            else if (labelText.Contains("Weekly", StringComparison.OrdinalIgnoreCase))
            {
                windowName = "Week";
            }
            else
            {
                windowName = labelText;
            }

            if (!percentStr.EndsWith('%'))
                continue;

            string numPart = percentStr[..^1].Trim();
            if (!int.TryParse(numPart, NumberStyles.Integer, CultureInfo.InvariantCulture, out int remainingPercent))
                continue;

            // NN is remaining -> used = 100 - NN
            double usedPercent = Math.Clamp(100.0 - remainingPercent, 0.0, 100.0);

            if (!DateTimeOffset.TryParse(resetStr, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTimeOffset resetsAt))
                continue;

            if (!familyMap.TryGetValue(familyName, out var windowList))
            {
                windowList = new List<QuotaWindow>();
                familyMap[familyName] = windowList;
                familyOrder.Add(familyName);
            }

            windowList.Add(new QuotaWindow(windowName, usedPercent, resetsAt, isEstimated: false));
        }

        var families = new List<QuotaFamily>();
        foreach (var famName in familyOrder)
        {
            var sortedWindows = familyMap[famName]
                .OrderBy(w => GetWindowOrder(w.Name))
                .ToList();
            families.Add(new QuotaFamily(famName, sortedWindows));
        }

        return new QuotaSnapshot(providerId, label, now, families);
    }

    private static int GetWindowOrder(string name)
    {
        if (string.Equals(name, "5h", StringComparison.OrdinalIgnoreCase))
            return 0;
        if (string.Equals(name, "Week", StringComparison.OrdinalIgnoreCase))
            return 1;
        return 2;
    }
}
