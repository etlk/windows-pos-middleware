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
        Assert.StartsWith("^XA^CI28^PW320^LL240^FO16,8", LabelFormat.Build("zpl", Html));

    [Fact]
    public void Tspl_HasSize() =>
        Assert.Contains("SIZE 40 mm,30 mm", LabelFormat.Build("tspl", Html));

    [Fact]
    public void Cpcl_FallsBackToComputedHeight() =>
        Assert.StartsWith("! 0 200 200 80 1", LabelFormat.Build("cpcl", "a\nb"));

    [Fact]
    public void Wrap_BreaksOnWordsAndHardBreaksLongWords()
    {
        Assert.Equal(new[] { "Strawberry", "Sundae (Boba /", "Cold)" },
            LabelFormat.Wrap("Strawberry Sundae (Boba / Cold)", 14));
        Assert.Equal(new[] { "abcd", "efgh", "ij" }, LabelFormat.Wrap("abcdefghij", 4));
    }

    [Fact]
    public void LongKotLabel_FitsInside40x30()
    {
        var kot = "<style>@page { size: 40mm 30mm; }</style><div>Item Label - Cart #</div><div>Number 2</div>" +
                  "<div>Strawberry Sundae</div><div>(Boba / Cold / Normal Ice)</div><div>LKR 530.00</div>" +
                  "<div>2026-11-20 16:56</div><div>Consume within 24 hrs</div>";
        var z = LabelFormat.Build("zpl", kot);
        var ys = System.Text.RegularExpressions.Regex.Matches(z, @"\^FO16,(\d+)")
            .Select(m => int.Parse(m.Groups[1].Value)).ToList();
        Assert.True(ys.Count >= 7 && ys.Max() < 240, z);
    }

    [Fact]
    public void TitleIsNotLabelContent() =>
        Assert.DoesNotContain("Cart", LabelFormat.Build("zpl",
            "<html><head><title>Item Label - Cart #119</title></head><body><div>Number 2</div></body></html>"));

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

public class NbspPaddingTests
{
    [Fact]
    public void NbspPadding_SurvivesWhitespaceCollapse()
    {
        var html = "<table><tr><td>Product      Qty</td><td>Total</td></tr></table>";
        var text = string.Join("\n", MiddlewareApp.Core.Services.ReceiptFormatter.Format(html, 48)
            .OfType<MiddlewareApp.Core.Services.TextLine>().Select(l => l.Text));
        // padding stays as NBSP in the model; EscPosRenderer.Asciify prints it as spaces
        Assert.Contains("Product      Qty", text.Replace(' ', ' '));
    }

    [Fact]
    public void EdgeAndSpacerOnlyNbsp_AreTrimmedAsBefore()
    {
        var html = "<div>Hello</div><div>  </div><div> World </div>";
        var lines = MiddlewareApp.Core.Services.ReceiptFormatter.Format(html, 48)
            .OfType<MiddlewareApp.Core.Services.TextLine>().Select(l => l.Text).ToList();
        Assert.Contains("World", lines);
        Assert.DoesNotContain(lines, l => l.Length > 0 && l.Trim(' ', ' ').Length == 0);
    }
}
