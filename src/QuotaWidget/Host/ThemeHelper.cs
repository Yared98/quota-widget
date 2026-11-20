using Microsoft.Win32;

namespace QuotaWidget.Host;

public static class ThemeHelper
{
    public static bool IsLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            if (key != null)
            {
                var val = key.GetValue("SystemUsesLightTheme");
                if (val is int intVal)
                {
                    return intVal == 1;
                }
            }
        }
        catch
        {
            // default to dark
        }
        return false;
    }
}
