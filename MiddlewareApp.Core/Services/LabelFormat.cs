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
    private const int Mx = 16; // left/right margin (2 mm) - labels have die-cut edges
    private const int My = 8;

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
            // <head>/<title> is not label content
            text = Regex.Replace(htmlOrText, @"<head[\s\S]*?</head>|<title[\s\S]*?</title>|<script[\s\S]*?</script>|<style[\s\S]*?</style>", "", RegexOptions.IgnoreCase);
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

    /// <summary>Word-wrap to <paramref name="max"/> chars; words longer than that are hard-broken.</summary>
    public static List<string> Wrap(string text, int max)
    {
        var output = new List<string>();
        var cur = "";
        foreach (var w in text.Split(' '))
        {
            var word = w;
            while (word.Length > max)
            {
                if (cur.Length > 0) { output.Add(cur); cur = ""; }
                output.Add(word[..max]);
                word = word[max..];
            }
            if (word.Length == 0) continue;
            if (cur.Length == 0) cur = word;
            else if (cur.Length + 1 + word.Length <= max) cur += " " + word;
            else { output.Add(cur); cur = word; }
        }
        if (cur.Length > 0) output.Add(cur);
        return output;
    }

    /// <summary>Built-in TSPL fonts 3/2/1 are 16/12/8 dots wide; ZPL font 0 and CPCL font 7 are scaled.</summary>
    private static int CharWidth(string lang, int pitch) =>
        lang == "tspl"
            ? (pitch >= 28 ? 16 : pitch >= 22 ? 12 : 8)
            : (int)Math.Ceiling((pitch - 4) * 0.55);

    /// <summary>
    /// Largest line pitch (32 down to 16 dots) at which the wrapped text fits the label
    /// height. Without a known label size nothing is wrapped (the printer clips).
    /// </summary>
    private static (int Pitch, List<string> Rows) Layout(string lang, List<string> lines, (double W, double H)? size)
    {
        if (size is not { } sz) return (32, lines);
        var w = (int)Math.Round(sz.W * Dpm);
        var h = (int)Math.Round(sz.H * Dpm);
        var pitch = 32;
        var rows = new List<string>();
        for (; pitch >= 16; pitch -= 4)
        {
            var max = Math.Max(1, (w - 2 * Mx) / CharWidth(lang, pitch));
            rows = lines.SelectMany(l => Wrap(l, max)).ToList();
            if (rows.Count * pitch <= h - 2 * My) break;
        }
        return (Math.Max(pitch, 16), rows);
    }

    /// <param name="lang">"zpl", "tspl" or "cpcl"</param>
    public static string Build(string lang, string htmlOrText)
    {
        lang = lang.ToLowerInvariant();
        if (lang is not ("zpl" or "tspl" or "cpcl"))
            throw new ArgumentException($"Unknown label language: {lang}");

        var size = ParseSize(htmlOrText);
        var (pitch, rows) = Layout(lang, Lines(htmlOrText), size);
        var w = size is { } s1 ? (int)Math.Round(s1.W * Dpm) : 0;
        var h = size is { } s2 ? (int)Math.Round(s2.H * Dpm) : 0;
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        int Y(int i) => My + i * pitch;
        var cw = CharWidth(lang, pitch);
        var sb = new StringBuilder();

        switch (lang)
        {
            case "zpl":
                sb.Append("^XA^CI28");
                if (size != null) sb.Append($"^PW{w}^LL{h}");
                for (var i = 0; i < rows.Count; i++)
                    sb.Append($"^FO{Mx},{Y(i)}^A0N,{pitch - 4},{cw}^FD{Clean(rows[i])}^FS");
                sb.Append("^XZ\r\n");
                break;

            case "tspl":
                var font = pitch >= 28 ? "3" : pitch >= 22 ? "2" : "1";
                if (size is { } t)
                    sb.Append($"SIZE {t.W.ToString(inv)} mm,{t.H.ToString(inv)} mm\r\nGAP 2 mm,0 mm\r\n");
                sb.Append("CLS\r\n");
                for (var i = 0; i < rows.Count; i++)
                    sb.Append($"TEXT {Mx},{Y(i)},\"{font}\",0,1,1,\"{Clean(rows[i])}\"\r\n");
                sb.Append("PRINT 1,1\r\n");
                break;

            default: // cpcl: label height is mandatory
                var height = h > 0 ? h : rows.Count * pitch + 2 * My;
                sb.Append($"! 0 200 200 {height} 1\r\n");
                if (w > 0) sb.Append($"PAGE-WIDTH {w}\r\n");
                for (var i = 0; i < rows.Count; i++)
                    sb.Append($"TEXT 7 0 {Mx} {Y(i)} {Clean(rows[i])}\r\n");
                sb.Append("FORM\r\nPRINT\r\n");
                break;
        }
        return sb.ToString();
    }
}
