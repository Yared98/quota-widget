using QuotaWidget.Model;
using Xunit;

namespace QuotaWidget.Tests;

public class ColorThresholdsTests
{
    [Theory]
    [InlineData(0.0, ColorThresholds.GreenHex)]
    [InlineData(25.0, ColorThresholds.GreenHex)]
    [InlineData(49.9, ColorThresholds.GreenHex)]
    [InlineData(50.0, ColorThresholds.AmberHex)]
    [InlineData(65.0, ColorThresholds.AmberHex)]
    [InlineData(80.0, ColorThresholds.AmberHex)]
    [InlineData(80.01, ColorThresholds.RedHex)]
    [InlineData(81.0, ColorThresholds.RedHex)]
    [InlineData(100.0, ColorThresholds.RedHex)]
    public void Boundaries_50_And_80(double usedPercent, string expectedHex)
    {
        string actualHex = ColorThresholds.GetColorHex(usedPercent);
        Assert.Equal(expectedHex, actualHex);
    }
}
