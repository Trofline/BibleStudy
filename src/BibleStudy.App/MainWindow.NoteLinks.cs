using Avalonia.Controls;
using Avalonia.Layout;
using BibleStudy.Core.Models;

namespace BibleStudy.App;

/// <summary>
/// Bible references in a note ("Joh 3,16", "Röm 8,28-30") become buttons under the editor that jump there; and a verse shows which
/// other notes mention it ("Erwähnt in Notizen zu …"), so a thought can be followed in both directions.
/// </summary>
public partial class MainWindow
{
    static string RangeLabel(Func<VerseRef, string> label, VerseRef r, int end) => label(r) + (end > r.Verse ? "–" + end : "");

    Button LinkChip(string text, string tip, VerseRef go)
    {
        var b = new Button { Content = text, Classes = { "ghost" }, Padding = new(8, 3), Margin = new(0, 0, 6, 4), FontSize = 12.5 };
        ToolTip.SetTip(b, tip);
        b.Click += async (_, _) => await GoToAsync(go.Book, go.Chapter, go.Verse);
        return b;
    }

    static TextBlock LinkCaption(string text) => new() { Text = text, Classes = { "caption" }, VerticalAlignment = VerticalAlignment.Center, Margin = new(0, 0, 8, 4) };

    void ShowNoteLinks(bool backlinks)
    {
        // what this note points to
        NoteLinksPanel.Children.Clear();
        var seen = new HashSet<(VerseRef, int)>();
        foreach (var (first, end) in DocumentLinks.VerseRanges(NoteEditor.Text ?? ""))
        {
            if (first == _noteRef || !seen.Add((first, end))) continue;
            if (NoteLinksPanel.Children.Count == 0) NoteLinksPanel.Children.Add(LinkCaption(L("Verweist auf:")));
            NoteLinksPanel.Children.Add(LinkChip("→ " + RangeLabel(Label, first, end), L("Zu dieser Stelle springen"), first));
            if (seen.Count >= 30) break;
        }
        NoteLinksPanel.IsVisible = NoteLinksPanel.Children.Count > 0;
        if (!backlinks) return;

        // which other notes mention this verse
        NoteBacklinksPanel.Children.Clear();
        if (_noteRef is { } me && _vault is not null)
        {
            foreach (var n in _vault.GetNotes())
            {
                if (n.Ref == me) continue;
                if (!DocumentLinks.VerseRanges(n.Text).Any(x => x.First.Book == me.Book && x.First.Chapter == me.Chapter && me.Verse >= x.First.Verse && me.Verse <= x.EndVerse)) continue;
                if (NoteBacklinksPanel.Children.Count == 0) NoteBacklinksPanel.Children.Add(LinkCaption(L("Erwähnt in Notizen zu:")));
                NoteBacklinksPanel.Children.Add(LinkChip("← " + Label(n.Ref), Shorten(n.Text.ReplaceLineEndings(" ").Trim(), 200), n.Ref));
            }
        }
        NoteBacklinksPanel.IsVisible = NoteBacklinksPanel.Children.Count > 0;
    }
}
