using MiddlewareApp.Core.Models;
using MiddlewareApp.Core.Services;
using Xunit;

namespace MiddlewareApp.Core.Tests;

public class LabelFormatTests
{
    private const string Html =
        "<style>@page { size: 40mm 30mm; }</style><div>Cart #119</div><div>2 x Rice</div>";

    [Fact]
    public void ParsesPageSize() => Assert.Equal((40d, 30d), LabelFormat.ParseSize(Html));

    [Fact]
    public void Zpl_UsesHtmlSize() =>
        Assert.StartsWith("^XA^CI28^PW320^LL240^FO8,8", LabelFormat.Build("zpl", Html));

    [Fact]
    public void Tspl_HasSize() =>
        Assert.Contains("SIZE 40 mm,30 mm", LabelFormat.Build("tspl", Html));

    [Fact]
    public void Cpcl_FallsBackToComputedHeight() =>
        Assert.StartsWith("! 0 200 200 80 1", LabelFormat.Build("cpcl", "a\nb"));

    [Fact]
    public void LanguageDefaultsToEscPos()
    {
        Assert.Null(PrintConfigHelpers.LabelLanguage(new PrintConfig()));
        Assert.Null(PrintConfigHelpers.LabelLanguage(new PrintConfig { PrintLanguage = "escpos" }));
        Assert.Equal("zpl", PrintConfigHelpers.LabelLanguage(new PrintConfig { PrintLanguage = "ZPL" }));
    }

    [Fact]
    public void NullLanguageIsOmittedFromJson() =>
        Assert.DoesNotContain("print_language",
            System.Text.Json.JsonSerializer.Serialize(new PrintConfig()));
}
