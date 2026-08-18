using MiddlewareApp.Core.Services;
using Xunit;

namespace MiddlewareApp.Core.Tests;

public class ReceiptFormatterTests
{
    private static List<TextLine> TextLines(IReadOnlyList<ReceiptLine> lines) =>
        lines.OfType<TextLine>().Where(t => t.Text.Length > 0).ToList();

    [Fact]
    public void CharsPerLine_Is48For80mm_32For58mm()
    {
        Assert.Equal(48, ReceiptFormatter.CharsPerLine("80mm"));
        Assert.Equal(32, ReceiptFormatter.CharsPerLine("58mm"));
        Assert.Equal(48, ReceiptFormatter.CharsPerLine(null));
    }

    [Fact]
    public void PlainText_NoTags_PrintsLeftAlignedWrapped()
    {
        var lines = ReceiptFormatter.Format("hello world", 48);
        var text = TextLines(lines);
        Assert.Single(text);
        Assert.Equal("hello world", text[0].Text);
        Assert.Equal(LineAlign.Left, text[0].Align);
    }

    [Fact]
    public void HeaderClass_H1toH4_AndPoweredBy_AreCentered()
    {
        var html = """
            <html><body>
              <div class="receipt-header">My Shop</div>
              <h2>Invoice 42</h2>
              <p>Regular line</p>
              <p>Powered by Cloud POS</p>
            </body></html>
            """;
        var text = TextLines(ReceiptFormatter.Format(html, 48));

        Assert.Equal(LineAlign.Center, text.Single(t => t.Text == "My Shop").Align);
        Assert.Equal(LineAlign.Center, text.Single(t => t.Text == "Invoice 42").Align);
        Assert.Equal(LineAlign.Left, text.Single(t => t.Text == "Regular line").Align);
        Assert.Equal(LineAlign.Center, text.Single(t => t.Text == "Powered by Cloud POS").Align);
    }

    [Fact]
    public void Hr_AndSeparatorTableRows_BecomeDashes_AndConsecutiveOnesCollapse()
    {
        var html = """
            <body><p>a</p><hr><hr>
            <table><tr><td>----</td><td>====</td></tr><tr><td>x</td><td>1</td></tr></table>
            </body>
            """;
        var lines = ReceiptFormatter.Format(html, 48);
        // hr + hr + separator row collapse to a single dash line
        Assert.Equal(1, lines.Count(l => l is DashLine));
    }

    [Fact]
    public void TableRow_LastCellRightAligned_OnSameLine()
    {
        var html = "<body><table><tr><td>Fried Rice</td><td>1,200.00</td></tr></table></body>";
        var text = TextLines(ReceiptFormatter.Format(html, 48));
        var row = Assert.Single(text);
        Assert.Equal(48, row.Text.Length);
        Assert.StartsWith("Fried Rice", row.Text);
        Assert.EndsWith("1,200.00", row.Text);
    }

    [Fact]
    public void TableRow_ThreeCells_JoinsLeftCells()
    {
        var html = "<body><table><tr><td>2x</td><td>Kottu</td><td>900.00</td></tr></table></body>";
        var text = TextLines(ReceiptFormatter.Format(html, 48));
        var row = Assert.Single(text);
        Assert.StartsWith("2x Kottu", row.Text);
        Assert.EndsWith("900.00", row.Text);
    }

    [Fact]
    public void TableRow_LongLeft_RightValueOnLastWrappedLineOrOwnLine()
    {
        var left = string.Join(" ", Enumerable.Repeat("word", 20)); // way over one line
        var html = $"<body><table><tr><td>{left}</td><td>55.00</td></tr></table></body>";
        var text = TextLines(ReceiptFormatter.Format(html, 48));
        Assert.True(text.Count > 1);
        Assert.EndsWith("55.00", text[^1].Text);
    }

    [Fact]
    public void MarkupChars_AreStripped_AndEntitiesDecoded()
    {
        var html = "<body><p>A &amp; B [x] &lt;y&gt;</p></body>";
        var text = TextLines(ReceiptFormatter.Format(html, 48));
        Assert.Equal("A & B x y", text.Single().Text);
    }

    [Fact]
    public void Images_MaxTwoDistinct_HttpOnly_Deduped()
    {
        var html = """
            <body>
              <img src="https://a.example/logo.png">
              <img src="https://a.example/logo.png">
              <img src="https://b.example/2.png">
              <img src="https://c.example/3.png">
              <img src="file:///local.png">
            </body>
            """;
        var images = ReceiptFormatter.Format(html, 48).OfType<ImageLine>().ToList();
        Assert.Equal(2, images.Count);
        Assert.Equal("https://a.example/logo.png", images[0].Url);
        Assert.Equal("https://b.example/2.png", images[1].Url);
    }

    [Fact]
    public void ScriptAndStyle_AreStripped()
    {
        var html = "<body><style>.x{color:red}</style><script>alert(1)</script><p>keep</p></body>";
        var text = TextLines(ReceiptFormatter.Format(html, 48));
        Assert.Equal("keep", Assert.Single(text).Text);
    }

    [Fact]
    public void LongText_WrapsAtWidth()
    {
        var words = string.Join(" ", Enumerable.Repeat("abcde", 30));
        var text = TextLines(ReceiptFormatter.Format($"<body><p>{words}</p></body>", 32));
        Assert.All(text, t => Assert.True(t.Text.Length <= 32));
        Assert.True(text.Count >= 5);
    }

    [Fact]
    public void Output_EndsWithSixBlankLines()
    {
        var lines = ReceiptFormatter.Format("<body><p>x</p></body>", 48);
        var lastSix = lines.Skip(lines.Count - 6).ToList();
        Assert.All(lastSix, l => Assert.True(l is TextLine { Text: "" }));
    }
}
