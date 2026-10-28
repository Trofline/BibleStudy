using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using BibleStudy.Core.Models;
using BibleStudy.Core.Providers;
using BibleStudy.Core.Search;
using BibleStudy.Core.Storage;

namespace BibleStudy.App;

/// <summary>Reference library: free commentaries (Matthew Henry, JFB, Keil-Delitzsch, Tyndale …) and the Bible dictionary of people and places.</summary>
public partial class MainWindow
{
    const int CommentaryTab = 16, LexiconTab = 17;

    IReadOnlyList<CommentaryInfo> _commentaries = [];
    (string Id, string Book, int Chapter)? _shownCommentary;
    readonly List<(int Verse, Border Card)> _commentaryCards = [];
    IReadOnlyList<LexiconItem>? _lexIndex;
    (string Book, int Chapter)? _lexChapter;
    bool _libraryLoading;

    void InitLibrary()
    {
        CommentaryBox.DisplayMemberBinding = new Avalonia.Data.Binding(nameof(CommentaryInfo.Name));
        CommentaryBox.SelectionChanged += async (_, _) =>
        {
            if (_libraryLoading || CommentaryBox.SelectedItem is not CommentaryInfo c) return;
            _settings.Commentary = c.Id; SaveSettings(); _shownCommentary = null;
            await ShowCommentaryAsync();
        };
        LexSearchBox.TextChanged += async (_, _) => await LexSearchAsync();
    }

    /// <summary>Called when the chapter or the verse changes.</summary>
    void LibraryFollowReader()
    {
        if (TabActive(CommentaryTab)) _ = ShowCommentaryAsync();
        if (TabActive(LexiconTab)) _ = ShowLexiconAsync();
    }

    // ---- commentaries -------------------------------------------------------------------------------------------------

    async Task ShowCommentaryAsync()
    {
        try
        {
            if (_commentaries.Count == 0)
            {
                CommentaryInfo.Text = L("Lade Kommentarliste …");
                _commentaries = await _hello.GetCommentariesAsync();
                _libraryLoading = true;
                CommentaryBox.ItemsSource = _commentaries;
                CommentaryBox.SelectedItem = _commentaries.FirstOrDefault(c => c.Id == _settings.Commentary) ?? _commentaries.FirstOrDefault();
                _libraryLoading = false;
            }
            if (CommentaryBox.SelectedItem is not CommentaryInfo c || _curBook == "") return;
            if (_shownCommentary != (c.Id, _curBook, _curChapter))
            {
                _shownCommentary = (c.Id, _curBook, _curChapter);
                CommentaryPanel.Children.Clear(); _commentaryCards.Clear();
                CommentaryInfo.Text = L("Lade {0} zu {1} {2} …", c.Name, BookName(_curBook), _curChapter);
                var chapter = await _hello.GetCommentaryAsync(c.Id, _curBook, _curChapter);
                if (_shownCommentary != (c.Id, _curBook, _curChapter)) return;
                CommentaryInfo.Text = chapter is null || (chapter.Notes.Count == 0 && chapter.Introduction.Length == 0)
                    ? L("{0} hat zu {1} {2} keine Anmerkungen. Wähle oben einen anderen Kommentar.", c.Name, BookName(_curBook), _curChapter)
                    : L("{0} zu {1} {2} · {3} · Englisch", c.Name, BookName(_curBook), _curChapter, (c.LicenseUrl.Contains("publicdomain") ? "gemeinfrei" : L("Lizenz ") + c.LicenseUrl));
                if (chapter is null) return;
                if (chapter.Introduction.Length > 0)
                    CommentaryPanel.Children.Add(new Expander { Header = L("Einleitung"), HorizontalAlignment = HorizontalAlignment.Stretch, Content = new SelectableTextBlock { Text = chapter.Introduction, TextWrapping = TextWrapping.Wrap } });
                foreach (var note in chapter.Notes) CommentaryPanel.Children.Add(CommentaryCard(c, note));
            }
            HighlightCommentary();
        }
        catch (Exception ex) { CommentaryInfo.Text = L("Kommentar nicht verfügbar: ") + ex.Message; }
    }

    Border CommentaryCard(CommentaryInfo c, CommentaryNote note)
    {
        var r = new VerseRef(_curBook, _curChapter, note.Verse);
        var go = new Button { Classes = { "ghost" }, Padding = new(0), Content = L("Vers {0}", note.Verse), FontWeight = FontWeight.SemiBold, Foreground = Res("BsAccent") };
        go.Click += async (_, _) => await GoToAsync(r.Book, r.Chapter, r.Verse);
        var cite = new Button { Classes = { "ghost" }, Content = L("In Dokument zitieren"), FontSize = 12 };
        cite.Click += (_, _) => CiteIntoDocument(L("> {0}\n> — {1} zu {2}\n\n", note.Text.Replace("\n\n", L("\n>\n> ")), c.Name, Label(r)), $"{c.Name}: {Label(r)}", r);
        var head = new DockPanel { Children = { cite, go } };
        DockPanel.SetDock(cite, Dock.Right);
        var card = new Border { Classes = { "card" }, Child = new StackPanel { Spacing = 6, Children = { head, new SelectableTextBlock { Text = note.Text, TextWrapping = TextWrapping.Wrap, LineHeight = 21 } } } };
        _commentaryCards.Add((note.Verse, card));
        return card;
    }

    /// <summary>Marks the note that covers the selected verse and scrolls to it.</summary>
    void HighlightCommentary()
    {
        if (_selected is not int v || _commentaryCards.Count == 0) return;
        var target = _commentaryCards.Where(x => x.Verse <= v).Select(x => x.Card).LastOrDefault() ?? _commentaryCards[0].Card;
        foreach (var (_, card) in _commentaryCards) card.BorderBrush = card == target ? Res("BsAccent") : Res("BsBorder");
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (target.TranslatePoint(new Point(0, 0), CommentaryPanel) is { } p) CommentaryScroll.Offset = new Vector(0, Math.Max(0, p.Y - 8));
        }, Avalonia.Threading.DispatcherPriority.Background);
    }

    /// <summary>Quote into the open document, or start a new document with the quote.</summary>
    void CiteIntoDocument(string quote, string newTitle, VerseRef? r)
    {
        if (_doc is not null) { SideTabs.SelectedIndex = KnowledgeTab; InsertIntoDoc(quote, null); return; }
        SideTabs.SelectedIndex = KnowledgeTab;
        OpenDocument(new Document(0, newTitle, quote, null, false, DateTime.Now, r is { } x ? AutoTags(x).ToList() : []));
    }

    // ---- dictionary of people and places -------------------------------------------------------------------------------

    async Task ShowLexiconAsync()
    {
        try
        {
            if (_lexIndex is null)
            {
                LexInfo.Text = L("Lade Lexikon (einmalig) …");
                _lexIndex = await _hello.GetLexiconIndexAsync();
                LexInfo.Text = L("{0} Personen und {1} Orte · Texte englisch · {2}", _lexIndex.Count(x => x.Kind == "people"), _lexIndex.Count(x => x.Kind == "places"), HelloAoProvider.LexiconAttribution);
            }
            if (_curBook != "" && _lexChapter != (_curBook, _curChapter))
            {
                _lexChapter = (_curBook, _curChapter);
                LexChapterChips.Children.Clear();
                var items = await _hello.GetChapterLexiconAsync(_curBook, _curChapter);
                if (_lexChapter != (_curBook, _curChapter)) return;
                LexChapterChips.Children.Add(new TextBlock { Text = L("In {0} {1}:", BookName(_curBook), _curChapter), Classes = { "caption" }, VerticalAlignment = VerticalAlignment.Center, Margin = new(0, 0, 6, 4) });
                foreach (var (item, _) in items.Take(40))
                {
                    var it = item;
                    LexChapterChips.Children.Add(FilterChip((item.Kind == "places" ? "📍 " : "") + item.Name, false, null, () => _ = ShowLexEntryAsync(it)));
                }
                if (items.Count == 0) LexChapterChips.Children.Add(new TextBlock { Text = L("keine Einträge"), Classes = { "caption" } });
            }
            if (string.IsNullOrWhiteSpace(LexSearchBox.Text) && LexPanel.Children.Count == 0)
                LexPanel.Children.Add(new TextBlock { Text = L("Suche oben nach einer Person oder einem Ort, oder klicke auf einen Namen aus dem aktuellen Kapitel."), Classes = { "caption" }, TextWrapping = TextWrapping.Wrap });
        }
        catch (Exception ex) { LexInfo.Text = L("Lexikon nicht verfügbar: ") + ex.Message; }
    }

    async Task LexSearchAsync()
    {
        var q = SearchQuery.Fold(LexSearchBox.Text?.Trim() ?? "", true);
        if (q.Length < 2) return;
        _lexIndex ??= await _hello.GetLexiconIndexAsync();
        var hits = _lexIndex.Where(x => SearchQuery.Fold(x.Name, true).Contains(q))
            .OrderBy(x => SearchQuery.Fold(x.Name, true).StartsWith(q) ? 0 : 1).ThenByDescending(x => x.References).Take(60).ToList();
        LexPanel.Children.Clear();
        foreach (var h in hits)
        {
            var item = h;
            var b = new Button { Classes = { "ghost" }, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left,
                Content = L("{0}  {1}   ·   {2} Stellen", (h.Kind == "places" ? "📍" : "👤"), h.Name, h.References) };
            b.Click += async (_, _) => await ShowLexEntryAsync(item);
            LexPanel.Children.Add(b);
        }
        if (hits.Count == 0) LexPanel.Children.Add(new TextBlock { Text = L("Nichts gefunden. Die Namen sind englisch (z. B. Nebuchadnezzar, Jerusalem, Moses)."), Classes = { "caption" }, TextWrapping = TextWrapping.Wrap });
    }

    async Task ShowLexEntryAsync(LexiconItem item)
    {
        LexPanel.Children.Clear();
        LexPanel.Children.Add(new TextBlock { Text = L("Lade …"), Classes = { "caption" } });
        try
        {
            var e = await _hello.GetLexiconEntryAsync(item.Kind, item.Id);
            LexPanel.Children.Clear();
            if (e is null) return;
            LexPanel.Children.Add(new TextBlock { Text = e.Name, Classes = { "h2" } });
            var kind = e.Kind == "places" ? L("Ort") + (e.FeatureType.Length > 0 ? $" ({e.FeatureType})" : "") : L("Person");
            LexPanel.Children.Add(new TextBlock { Text = kind + (e.AlsoCalled.Count > 0 ? L(" · auch: ") + string.Join(", ", e.AlsoCalled) : ""), Classes = { "caption" } });
            var text = string.Join("\n\n", e.Description);
            LexPanel.Children.Add(text.Length > 0 ? new SelectableTextBlock { Text = text, TextWrapping = TextWrapping.Wrap, LineHeight = 21 } : new TextBlock { Text = L("Kein Lexikontext vorhanden."), Classes = { "caption" } });
            var cite = new Button { Content = L("In Dokument zitieren"), HorizontalAlignment = HorizontalAlignment.Left };
            cite.Click += (_, _) => CiteIntoDocument(L("> {0}\n> — Lexikon (Easton/Theographic): {1}\n\n", text.Replace("\n\n", L("\n>\n> ")), e.Name), L("Lexikon: {0}", e.Name), null);
            LexPanel.Children.Add(cite);
            if (e.Related.Count > 0)
            {
                LexPanel.Children.Add(Eyebrow(L("Verwandt")));
                var rel = new WrapPanel();
                foreach (var r in e.Related.Where(r => r.Id.Length > 0).Take(30)) { var it = r; rel.Children.Add(FilterChip(r.Name, false, null, () => _ = ShowLexEntryAsync(it))); }
                LexPanel.Children.Add(rel);
            }
            if (e.References.Count > 0)
            {
                LexPanel.Children.Add(Eyebrow(L("Bibelstellen ({0})", e.References.Count)));
                var refs = new WrapPanel();
                foreach (var r in e.References.Take(80))
                {
                    var x = r;
                    refs.Children.Add(FilterChip($"{ShortBookId(r.Book)} {r.Chapter},{r.Verse}", false, HomeView.ColorOf(r.Book), () => _ = GoToAsync(x.Book, x.Chapter, x.Verse), Label(r)));
                }
                LexPanel.Children.Add(refs);
                if (e.References.Count > 80) LexPanel.Children.Add(new TextBlock { Text = L("… und {0} weitere", e.References.Count - 80), Classes = { "caption" } });
            }
        }
        catch (Exception ex) { LexPanel.Children.Clear(); LexPanel.Children.Add(new TextBlock { Text = L("Eintrag nicht verfügbar: ") + ex.Message, TextWrapping = TextWrapping.Wrap }); }
    }

    string ShortBookId(string id) => _books.FirstOrDefault(b => b.Id == id) is { } b ? ShortBook(b) : id;
}
