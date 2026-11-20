namespace QuotaWidget.Model;

public static class ColorThresholds
{
    public const string GreenHex = "#10b981";
    public const string AmberHex = "#f59e0b";
    public const string RedHex = "#ef4444";
    public const string TrackHex = "#2a2d34";
    public const string DarkTrackHex = "#2a2d34";
    public const string LightTrackHex = "#2a2d34";

    public const string PanelBgHex = "#141517";
    public const string CardBgHex = "#1c1e22";
    public const string BorderHex = "#12FFFFFF"; // white 7%
    public const string TextPrimaryHex = "#f3f4f6";
    public const string TextSecondaryHex = "#9ca3af";
    public const string TextMutedHex = "#6b7280";
    public const string DangerRowBgHex = "#14EF4444"; // red 8%
    public const string RedLightHex = "#fca5a5";
    public const string TagBgHex = "#0FFFFFFF"; // white 6%
    public const string TagExhaustedBgHex = "#26EF4444"; // red 15%
    public const string RedBorder30Hex = "#4DEF4444"; // red 30%
    public const string DashedBorderHex = "#0FFFFFFF"; // white 6%

    public static string GetColorHex(double usedPercent)
    {
        if (usedPercent < 50.0)
            return GreenHex;
        if (usedPercent <= 80.0)
            return AmberHex;
        return RedHex;
    }
}
