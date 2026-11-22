namespace BibleStudy.Core;

/// <summary>
/// Which words of one translation of a verse are not in another: the words are lined up (longest common run of words, upper and lower case and
/// punctuation do not count) and what is left over in the second text is what differs. Only meant for two texts of one language.
/// </summary>
public static class WordDiff
{
    /// <summary>The stretches of <paramref name="text"/> (start, end) whose words are not in <paramref name="other"/>; stretches that touch over a blank are one.</summary>
    public static List<(int Start, int End)> Differing(string other, string text)
    {
        var a = Words(other); var b = Words(text);
        var result = new List<(int, int)>();
        if (b.Count == 0) return result;
        var keep = new bool[b.Count];
        if (a.Count > 0)
        {
            // longest common subsequence of the normalised words
            var len = new int[a.Count + 1, b.Count + 1];
            for (var i = a.Count - 1; i >= 0; i--)
                for (var j = b.Count - 1; j >= 0; j--)
                    len[i, j] = a[i].Key == b[j].Key ? len[i + 1, j + 1] + 1 : Math.Max(len[i + 1, j], len[i, j + 1]);
            for (int i = 0, j = 0; i < a.Count && j < b.Count;)
            {
                if (a[i].Key == b[j].Key) { keep[j] = true; i++; j++; }
                else if (len[i + 1, j] >= len[i, j + 1]) i++;
                else j++;
            }
        }
        for (var j = 0; j < b.Count; j++)
        {
            if (keep[j]) continue;
            if (result.Count > 0 && result[^1].Item2 <= b[j].Start && text[result[^1].Item2..b[j].Start].All(char.IsWhiteSpace) && j > 0 && !keep[j - 1])
                result[^1] = (result[^1].Item1, b[j].End);
            else result.Add((b[j].Start, b[j].End));
        }
        return result;
    }

    static List<(string Key, int Start, int End)> Words(string s)
    {
        var list = new List<(string, int, int)>();
        for (var i = 0; i < s.Length;)
        {
            while (i < s.Length && !char.IsLetterOrDigit(s[i])) i++;
            var start = i;
            while (i < s.Length && (char.IsLetterOrDigit(s[i]) || ((s[i] is '\'' or '’' or '-') && i + 1 < s.Length && char.IsLetterOrDigit(s[i + 1])))) i++;
            if (i > start) list.Add((s[start..i].ToLowerInvariant().Replace("’", "'"), start, i));
        }
        return list;
    }
}
