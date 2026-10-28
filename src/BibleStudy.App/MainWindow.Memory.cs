using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using BibleStudy.Core;
using BibleStudy.Core.Models;

namespace BibleStudy.App;

/// <summary>
/// Learning Bible verses by heart: add verses, then practice with gaps that grow as you get better
/// (spaced repetition, the same boxes as the vocabulary trainer). The text comes from the translation you read in.
/// </summary>
public partial class MainWindow
{
    const int MemoryTab = 24;

    LearnStore? _memoryStore;
    LearnStore Memory => _memoryStore ??= new LearnStore(Path.Combine(AppPaths.Root, "memory.json"));
    readonly StackPanel _memoryArea = new() { Spacing = 8 };
    List<string> _memoryQueue = [];
    int _memoryPos;

    static string MemoryKey(VerseRef r) => $"{r.Book}|{r.Chapter}|{r.Verse}";

    static VerseRef? MemoryRef(string key) => key.Split('|') is [var b, var c, var v] && int.TryParse(c, out var ci) && int.TryParse(v, out var vi) ? new VerseRef(b, ci, vi) : null;

    void BuildMemoryUi()
    {
        MemoryPanel.Children.Clear();
        MemoryPanel.Children.Add(new TextBlock { Text = L("VERSE AUSWENDIG LERNEN"), Classes = { "eyebrow" } });
        MemoryPanel.Children.Add(new TextBlock { Text = L("Füge Verse hinzu und übe sie mit Lücken, die mit dem Können wachsen. Gewusstes kommt später wieder, Vergessenes früher."), Classes = { "caption" }, TextWrapping = TextWrapping.Wrap });

        var now = DateTime.Now;
        var ids = Memory.Ids.ToList();
        var due = ids.Count(i => Memory.IsDue(i, now));
        MemoryPanel.Children.Add(new TextBlock { Text = L("{0} Verse · heute fällig: {1} · sicher gelernt: {2}", ids.Count, due, Memory.Known), Classes = { "caption" } });

        var row = new WrapPanel();
        var add = new Button { Content = _selected is int v ? L("＋ {0} hinzufügen", Label(new VerseRef(_curBook, _curChapter, v))) : L("＋ Angeklickten Vers hinzufügen"), IsEnabled = _selected is not null, Margin = new(0, 0, 8, 8) };
        add.Click += (_, _) =>
        {
            if (_selected is not int sv) return;
            Memory.Add(MemoryKey(new VerseRef(_curBook, _curChapter, sv)), DateTime.Now);
            BuildMemoryUi();
        };
        var practice = new Button { Content = due > 0 ? L("Üben ({0})", due) : L("Üben"), Classes = { "primary" }, IsEnabled = ids.Count > 0, Margin = new(0, 0, 8, 8) };
        practice.Click += (_, _) => StartMemory(all: due == 0);
        row.Children.Add(practice); row.Children.Add(add);
        MemoryPanel.Children.Add(row);

        _memoryArea.Children.Clear();
        MemoryPanel.Children.Add(_memoryArea);

        // the list of verses
        if (ids.Count == 0) MemoryPanel.Children.Add(new TextBlock { Text = L("Noch keine Verse. Klicke im Text einen Vers an und füge ihn hinzu."), Classes = { "caption" }, TextWrapping = TextWrapping.Wrap });
        foreach (var id in ids.OrderBy(i => Memory.Get(i)!.Due))
        {
            if (MemoryRef(id) is not { } r) continue;
            var card = Memory.Get(id)!;
            var open = FilterChip(Label(r), false, HomeView.ColorOf(r.Book), () => _ = GoToAsync(r.Book, r.Chapter, r.Verse), L("Springt zur Stelle"));
            var state = new TextBlock { Text = card.Due <= now ? L("fällig") : L("in {0} Tagen", Math.Max(1, (int)Math.Ceiling((card.Due - now).TotalDays))), Classes = { "caption" }, VerticalAlignment = VerticalAlignment.Center };
            var remove = new Button { Content = "✕", Classes = { "tool" }, Width = 28, Height = 28 };
            ToolTip.SetTip(remove, L("Entfernen"));
            var key = id;
            remove.Click += (_, _) => { Memory.Remove(key); BuildMemoryUi(); };
            var line = new DockPanel();
            DockPanel.SetDock(remove, Dock.Right);
            line.Children.Add(remove);
            line.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { open, state } });
            MemoryPanel.Children.Add(line);
        }
    }

    void StartMemory(bool all)
    {
        var now = DateTime.Now;
        _memoryQueue = Memory.Ids.Where(i => all || Memory.IsDue(i, now)).OrderBy(i => Memory.Get(i)!.Due).ToList();
        _memoryPos = 0;
        _ = ShowMemoryCardAsync();
    }

    async Task ShowMemoryCardAsync()
    {
        if (_memoryPos >= _memoryQueue.Count)
        {
            BuildMemoryUi();
            _memoryArea.Children.Add(Card(new TextBlock { Text = L("Fertig für jetzt – {0} Verse geübt.", _memoryQueue.Count), TextWrapping = TextWrapping.Wrap }));
            return;
        }
        var key = _memoryQueue[_memoryPos];
        _memoryArea.Children.Clear();
        if (MemoryRef(key) is not { } r || TranslationBox.SelectedItem is not TranslationInfo t) { _memoryPos++; await ShowMemoryCardAsync(); return; }
        string text;
        try
        {
            var ch = await _provider.GetChapterAsync(t.Id, r.Book, r.Chapter);
            text = string.Concat(ch.Verses.FirstOrDefault(x => x.Number == r.Verse)?.Segments.Where(s => s.NoteId is null).Select(s => s.Text) ?? []).Trim();
        }
        catch (Exception ex) { _memoryArea.Children.Add(Card(new TextBlock { Text = L("Fehler: ") + ex.Message, TextWrapping = TextWrapping.Wrap })); return; }
        if (text.Length == 0) { _memoryPos++; await ShowMemoryCardAsync(); return; }

        var box = Memory.Get(key)?.Box ?? 0;
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var hidden = HiddenWords(words, key, box);
        var gapped = string.Join(" ", words.Select((w, i) => hidden.Contains(i) ? Gap(w) : w));

        var title = new TextBlock { Text = $"{Label(r)} · {t.ShortName}", FontWeight = FontWeight.SemiBold, HorizontalAlignment = HorizontalAlignment.Center };
        var body = new TextBlock { Text = gapped, FontSize = 20, FontFamily = ScriptureFont, TextWrapping = TextWrapping.Wrap };
        var reveal = new Button { Content = L("Aufdecken"), Classes = { "primary" }, HorizontalAlignment = HorizontalAlignment.Center };
        var rate = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center, IsVisible = false };
        var knew = new Button { Content = L("Gewusst"), Classes = { "primary" } };
        var again = new Button { Content = L("Noch nicht") };
        rate.Children.Add(again); rate.Children.Add(knew);
        reveal.Click += (_, _) => { body.Text = text; reveal.IsVisible = false; rate.IsVisible = true; };
        async void Rate(bool ok)
        {
            Memory.Answer(key, ok, DateTime.Now);
            if (!ok) _memoryQueue.Add(key);
            _memoryPos++;
            await ShowMemoryCardAsync();
        }
        knew.Click += (_, _) => Rate(true);
        again.Click += (_, _) => Rate(false);

        var card = Card(new StackPanel
        {
            Spacing = 10,
            Children = { new TextBlock { Text = L("Karte {0} von {1}", _memoryPos + 1, _memoryQueue.Count), Classes = { "caption" }, HorizontalAlignment = HorizontalAlignment.Center }, title, body, reveal, rate },
        });
        _memoryArea.Children.Add(card);
        AnimateIn(card, 24, 0);
    }

    /// <summary>More words are hidden the better the verse is known; which ones stays the same for a verse and level.</summary>
    static HashSet<int> HiddenWords(string[] words, string key, int box)
    {
        var share = box switch { 0 => 0.25, 1 => 0.45, 2 => 0.65, 3 => 0.85, _ => 1.0 };
        var count = (int)Math.Round(words.Length * share);
        var rng = new Random(key.GetHashCode(StringComparison.Ordinal) ^ box);
        return Enumerable.Range(0, words.Length).OrderBy(_ => rng.Next()).Take(count).ToHashSet();
    }

    /// <summary>Keeps the first letter and punctuation as a hint: "Gott" becomes "G___".</summary>
    static string Gap(string word)
    {
        var letters = word.Count(char.IsLetterOrDigit);
        if (letters == 0) return word;
        var first = word.First(char.IsLetterOrDigit);
        var tail = new string(word.Reverse().TakeWhile(c => !char.IsLetterOrDigit(c)).Reverse().ToArray());
        return first + new string('_', Math.Max(1, letters - 1)) + tail;
    }
}
