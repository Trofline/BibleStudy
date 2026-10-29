using BibleStudy.Core.Models;

namespace BibleStudy.Core.Storage;

public enum OwnKind { Document, Note, Mark, Prayer, Todo, MyXref }

/// <summary>One thing of the user's own: <paramref name="Title"/> is empty when the verse (<paramref name="Ref"/>) is the title.</summary>
public sealed record OwnHit(OwnKind Kind, string Title, string Snippet, VerseRef? Ref, long Id);

/// <summary>
/// Everything the user wrote, found from one place: notes on verses, highlights with their notes and tags, documents, prayers, to-dos
/// and own cross references. <see cref="Search"/> looks for words, <see cref="About"/> for a verse ("where did I write about this?").
/// </summary>
public static class OwnSearch
{
    static bool Has(string? haystack, string needle) => haystack is { Length: > 0 } && haystack.Contains(needle, StringComparison.CurrentCultureIgnoreCase);

    /// <summary>The part of the text around the first hit (or the start), on one line.</summary>
    public static string Excerpt(string text, string needle, int width = 90)
    {
        var flat = text.ReplaceLineEndings(" ").Replace("#", "").Replace("**", "").Trim();
        var at = needle.Length == 0 ? -1 : flat.IndexOf(needle, StringComparison.CurrentCultureIgnoreCase);
        if (at < 0) return flat.Length <= width ? flat : flat[..width].TrimEnd() + "…";
        var from = Math.Max(0, at - width / 3);
        var piece = flat[from..Math.Min(flat.Length, from + width)].Trim();
        return (from > 0 ? "…" : "") + piece + (from + width < flat.Length ? "…" : "");
    }

    /// <summary>All of the user's own things that contain the text (every word of it must occur in the same thing).</summary>
    public static List<OwnHit> Search(VaultStore vault, string text, int max = 60)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length == 0) return [];
        bool Match(params string?[] fields) => words.All(w => fields.Any(f => Has(f, w)));
        var first = words[0];
        var hits = new List<OwnHit>();

        foreach (var d in vault.GetDocuments())
            if (Match(d.Title, d.Body, string.Join(" ", d.Tags)))
                hits.Add(new(OwnKind.Document, d.Title, Excerpt(d.Body, Has(d.Body, first) ? first : "", 90), null, d.Id));
        foreach (var n in vault.GetNotes())
            if (Match(n.Text)) hits.Add(new(OwnKind.Note, "", Excerpt(n.Text, first), n.Ref, 0));
        foreach (var m in vault.GetMarks())
        {
            var tags = string.Join(" ", m.Tags);
            if (!Match(m.Text, m.Note, tags)) continue;
            var shown = m.Note.Length > 0 && Has(m.Note, first) ? m.Note : m.Text;
            hits.Add(new(OwnKind.Mark, "", Excerpt(shown, first) + (tags.Length > 0 ? "  #" + string.Join(" #", m.Tags) : ""), m.Ref, m.Id));
        }
        foreach (var p in vault.GetPrayers())
            if (Match(p.Text, p.Note)) hits.Add(new(OwnKind.Prayer, "", Excerpt(p.Text, first), p.Ref, p.Id));
        foreach (var t in vault.GetTodos())
            if (Match(t.Text)) hits.Add(new(OwnKind.Todo, "", Excerpt(t.Text, first), t.Ref, t.Id));
        foreach (var x in vault.GetMyXrefs())
            if (x.Note.Length > 0 && Match(x.Note)) hits.Add(new(OwnKind.MyXref, "", Excerpt(x.Note, first), x.From, x.Id));
        return hits.Take(max).ToList();
    }

    /// <summary>True when the text mentions the verse, also inside a range ("Joh 3,14-18" mentions Joh 3,16).</summary>
    public static bool Mentions(string text, VerseRef r) =>
        DocumentLinks.VerseRanges(text).Any(x => x.First.Book == r.Book && x.First.Chapter == r.Chapter && r.Verse >= x.First.Verse && r.Verse <= x.EndVerse);

    /// <summary>Everything the user has about a verse: written on it, or mentioning it somewhere else.</summary>
    public static List<OwnHit> About(VaultStore vault, VerseRef r)
    {
        var hits = new List<OwnHit>();
        foreach (var n in vault.GetNotes())
            if (n.Ref == r) hits.Add(new(OwnKind.Note, "", n.Text.ReplaceLineEndings(" ").Trim(), n.Ref, 0));
            else if (Mentions(n.Text, r)) hits.Add(new(OwnKind.Note, "", Excerpt(n.Text, ""), n.Ref, 0));
        foreach (var m in vault.GetMarks(r.Book, r.Chapter))
            if (m.Ref == r && (m.Note.Length > 0 || m.Tags.Count > 0)) hits.Add(new(OwnKind.Mark, "", Excerpt(m.Note.Length > 0 ? m.Note : m.Text, "") + (m.Tags.Count > 0 ? "  #" + string.Join(" #", m.Tags) : ""), m.Ref, m.Id));
        foreach (var m in vault.GetMarks())
            if (m.Ref != r && m.Note.Length > 0 && Mentions(m.Note, r)) hits.Add(new(OwnKind.Mark, "", Excerpt(m.Note, ""), m.Ref, m.Id));
        foreach (var d in vault.GetDocuments())
            if (Mentions(d.Body, r)) hits.Add(new(OwnKind.Document, d.Title, Excerpt(d.Body, ""), null, d.Id));
        foreach (var p in vault.GetPrayers())
            if (p.Ref == r || Mentions(p.Text, r)) hits.Add(new(OwnKind.Prayer, "", Excerpt(p.Text, ""), p.Ref, p.Id));
        foreach (var t in vault.GetTodos())
            if (t.Ref == r) hits.Add(new(OwnKind.Todo, "", t.Text, t.Ref, t.Id));
        foreach (var x in vault.GetMyXrefs(r.Book, r.Chapter))
            if (x.From == r) hits.Add(new(OwnKind.MyXref, "", x.Note, x.To, x.Id));
            else if (x.To.Book == r.Book && x.To.Chapter == r.Chapter && r.Verse >= x.To.Verse && r.Verse <= x.EndVerse) hits.Add(new(OwnKind.MyXref, "", x.Note, x.From, x.Id));
        return hits;
    }
}
