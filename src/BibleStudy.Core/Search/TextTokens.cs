namespace BibleStudy.Core.Search;

/// <summary>
/// Some translations carry the Greek or Hebrew word numbers (G2962, H430) and a source mark (TR) inside the verse text where the underlying
/// editions differ. Those marks are no words of the translation and must not be counted, searched for or matched as words.
/// </summary>
public static class TextTokens
{
    /// <summary>True for a Strong's number such as g2962 or H430 (upper or lower case).</summary>
    public static bool IsStrongTag(ReadOnlySpan<char> w)
    {
        if (w.Length < 2 || w.Length > 5 || (w[0] is not ('g' or 'G' or 'h' or 'H'))) return false;
        foreach (var c in w[1..]) if (!char.IsAsciiDigit(c)) return false;
        return true;
    }

    /// <summary>The number of real words of the verse: marks like "G2962 TR" are left out.</summary>
    public static int CountWords(CorpusVerse v)
    {
        var n = 0;
        var afterTag = false;
        for (var i = 0; i < v.Words.Length; i++)
        {
            var (s, l) = v.Words[i];
            var token = v.Lower.AsSpan(s, l);
            if (IsStrongTag(token)) { afterTag = true; continue; }
            if (afterTag && token is "tr") { afterTag = false; continue; }
            afterTag = false;
            n++;
        }
        return n;
    }
}
