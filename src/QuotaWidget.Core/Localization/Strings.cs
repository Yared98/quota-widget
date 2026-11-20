using System.Globalization;

namespace QuotaWidget.Localization;

public static class Strings
{
    public const string DefaultLanguage = "auto";
    public const string En = "en";
    public const string PtBr = "pt-BR";

    public static string ResolveLanguage(string? configuredLanguage, CultureInfo? uiCulture = null)
    {
        if (string.IsNullOrWhiteSpace(configuredLanguage) ||
            string.Equals(configuredLanguage, "auto", StringComparison.OrdinalIgnoreCase))
        {
            var culture = uiCulture ?? CultureInfo.CurrentUICulture;
            return culture.Name.StartsWith("pt", StringComparison.OrdinalIgnoreCase) ? PtBr : En;
        }

        if (configuredLanguage.StartsWith("pt", StringComparison.OrdinalIgnoreCase))
            return PtBr;

        return En;
    }

    public static string GetTitle(string lang)
    {
        return lang == PtBr ? "Cotas de IA" : "AI quotas";
    }

    public static string GetExhaustedTag(string lang)
    {
        return lang == PtBr ? "Esgotado" : "Exhausted";
    }

    public static string FormatHeaderUpdate(DateTimeOffset now, string lang)
    {
        var local = now.ToLocalTime();
        return lang == PtBr ? $"atualizado {local:HH:mm}" : $"updated {local:HH:mm}";
    }

    public static string FormatReadAgo(DateTimeOffset readAt, DateTimeOffset now, string lang)
    {
        var diff = now - readAt;
        if (diff < TimeSpan.Zero)
            diff = TimeSpan.Zero;

        if (diff.TotalMinutes < 60)
        {
            int minutes = Math.Max(0, (int)diff.TotalMinutes);
            return lang == PtBr ? $"lido há {minutes} min" : $"read {minutes} min ago";
        }
        else if (diff.TotalHours < 24)
        {
            int hours = (int)diff.TotalHours;
            return lang == PtBr ? $"lido há {hours} h" : $"read {hours} h ago";
        }
        else
        {
            int days = (int)diff.TotalDays;
            if (lang == PtBr)
                return days == 1 ? "lido há 1 dia" : $"lido há {days} dias";
            else
                return days == 1 ? "read 1 day ago" : $"read {days} days ago";
        }
    }

    public static string FormatReset(DateTimeOffset? resetsAt, bool isEstimated, DateTimeOffset now, string lang)
    {
        if (!resetsAt.HasValue)
        {
            if (isEstimated)
                return lang == PtBr ? "estimado" : "estimated";
            return string.Empty;
        }

        var localReset = resetsAt.Value.ToLocalTime();
        var localNow = now.ToLocalTime();

        if (localReset.Date == localNow.Date)
        {
            return $"↻ {localReset:HH:mm}";
        }

        bool isMidnight = localReset.Hour == 0 && localReset.Minute == 0 && localReset.Second == 0;
        if (isMidnight)
        {
            return lang == PtBr
                ? $"↻ {localReset:dd/MM}"
                : $"↻ {localReset:MM/dd}";
        }

        return lang == PtBr
            ? $"↻ {localReset:dd/MM} {localReset:HH}h"
            : $"↻ {localReset:MM/dd} {localReset:HH}h";
    }

    /// <summary>Translates the standard window names providers use ("5h", "Week", "Month"); others pass through.</summary>
    public static string WindowName(string name, string lang)
    {
        if (string.IsNullOrWhiteSpace(name))
            return string.Empty;

        return name.ToLowerInvariant() switch
        {
            "5h" => "5h",
            "week" => lang == PtBr ? "Semana" : "Week",
            "month" => lang == PtBr ? "Mês" : "Month",
            "day" => lang == PtBr ? "Dia" : "Day",
            _ => name,
        };
    }

    public static string GetMenuRefresh(string lang) => lang == PtBr ? "Atualizar agora" : "Refresh now";
    public static string GetMenuOpenConfig(string lang) => lang == PtBr ? "Abrir configurações" : "Open config";
    public static string GetMenuReloadConfig(string lang) => lang == PtBr ? "Recarregar configurações" : "Reload config";
    public static string GetMenuExit(string lang) => lang == PtBr ? "Sair" : "Exit";
}
