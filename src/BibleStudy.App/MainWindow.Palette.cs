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
    string _paletteText = "";   // what the list was made for: Enter before it caught up with the typing goes by the text itself

    void InitPalette()
    {
        _palette.PlacementTarget = QuickJumpBox; _palette.Placement = PlacementMode.BottomEdgeAlignedRight; _palette.IsLightDismissEnabled = false;
        _palette.VerticalOffset = 8;
        var frame = new Border { Width = 540, CornerRadius = new(12), BorderThickness = new(1), Padding = new(6), Child = _paletteList };
        frame.Bind(Border.BackgroundProperty, this.GetResourceObservable("BsSurface"));
        frame.Bind(Border.BorderBrushProperty, this.GetResourceObservable("BsBorder"));
        _palette.Child = frame;
        ((Panel)QuickJumpBox.Parent!).Children.Add(_palette);

        // a click anywhere else closes the list (a click on an empty area does not move the focus, so the focus-lost rule below never fired)
        AddHandler(PointerPressedEvent, (object? _, PointerPressedEventArgs e) =>
        {
            if (!_palette.IsOpen || QuickJumpBox.IsPointerOver || _paletteList.IsPointerOver) return;
            _palette.IsOpen = false;
            if (QuickJumpBox.IsFocused) Focus();
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
        Deactivated += (_, _) => _palette.IsOpen = false;   // switching to another program also closes it
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
                    if (text != _paletteText) item = null;
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
        yield return C(L("Rundgang starten"), () => { DispatcherTimer.RunOnce(StartTour, TimeSpan.FromMilliseconds(350)); return Task.CompletedTask; });   // after the command list has closed: two popups at once confuse the renderer
        yield return C(L("Kapitel-Bericht"), ShowChapterReportAsync);
        yield return C(L("Graph oder Ansicht als Bild in eigenem Fenster"), () => { PopOutCurrentView(); return Task.CompletedTask; });
        yield return C(L("Patch Notes"), ShowPatchNotesAsync);
        yield return C(L("Projekt auf GitHub öffnen"), () => { OpenUrl(ProjectUrl); return Task.CompletedTask; });
        yield return C(L("Einstellungen"), () => Work("Mehr", SettingsTab));
        yield return C(L("Neue Notiz"), () => { OpenWork(NavGroups.First(g => g.Name == "Dokumente"), KnowledgeTab); OpenDocument(new Document(0, "", "", null, false, DateTime.Now, [])); return Task.CompletedTask; });
        yield return C(L("Lesepläne"), () => Work("Planen", PlanTab));
        yield return C(L("Gebete"), () => Work("Planen", PrayerTab));
        yield return C(L("Leseliste"), () => Work("Planen", 6));
        yield return C(L("Sprachen"), () => { OpenWork(GroupOf(LearnTab)!, LearnTab); return Task.CompletedTask; });   // its group differs between the normal and the compact look
        // the trash holds documents as well, but lives in the explorer of the marks: from here it is found without knowing that
        yield return C(L("Papierkorb"), () => { OpenWork(GroupOf(ExplorerTab)!, ExplorerTab); BuildExplorer(); ExOpenTrash(); return Task.CompletedTask; });
        yield return C(L("Statistik"), () => Work("Planen", StatsTab));
        yield return C(L("Materialien"), () => Work("Material", MaterialsTab));
        if (CommentariesOn) yield return C(L("Kommentare"), () => { OpenCommentary(_selected is int v ? new VerseRef(_curBook, _curChapter, v) : null); return Task.CompletedTask; });
        yield return C(L("Geteilte Ansicht an/aus"), () => { SplitTopBtn.IsChecked = SplitTopBtn.IsChecked != true; return Task.CompletedTask; });
        yield return C(L("Timer stellen"), () => { ShowTimerFlyout(ClockChip.IsVisible ? ClockChip : StatusBar); return Task.CompletedTask; });
        yield return C(L("Satzdiagramm"), () => Side(SentenceTab));
        yield return C(L("Perikopen-Analyse"), () => Side(PericopeTab));
        yield return C(L("Chiasmus-Finder"), ShowChiasmFinderAsync);
        yield return C(L("Auswendig lernen"), () => Work("Planen", MemoryTab));
        yield return C(L("Gemeinsam studieren"), () => Side(CollabTab));
        yield return C(L("Verweis-Netz AT ↔ NT"), async () => { await ShowGraphAsync(); GraphPane.ShowOtNt(); });
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
        // commands of allowed plugins
        foreach (var (plugin, command) in _plugins.Commands())
        {
            var p = plugin; var c = command;
            yield return new PaletteItem(L("Plugin"), c.Title, p.Manifest.Name, () => RunPluginCommandAsync(p, c));
        }
    }

    async Task RefreshPaletteAsync()
    {
        var version = ++_paletteVersion;
        var text = QuickJumpBox.Text?.Trim() ?? "";
        // while typing fast only the last letter starts the search through documents, notes and marks (each key used to start one)
        if (text.Length > 0) { await Task.Delay(140); if (version != _paletteVersion) return; }
        var items = new List<PaletteItem>();

        if (text.Length == 0) items.AddRange(Commands());   // all of them: the list scrolls
        else
        {
            if (PassageParser.Parse(text, out var parsed) is { Count: > 0 } p)
                items.Add(new(L("Stelle"), string.Join("; ", p.Select(x => Label(new VerseRef(x.Book, x.Chapter, x.FromVerse ?? 1)))), L("Gehe zu"), () => GoToAsync(p[0].Book, p[0].Chapter, p[0].FromVerse)));
            else if (DocumentLinks.BookId(text) is { } book && _books.FirstOrDefault(b => b.Id == book) is { } bk)
                items.Add(new(L("Stelle"), bk.CommonName, L("Gehe zu"), () => GoToAsync(book, 1)));

            // a command is found by its title, by the plugin it belongs to, and by its category: "plugin" lists all plugin commands, "befehl" all commands
            bool ByKind(PaletteItem c) => text.Length >= 3 && (c.Kind.StartsWith(text, StringComparison.CurrentCultureIgnoreCase) || text.StartsWith(c.Kind, StringComparison.CurrentCultureIgnoreCase) && text.Length <= c.Kind.Length + 2);
            items.AddRange(Commands().Where(c => c.Title.Contains(text, StringComparison.CurrentCultureIgnoreCase) || c.Sub.Contains(text, StringComparison.CurrentCultureIgnoreCase) || ByKind(c)).Take(25));

            // everything of your own: documents, notes, highlights (with their notes and tags), prayers, to-dos, own cross references
            var vault = _vault;
            var own = await Task.Run(() => OwnSearch.Search(vault, text, 8));   // off the window: a big vault must not make the typing stutter
            if (version != _paletteVersion) return;
            foreach (var h in own)
            {
                var hit = h;
                items.Add(new(OwnKindLabel(hit.Kind), OwnTitle(hit), Shorten(hit.Snippet, 80), () => OpenOwn(hit)));
            }

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
        _paletteItems = items; _paletteText = QuickJumpBox.Text?.Trim() ?? "";
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
