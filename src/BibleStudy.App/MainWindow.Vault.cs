using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using BibleStudy.Core.Models;
using BibleStudy.Core.Storage;

namespace BibleStudy.App;

/// <summary>Vaults, highlights, collection (folders/tags), queue and to-dos.</summary>
public partial class MainWindow
{
    const int SettingsTab = 12;
    static readonly string[] PresetColors = ["#FFD54F", "#FFA95C", "#F26D6D", "#E983B8", "#B28DF0", "#6FA8F2", "#55C2C2", "#7ACB7A"];

    VaultStore _vault = null!;
    List<Mark> _chapterMarks = [];
    Dictionary<int, string> _wholeMarks = [];
    (int Verse, string TranslationId, int Start, int End, string Text)? _selection;
    Mark? _editMark;
    string _editColor = PresetColors[0];
    bool _vaultUiLoading;

    // ---- vaults -----------------------------------------------------------------------------------------------------

    VaultStore OpenVault(string name)
    {
        var path = Path.Combine(_root, "vaults", name, "vault.db");
        var fresh = !File.Exists(path);
        var vault = new VaultStore(path, name);
        if (fresh && name == "Standard") vault.ImportLegacy(Path.Combine(_root, "user.db"), PresetColors[0]);
        return vault;
    }

    List<string> VaultNames()
    {
        var dir = Path.Combine(_root, "vaults");
        var names = Directory.Exists(dir) ? Directory.GetDirectories(dir).Select(Path.GetFileName).OfType<string>().ToList() : [];
        if (!names.Contains(_vault.Name)) names.Add(_vault.Name);
        return [.. names.Order(StringComparer.CurrentCultureIgnoreCase)];
    }

    async Task SwitchVaultAsync(string name)
    {
        _vault = OpenVault(name);
        _settings.Vault = name; SaveSettings();
        _read = _vault.GetReadChapters();
        _sermonVault = null; if (TabActive(PrayerTab)) BuildPrayerUi(); CloseEditor(); CloseDocument(); RefreshLists(); RefreshDocs(); RefreshQueue(); RefreshTodos(); RefreshVaultUi();
        if (_windows.TryGetValue("group:Dokumente", out var docs)) docs.SelectAction?.Invoke(docs.Current);
        if (_windows.TryGetValue("group:Predigt", out var sermon)) sermon.SelectAction?.Invoke(sermon.Current);
        if (Home.IsVisible) Home.Show(_books, _read, Home.OpenBook);
        await ShowChapterAsync(_selected);
    }

    async Task CreateVaultAsync()
    {
        var name = NewVaultBox.Text?.Trim() ?? "";
        if (name.Length == 0 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) { VaultInfo.Text = L("Ungültiger Name (keine Sonderzeichen wie \\ / : * ? \" < > |)."); return; }
        NewVaultBox.Text = "";
        await SwitchVaultAsync(name);
    }

    void RefreshVaultUi()
    {
        _vaultUiLoading = true;
        VaultBox.ItemsSource = VaultNames();
        VaultBox.SelectedItem = _vault.Name;
        if (VaultQuickBox is not null) { VaultQuickBox.ItemsSource = VaultNames(); VaultQuickBox.SelectedItem = _vault.Name; }
        _vaultUiLoading = false;
        VaultInfo.Text = L("Aktiver Vault: {0}", _vault.Name);
        if (VaultTopText is not null) VaultTopText.Text = _vault.Name;
    }

    void InitVault()
    {
        MarkBtn.Click += (_, _) => ShowColorFlyout();
        FolderFilterBox.SelectionChanged += (_, _) =>
        {
            if (_vaultUiLoading) return;
            DeleteFolderBtn.IsVisible = FolderFilterBox.SelectedItem is Folder;
            ApplyCollectionFilter();
        };
        TagFilterBox.SelectionChanged += (_, _) => { if (!_vaultUiLoading) ApplyCollectionFilter(); };
        DeleteFolderBtn.Click += (_, _) => { if (FolderFilterBox.SelectedItem is Folder f) { _vault.DeleteFolder(f.Id); RefreshCollection(); } };
        NewFolderBox.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter || string.IsNullOrWhiteSpace(NewFolderBox.Text)) return;
            _vault.AddFolder(NewFolderBox.Text); NewFolderBox.Text = ""; NewFolderBox.IsVisible = false; RefreshCollection();
        };
        MarksList.SelectionChanged += async (_, _) =>
        {
            if (MarksList.SelectedItem is not Entry { Id: long id } e) return;
            if (id >= DocRowBase)   // document row: open it
            {
                MarksList.SelectedItem = null;
                if (_vault.GetDocuments().FirstOrDefault(d => d.Id == id - DocRowBase) is { } doc) { SideTabs.SelectedIndex = KnowledgeTab; OpenDocument(doc); }
                return;
            }
            if (id < 0)   // folder row: fold in or out
            {
                var fid = -1 - id; _foldersTouched = true;
                if (!_openFolders.Remove(fid)) _openFolders.Add(fid);
                ApplyCollectionFilter(); return;
            }
            await GoToAsync(e.Ref.Book, e.Ref.Chapter, e.Ref.Verse);
            LoadEditor(id);
        };
        MarksDetailBtn.IsCheckedChanged += (_, _) => ApplyCollectionFilter();
        var more = new MenuFlyout();
        void AddItem(string text, Action act) { var i = new MenuItem { Header = text }; i.Click += (_, _) => act(); more.Items.Add(i); }
        AddItem(L("Alle Ordner aufklappen"), () => { _foldersTouched = true; foreach (var f in _vault.GetFolders()) _openFolders.Add(f.Id); _openFolders.Add(0); ApplyCollectionFilter(); });
        AddItem(L("Alle Ordner zuklappen"), () => { _foldersTouched = true; _openFolders.Clear(); ApplyCollectionFilter(); });
        AddItem(L("Neues Dokument im gewählten Ordner"), () =>
        {
            _newDocFolder = (FolderFilterBox.SelectedItem as Folder)?.Id;
            SideTabs.SelectedIndex = KnowledgeTab;
            OpenDocument(new Document(0, "", "", null, false, DateTime.Now, [], _newDocFolder));
        });
        AddItem(L("Neuer Ordner …"), () => { NewFolderBox.IsVisible = true; NewFolderBox.Focus(); });
        AddItem(L("Gewählten Ordner löschen"), () => { if (DeleteFolderBtn.IsVisible) DeleteFolderBtn.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); });
        MarksMoreBtn.Flyout = more;
        MarkSaveBtn.Click += async (_, _) => await SaveEditedMarkAsync();
        MarkDeleteBtn.Click += async (_, _) =>
        {
            if (_editMark is not { } m) return;
            _vault.DeleteMark(m.Id); CloseEditor(); RefreshCollection();
            await ShowChapterAsync(_selected);
        };
        MarkQueueBtn.Click += (_, _) => { if (_editMark is { } m) AddToQueue([m.Ref]); };

        QueueNextBtn.Click += async (_, _) => await QueueNextAsync();
        QueueAddBtn.Click += (_, _) =>
        {
            if (_selected is int v) AddToQueue([new VerseRef(_curBook, _curChapter, v)]); else StatusText.Text = L("Erst einen Vers anklicken.");
        };
        QueueRemoveBtn.Click += (_, _) => { if (QueueList.SelectedItem is Entry { Id: long id }) { _vault.RemoveFromQueue(id); RefreshQueue(); } };
        QueueClearBtn.Click += (_, _) => { _vault.ClearQueue(); RefreshQueue(); };
        QueueList.SelectionChanged += async (_, _) => { if (QueueList.SelectedItem is Entry e) await GoToAsync(e.Ref.Book, e.Ref.Chapter, e.Ref.Verse); };
        XrefQueueBtn.Click += (_, _) => { if (_xrefs.Count > 0) AddToQueue(_xrefs.Select(x => x.Target)); };
        TodoBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) AddTodo(); };

        VaultBox.SelectionChanged += async (_, _) => { if (!_vaultUiLoading && VaultBox.SelectedItem is string n && n != _vault.Name) await SwitchVaultAsync(n); };
        NewVaultBtn.Click += async (_, _) => await CreateVaultAsync();
        AutoTagCheck.IsChecked = _settings.AutoTagBook;
        AutoTagCheck.IsCheckedChanged += (_, _) => { _settings.AutoTagBook = AutoTagCheck.IsChecked == true; SaveSettings(); };
        BuildDefaultColorPanel();
        RefreshVaultUi(); RefreshQueue(); RefreshTodos();
    }

    // ---- highlights -------------------------------------------------------------------------------------------------

    void LoadChapterMarks()
    {
        _chapterMarks = _vault.GetMarks(_curBook, _curChapter);
        _wholeMarks = _chapterMarks.Where(m => m.WholeVerse).GroupBy(m => m.Ref.Verse).ToDictionary(g => g.Key, g => g.Last().Color);
    }

    static string Shorten(string text, int max = 40) => text.Length <= max ? text : text[..max].TrimEnd() + "…";

    IEnumerable<string> AutoTags(VerseRef r) => _settings.AutoTagBook ? [BookName(r.Book)] : [];

    string VerseTextOf(int verse) => _curChapterData?.Verses.FirstOrDefault(v => v.Number == verse)?.Text ?? "";

    static int RawStart(CellInfo c, int p)
    {
        foreach (var r in c.Runs) if (p < r.Tb + r.Len) return r.Raw + Math.Max(0, p - r.Tb);
        return c.RawText.Length;
    }

    static int RawEnd(CellInfo c, int p)
    {
        for (var i = c.Runs.Count - 1; i >= 0; i--) if (p > c.Runs[i].Tb) return c.Runs[i].Raw + Math.Min(c.Runs[i].Len, p - c.Runs[i].Tb);
        return 0;
    }

    /// <summary>Remembers the text the user selected in a verse, so a color can be applied to exactly that range.</summary>
    void CaptureSelection(PointerReleasedEventArgs e)
    {
        _selection = null;
        if (e.Source is not SelectableTextBlock { Tag: CellInfo cell } tb) return;
        int a = Math.Min(tb.SelectionStart, tb.SelectionEnd), b = Math.Max(tb.SelectionStart, tb.SelectionEnd);
        if (b <= a) return;
        int start = RawStart(cell, a), end = Math.Min(RawEnd(cell, b), cell.RawText.Length);
        if (end <= start) return;
        var text = cell.RawText[start..end];
        if (string.IsNullOrWhiteSpace(text)) return;
        _selection = (cell.Verse, cell.TranslationId, start, end, text);
        StatusText.Text = L("Ausgewählt: „{0}“ · Farbe über den Marker-Knopf wählen", Shorten(text, 60));
    }

    async Task ApplyColorAsync(string hex)
    {
        if (_selection is { } s)
        {
            var r = new VerseRef(_curBook, _curChapter, s.Verse);
            var overlapping = _chapterMarks.Where(m => !m.WholeVerse && m.Ref.Verse == s.Verse && m.TranslationId == s.TranslationId && m.Start < s.End && m.End > s.Start).ToList();
            if (overlapping.FirstOrDefault(m => m.Start == s.Start && m.End == s.End) is { } same)
                _vault.UpdateMark(same.Id, hex, same.Note, same.FolderId, same.Tags);   // recolor, keep note and tags
            else
            {
                foreach (var m in overlapping) _vault.DeleteMark(m.Id);
                _vault.AddMark(s.TranslationId, r, s.Start, s.End, s.Text, hex, "", null, AutoTags(r));
            }
            _selection = null;
        }
        else if (_selected is int v)
        {
            var r = new VerseRef(_curBook, _curChapter, v);
            foreach (var m in _chapterMarks.Where(m => m.WholeVerse && m.Ref.Verse == v)) _vault.DeleteMark(m.Id);
            _vault.AddMark(Mark.AnyTranslation, r, -1, -1, VerseTextOf(v), hex, "", null, AutoTags(r));
        }
        else { StatusText.Text = L("Erst Text auswählen oder einen Vers anklicken."); return; }
        RefreshCollection();
        await ShowChapterAsync(_selected);
    }

    async Task RemoveMarksAsync()
    {
        IEnumerable<Mark> victims = _selection is { } s
            ? _chapterMarks.Where(m => !m.WholeVerse && m.Ref.Verse == s.Verse && m.TranslationId == s.TranslationId && m.Start < s.End && m.End > s.Start)
            : _selected is int v ? _chapterMarks.Where(m => m.Ref.Verse == v) : [];
        var list = victims.ToList();
        if (list.Count == 0) { StatusText.Text = L("Nichts zum Entfernen ausgewählt."); return; }
        foreach (var m in list) _vault.DeleteMark(m.Id);
        _selection = null;
        RefreshCollection();
        await ShowChapterAsync(_selected);
    }

    /// <summary>Quick save of the whole clicked verse in the default color; again removes it.</summary>
    void ToggleBookmark()
    {
        if (_selected is not int v) { StatusText.Text = L("Erst einen Vers anklicken."); return; }
        var existing = _chapterMarks.Where(m => m.WholeVerse && m.Ref.Verse == v).ToList();
        if (existing.Count > 0) foreach (var m in existing) _vault.DeleteMark(m.Id);
        else
        {
            var r = new VerseRef(_curBook, _curChapter, v);
            _vault.AddMark(Mark.AnyTranslation, r, -1, -1, VerseTextOf(v), _settings.DefaultColor, "", null, AutoTags(r));
        }
        LoadChapterMarks(); Paint(); RefreshCollection();
    }

    Border Swatch(string hex, Action<string> pick, bool selected = false, double size = 26)
    {
        var b = new Border
        {
            Width = size, Height = size, CornerRadius = new(size / 2), Margin = new(0, 0, 6, 6), Cursor = new Cursor(StandardCursorType.Hand),
            Background = new SolidColorBrush(Color.Parse(hex)), BorderThickness = new(selected ? 3 : 1),
        };
        b.Bind(Border.BorderBrushProperty, this.GetResourceObservable(selected ? "BsText" : "BsBorder"));
        ToolTip.SetTip(b, hex);
        b.PointerPressed += (_, _) => pick(hex);
        return b;
    }

    static string? NormalizeHex(string? text)
    {
        var t = (text ?? "").Trim();
        if (t.Length > 0 && t[0] != '#') t = "#" + t;
        return Color.TryParse(t, out var c) ? $"#{c.R:X2}{c.G:X2}{c.B:X2}" : null;
    }


    /// <summary>Own colors: three sliders and a preview; the color is remembered with the presets.</summary>
    StackPanel BuildRgbPicker(Action<string> pick, out Button openButton, out Button applyButton, bool startOpen = false)
    {
        var start = Avalonia.Media.Color.Parse(_settings.DefaultColor);
        Slider Channel(byte v) => new() { Minimum = 0, Maximum = 255, Value = v, Height = 24 };
        var sr = Channel(start.R); var sg = Channel(start.G); var sb = Channel(start.B);
        var preview = new Border { Height = 28, CornerRadius = new(8) };
        string Current() => $"#{(int)sr.Value:X2}{(int)sg.Value:X2}{(int)sb.Value:X2}";
        void Update() => preview.Background = new SolidColorBrush(Avalonia.Media.Color.Parse(Current()));
        sr.ValueChanged += (_, _) => Update(); sg.ValueChanged += (_, _) => Update(); sb.ValueChanged += (_, _) => Update();
        Update();
        var panel = new StackPanel { IsVisible = startOpen, Spacing = 2, Children = { new TextBlock { Text = L("Rot · Grün · Blau"), Classes = { "caption" } }, sr, sg, sb, preview } };
        var open = new Button { Content = L("＋ Eigene Farbe …"), IsVisible = !startOpen };
        var apply = new Button { Content = L("Farbe verwenden und speichern"), Classes = { "primary" }, IsVisible = startOpen };
        open.Click += (_, _) => { panel.IsVisible = true; apply.IsVisible = true; open.IsVisible = false; };
        apply.Click += (_, _) =>
        {
            var hex = Current();
            if (!PresetColors.Contains(hex) && !_settings.CustomColors.Contains(hex)) { _settings.CustomColors.Add(hex); SaveSettings(); }
            pick(hex);
        };
        openButton = open; applyButton = apply;
        return panel;
    }

    void ShowColorFlyout()
    {
        var flyout = new Flyout { Placement = PlacementMode.BottomEdgeAlignedRight };
        void Pick(string hex) { flyout.Hide(); _ = ApplyColorAsync(hex); }
        var title = new TextBlock
        {
            FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap,
            Text = _selection is { } s ? L("Auswahl: „{0}“", Shorten(s.Text)) : _selected is int v ? L("Ganzer Vers {0}", v) : L("Erst Text auswählen oder einen Vers anklicken"),
        };
        var swatches = new WrapPanel();
        foreach (var hex in PresetColors.Concat(_settings.CustomColors)) swatches.Children.Add(Swatch(hex, Pick));
        var picker = BuildRgbPicker(Pick, out var custom, out var apply);
        var error = new TextBlock { Classes = { "caption" } };
        var remove = new Button { Content = L("Markierung entfernen") };
        remove.Click += (_, _) => { flyout.Hide(); _ = RemoveMarksAsync(); };
        flyout.Content = new StackPanel
        {
            Spacing = 8, Width = 280,
            Children = { title, swatches, custom, picker, apply, error, remove },
        };
        flyout.ShowAt(MarkBtn);
    }

    void BuildDefaultColorPanel()
    {
        DefaultColorPanel.Children.Clear();
        foreach (var hex in PresetColors.Concat(_settings.CustomColors))
            DefaultColorPanel.Children.Add(Swatch(hex, h => { _settings.DefaultColor = h; SaveSettings(); BuildDefaultColorPanel(); }, hex == _settings.DefaultColor));
    }

    // ---- collection (folders, tags, editor) ---------------------------------------------------------------------------

    void RefreshCollection()
    {
        if (_vault is null) return;
        var keepFolder = (FolderFilterBox.SelectedItem as Folder)?.Id;
        var keepNoFolder = FolderFilterBox.SelectedIndex == 1;
        var keepTag = TagFilterBox.SelectedIndex > 0 ? TagFilterBox.SelectedItem as string : null;
        var folders = _vault.GetFolders();
        var tags = _vault.GetTags().Select(t => t.Tag).ToList();

        _vaultUiLoading = true;
        FolderFilterBox.ItemsSource = new List<object> { L("Alle Ordner"), L("Ohne Ordner") }.Concat(folders).ToList();
        FolderFilterBox.SelectedIndex = keepNoFolder ? 1 : keepFolder is long id && folders.FindIndex(f => f.Id == id) is var i and >= 0 ? i + 2 : 0;
        TagFilterBox.ItemsSource = new List<string> { L("Alle Tags") }.Concat(tags).ToList();
        TagFilterBox.SelectedIndex = keepTag is not null && tags.IndexOf(keepTag) is var j and >= 0 ? j + 1 : 0;
        _vaultUiLoading = false;
        DeleteFolderBtn.IsVisible = FolderFilterBox.SelectedItem is Folder;
        ApplyCollectionFilter();
    }

    readonly HashSet<long> _openFolders = [];
    bool _foldersTouched;

    static int NaturalCompare(string a, string b)
    {
        var x = System.Text.RegularExpressions.Regex.Split(a, @"(\d+)"); var y = System.Text.RegularExpressions.Regex.Split(b, @"(\d+)");
        for (var i = 0; i < Math.Min(x.Length, y.Length); i++)
        {
            var c = long.TryParse(x[i], out var nx) && long.TryParse(y[i], out var ny) ? nx.CompareTo(ny) : string.Compare(x[i], y[i], StringComparison.CurrentCultureIgnoreCase);
            if (c != 0) return c;
        }
        return x.Length.CompareTo(y.Length);
    }

    /// <summary>The folders as a collapsible tree; verses that follow each other with the same note and color are shown as one range (19,24–29).</summary>
    void ApplyCollectionFilter()
    {
        long? folder = (FolderFilterBox.SelectedItem as Folder)?.Id;
        var marks = _vault.GetMarks(folderId: folder, noFolder: FolderFilterBox.SelectedIndex == 1,
            tag: TagFilterBox.SelectedIndex > 0 ? TagFilterBox.SelectedItem as string : null);
        var detail = MarksDetailBtn.IsChecked == true;
        var folders = _vault.GetFolders().OrderBy(f => f.Name, Comparer<string>.Create(NaturalCompare)).ToList();
        var rows = new List<Entry>();
        var tagFilter = TagFilterBox.SelectedIndex > 0 ? TagFilterBox.SelectedItem as string : null;
        var docs = _vault.GetDocuments(tag: tagFilter)
            .Where(d => FolderFilterBox.SelectedIndex == 1 ? d.FolderId is null : folder is null || d.FolderId == folder).ToList();
        // few folders: start expanded; many: collapsed (until the user decides)
        if (!_foldersTouched) { _openFolders.Clear(); if (folders.Count <= 3 || folder is not null) foreach (var f in folders) _openFolders.Add(f.Id); _openFolders.Add(0); }
        foreach (var (id, name) in folders.Select(f => (f.Id, f.Name)).Append((0L, L("Ohne Ordner"))))
        {
            var inFolder = marks.Where(m => (m.FolderId ?? 0) == id).OrderBy(m => BookOrder(m.Ref.Book)).ThenBy(m => m.Ref.Chapter).ThenBy(m => m.Ref.Verse).ToList();
            var folderDocs = docs.Where(d => (d.FolderId ?? 0) == id).OrderBy(d => d.Title, Comparer<string>.Create(NaturalCompare)).ToList();
            if (inFolder.Count == 0 && folderDocs.Count == 0) continue;
            var open = _openFolders.Contains(id);
            var counts = string.Join(" · ", new[] { folderDocs.Count > 0 ? L("{0} Dok.", folderDocs.Count) : null, inFolder.Count > 0 ? L("{0} Verse", inFolder.Count) : null }.OfType<string>());
            rows.Add(new Entry(default, $"{(open ? "▾" : "▸")}  {name}   ·   {counts}", -1 - id, null));
            if (!open) continue;
            foreach (var d in folderDocs)
            {
                var preview = string.Join(" ", d.Body.Split((char)10).Where(l => !l.TrimStart().StartsWith('#') && !l.StartsWith("**Quelle")).Take(4)).Trim();
                rows.Add(new Entry(default, detail ? L("📄  {0}\n{1}", d.Title, Shorten(preview, 120)) : $"📄  {d.Title}", DocRowBase + d.Id, null));
            }
            for (var i = 0; i < inFolder.Count; i++)
            {
                var first = inFolder[i]; var last = first; var run = new List<Mark> { first };
                while (i + 1 < inFolder.Count && inFolder[i + 1] is var n && n.WholeVerse && last.WholeVerse && n.Ref.Book == last.Ref.Book && n.Ref.Chapter == last.Ref.Chapter
                       && n.Ref.Verse == last.Ref.Verse + 1 && n.Note == first.Note && n.Color == first.Color && n.FolderId == first.FolderId)
                { run.Add(n); last = n; i++; }
                rows.Add(new Entry(first.Ref, MarkRowDisplay(run, detail), first.Id, first.Color));
            }
        }
        MarksList.ItemsSource = rows;
        MarksEmpty.IsVisible = marks.Count == 0 && docs.Count == 0;
    }

    const long DocRowBase = 1_000_000_000_000;

    int BookOrder(string id) { var i = _books.ToList().FindIndex(b => b.Id == id); return i < 0 ? int.MaxValue : i; }

    string MarkRowDisplay(List<Mark> run, bool detail)
    {
        var m = run[0]; var last = run[^1];
        var label = run.Count == 1 ? Label(m.Ref) : $"{BookName(m.Ref.Book)} {m.Ref.Chapter},{m.Ref.Verse}–{last.Ref.Verse}";
        if (!detail) return label;
        var lines = new List<string>();
        var text = string.Join(" ", run.Select(r => r.Text));
        if (!m.WholeVerse) lines.Add($"„{Shorten(m.Text, 100)}“"); else if (text.Length > 0) lines.Add(Shorten(text, run.Count > 1 ? 220 : 100));
        if (m.Note.Length > 0) lines.Add("✎ " + Shorten(m.Note.ReplaceLineEndings(" "), 100));
        if (m.Tags.Count > 0) lines.Add(string.Join("  ", m.Tags.Select(t => "#" + t)));
        return lines.Count == 0 ? label : label + "\n" + string.Join("\n", lines);
    }

    string MarkDisplay(Mark m)
    {
        var lines = new List<string>();
        if (!m.WholeVerse) lines.Add($"„{Shorten(m.Text, 100)}“");
        else if (m.Text.Length > 0) lines.Add(Shorten(m.Text, 100));
        if (m.Note.Length > 0) lines.Add("✎ " + Shorten(m.Note.ReplaceLineEndings(" "), 100));
        if (m.Tags.Count > 0) lines.Add(string.Join("  ", m.Tags.Select(t => "#" + t)));
        return lines.Count == 0 ? Label(m.Ref) : Label(m.Ref) + "\n" + string.Join("\n", lines);
    }

    void LoadEditor(long id)
    {
        if (_vault.GetMarks().FirstOrDefault(x => x.Id == id) is not { } m) { CloseEditor(); return; }
        _editMark = m; _editColor = m.Color;
        MarkEditor.IsVisible = true;
        MarkEditTitle.Text = Label(m.Ref) + (m.WholeVerse ? L(" (ganzer Vers)") : $" „{Shorten(m.Text)}“");
        BuildEditSwatches();
        MarkNoteBox.Text = m.Note;
        MarkTagsBox.Text = string.Join(", ", m.Tags);
        var folders = _vault.GetFolders();
        MarkFolderBox.ItemsSource = new List<object> { L("(kein Ordner)") }.Concat(folders).ToList();
        MarkFolderBox.SelectedIndex = m.FolderId is long fid ? folders.FindIndex(f => f.Id == fid) + 1 : 0;
    }

    void CloseEditor() { _editMark = null; MarkEditor.IsVisible = false; }

    void BuildEditSwatches()
    {
        EditSwatches.Children.Clear();
        var all = PresetColors.Concat(_settings.CustomColors).ToList();
        if (!all.Contains(_editColor)) all.Add(_editColor);
        foreach (var hex in all) EditSwatches.Children.Add(Swatch(hex, h => { _editColor = h; BuildEditSwatches(); }, hex == _editColor));
        var rgb = BuildRgbPicker(h => { _editColor = h; BuildEditSwatches(); }, out var open, out var apply);
        EditSwatches.Children.Add(new StackPanel { Width = 240, Spacing = 4, Margin = new(0, 6, 0, 0), Children = { open, rgb, apply } });
    }

    async Task SaveEditedMarkAsync()
    {
        if (_editMark is not { } m) return;
        var folders = _vault.GetFolders();
        long? folder = MarkFolderBox.SelectedIndex > 0 && MarkFolderBox.SelectedIndex <= folders.Count ? folders[MarkFolderBox.SelectedIndex - 1].Id : null;
        _vault.UpdateMark(m.Id, _editColor, MarkNoteBox.Text ?? "", folder, (MarkTagsBox.Text ?? "").Split(',', ';'));
        RefreshCollection();
        LoadEditor(m.Id);
        await ShowChapterAsync(_selected);
    }

    // ---- queue ------------------------------------------------------------------------------------------------------

    void AddToQueue(IEnumerable<VerseRef> refs)
    {
        var list = refs.ToList();
        _vault.AddToQueue(list.Select(r => (r, Label(r))));
        RefreshQueue();
        QueueInfo.Text = L("{0} hinzugefügt · {1} auf der Leseliste", list.Count, _vault.GetQueue().Count);
    }

    void RefreshQueue()
    {
        var items = _vault.GetQueue();
        QueueList.ItemsSource = items.Select(q => new Entry(q.Ref, q.Label, q.Id)).ToList();
        QueueInfo.Text = items.Count == 0 ? L("Die Leseliste ist leer. Füge Verse über „Vers hinzufügen“ hinzu oder schicke Suchergebnisse und Querverweise hierher; „Nächster“ springt dann der Reihe nach durch.") : L("{0} Verse auf der Leseliste", items.Count);
    }

    async Task QueueNextAsync()
    {
        if (_vault.GetQueue().FirstOrDefault() is not { } first) { QueueInfo.Text = L("Die Leseliste ist leer."); return; }
        _vault.RemoveFromQueue(first.Id);
        RefreshQueue();
        QueueInfo.Text = L("Jetzt: {0} · noch {1} offen", first.Label, _vault.GetQueue().Count);
        await GoToAsync(first.Ref.Book, first.Ref.Chapter, first.Ref.Verse);
    }

    // ---- to-dos -----------------------------------------------------------------------------------------------------

    void AddTodo()
    {
        var text = TodoBox.Text?.Trim();
        if (string.IsNullOrEmpty(text)) return;
        VerseRef? r = TodoVerseCheck.IsChecked == true && _selected is int v ? new VerseRef(_curBook, _curChapter, v) : null;
        _vault.AddTodo(text, r);
        TodoBox.Text = "";
        RefreshTodos();
    }

    void RefreshTodos()
    {
        TodoPanel.Children.Clear();
        if (_vault.GetTodos().Count == 0) TodoPanel.Children.Add(new TextBlock { Text = L("Noch keine Aufgaben. Schreibe oben eine hinein, z. B. „Römer 8 vergleichen“; auf Wunsch hängt sie den angeklickten Vers an."), TextWrapping = TextWrapping.Wrap, Opacity = 0.65, Margin = new(2, 8) });
        foreach (var t in _vault.GetTodos())
        {
            var id = t.Id;
            var label = new TextBlock { Text = t.Text, TextWrapping = TextWrapping.Wrap, Opacity = t.Done ? 0.55 : 1, TextDecorations = t.Done ? TextDecorations.Strikethrough : null };
            var check = new CheckBox { IsChecked = t.Done, Content = label, VerticalAlignment = VerticalAlignment.Center };
            check.IsCheckedChanged += (_, _) => { _vault.SetTodoDone(id, check.IsChecked == true); RefreshTodos(); };
            var delete = new Button { Classes = { "tool" }, Width = 28, Height = 28, Content = new Icon { Data = Geometry.Parse("M18 6L6 18 M6 6l12 12"), IconSize = 14 } };
            ToolTip.SetTip(delete, L("Aufgabe löschen"));
            delete.Click += (_, _) => { _vault.DeleteTodo(id); RefreshTodos(); };
            var row = new DockPanel();
            DockPanel.SetDock(delete, Dock.Right);
            row.Children.Add(delete);
            if (t.Ref is { } r)
            {
                var link = new Button { Content = Label(r), FontSize = 12, Padding = new(8, 2), Margin = new(0, 0, 4, 0) };
                link.Click += async (_, _) => await GoToAsync(r.Book, r.Chapter, r.Verse);
                DockPanel.SetDock(link, Dock.Right);
                row.Children.Add(link);
            }
            row.Children.Add(check);
            TodoPanel.Children.Add(row);
        }
    }
}
