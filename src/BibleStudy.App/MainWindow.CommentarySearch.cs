using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using BibleStudy.Core.Models;

namespace BibleStudy.App;

/// <summary>
/// Full-text search in the chosen commentary. The commentary is read chapter by chapter once (kept in the cache folder, so later it
/// works offline) and then searched in memory: every word of the search must occur in the note.
/// </summary>
public partial class MainWindow
{
    sealed record CommentaryHit(string Book, int Chapter, int Verse, string Text);

    readonly Dictionary<string, List<CommentaryHit>> _commentaryIndex = [];
    bool _commentaryIndexing;

    void InitCommentarySearch()
    {
        CommentarySearchBox.KeyDown += async (_, e) => { if (e.Key == Key.Enter) await SearchCommentaryAsync(); };
        CommentarySearchBtn.Click += async (_, _) => await SearchCommentaryAsync();
    }

    async Task<List<CommentaryHit>?> CommentaryIndexAsync(CommentaryInfo c)
    {
        if (_commentaryIndex.TryGetValue(c.Id, out var ready)) return ready;
        if (_commentaryIndexing) { CommentaryInfo.Text = L("Der Kommentar wird gerade eingelesen – bitte kurz warten."); return null; }
        _commentaryIndexing = true;
        try
        {
            var chapters = _books.SelectMany(b => Enumerable.Range(1, b.NumberOfChapters).Select(ch => (b.Id, ch))).ToList();
            var hits = new List<CommentaryHit>(); var done = 0;
            using var gate = new SemaphoreSlim(6);
            await Task.WhenAll(chapters.Select(async x =>
            {
                await gate.WaitAsync();
                try
                {
                    var chapter = await _hello.GetCommentaryAsync(c.Id, x.Id, x.ch);
                    if (chapter is null) return;
                    var local = new List<CommentaryHit>();
                    if (chapter.Introduction.Length > 0) local.Add(new CommentaryHit(x.Id, x.ch, 0, chapter.Introduction));
                    local.AddRange(chapter.Notes.Select(n => new CommentaryHit(x.Id, x.ch, n.Verse, n.Text)));
                    lock (hits) hits.AddRange(local);
                }
                catch { /* a chapter that cannot be read is skipped */ }
                finally
                {
                    gate.Release();
                    if (Interlocked.Increment(ref done) % 25 == 0) Avalonia.Threading.Dispatcher.UIThread.Post(() => CommentaryInfo.Text = L("Einmalig: {0} wird eingelesen … {1}/{2} Kapitel", c.Name, done, chapters.Count));
                }
            }));
            var order = _books.Select((b, i) => (b.Id, i)).ToDictionary(p => p.Id, p => p.i);
            hits = hits.OrderBy(h => order.GetValueOrDefault(h.Book, 999)).ThenBy(h => h.Chapter).ThenBy(h => h.Verse).ToList();
            return _commentaryIndex[c.Id] = hits;
        }
        finally { _commentaryIndexing = false; }
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
