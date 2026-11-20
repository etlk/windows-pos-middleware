using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace MiddlewareApp.Core.Services;

/// <summary>
/// Label-printer command builders (ZPL / TSPL / CPCL), 203 dpi = 8 dots/mm. Mirrors the
/// Android labelFormat.ts. Label size comes from the job HTML's <c>@page { size: Wmm Hmm }</c>;
/// when absent no size command is sent and the printer uses its own calibrated media.
/// </summary>
public static class LabelFormat
{
    private const int Dpm = 8;
    private const int Margin = 8;

    private static readonly Regex PageSize = new(
        @"@page[^{]*\{[^}]*?size:\s*([\d.]+)\s*mm\s+([\d.]+)\s*mm",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static (double W, double H)? ParseSize(string html)
    {
        var m = PageSize.Match(html);
        if (!m.Success) return null;
        return (double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture),
                double.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture));
    }

    public static List<string> Lines(string htmlOrText)
    {
        var text = htmlOrText;
        if (Regex.IsMatch(htmlOrText, @"</?[a-z][\s\S]*>", RegexOptions.IgnoreCase))
        {
            text = Regex.Replace(htmlOrText, @"<script[\s\S]*?</script>|<style[\s\S]*?</style>", "", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"</t[dh]>", " ", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"<br\s*/?>|</(p|div|h[1-6]|li|tr)>", "\n", RegexOptions.IgnoreCase);
            text = WebUtility.HtmlDecode(Regex.Replace(text, @"<[^>]+>", ""));
        }
        return text.Split('\n')
            .Select(l => Regex.Replace(l, @"\s+", " ").Trim())
            .Where(l => l.Length > 0)
            .ToList();
    }

    /// <summary>Printer fonts are ASCII only; strip characters the languages use as delimiters.</summary>
    private static string Clean(string s) =>
        new(s.Select(c => c is '"' or '^' or '~' or '\\' ? ' ' : c).ToArray());

    /// <param name="lang">"zpl", "tspl" or "cpcl"</param>
    public static string Build(string lang, string htmlOrText)
    {
        var size = ParseSize(htmlOrText);
        var lines = Lines(htmlOrText);
        var w = size is { } s1 ? (int)Math.Round(s1.W * Dpm) : 0;
        var h = size is { } s2 ? (int)Math.Round(s2.H * Dpm) : 0;
        // Line pitch 32 dots, shrunk so every line fits the label height.
        var pitch = size != null && lines.Count > 0
            ? Math.Max(16, Math.Min(32, (h - 2 * Margin) / lines.Count))
            : 32;
        int Y(int i) => Margin + i * pitch;
        var sb = new StringBuilder();

        switch (lang.ToLowerInvariant())
        {
            case "zpl":
                sb.Append("^XA^CI28");
                if (size != null) sb.Append($"^PW{w}^LL{h}");
                for (var i = 0; i < lines.Count; i++)
                    sb.Append($"^FO{Margin},{Y(i)}^A0N,{pitch - 4},0^FD{Clean(lines[i])}^FS");
                sb.Append("^XZ\r\n");
                break;

            case "tspl":
                var font = pitch >= 28 ? "3" : pitch >= 22 ? "2" : "1";
                if (size is { } t)
                    sb.Append($"SIZE {t.W.ToString(System.Globalization.CultureInfo.InvariantCulture)} mm,{t.H.ToString(System.Globalization.CultureInfo.InvariantCulture)} mm\r\nGAP 2 mm,0 mm\r\n");
                sb.Append("CLS\r\n");
                for (var i = 0; i < lines.Count; i++)
                    sb.Append($"TEXT {Margin},{Y(i)},\"{font}\",0,1,1,\"{Clean(lines[i])}\"\r\n");
                sb.Append("PRINT 1,1\r\n");
                break;

            case "cpcl":
                // label height is mandatory
                var height = h > 0 ? h : lines.Count * pitch + 2 * Margin;
                sb.Append($"! 0 200 200 {height} 1\r\n");
                if (w > 0) sb.Append($"PAGE-WIDTH {w}\r\n");
                for (var i = 0; i < lines.Count; i++)
                    sb.Append($"TEXT 7 0 {Margin} {Y(i)} {Clean(lines[i])}\r\n");
                sb.Append("FORM\r\nPRINT\r\n");
                break;

            default:
                throw new ArgumentException($"Unknown label language: {lang}");
        }
        return sb.ToString();
    }
}
