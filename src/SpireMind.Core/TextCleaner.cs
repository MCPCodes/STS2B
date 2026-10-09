using System.Text;
using System.Text.RegularExpressions;

namespace SpireMind.Core;

/// <summary>
/// Card, relic and event text in the game is BBCode with inline icons. The model reads it
/// better as plain text, with the icons that carry meaning (energy, stars) kept as tokens.
/// </summary>
public static partial class TextCleaner
{
    [GeneratedRegex(@"\[img[^\]]*\](?<path>.*?)\[/img\]", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex ImageTag();

    [GeneratedRegex(@"\[/?[a-zA-Z_]+(?:[= ][^\]]*)?\]")]
    private static partial Regex AnyTag();

    [GeneratedRegex(@"[ \t]+")]
    private static partial Regex Spaces();

    [GeneratedRegex(@"\s*\n\s*")]
    private static partial Regex Newlines();

    public static string Clean(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";

        var s = ImageTag().Replace(text, m => IconToken(m.Groups["path"].Value));
        s = AnyTag().Replace(s, "");
        s = Spaces().Replace(s, " ");
        s = Newlines().Replace(s, " / ");
        return s.Trim().Trim('/').Trim();
    }

    private static string IconToken(string path)
    {
        var p = path.ToLowerInvariant();
        if (p.Contains("energy")) return "(E)";
        if (p.Contains("star")) return "(Star)";
        return "";
    }

    /// <summary>Join non-empty parts with ", " (handy for compact labels).</summary>
    public static string Join(params string?[] parts)
    {
        var sb = new StringBuilder();
        foreach (var p in parts)
        {
            if (string.IsNullOrWhiteSpace(p)) continue;
            if (sb.Length > 0) sb.Append(", ");
            sb.Append(p);
        }
        return sb.ToString();
    }
}
