using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using BibleStudy.Core.Models;
using BibleStudy.Core.Search;
using BibleStudy.Core.Storage;

namespace BibleStudy.App;

/// <summary>
/// One box for everything (Ctrl+K): passages, books, commands, documents, notes, prayers, Strong's numbers, and "search the text".
/// Results appear as you type; arrow keys and Enter pick one.
/// </summary>
public partial class MainWindow
{
    sealed record PaletteItem(string Kind, string Title, string Sub, Func<Task> Run);

    readonly Popup _palette = new();
    readonly ListBox _paletteList = new() { MaxHeight = 440 };
    List<PaletteItem> _paletteItems = [];
    int _paletteVersion;

    void InitPalette()
    {
        _palette.PlacementTarget = QuickJumpBox; _palette.Placement = PlacementMode.BottomEdgeAlignedRight; _palette.IsLightDismissEnabled = false;
        _palette.VerticalOffset = 8;
        var frame = new Border { Width = 540, CornerRadius = new(12), BorderThickness = new(1), Padding = new(6), Child = _paletteList };
        frame.Bind(Border.BackgroundProperty, this.GetResourceObservable("BsSurface"));
        frame.Bind(Border.BorderBrushProperty, this.GetResourceObservable("BsBorder"));
        _palette.Child = frame;
        ((Panel)QuickJumpBox.Parent!).Children.Add(_palette);

        QuickJumpBox.TextChanged += (_, _) => _ = RefreshPaletteAsync();
        QuickJumpBox.GotFocus += (_, _) => _ = RefreshPaletteAsync();
        QuickJumpBox.LostFocus += (_, _) => DispatcherTimer.RunOnce(() => { if (!QuickJumpBox.IsFocused && !_paletteList.IsPointerOver) _palette.IsOpen = false; }, TimeSpan.FromMilliseconds(180));
        QuickJumpBox.AddHandler(KeyDownEvent, async (object? _, KeyEventArgs e) =>
        {
            switch (e.Key)
            {
                case Key.Down: e.Handled = true; MovePalette(1); break;
                case Key.Up: e.Handled = true; MovePalette(-1); break;
                case Key.Escape: e.Handled = true; QuickJumpBox.Text = ""; _palette.IsOpen = false; Focus(); break;
                case Key.Enter:
                    e.Handled = true;
                    var item = _paletteList.SelectedIndex is >= 0 and var i && i < _paletteItems.Count ? _paletteItems[i] : null;
                    var text = QuickJumpBox.Text?.Trim() ?? "";
                    _palette.IsOpen = false; QuickJumpBox.Text = ""; Focus();
                    if (item is not null) await item.Run(); else await QuickJumpAsync(text);
                    break;
            }
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        _paletteList.PointerReleased += async (_, _) =>
        {
            if (_paletteList.SelectedItem is not null && _paletteList.SelectedIndex is >= 0 and var i && i < _paletteItems.Count)
            {
                var item = _paletteItems[i];
                _palette.IsOpen = false; QuickJumpBox.Text = ""; Focus();
                await item.Run();
            }
        };
    }

    void MovePalette(int step)
    {
        if (_paletteItems.Count == 0) return;
        _paletteList.SelectedIndex = Math.Clamp(_paletteList.SelectedIndex + step, 0, _paletteItems.Count - 1);
        _paletteList.ScrollIntoView(_paletteList.SelectedIndex);
    }

    IEnumerable<PaletteItem> Commands()
    {
        PaletteItem C(string title, Func<Task> run) => new(L("Befehl"), title, "", run);
        Task Work(string group, int? tab = null) { OpenWork(NavGroups.First(g => g.Name == group), tab); return Task.CompletedTask; }
        Task Side(int tab) { SetPanelOpen(true); SideTabs.SelectedIndex = tab; return Task.CompletedTask; }
        yield return C(L("Startseite"), () => { ShowHome(); return Task.CompletedTask; });
        yield return C(L("Einstellungen"), () => Work("Mehr", SettingsTab));
        yield return C(L("Neue Notiz"), () => { OpenWork(NavGroups.First(g => g.Name == "Dokumente"), KnowledgeTab); OpenDocument(new Document(0, "", "", null, false, DateTime.Now, [])); return Task.CompletedTask; });
        yield return C(L("Lesepläne"), () => Work("Planen", PlanTab));
        yield return C(L("Gebete"), () => Work("Planen", PrayerTab));
        yield return C(L("Leseliste"), () => Work("Planen", 6));
        yield return C(L("Sprachen"), () => Work("Planen", LearnTab));
        yield return C(L("Auswendig lernen"), () => Work("Planen", MemoryTab));
        yield return C(L("Gemeinsam studieren"), () => Side(CollabTab));
        yield return C(L("Verweis-Netz AT ↔ NT"), async () => await ShowRefNetAsync());
        yield return C(L("Predigt-Editor"), () => Work("Predigt", SermonTab));
        yield return C(L("Analyse"), () => Side(AnalysisTab));
        yield return C(L("Vers-Übersicht öffnen"), () => Side(GuideTab));
        yield return C(L("Wissensgraph"), () => { ShowKnowledgeGraph(); return Task.CompletedTask; });
        yield return C(L("Design wechseln (hell/dunkel)"), () => { ThemeBtn.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); return Task.CompletedTask; });
        yield return C(L("Fokus-Modus (nur Text)"), () => { ToggleFocusMode(); return Task.CompletedTask; });
        yield return C(L("Vers mit Stelle kopieren"), CopyVerseAsync);
        foreach (var name in BuiltInLayouts)
        {
            var n = name;
            yield return C(L("Layout laden: {0}", LayoutLabel(n)), () => { ApplyLayout(FindLayout(n)!, n); return Task.CompletedTask; });
        }
    }

    async Task RefreshPaletteAsync()
    {
        var version = ++_paletteVersion;
        var text = QuickJumpBox.Text?.Trim() ?? "";
        var items = new List<PaletteItem>();

        if (text.Length == 0) items.AddRange(Commands().Take(8));
        else
        {
            if (PassageParser.Parse(text, out var parsed) is { Count: > 0 } p)
                items.Add(new(L("Stelle"), string.Join("; ", p.Select(x => Label(new VerseRef(x.Book, x.Chapter, x.FromVerse ?? 1)))), L("Gehe zu"), () => GoToAsync(p[0].Book, p[0].Chapter, p[0].FromVerse)));
            else if (DocumentLinks.BookId(text) is { } book && _books.FirstOrDefault(b => b.Id == book) is { } bk)
                items.Add(new(L("Stelle"), bk.CommonName, L("Gehe zu"), () => GoToAsync(book, 1)));

            items.AddRange(Commands().Where(c => c.Title.Contains(text, StringComparison.CurrentCultureIgnoreCase)).Take(5));

            foreach (var d in _vault.GetDocuments(text).Take(6))
            {
                var doc = d;
                items.Add(new(L("Dokument"), doc.Title, Shorten(doc.Body.ReplaceLineEndings(" ").Replace("#", "").Replace("**", "").Trim(), 70), () =>
                {
                    OpenWork(NavGroups.First(g => g.Name == "Dokumente"), KnowledgeTab); OpenDocument(doc); return Task.CompletedTask;
                }));
            }
            foreach (var n in _vault.GetNotes().Where(n => n.Text.Contains(text, StringComparison.CurrentCultureIgnoreCase)).Take(5))
            {
                var note = n;
                items.Add(new(L("Notiz"), Label(note.Ref), Shorten(note.Text.ReplaceLineEndings(" "), 70), () => GoToAsync(note.Ref.Book, note.Ref.Chapter, note.Ref.Verse)));
            }
            foreach (var pr in _vault.GetPrayers().Where(x => x.Text.Contains(text, StringComparison.CurrentCultureIgnoreCase)).Take(3))
                items.Add(new(L("Gebet"), Shorten(pr.Text.ReplaceLineEndings(" "), 70), "", () => { OpenWork(NavGroups.First(g => g.Name == "Planen"), PrayerTab); return Task.CompletedTask; }));

            items.Add(new(L("Suche"), L("Im Bibeltext suchen: {0}", text), "", () => QuickJumpAsync(text)));
        }
        ShowPalette(items);

        // Strong's entries come from a dictionary that may need a moment: add them when they arrive
        if (text.Length >= 2 && (System.Text.RegularExpressions.Regex.IsMatch(text, @"^[GgHh]\d{1,4}$") || text.Any(c => c > 0x370)))
        {
            try
            {
                var found = await _strongs.SearchAsync(text, 4);
                if (version != _paletteVersion) return;
                foreach (var e in found.Reverse())
                {
                    var entry = e;
                    items.Insert(Math.Min(1, items.Count), new(L("Wort"), $"{entry.Id}  {entry.Lemma}", Shorten(Mean(entry.Definition), 70), () => { SetPanelOpen(true); SideTabs.SelectedIndex = StrongTab; return ShowStrongDetailAsync(entry); }));
                }
                ShowPalette(items);
            }
            catch { /* offline: the other results stay */ }
        }

        // people and places of the Bible dictionary (like a fact book): a click opens the entry
        if (text.Length >= 3)
        {
            try
            {
                var index = _lexIndex ??= await _hello.GetLexiconIndexAsync();
                if (version != _paletteVersion) return;
                var found = index.Where(i => i.Name.Contains(text, StringComparison.CurrentCultureIgnoreCase)).OrderByDescending(i => i.References).Take(3).Reverse().ToList();
                foreach (var it in found)
                {
                    var item = it;
                    items.Insert(Math.Min(1, items.Count), new(item.Kind == "places" ? L("Ort") : L("Person"), item.Name, L("{0} Erwähnungen · öffnet das Lexikon", item.References),
                        () => { OpenWork(NavGroups.First(g => g.Name == "Bücher"), LexiconTab); return ShowLexEntryAsync(item); }));
                }
                if (found.Count > 0) ShowPalette(items);
            }
            catch { /* offline: the other results stay */ }
        }
    }

    void ShowPalette(List<PaletteItem> items)
    {
        _paletteItems = items;
        _paletteList.ItemsSource = items.Select(i =>
        {
            var badge = new Border { Width = 74, CornerRadius = new(6), Padding = new(6, 1), VerticalAlignment = VerticalAlignment.Center, Margin = new(0, 0, 10, 0), Child = new TextBlock { Text = i.Kind, FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center } };
            badge.Bind(Border.BackgroundProperty, this.GetResourceObservable("BsSurface2"));
            var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { new TextBlock { Text = i.Title, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 400 } } };
            if (i.Sub.Length > 0) texts.Children.Add(new TextBlock { Text = i.Sub, Classes = { "caption" }, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 400 });
            return new StackPanel { Orientation = Orientation.Horizontal, Children = { badge, texts }, Margin = new(2, 3) };
        }).ToList();
        _paletteList.SelectedIndex = items.Count > 0 ? 0 : -1;
        _palette.IsOpen = items.Count > 0 && QuickJumpBox.IsFocused;
    }
}
