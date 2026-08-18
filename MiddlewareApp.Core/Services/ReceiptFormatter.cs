using System.Text;
using System.Text.RegularExpressions;
using HtmlAgilityPack;

namespace MiddlewareApp.Core.Services;

public enum LineAlign { Left, Center, Right }

public abstract record ReceiptLine;

/// <summary>A single physical printed line (Text is at most the line width, unpadded).</summary>
public sealed record TextLine(string Text, LineAlign Align) : ReceiptLine;

/// <summary>Full-width dash separator (max 48 dashes, centered).</summary>
public sealed record DashLine : ReceiptLine;

/// <summary>Centered logo image (http/https URL).</summary>
public sealed record ImageLine(string Url) : ReceiptLine;

/// <summary>
/// Port of the Android receipt HTML → thermal-text converter (spec §5.1).
/// Produces the [L]/[C]/[R] line dialect as structured lines; EscPosRenderer turns
/// them into raw bytes.
/// </summary>
public static class ReceiptFormatter
{
    private const int MaxDashes = 48;
    private const int MaxDistinctImages = 2;

    /// <summary>48 chars per line for 80 mm paper, 32 for 58 mm.</summary>
    public static int CharsPerLine(string? paperSize) =>
        paperSize != null && paperSize.Trim().StartsWith("58") ? 32 : 48;

    public static IReadOnlyList<ReceiptLine> Format(string content, int width)
    {
        var lines = new List<ReceiptLine>();
        content ??= "";

        if (!Regex.IsMatch(content, @"<\s*[a-zA-Z!/]"))
        {
            // No HTML tags at all: plain left-aligned wrapped text.
            foreach (var raw in content.Replace("\r\n", "\n").Split('\n'))
            {
                var text = CleanText(raw);
                if (text.Length == 0) continue;
                foreach (var seg in Wrap(text, width))
                    lines.Add(new TextLine(seg, LineAlign.Left));
            }
        }
        else
        {
            var doc = new HtmlDocument();
            doc.LoadHtml(content);
            var root = doc.DocumentNode.SelectSingleNode("//body") ?? doc.DocumentNode;

            foreach (var junk in root.Descendants().Where(n => n.Name is "script" or "style").ToList())
                junk.Remove();

            var ctx = new FormatContext(lines, width);
            var buffer = new StringBuilder();
            WalkNode(root, centered: false, ctx, buffer);
            ctx.FlushText(buffer, centered: false);
        }

        // 6 blank lines so the footer clears the cutter (renderer then feeds + cuts).
        for (var i = 0; i < 6; i++)
            lines.Add(new TextLine("", LineAlign.Left));
        return lines;
    }

    private sealed class FormatContext
    {
        private readonly List<ReceiptLine> _lines;
        private readonly HashSet<string> _imageUrls = new(StringComparer.OrdinalIgnoreCase);
        public int Width { get; }

        public FormatContext(List<ReceiptLine> lines, int width)
        {
            _lines = lines;
            Width = width;
        }

        public void FlushText(StringBuilder buffer, bool centered)
        {
            var text = CleanText(buffer.ToString());
            buffer.Clear();
            if (text.Length == 0) return;

            var align = centered || text.StartsWith("Powered by", StringComparison.OrdinalIgnoreCase)
                ? LineAlign.Center
                : LineAlign.Left;
            foreach (var seg in Wrap(text, Width))
                _lines.Add(new TextLine(seg, align));
        }

        public void AddDash()
        {
            // Collapse consecutive dash lines.
            if (_lines.Count > 0 && _lines[^1] is DashLine) return;
            _lines.Add(new DashLine());
        }

        public void AddImage(string url)
        {
            if (_imageUrls.Contains(url)) return; // same logo repeated
            if (_imageUrls.Count >= MaxDistinctImages) return;
            _imageUrls.Add(url);
            _lines.Add(new ImageLine(url));
        }

        public void AddRaw(TextLine line) => _lines.Add(line);
    }

    private static void WalkNode(HtmlNode node, bool centered, FormatContext ctx, StringBuilder buffer)
    {
        switch (node.NodeType)
        {
            case HtmlNodeType.Text:
                var text = ((HtmlTextNode)node).Text;
                if (!string.IsNullOrWhiteSpace(text))
                {
                    if (buffer.Length > 0) buffer.Append(' ');
                    buffer.Append(text);
                }
                return;

            case HtmlNodeType.Document:
            case HtmlNodeType.Element:
                break;

            default:
                return; // comments etc.
        }

        var name = node.Name.ToLowerInvariant();

        switch (name)
        {
            case "hr":
                ctx.FlushText(buffer, centered);
                ctx.AddDash();
                return;

            case "br":
                ctx.FlushText(buffer, centered);
                return;

            case "img":
                ctx.FlushText(buffer, centered);
                var src = node.GetAttributeValue("src", "");
                if (src.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                    src.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    ctx.AddImage(src);
                return;

            case "table":
                ctx.FlushText(buffer, centered);
                EmitTable(node, centered, ctx);
                return;
        }

        var childCentered = centered || IsCenteredElement(node);
        var isBlock = IsBlockElement(name);

        if (isBlock) ctx.FlushText(buffer, centered);

        foreach (var child in node.ChildNodes)
            WalkNode(child, childCentered, ctx, buffer);

        if (isBlock) ctx.FlushText(buffer, childCentered);
    }

    private static bool IsBlockElement(string name) => name is
        "p" or "div" or "section" or "article" or "header" or "footer" or "main" or
        "h1" or "h2" or "h3" or "h4" or "h5" or "h6" or
        "ul" or "ol" or "li" or "tr" or "blockquote" or "pre" or "address" or
        "body" or "html" or "#document";

    private static bool IsCenteredElement(HtmlNode node)
    {
        var name = node.Name.ToLowerInvariant();
        if (name is "h1" or "h2" or "h3" or "h4") return true;
        var cls = node.GetAttributeValue("class", "");
        return cls.Contains("header", StringComparison.OrdinalIgnoreCase)
            || cls.Contains("footer", StringComparison.OrdinalIgnoreCase);
    }

    private static void EmitTable(HtmlNode table, bool centered, FormatContext ctx)
    {
        foreach (var row in table.Descendants("tr"))
        {
            var cells = row.ChildNodes
                .Where(n => n.Name is "td" or "th")
                .Select(c => CleanText(c.InnerText))
                .ToList();
            if (cells.Count == 0) continue;

            if (cells.All(IsSeparatorCell))
            {
                ctx.AddDash();
                continue;
            }

            if (cells.Count == 1)
            {
                var buf = new StringBuilder(cells[0]);
                ctx.FlushText(buf, centered);
                continue;
            }

            // Last cell right-aligned on the same line as the joined left cells
            // (single left column padded with spaces — never 50/50 columns).
            var left = CleanText(string.Join(" ", cells.Take(cells.Count - 1)));
            var right = cells[^1];
            EmitLeftRight(left, right, ctx);
        }
    }

    private static void EmitLeftRight(string left, string right, FormatContext ctx)
    {
        var width = ctx.Width;

        if (right.Length == 0)
        {
            foreach (var seg in Wrap(left, width))
                ctx.AddRaw(new TextLine(seg, LineAlign.Left));
            return;
        }

        if (left.Length == 0)
        {
            foreach (var seg in Wrap(right, width))
                ctx.AddRaw(new TextLine(seg, LineAlign.Right));
            return;
        }

        var leftSegs = Wrap(left, width);
        // Right value sits on the last wrapped line if it fits, else on its own right-aligned line.
        var last = leftSegs[^1];
        if (last.Length + 1 + right.Length <= width)
        {
            for (var i = 0; i < leftSegs.Count - 1; i++)
                ctx.AddRaw(new TextLine(leftSegs[i], LineAlign.Left));
            var pad = width - last.Length - right.Length;
            ctx.AddRaw(new TextLine(last + new string(' ', pad) + right, LineAlign.Left));
        }
        else
        {
            foreach (var seg in leftSegs)
                ctx.AddRaw(new TextLine(seg, LineAlign.Left));
            foreach (var seg in Wrap(right, width))
                ctx.AddRaw(new TextLine(seg, LineAlign.Right));
        }
    }

    private static bool IsSeparatorCell(string text) =>
        text.Length == 0 || text.All(c => c is '-' or '_' or '=' or '.' or '*');

    /// <summary>Decode entities, strip the thermal-markup chars &lt; &gt; [ ], collapse whitespace.</summary>
    internal static string CleanText(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return "";
        var text = HtmlEntity.DeEntitize(raw);
        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (c is '<' or '>' or '[' or ']') continue;
            sb.Append(c);
        }
        return Regex.Replace(sb.ToString(), @"\s+", " ").Trim();
    }

    internal static List<string> Wrap(string text, int width)
    {
        var result = new List<string>();
        if (text.Length == 0) return result;

        var current = new StringBuilder();
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var w = word;
            while (w.Length > width)
            {
                // A word longer than the line is hard-split.
                if (current.Length > 0)
                {
                    result.Add(current.ToString());
                    current.Clear();
                }
                result.Add(w[..width]);
                w = w[width..];
            }
            if (w.Length == 0) continue;

            if (current.Length == 0)
                current.Append(w);
            else if (current.Length + 1 + w.Length <= width)
                current.Append(' ').Append(w);
            else
            {
                result.Add(current.ToString());
                current.Clear();
                current.Append(w);
            }
        }
        if (current.Length > 0) result.Add(current.ToString());
        if (result.Count == 0) result.Add("");
        return result;
    }

    /// <summary>Renders the DantSu-style dialect string ("[L]text" / "[C]text" / "[R]text") — used by tests and logs.</summary>
    public static string ToDialect(IEnumerable<ReceiptLine> lines, int width)
    {
        var sb = new StringBuilder();
        foreach (var line in lines)
        {
            switch (line)
            {
                case TextLine t:
                    sb.Append(t.Align switch { LineAlign.Center => "[C]", LineAlign.Right => "[R]", _ => "[L]" });
                    sb.AppendLine(t.Text);
                    break;
                case DashLine:
                    sb.Append("[C]").AppendLine(new string('-', Math.Min(width, MaxDashes)));
                    break;
                case ImageLine img:
                    sb.Append("[C]<img>").AppendLine(img.Url);
                    break;
            }
        }
        return sb.ToString();
    }
}
