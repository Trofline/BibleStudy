using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using BibleStudy.Core.Models;

namespace BibleStudy.App;

/// <summary>
/// Full-text search in the chosen commentary: it is read from the own store once and searched in memory; every word of the search must occur in the note.
/// </summary>
public partial class MainWindow
{
    sealed record CommentaryHit(string Book, int Chapter, int Verse, string Text);

    readonly Dictionary<string, List<CommentaryHit>> _commentaryIndex = [];

    void InitCommentarySearch()
    {
        CommentarySearchBox.KeyDown += async (_, e) => { if (e.Key == Key.Enter) await SearchCommentaryAsync(); };
        CommentarySearchBtn.Click += async (_, _) => await SearchCommentaryAsync();
    }

    /// <summary>All entries of the commentary in Bible order (read from the own store once and kept until the commentary is replaced or removed).</summary>
    async Task<List<CommentaryHit>?> CommentaryIndexAsync(CommentaryInfo c)
    {
        if (_commentaryIndex.TryGetValue(c.Id, out var ready)) return ready;
        var all = await Task.Run(() => _ownCommentaries.All(c.Id));
        var order = _books.Select((b, i) => (b.Id, i)).ToDictionary(p => p.Id, p => p.i);
        var hits = all.Select(h => new CommentaryHit(h.Book, h.Chapter, h.Verse, h.Text))
            .OrderBy(h => order.GetValueOrDefault(h.Book, 999)).ThenBy(h => h.Chapter).ThenBy(h => h.Verse).ToList();
        return _commentaryIndex[c.Id] = hits;
    }

    async Task SearchCommentaryAsync()
    {
        var query = CommentarySearchBox.Text?.Trim() ?? "";
        if (query.Length < 2 || CommentaryBox.SelectedItem is not CommentaryInfo c) return;
        var index = await CommentaryIndexAsync(c);
        if (index is null) return;
        var words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var found = index.Where(h => words.All(w => h.Text.Contains(w, StringComparison.CurrentCultureIgnoreCase))).ToList();
        CommentaryPanel.Children.Clear(); _commentaryCards.Clear();   // the cards of the chapter are gone: marking a verse must not reach for them
        CommentaryInfo.Text = found.Count == 0 ? L("Keine Treffer in {0}.", c.Name) : L("{0} Treffer in {1}{2}", found.Count, c.Name, found.Count > 100 ? L(" (die ersten 100)") : "");
        var back = new Button { Content = L("← Zurück zum Kapitel"), HorizontalAlignment = HorizontalAlignment.Left };
        back.Click += async (_, _) => { _shownCommentary = null; await ShowCommentaryAsync(); };
        CommentaryPanel.Children.Add(back);
        foreach (var h in found.Take(100))
        {
            var hit = h;
            var title = BookName(hit.Book) + " " + hit.Chapter + (hit.Verse > 0 ? "," + hit.Verse : "");
            var go = new Button { Classes = { "ghost" }, Padding = new(0), Content = title, FontWeight = FontWeight.SemiBold, Foreground = Res("BsAccent"), HorizontalAlignment = HorizontalAlignment.Left };
            go.Click += async (_, _) => { _shownCommentary = null; await GoToAsync(hit.Book, hit.Chapter, hit.Verse > 0 ? hit.Verse : null); };
            var at = hit.Text.IndexOf(words[0], StringComparison.CurrentCultureIgnoreCase);
            var from = Math.Max(0, at - 80);
            var snippet = (from > 0 ? "… " : "") + hit.Text.Substring(from, Math.Min(260, hit.Text.Length - from)).Replace('\n', ' ') + (from + 260 < hit.Text.Length ? " …" : "");
            var card = new Border { Classes = { "card" }, Child = new StackPanel { Spacing = 4, Children = { go, new TextBlock { Text = snippet, TextWrapping = TextWrapping.Wrap } } } };
            CommentaryPanel.Children.Add(card);
        }
    }
}
