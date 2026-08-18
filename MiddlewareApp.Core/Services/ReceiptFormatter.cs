using System.Text;
using System.Text.RegularExpressions;
using HtmlAgilityPack;

namespace MiddlewareApp.Core.Services;

public enum LineAlign { Left, Center, Right }

public abstract record ReceiptLine;

/// <summary>
/// A single physical printed line (Text is at most the line width, unpadded).
/// Wide lines print double width+height, so they hold half the characters.
/// </summary>
public sealed record TextLine(string Text, LineAlign Align, bool Bold = false, bool Wide = false) : ReceiptLine;

/// <summary>Full-width dash separator (max 48 dashes, centered).</summary>
public sealed record DashLine : ReceiptLine;

/// <summary>Centered logo image (http/https URL).</summary>
public sealed record ImageLine(string Url) : ReceiptLine;

/// <summary>
/// Port of the Android receipt HTML → thermal-text converter (spec §5.1).
/// Produces the [L]/[C]/[R] line dialect as structured lines; EscPosRenderer turns
/// them into raw bytes.
///
/// Honors the subset of CSS a thermal printer can express — text-align,
/// font-weight, large font-size (double-size characters), display:none /
/// visibility:hidden — from inline style attributes and simple single-selector
/// rules (tag, .class, #id) in &lt;style&gt; blocks. Everything else
/// (colors, fonts, margins, combinator selectors) is ignored.
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

            // Harvest simple stylesheet rules before stripping the style nodes.
            var rules = ParseStylesheets(doc.DocumentNode);

            foreach (var junk in root.Descendants().Where(n => n.Name is "script" or "style").ToList())
                junk.Remove();

            var ctx = new FormatContext(lines, width, rules);
            var buffer = new TextBuffer();
            WalkNode(root, NodeStyle.Default, ctx, buffer);
            ctx.FlushText(buffer, NodeStyle.Default);
        }

        // 6 blank lines so the footer clears the cutter (renderer then feeds + cuts).
        for (var i = 0; i < 6; i++)
            lines.Add(new TextLine("", LineAlign.Left));
        return lines;
    }

    /// <summary>
    /// Resolved presentation for a node: null Align means "inherit/default left".
    /// DashAbove/DashBelow come from visible border-top/border-bottom and are
    /// per-node (never inherited) — they print as dash separator lines.
    /// </summary>
    private readonly record struct NodeStyle(LineAlign? Align, bool Bold, bool Wide, bool Hidden,
        bool DashAbove, bool DashBelow)
    {
        public static readonly NodeStyle Default = new(null, false, false, false, false, false);
    }

    /// <summary>Text accumulated between block boundaries plus the styling of its runs.</summary>
    private sealed class TextBuffer
    {
        public StringBuilder Sb { get; } = new();
        public bool Bold { get; private set; }
        public bool Wide { get; private set; }

        public void Append(string text, NodeStyle style)
        {
            if (Sb.Length > 0) Sb.Append(' ');
            Sb.Append(text);
            Bold |= style.Bold;
            Wide |= style.Wide;
        }

        public void Clear()
        {
            Sb.Clear();
            Bold = false;
            Wide = false;
        }
    }

    private sealed record SimpleSelector(string? Tag, IReadOnlyList<string> Classes, string? Id);

    /// <summary>Chain of simple selectors joined by descendant/child combinators; the last one is the subject.</summary>
    private sealed record CssRule(IReadOnlyList<SimpleSelector> Chain, string Declarations);

    private sealed class FormatContext
    {
        private readonly List<ReceiptLine> _lines;
        private readonly HashSet<string> _imageUrls = new(StringComparer.OrdinalIgnoreCase);
        public int Width { get; }
        public IReadOnlyList<CssRule> Rules { get; }

        public FormatContext(List<ReceiptLine> lines, int width, IReadOnlyList<CssRule> rules)
        {
            _lines = lines;
            Width = width;
            Rules = rules;
        }

        public int EffectiveWidth(bool wide) => wide ? Math.Max(8, Width / 2) : Width;

        public void FlushText(TextBuffer buffer, NodeStyle style)
        {
            var text = CleanText(buffer.Sb.ToString());
            var bold = buffer.Bold || style.Bold;
            var wide = buffer.Wide || style.Wide;
            buffer.Clear();
            if (text.Length == 0) return;

            var align = style.Align ?? LineAlign.Left;
            if (align == LineAlign.Left && text.StartsWith("Powered by", StringComparison.OrdinalIgnoreCase))
                align = LineAlign.Center;
            foreach (var seg in Wrap(text, EffectiveWidth(wide)))
                _lines.Add(new TextLine(seg, align, bold, wide));
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

    private static void WalkNode(HtmlNode node, NodeStyle style, FormatContext ctx, TextBuffer buffer)
    {
        switch (node.NodeType)
        {
            case HtmlNodeType.Text:
                var text = ((HtmlTextNode)node).Text;
                if (!string.IsNullOrWhiteSpace(text))
                    buffer.Append(text, style);
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
                ctx.FlushText(buffer, style);
                ctx.AddDash();
                return;

            case "br":
                ctx.FlushText(buffer, style);
                return;

            case "img":
                if (ComputeStyle(node, style, ctx.Rules).Hidden) return;
                ctx.FlushText(buffer, style);
                var src = node.GetAttributeValue("src", "");
                if (src.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                    src.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    ctx.AddImage(src);
                return;

            case "table":
                var tableStyle = ComputeStyle(node, style, ctx.Rules);
                if (tableStyle.Hidden) return;
                ctx.FlushText(buffer, style);
                EmitTable(node, tableStyle, ctx);
                return;
        }

        var childStyle = node.NodeType == HtmlNodeType.Document ? style : ComputeStyle(node, style, ctx.Rules);
        if (childStyle.Hidden) return;
        var isBlock = IsBlockElement(name);

        if (isBlock)
        {
            ctx.FlushText(buffer, style);
            if (childStyle.DashAbove) ctx.AddDash();
        }

        foreach (var child in node.ChildNodes)
            WalkNode(child, childStyle, ctx, buffer);

        if (isBlock)
        {
            ctx.FlushText(buffer, childStyle);
            if (childStyle.DashBelow) ctx.AddDash();
        }
    }

    private static bool IsBlockElement(string name) => name is
        "p" or "div" or "section" or "article" or "header" or "footer" or "main" or
        "h1" or "h2" or "h3" or "h4" or "h5" or "h6" or
        "ul" or "ol" or "li" or "tr" or "blockquote" or "pre" or "address" or
        "body" or "html" or "#document";

    /// <summary>Tag defaults → stylesheet rules → legacy align attribute → inline style (highest wins).</summary>
    private static NodeStyle ComputeStyle(HtmlNode node, NodeStyle inherited, IReadOnlyList<CssRule> rules)
    {
        // Borders never inherit — each node starts without them.
        var style = inherited with { DashAbove = false, DashBelow = false };
        var name = node.Name.ToLowerInvariant();

        if (name is "h1" or "h2" or "h3" or "h4") style = style with { Align = LineAlign.Center };
        if (name is "h1" or "h2" or "h3" or "h4" or "h5" or "h6" or "b" or "strong" or "th")
            style = style with { Bold = true };
        if (name is "h1" or "h2") style = style with { Wide = true };

        var cls = node.GetAttributeValue("class", "");
        if (cls.Contains("header", StringComparison.OrdinalIgnoreCase) ||
            cls.Contains("footer", StringComparison.OrdinalIgnoreCase))
            style = style with { Align = LineAlign.Center };

        foreach (var rule in rules)
        {
            if (Matches(rule, node))
                style = ApplyDeclarations(style, rule.Declarations);
        }

        var alignAttr = node.GetAttributeValue("align", "").Trim().ToLowerInvariant();
        if (alignAttr.Length > 0)
            style = ApplyAlign(style, alignAttr);

        var inline = node.GetAttributeValue("style", "");
        if (inline.Length > 0)
            style = ApplyDeclarations(style, inline);

        return style;
    }

    private static NodeStyle ApplyAlign(NodeStyle style, string value) => value switch
    {
        "center" => style with { Align = LineAlign.Center },
        "right" or "end" => style with { Align = LineAlign.Right },
        "left" or "start" or "justify" => style with { Align = LineAlign.Left },
        _ => style,
    };

    private static NodeStyle ApplyDeclarations(NodeStyle style, string css)
    {
        foreach (var decl in css.Split(';'))
        {
            var idx = decl.IndexOf(':');
            if (idx <= 0) continue;
            var prop = decl[..idx].Trim().ToLowerInvariant();
            var value = decl[(idx + 1)..].Trim().ToLowerInvariant();
            if (value.Length == 0) continue;

            switch (prop)
            {
                case "text-align":
                    style = ApplyAlign(style, value);
                    break;

                case "font-weight":
                    if (value is "bold" or "bolder") style = style with { Bold = true };
                    else if (value is "normal" or "lighter") style = style with { Bold = false };
                    else if (int.TryParse(value, out var weight)) style = style with { Bold = weight >= 600 };
                    break;

                case "font-size":
                    var wide = IsLargeFontSize(value);
                    if (wide.HasValue) style = style with { Wide = wide.Value };
                    break;

                case "display":
                    if (value == "none") style = style with { Hidden = true };
                    break;

                case "visibility":
                    if (value == "hidden" || value == "collapse") style = style with { Hidden = true };
                    else if (value == "visible") style = style with { Hidden = false };
                    break;

                case "border-top":
                    if (IsVisibleBorder(value)) style = style with { DashAbove = true };
                    break;

                case "border-bottom":
                    if (IsVisibleBorder(value)) style = style with { DashBelow = true };
                    break;

                case "border":
                    if (IsVisibleBorder(value)) style = style with { DashAbove = true, DashBelow = true };
                    break;
            }
        }
        return style;
    }

    /// <summary>Receipt templates draw separators as borders; any visible border style becomes a dash line.</summary>
    private static bool IsVisibleBorder(string value) =>
        Regex.IsMatch(value, @"\b(solid|dashed|dotted|double)\b");

    /// <summary>Roughly 1.5× the default receipt font counts as "large" → double-size characters.</summary>
    private static bool? IsLargeFontSize(string value)
    {
        switch (value)
        {
            case "large" or "x-large" or "xx-large" or "larger":
                return true;
            case "xx-small" or "x-small" or "small" or "smaller" or "medium":
                return false;
        }

        var m = Regex.Match(value, @"^(\d+(?:\.\d+)?)(px|pt|em|rem|%)?$");
        if (!m.Success) return null;
        var size = double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        return m.Groups[2].Value switch
        {
            "pt" => size >= 15,
            "em" or "rem" => size >= 1.5,
            "%" => size >= 150,
            _ => size >= 20, // px or unitless
        };
    }

    /// <summary>
    /// Parses &lt;style&gt; blocks into rules. Supports comma lists and descendant/child
    /// chains of simple selectors (tag, .class, #id, tag.class). Pseudo, attribute and
    /// sibling selectors are skipped, as are at-rules (@page, @media, …).
    /// </summary>
    private static List<CssRule> ParseStylesheets(HtmlNode documentNode)
    {
        var rules = new List<CssRule>();
        foreach (var styleNode in documentNode.Descendants("style"))
        {
            var css = Regex.Replace(styleNode.InnerText, @"/\*.*?\*/", "", RegexOptions.Singleline);
            // Statement at-rules would otherwise glue onto the next selector; block
            // at-rule wrappers are unwrapped (stray closers are harmless — the rule
            // regex can't match across braces).
            css = Regex.Replace(css, @"@(import|charset|namespace)[^;]*;", "");
            css = Regex.Replace(css, @"@(media|supports)[^{}]*\{", "");
            foreach (Match block in Regex.Matches(css, @"([^{}]+)\{([^}]*)\}"))
            {
                var declarations = block.Groups[2].Value;
                foreach (var selector in block.Groups[1].Value.Split(','))
                {
                    var rule = ParseSelector(selector.Trim(), declarations);
                    if (rule != null) rules.Add(rule);
                }
            }
        }
        return rules;
    }

    private static CssRule? ParseSelector(string selector, string declarations)
    {
        if (selector.Length == 0 || selector.StartsWith('@')) return null;

        // Child combinators are approximated as descendants.
        var parts = selector.Split(new[] { ' ', '\t', '>' }, StringSplitOptions.RemoveEmptyEntries);
        var chain = new List<SimpleSelector>(parts.Length);
        foreach (var part in parts)
        {
            var simple = ParseSimpleSelector(part);
            if (simple == null) return null; // unsupported piece → skip the whole selector
            chain.Add(simple);
        }
        return chain.Count > 0 ? new CssRule(chain, declarations) : null;
    }

    private static SimpleSelector? ParseSimpleSelector(string selector)
    {
        var m = Regex.Match(selector, @"^([a-zA-Z][a-zA-Z0-9]*)?((?:[.#][\w-]+)+)?$");
        if (!m.Success || selector.Length == 0) return null;

        var tag = m.Groups[1].Success ? m.Groups[1].Value.ToLowerInvariant() : null;
        var classes = new List<string>();
        string? id = null;
        foreach (Match part in Regex.Matches(m.Groups[2].Value, @"[.#][\w-]+"))
        {
            if (part.Value[0] == '.') classes.Add(part.Value[1..]);
            else id = part.Value[1..];
        }
        if (tag == null && classes.Count == 0 && id == null) return null;
        return new SimpleSelector(tag, classes, id);
    }

    private static bool Matches(CssRule rule, HtmlNode node)
    {
        var chain = rule.Chain;
        if (!MatchesSimple(chain[^1], node)) return false;

        // Remaining parts must match ancestors, right to left.
        var ancestor = node.ParentNode;
        for (var i = chain.Count - 2; i >= 0; i--)
        {
            while (ancestor != null && !MatchesSimple(chain[i], ancestor))
                ancestor = ancestor.ParentNode;
            if (ancestor == null) return false;
            ancestor = ancestor.ParentNode;
        }
        return true;
    }

    private static bool MatchesSimple(SimpleSelector sel, HtmlNode node)
    {
        if (node.NodeType != HtmlNodeType.Element && node.NodeType != HtmlNodeType.Document) return false;
        if (sel.Tag != null && !node.Name.Equals(sel.Tag, StringComparison.OrdinalIgnoreCase)) return false;
        if (sel.Id != null && !sel.Id.Equals(node.Id, StringComparison.OrdinalIgnoreCase)) return false;
        if (sel.Classes.Count > 0)
        {
            var classes = node.GetAttributeValue("class", "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
            foreach (var required in sel.Classes)
            {
                if (!classes.Contains(required, StringComparer.OrdinalIgnoreCase)) return false;
            }
        }
        return true;
    }

    private static void EmitTable(HtmlNode table, NodeStyle style, FormatContext ctx)
    {
        // Track thead/tbody/tfoot so a bordered group prints its separator once,
        // around the whole group (e.g. a dashed line under the column headers).
        HtmlNode? currentGroup = null;
        var groupStyle = style;

        foreach (var row in table.Descendants("tr"))
        {
            var parent = row.ParentNode;
            var group = parent != null && parent.Name is "thead" or "tbody" or "tfoot" ? parent : null;
            if (group != currentGroup)
            {
                if (currentGroup != null && groupStyle.DashBelow) ctx.AddDash();
                currentGroup = group;
                groupStyle = group != null ? ComputeStyle(group, style, ctx.Rules) : style;
                if (group != null && groupStyle.DashAbove) ctx.AddDash();
            }
            if (groupStyle.Hidden) continue;

            var rowStyle = ComputeStyle(row, groupStyle, ctx.Rules);
            if (rowStyle.Hidden) continue;

            var cells = row.ChildNodes
                .Where(n => n.Name is "td" or "th")
                .Select(c => (Style: CellStyle(c, rowStyle, ctx.Rules), Text: CleanText(c.InnerText)))
                .Where(c => !c.Style.Hidden)
                .ToList();
            if (cells.Count == 0) continue;

            if (rowStyle.DashAbove || cells.Any(c => c.Style.DashAbove)) ctx.AddDash();
            EmitTableRow(cells, ctx);
            if (rowStyle.DashBelow || cells.Any(c => c.Style.DashBelow)) ctx.AddDash();
        }

        if (currentGroup != null && groupStyle.DashBelow) ctx.AddDash();
    }

    private static void EmitTableRow(List<(NodeStyle Style, string Text)> cells, FormatContext ctx)
    {
        if (cells.All(c => IsSeparatorCell(c.Text)))
        {
            ctx.AddDash();
            return;
        }

        if (cells.Count == 1)
        {
            var buf = new TextBuffer();
            buf.Append(cells[0].Text, cells[0].Style);
            ctx.FlushText(buf, cells[0].Style);
            return;
        }

        // Last cell right-aligned on the same line as the joined left cells
        // (single left column padded with spaces — never 50/50 columns).
        var bold = cells.Any(c => c.Style.Bold && c.Text.Length > 0);
        var wide = cells.Any(c => c.Style.Wide && c.Text.Length > 0);
        var left = CleanText(string.Join(" ", cells.Take(cells.Count - 1).Select(c => c.Text)));
        var right = cells[^1].Text;
        EmitLeftRight(left, right, ctx, bold, wide);
    }

    /// <summary>Cell style including bold implied by a nested &lt;b&gt;/&lt;strong&gt; wrapping the content.</summary>
    private static NodeStyle CellStyle(HtmlNode cell, NodeStyle rowStyle, IReadOnlyList<CssRule> rules)
    {
        var style = ComputeStyle(cell, rowStyle, rules);
        if (!style.Bold && cell.Descendants().Any(d => d.Name is "b" or "strong"))
            style = style with { Bold = true };
        return style;
    }

    private static void EmitLeftRight(string left, string right, FormatContext ctx, bool bold, bool wide)
    {
        var width = ctx.EffectiveWidth(wide);

        if (right.Length == 0)
        {
            foreach (var seg in Wrap(left, width))
                ctx.AddRaw(new TextLine(seg, LineAlign.Left, bold, wide));
            return;
        }

        if (left.Length == 0)
        {
            foreach (var seg in Wrap(right, width))
                ctx.AddRaw(new TextLine(seg, LineAlign.Right, bold, wide));
            return;
        }

        var leftSegs = Wrap(left, width);
        // Right value sits on the last wrapped line if it fits, else on its own right-aligned line.
        var last = leftSegs[^1];
        if (last.Length + 1 + right.Length <= width)
        {
            for (var i = 0; i < leftSegs.Count - 1; i++)
                ctx.AddRaw(new TextLine(leftSegs[i], LineAlign.Left, bold, wide));
            var pad = width - last.Length - right.Length;
            ctx.AddRaw(new TextLine(last + new string(' ', pad) + right, LineAlign.Left, bold, wide));
        }
        else
        {
            foreach (var seg in leftSegs)
                ctx.AddRaw(new TextLine(seg, LineAlign.Left, bold, wide));
            foreach (var seg in Wrap(right, width))
                ctx.AddRaw(new TextLine(seg, LineAlign.Right, bold, wide));
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
                    if (t.Wide) sb.Append("<w>");
                    if (t.Bold) sb.Append("<b>");
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
