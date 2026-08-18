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
    public void InlineTextAlign_CenterAndRight_AreHonored()
    {
        var html = """
            <body>
              <div style="text-align:center">Centered</div>
              <p style="text-align: right;">Right side</p>
              <p>Plain</p>
            </body>
            """;
        var text = TextLines(ReceiptFormatter.Format(html, 48));

        Assert.Equal(LineAlign.Center, text.Single(t => t.Text == "Centered").Align);
        Assert.Equal(LineAlign.Right, text.Single(t => t.Text == "Right side").Align);
        Assert.Equal(LineAlign.Left, text.Single(t => t.Text == "Plain").Align);
    }

    [Fact]
    public void StyleBlock_ClassAndTagRules_AreApplied()
    {
        var html = """
            <html><head><style>
              .centered { text-align: center; }
              p.totals { font-weight: bold; }
            </style></head>
            <body>
              <div class="centered">Shop Name</div>
              <p class="totals">Total 1,200.00</p>
              <p>Plain</p>
            </body></html>
            """;
        var text = TextLines(ReceiptFormatter.Format(html, 48));

        Assert.Equal(LineAlign.Center, text.Single(t => t.Text == "Shop Name").Align);
        Assert.True(text.Single(t => t.Text == "Total 1,200.00").Bold);
        Assert.False(text.Single(t => t.Text == "Plain").Bold);
    }

    [Fact]
    public void BoldTags_AndFontWeight_MarkLinesBold()
    {
        var html = """
            <body>
              <p><b>Grand Total</b></p>
              <p><strong>Due</strong></p>
              <p style="font-weight:700">Amount</p>
              <p>Regular</p>
            </body>
            """;
        var text = TextLines(ReceiptFormatter.Format(html, 48));

        Assert.True(text.Single(t => t.Text == "Grand Total").Bold);
        Assert.True(text.Single(t => t.Text == "Due").Bold);
        Assert.True(text.Single(t => t.Text == "Amount").Bold);
        Assert.False(text.Single(t => t.Text == "Regular").Bold);
    }

    [Fact]
    public void DisplayNone_AndVisibilityHidden_AreSkipped()
    {
        var html = """
            <body>
              <p style="display:none">secret</p>
              <div style="visibility:hidden"><p>also secret</p></div>
              <p>visible</p>
            </body>
            """;
        var text = TextLines(ReceiptFormatter.Format(html, 48));
        Assert.Equal("visible", Assert.Single(text).Text);
    }

    [Fact]
    public void H1_AndLargeFontSize_PrintDoubleSize_WrappedAtHalfWidth()
    {
        var html = """<body><h1>Super Fresh Mart Colombo Seven</h1><p style="font-size:24px">BIG</p></body>""";
        var text = TextLines(ReceiptFormatter.Format(html, 48));

        var title = text.Where(t => t.Text.StartsWith("Super") || t.Text.Contains("Colombo")).ToList();
        Assert.NotEmpty(title);
        Assert.All(title, t =>
        {
            Assert.True(t.Wide);
            Assert.True(t.Bold);
            Assert.Equal(LineAlign.Center, t.Align);
            Assert.True(t.Text.Length <= 24); // wide chars occupy two columns
        });
        Assert.True(text.Single(t => t.Text == "BIG").Wide);
    }

    [Fact]
    public void InlineStyle_Overrides_TagDefaults()
    {
        var html = """<body><h3 style="font-weight:normal; text-align:left">Note</h3></body>""";
        var line = Assert.Single(TextLines(ReceiptFormatter.Format(html, 48)));
        Assert.False(line.Bold);
        Assert.Equal(LineAlign.Left, line.Align);
    }

    [Fact]
    public void TableTotalsRow_WithBoldCells_PrintsBoldPaddedRow()
    {
        var html = "<body><table><tr><td><b>Total</b></td><td><b>1,200.00</b></td></tr></table></body>";
        var row = Assert.Single(TextLines(ReceiptFormatter.Format(html, 48)));
        Assert.True(row.Bold);
        Assert.Equal(48, row.Text.Length);
        Assert.StartsWith("Total", row.Text);
        Assert.EndsWith("1,200.00", row.Text);
    }

    [Fact]
    public void TableCell_TextAlignRight_OnSingleCellRow()
    {
        var html = """<body><table><tr><td style="text-align:right">Thank you!</td></tr></table></body>""";
        var line = Assert.Single(TextLines(ReceiptFormatter.Format(html, 48)));
        Assert.Equal(LineAlign.Right, line.Align);
    }

    [Fact]
    public void DescendantSelectors_ApplyToNestedElements()
    {
        var html = """
            <html><head><style>
              .header p { font-weight: bold; }
              .header h2 { font-size: 18px; }
            </style></head>
            <body>
              <div class="header"><h2>Super Mart</h2><p>12 Main Street</p></div>
              <p>Outside</p>
            </body></html>
            """;
        var text = TextLines(ReceiptFormatter.Format(html, 48));

        var title = text.Single(t => t.Text == "Super Mart");
        Assert.False(title.Wide); // 18px is under the double-size threshold, overriding the h2 default
        Assert.True(title.Bold);
        Assert.True(text.Single(t => t.Text == "12 Main Street").Bold);
        Assert.False(text.Single(t => t.Text == "Outside").Bold);
    }

    [Fact]
    public void DashedBorders_BecomeDashLines()
    {
        var html = """
            <html><head><style>
              .order-details thead { border-bottom: 1px dashed #000; }
            </style></head>
            <body>
              <div class="order-details">
                <table>
                  <thead><tr><th style="text-align: left;">Item</th><th style="text-align: right;">Price</th></tr></thead>
                  <tbody><tr><td>Rice x 1</td><td style="text-align: right;">500.00</td></tr></tbody>
                </table>
              </div>
              <div style="border-top: 1px dashed #999;"><p><strong>Powered by ET Cloud POS</strong></p></div>
            </body></html>
            """;
        var lines = ReceiptFormatter.Format(html, 48);
        var dialect = ReceiptFormatter.ToDialect(lines, 48);

        // Dash under the column headers (thead border-bottom) and before Powered by (div border-top).
        Assert.Equal(2, lines.Count(l => l is DashLine));
        var flat = lines.Where(l => l is DashLine || (l is TextLine { Text.Length: > 0 })).ToList();
        Assert.True(flat[0] is TextLine h && h.Text.StartsWith("Item"), dialect);
        Assert.True(flat[1] is DashLine, dialect);
        Assert.True(flat[2] is TextLine, dialect);
        Assert.True(flat[3] is DashLine, dialect);
        Assert.True(flat[4] is TextLine p && p.Text == "Powered by ET Cloud POS", dialect);
    }

    [Fact]
    public void PaymentRows_WithBorderedCells_GetDashAfterEachRow()
    {
        var html = """
            <html><head><style>
              .payment-details tbody td { border-bottom: 1px dashed #000; }
            </style></head>
            <body>
              <table class="payment-details"><tbody>
                <tr><td>Order Total</td><td style="text-align: right;">1,500.00</td></tr>
                <tr><td>Paid</td><td style="text-align: right;">2,000.00</td></tr>
              </tbody></table>
            </body></html>
            """;
        var lines = ReceiptFormatter.Format(html, 48);
        var flat = lines.Where(l => l is DashLine || (l is TextLine { Text.Length: > 0 })).ToList();

        Assert.Collection(flat,
            l => Assert.StartsWith("Order Total", Assert.IsType<TextLine>(l).Text),
            l => Assert.IsType<DashLine>(l),
            l => Assert.StartsWith("Paid", Assert.IsType<TextLine>(l).Text),
            l => Assert.IsType<DashLine>(l));
    }

    [Fact]
    public void DefaultTemplate_RendersExpectedReceiptShape()
    {
        // Rendered output of the production 80mm Blade template (placeholders filled in).
        var html = """
            <!DOCTYPE html>
            <html>
            <head>
                <meta charset="UTF-8">
                <title>Order Receipt - Order #INV-001</title>
                <style>
                    @import url('https://fonts.googleapis.com/css2?family=Roboto+Mono&display=swap');
                </style>
                <style>
                    @page { size: 80mm auto; margin: 0; }
                    body { width: 80mm; margin: 0; padding: 10px; font-family: "Roboto Mono", monospace; font-weight: 400; font-size: 14px; }
                    .header, .footer { text-align: center; }
                    .header h2 { margin: 5px 0; font-size: 18px; }
                    .header p { font-size: 16px; font-weight: bold; }
                    .footer { font-size: 16px; font-weight: bold; }
                    .order-details table { width: 100%; border-collapse: collapse; }
                    .order-details thead { padding: 8px 0px; border-bottom: 1px dashed #000; }
                    .order-details th, .order-details tbody td { padding: 8px 0px; }
                    .payment-details th, .payment-details tbody td { padding: 8px 0px; border-bottom: 1px dashed #000; }
                </style>
                <script>window.onload = function() { window.print(); };</script>
            </head>
            <body>
            <div class="header">
                <img src="https://cdn.example.com/logo.png" alt="Store Logo" class="logo-image">
                <h2>Super Mart</h2>
                <p>12 Main Street<br>Colombo</p>
            </div>
            <div class="order-info">
                <p>Invoice No: INV-001</p>
                <p>Date: 2026-08-18 10:30 AM</p>
            </div>
            <div class="order-details">
                <table>
                    <thead><tr><th style="text-align: left;">Item</th><th style="text-align: right;">Price</th></tr></thead>
                    <tbody>
                        <tr><td style="padding: 4px 0;">Rice &amp; Curry x 2</td><td style="text-align: right; padding: 4px 0;">1,200.00</td></tr>
                        <tr><td colspan="2" style="border-bottom: 1px dashed #000; padding: 2px 0"></td></tr>
                    </tbody>
                    <tfoot>
                        <tr><td>Total</td><td style="text-align: right;">1,200.00</td></tr>
                    </tfoot>
                </table>
                <hr>
                <h3>Payment Summary</h3>
                <table class="payment-details"><tbody>
                    <tr><td>Order Total</td><td style="text-align: right;">1,200.00</td></tr>
                    <tr><td>Paid</td><td style="text-align: right;">1,500.00</td></tr>
                    <tr><td>Balance to Customer</td><td style="text-align: right;">300.00</td></tr>
                </tbody></table>
            </div>
            <div class="footer">
                <p>Thank you for your purchase!</p>
            </div>
            <div style="text-align: center; margin-top: 12px; padding-top: 8px; border-top: 1px dashed #999;">
                <p style="font-size: 10px; margin: 2px 0;"><strong>Powered by ET Cloud POS</strong></p>
            </div>
            </body>
            </html>
            """;
        var lines = ReceiptFormatter.Format(html, 48);
        var text = TextLines(lines);
        var dialect = ReceiptFormatter.ToDialect(lines, 48);

        // Logo survives as the first line.
        Assert.Equal("https://cdn.example.com/logo.png", Assert.IsType<ImageLine>(lines[0]).Url);

        // Header: store name centered + bold but NOT double-size (18px), address bold + centered.
        var title = text.Single(t => t.Text == "Super Mart");
        Assert.Equal(LineAlign.Center, title.Align);
        Assert.True(title.Bold);
        Assert.False(title.Wide);
        Assert.True(text.Single(t => t.Text == "12 Main Street").Bold);
        Assert.Equal(LineAlign.Center, text.Single(t => t.Text == "Colombo").Align);

        // Item row: name left, price right-padded onto the same 48-char line.
        var item = text.Single(t => t.Text.StartsWith("Rice & Curry"));
        Assert.Equal(48, item.Text.Length);
        Assert.EndsWith("1,200.00", item.Text);
        Assert.False(item.Wide);

        // Column headers are separated from items by a dash (thead border-bottom),
        // and every payment row is followed by a dash (payment-details td border-bottom).
        var flat = lines.Where(l => l is DashLine || (l is TextLine { Text.Length: > 0 }) || l is ImageLine).ToList();
        int IndexOf(string prefix) => flat.FindIndex(l => l is TextLine t && t.Text.StartsWith(prefix));
        Assert.True(flat[IndexOf("Item") + 1] is DashLine, dialect);
        Assert.True(flat[IndexOf("Order Total") + 1] is DashLine, dialect);
        Assert.True(flat[IndexOf("Paid") + 1] is DashLine, dialect);
        Assert.True(flat[IndexOf("Balance to Customer") + 1] is DashLine, dialect);

        // Footer and Powered-by: centered, bold, dash divider above Powered-by.
        var thanks = text.Single(t => t.Text == "Thank you for your purchase!");
        Assert.Equal(LineAlign.Center, thanks.Align);
        Assert.True(thanks.Bold);
        var powered = text.Single(t => t.Text == "Powered by ET Cloud POS");
        Assert.Equal(LineAlign.Center, powered.Align);
        Assert.True(powered.Bold);
        Assert.True(flat[IndexOf("Powered by") - 1] is DashLine, dialect);

        // Script and stylesheet text never leak into the receipt.
        Assert.DoesNotContain(text, t => t.Text.Contains("window.print") || t.Text.Contains("font-family"));
    }

    [Fact]
    public void Output_EndsWithSixBlankLines()
    {
        var lines = ReceiptFormatter.Format("<body><p>x</p></body>", 48);
        var lastSix = lines.Skip(lines.Count - 6).ToList();
        Assert.All(lastSix, l => Assert.True(l is TextLine { Text: "" }));
    }
}
