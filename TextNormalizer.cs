using System.Text;

namespace IFTextCompactor;

public static class TextNormalizer
{
    // Common "smart" punctuation collapsed down to plain ASCII equivalents,
    // since the target character set only covers 0-127.
    private static readonly Dictionary<char, string> Substitutions = new()
    {
        ['\u2018'] = "'",  // left single quote
        ['\u2019'] = "'",  // right single quote / apostrophe
        ['\u201A'] = ",",  // low single quote
        ['\u201C'] = "\"", // left double quote
        ['\u201D'] = "\"", // right double quote
        ['\u2013'] = "-",  // en dash
        ['\u2014'] = "-",  // em dash
        ['\u2026'] = "...",// ellipsis
        ['\u00A0'] = " ",  // non-breaking space
    };

    public static string Normalize(string s)
    {
        StringBuilder? sb = null;

        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (Substitutions.TryGetValue(c, out string? rep))
            {
                sb ??= new StringBuilder(s.Substring(0, i), s.Length + 4);
                sb.Append(rep);
            }
            else
            {
                sb?.Append(c);
            }
        }

        return sb?.ToString() ?? s;
    }

    // Throws if any character can't be represented in the 0-127 literal range.
    // Call this AFTER Normalize().
    public static void ValidateAscii(string s, string context)
    {
        foreach (char c in s)
        {
            if (c > 127)
            {
                throw new FormatException(
                    $"{context}: character '{c}' (U+{(int)c:X4}) is outside the supported 0-127 range. " +
                    "Add a substitution in TextNormalizer.Substitutions or edit the source text.");
            }
        }
    }
}
