using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using BibleStudy.Core.Models;
using BibleStudy.Core.Search;

namespace BibleStudy.App;

/// <summary>The reading list from a pasted text: a list of passages copied from a web page, an e-mail or a sermon outline goes in at once.</summary>
public partial class MainWindow
{
    const int QueuePasteLimit = 1000;

    /// <summary>The single verses of the passages (a chapter or a range across chapters is looked up in the shown translation); Missing gets what the Bible here does not have.</summary>
    async Task<(List<VerseRef> Verses, List<string> Missing)> ExpandPassagesAsync(IEnumerable<PassageRef> passages)
    {
        var verses = new List<VerseRef>(); var missing = new List<string>();
        var t = TranslationBox.SelectedItem as TranslationInfo;
        async Task<List<int>> VersesOf(string book, int chapter)
        {
            if (t is null) return [];
            try { return (await _provider.GetChapterAsync(t.Id, book, chapter)).Verses.Select(v => v.Number).ToList(); } catch { return []; }
        }
        foreach (var p in passages)
        {
            if (verses.Count >= QueuePasteLimit) break;
            var name = BookName(p.Book);
            if (_books.All(b => b.Id != p.Book)) { missing.Add(name); continue; }
            if (p.FromVerse is int from && p.ToChapter is null)
            {
                var to = Math.Max(from, p.ToVerse ?? from);
                for (var v = from; v <= to; v++) verses.Add(new VerseRef(p.Book, p.Chapter, v));
                continue;
            }
            // a whole chapter ("Ps 23", "Ps 23-25") or a range over chapters ("Joh 3,16-4,3")
            var lastChapter = p.ToChapter is int c2 ? Math.Max(c2, p.Chapter) : p.Chapter;
            for (var ch = p.Chapter; ch <= lastChapter; ch++)
            {
                var all = await VersesOf(p.Book, ch);
                if (all.Count == 0) { missing.Add($"{name} {ch}"); continue; }
                var first = ch == p.Chapter && p.FromVerse is int f ? f : 1;
                var last = ch == lastChapter && p.FromVerse is not null && p.ToVerse is int l ? l : int.MaxValue;
                verses.AddRange(all.Where(v => v >= first && v <= last).Select(v => new VerseRef(p.Book, ch, v)));
            }
        }
        return ([.. verses.Take(QueuePasteLimit)], missing);
    }

    void ShowQueuePaste(Control anchor)
    {
        var box = new TextBox
        {
            AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 170,
            PlaceholderText = L("Hier die Verse einfügen, einer pro Zeile oder mit Semikolon getrennt, z. B.:") + "\nJoh 3,16\nRöm 5,8\nPs 23",
        };
        var info = new TextBlock { Classes = { "caption" }, TextWrapping = TextWrapping.Wrap };
        var add = new Button { Content = L("Zur Leseliste hinzufügen"), Classes = { "primary" }, IsEnabled = false };
        var paste = new Button { Content = L("Aus der Zwischenablage") };
        var fly = new Flyout
        {
            Content = new StackPanel
            {
                Spacing = 8, Width = PW(360),
                Children =
                {
                    new TextBlock { Text = L("Verse in die Leseliste einfügen"), FontWeight = FontWeight.SemiBold },
                    new TextBlock { Text = L("Du kannst eine Liste von Bibelstellen hineinkopieren, auch mit Aufzählungszeichen und dem Text dahinter. Die Stellen werden erkannt und Vers für Vers aufgenommen."), Classes = { "caption" }, TextWrapping = TextWrapping.Wrap },
                    box, info,
                    new WrapPanel { Children = { add, new Border { Width = 6 }, paste } },
                },
            },
        };

        List<PassageRef> found = [];
        void Check()
        {
            found = PassageParser.Extract(box.Text ?? "", out var unknown);
            add.IsEnabled = found.Count > 0;
            info.Text = (box.Text ?? "").Trim().Length == 0 ? "" : found.Count == 0 ? L("Keine Bibelstelle erkannt.")
                : L("{0} Stellen erkannt", found.Count) + (unknown.Count > 0 ? "\n" + L("Nicht erkannt: {0}", string.Join(" · ", unknown.Take(5)) + (unknown.Count > 5 ? " …" : "")) : "");
        }
        box.TextChanged += (_, _) => Check();
        paste.Click += async (_, _) =>
        {
            if (TopLevel.GetTopLevel(this)?.Clipboard is { } cb && await Avalonia.Input.Platform.ClipboardExtensions.TryGetTextAsync(cb) is { Length: > 0 } text) box.Text = text;
        };
        add.Click += async (_, _) =>
        {
            var (verses, missing) = await ExpandPassagesAsync(found);
            fly.Hide();
            if (verses.Count == 0) { StatusText.Text = L("Keine Verse gefunden."); return; }
            AddToQueue(verses);
            if (missing.Count > 0) StatusText.Text = L("Nicht in dieser Übersetzung: {0}", string.Join(", ", missing.Distinct().Take(5)));
        };
        fly.ShowAt(anchor);
    }
}
