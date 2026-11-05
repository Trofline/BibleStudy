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
    Mark? _editMark;
    string _editColor = PresetColors[0];
    bool _vaultUiLoading;
    bool _switchingVault;

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
        // an open document is saved into the vault it belongs to, before the other vault takes over
        // (saved afterwards it went into the new vault and could overwrite a document there that has the same number)
        CloseEditor(); CloseDocument(); SaveNoteDraft(); SaveSermonBeforeVaultChange();
        _noteRef = null; NoteEditor.Text = _noteLoaded = "";
        _vault = OpenVault(name);
        RefreshHiddenXrefs();
        _settings.Vault = name; SaveSettings();
        _read = _vault.GetReadChapters();
        _myXrefKey = null;
        _sermonVault = null; if (TabActive(PrayerTab)) BuildPrayerUi(); RefreshLists(); RefreshDocs(); RefreshQueue(); RefreshTodos(); RefreshBookmarks(); RefreshVaultUi();
        if (_windows.TryGetValue("group:Dokumente", out var docs)) docs.SelectAction?.Invoke(docs.Current);
        if (_windows.TryGetValue("group:Predigt", out var sermon)) sermon.SelectAction?.Invoke(sermon.Current);
        if (Home.IsVisible) ShowHome();   // "continue" and "keep working" belong to the vault just opened
        _switchingVault = true;   // the chapter that stays on screen was not read in the new vault
        try { await RedrawChapterAsync(); }
        finally { _switchingVault = false; }
    }

    async Task CreateVaultAsync()
    {
        var name = NewVaultBox.Text?.Trim() ?? "";
        // "." or ".." would put the vault outside the vault folder; Windows also refuses names ending in a dot or space and device names like CON
        var device = name.Split('.')[0].Trim().ToUpperInvariant() is "CON" or "PRN" or "AUX" or "NUL" or "COM1" or "COM2" or "COM3" or "COM4" or "LPT1" or "LPT2" or "LPT3";
        if (name.Length == 0 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.Trim('.').Length == 0 || name.EndsWith('.') || device) { VaultInfo.Text = L("Ungültiger Name (keine Sonderzeichen wie \\ / : * ? \" < > |)."); return; }
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
        MarksList.SelectionChanged += async (_, _) => await OnMarksListPickedAsync();
        MarksDetailBtn.IsCheckedChanged += (_, _) => ApplyCollectionFilter();
        MarksAllVaultsBtn.IsChecked = _settings.MarksAllVaults;   // the state is kept between starts
        MarksAllVaultsBtn.IsCheckedChanged += (_, _) =>
        {
            _settings.MarksAllVaults = MarksAllVaultsBtn.IsChecked == true; SaveSettings();
            _foldersTouched = false; CloseEditor(); ApplyCollectionFilter();
            RefreshCells();   // the marks of the other vaults show in the text, or go away again
        };
        var more = new MenuFlyout();
        void AddItem(string text, Action act) { var i = new MenuItem { Header = text }; i.Click += (_, _) => act(); more.Items.Add(i); }
        AddItem(L("Alle Ordner aufklappen"), () =>
        {
            _foldersTouched = true;
            var names = VaultNames();
            for (var i = 0; i < names.Count; i++)
            {
                var vnum = names[i] == _vault.Name ? 0 : i + 1;
                foreach (var f in StoreOf(names[i]).GetFolders()) _openFolders.Add(vnum * FolderKeySpan + f.Id);
                _openFolders.Add(vnum * FolderKeySpan); _openFolders.Add(VaultKeyBase + i);
            }
            ApplyCollectionFilter();
        });
        AddItem(L("Alle Ordner zuklappen"), () => { _foldersTouched = true; _openFolders.Clear(); ApplyCollectionFilter(); });
        AddItem(L("Neuer Ordner …"), () => { NewFolderBox.IsVisible = true; NewFolderBox.Focus(); });
        AddItem(L("Gewählten Ordner löschen"), () => { if (DeleteFolderBtn.IsVisible) DeleteFolderBtn.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); });
        MarksMoreBtn.Flyout = more;
        MarkSaveBtn.Click += async (_, _) => await SaveEditedMarkAsync();
        MarkDeleteBtn.Click += (_, _) =>
        {
            if (_editMark is not { } m) return;
            PushUndo(m.Ref.Book, m.Ref.Chapter, jump: false);
            var t = _vault.TrashMark(m.Id);
            _markUndo[^1] = _markUndo[^1] with { Trash = [t] };
            CloseEditor(); RefreshCollection(); RefreshCells();
            ShowUndoToast(L("Markierung „{0}“ gelöscht.", Shorten(MarkLabel(m), 40)), UndoMarksAsync);
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
        InitTodos();

        VaultBox.SelectionChanged += async (_, _) => { if (!_vaultUiLoading && VaultBox.SelectedItem is string n && n != _vault.Name) await SwitchVaultAsync(n); };
        NewVaultBtn.Click += async (_, _) => await CreateVaultAsync();
        AutoTagCheck.IsChecked = _settings.AutoTagBook;
        AutoTagCheck.IsCheckedChanged += (_, _) => { _settings.AutoTagBook = AutoTagCheck.IsChecked == true; SaveSettings(); };
        BuildDefaultColorPanel();
        RefreshVaultUi(); RefreshQueue(); RefreshTodos(); RefreshBookmarks();
        DeleteVaultBtn.Click += async (_, _) => await DeleteVaultAsync();
    }

    // ---- highlights -------------------------------------------------------------------------------------------------

    void LoadChapterMarks()
    {
        LoadChapterBookmarks();
        _chapterMarks = _vault.GetMarks(_curBook, _curChapter);
        _wholeMarks = [];
        foreach (var m in _chapterMarks.Where(m => m.WholeVerse))   // a mark of several verses colors each of them
            for (var v = m.Ref.Verse; v <= m.LastVerse; v++) _wholeMarks[v] = m.Color;
        _headings = _vault.GetHeadings(_curBook, _curChapter);
        // with "all vaults" on, the marks of the other vaults show in the text too (to look at: negative ids, so they are never edited by mistake)
        _otherChapterMarks = []; _otherWhole = [];
        if (_settings.MarksAllVaults)
            foreach (var name in VaultNames().Where(n => n != _vault.Name))
                try
                {
                    foreach (var m in StoreOf(name).GetMarks(_curBook, _curChapter))
                    {
                        _otherChapterMarks.Add(m with { Id = -m.Id - 1 });
                        if (m.WholeVerse) for (var v = m.Ref.Verse; v <= m.LastVerse; v++) _otherWhole.TryAdd(v, m.Color);
                    }
                }
                catch { /* a vault that cannot be read just does not show */ }
    }

    List<Mark> _otherChapterMarks = [];
    Dictionary<int, string> _otherWhole = [];

    static string Shorten(string text, int max = 40) => text.Length <= max ? text : text[..max].TrimEnd() + "…";

    IEnumerable<string> AutoTags(VerseRef r) => _settings.AutoTagBook ? [BookName(r.Book)] : [];

    string VerseTextOf(int verse) => _curChapterData?.Verses.FirstOrDefault(v => v.Number == verse)?.Text ?? "";

    /// <summary>Quick save of the whole clicked verse in the default color; again removes it.</summary>
    Border Swatch(string hex, Action<string> pick, bool selected = false, double size = 26)
    {
        var b = new Border
        {
            Width = size, Height = size, CornerRadius = new(size / 2), Margin = new(0, 0, 6, 6), Cursor = new Cursor(StandardCursorType.Hand),
            Background = new SolidColorBrush(MarkColor(hex)), BorderThickness = new(selected ? 3 : 1),
        };
        b.Bind(Border.BorderBrushProperty, this.GetResourceObservable(selected ? "BsText" : "BsBorder"));
        ToolTip.SetTip(b, ColorName(hex) is { Length: > 0 } nm ? $"{nm}  ({hex})" : hex);
        b.PointerPressed += (_, _) => pick(hex);
        return b;
    }

    /// <summary>The meaning the user gave a color ("" if none).</summary>
    string ColorName(string? hex) => hex is not null && _settings.ColorNames.TryGetValue(hex.ToUpperInvariant(), out var n) ? n : "";

    /// <summary>One row per named color (dot + name), so a color can be found by its meaning.</summary>
    Control NamedColorRow(string hex, Action<string> pick, bool selected = false)
    {
        var dot = new Border { Width = 16, Height = 16, CornerRadius = new(8), Background = new SolidColorBrush(MarkColor(hex)), VerticalAlignment = VerticalAlignment.Center };
        var row = new Button
        {
            HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new(8, 5),
            Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { dot, new TextBlock { Text = ColorName(hex), VerticalAlignment = VerticalAlignment.Center, FontWeight = selected ? FontWeight.SemiBold : FontWeight.Normal } } },
        };
        row.Classes.Set("primary", selected);
        row.Click += (_, _) => pick(hex);
        return row;
    }

    /// <summary>Named colors as a list of rows, the rest as plain swatches.</summary>
    Control ColorChoice(Action<string> pick, string? selected = null)
    {
        var all = PresetColors.Concat(_settings.CustomColors).ToList();
        if (selected is not null && !all.Contains(selected)) all.Add(selected);
        var named = all.Where(h => ColorName(h).Length > 0).ToList();
        var plain = new WrapPanel();
        foreach (var hex in all.Where(h => !named.Contains(h))) plain.Children.Add(Swatch(hex, pick, hex == selected));
        var box = new StackPanel { Spacing = 2 };
        foreach (var hex in named) box.Children.Add(NamedColorRow(hex, pick, hex == selected));
        if (named.Count > 0 && plain.Children.Count > 0) box.Children.Add(new Border { Height = 6 });
        if (plain.Children.Count > 0) box.Children.Add(plain);
        return box;
    }

    /// <summary>Settings: give each color a meaning (shown when marking).</summary>
    void BuildColorNamesPanel()
    {
        ColorNamesPanel.Children.Clear();
        ColorNamesPanel.Children.Add(new TextBlock { Text = L("Farbbedeutungen"), Margin = new(0, 4, 0, 0) });
        ColorNamesPanel.Children.Add(new TextBlock { Text = L("Gib Farben einen Namen, etwa „Verheißung“ oder „Gebot“. Beim Markieren steht der Name dann neben der Farbe."), Classes = { "caption" }, TextWrapping = TextWrapping.Wrap });
        foreach (var hex in PresetColors.Concat(_settings.CustomColors))
        {
            var key = hex.ToUpperInvariant();
            var box = new TextBox { Text = ColorName(hex), PlaceholderText = L("Name (z. B. Verheißung)"), MaxLength = 30, MinWidth = 240 };
            box.LostFocus += (_, _) =>
            {
                var t = (box.Text ?? "").Trim();
                if (t.Length == 0) _settings.ColorNames.Remove(key); else _settings.ColorNames[key] = t;
                SaveSettings();
            };
            var dot = new Border { Width = 22, Height = 22, CornerRadius = new(11), Background = new SolidColorBrush(MarkColor(hex)), VerticalAlignment = VerticalAlignment.Center };
            ColorNamesPanel.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { dot, box } });
        }
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
            if (!PresetColors.Contains(hex) && !_settings.CustomColors.Contains(hex)) { _settings.CustomColors.Add(hex); SaveSettings(); BuildColorNamesPanel(); }
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
            Text = _sel is { Parts.Count: > 0 } ? L("Auswahl: „{0}“", Shorten(SelectedText())) : _selVerses.Count > 1 ? L("{0} Verse gewählt", _selVerses.Count) : _selected is int v ? L("Ganzer Vers {0}", v) : L("Erst Text auswählen oder einen Vers anklicken"),
        };
        var remove = new Button { Content = L("Markierung entfernen") };
        remove.Click += (_, _) => { flyout.Hide(); _ = RemoveMarksAsync(); };
        if (InsideMarkedVerse())   // a sub-mark: one choice, in its own color
        {
            var sub = new Button { Content = L("Untermarkierung"), Classes = { "primary" } };
            sub.Click += (_, _) => Pick(SubMarkColor());
            var normal = new Button { Content = L("Normale Farben …") };
            normal.Click += (_, _) => { flyout.Hide(); _normalMarking = true; ShowColorFlyout(); };
            flyout.Content = new StackPanel { Spacing = 8, Width = 280, Children = { title, sub, normal, remove } };
            flyout.ShowAt(MarkBtn);
            return;
        }
        var swatches = ColorChoice(Pick);
        var picker = BuildRgbPicker(Pick, out var custom, out var apply);
        var error = new TextBlock { Classes = { "caption" } };
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
        BuildColorNamesPanel();
    }

    // ---- collection (folders, tags, editor) ---------------------------------------------------------------------------

    void RefreshCollection()
    {
        if (_vault is null) return;
        var keepFolder = (FolderFilterBox.SelectedItem as Folder)?.Id;
        var keepNoFolder = FolderFilterBox.SelectedIndex == 1;
        var keepTag = TagFilterBox.SelectedIndex > 0 ? TagFilterBox.SelectedItem as string : null;
        var folders = FolderTree(_vault.GetFolders());
        var tags = _vault.GetTags().Select(t => t.Tag).ToList();

        _vaultUiLoading = true;
        FolderFilterBox.ItemsSource = new List<object> { L("Alle Ordner"), L("Ohne Ordner") }.Concat(folders).ToList();
        FolderFilterBox.SelectedIndex = keepNoFolder ? 1 : keepFolder is long id && folders.FindIndex(f => f.Id == id) is var i and >= 0 ? i + 2 : 0;
        TagFilterBox.ItemsSource = new List<string> { L("Alle Tags") }.Concat(tags).ToList();
        TagFilterBox.SelectedIndex = keepTag is not null && tags.IndexOf(keepTag) is var j and >= 0 ? j + 1 : 0;
        _vaultUiLoading = false;
        DeleteFolderBtn.IsVisible = FolderFilterBox.SelectedItem is Folder;
        if (!_exQuiet && _exBuilt && TabActive(ExplorerTab)) RefreshExplorer();
        ApplyCollectionFilter();
        RefreshNotesTree();   // the folders of the notes
        if (_noteRef is { } nr) LoadNoteFolderBox(nr); else { _noteFolderLoading = true; NoteFolderBox.ItemsSource = new List<object> { L("(kein Ordner)") }.Concat(folders).ToList(); NoteFolderBox.SelectedIndex = 0; _noteFolderLoading = false; }
    }

    void LoadEditor(long id)
    {
        if (_vault.GetMarks().FirstOrDefault(x => x.Id == id) is not { } m) { CloseEditor(); return; }
        _editMark = m; _editColor = m.Color;
        MarkEditor.IsVisible = true;
        MarkEditTitle.Text = MarkLabel(m) + (m.WholeVerse ? (m.MultiVerse ? L(" (ganze Verse)") : L(" (ganzer Vers)")) : $" „{Shorten(m.Text)}“");
        BuildEditSwatches();
        MarkNoteBox.Text = m.Note;
        MarkTagsBox.Text = string.Join(", ", m.Tags);
        var folders = FolderTree(_vault.GetFolders());
        MarkFolderBox.ItemsSource = new List<object> { L("(kein Ordner)") }.Concat(folders).ToList();
        MarkFolderBox.SelectedIndex = m.FolderId is long fid ? folders.FindIndex(f => f.Id == fid) + 1 : 0;
    }

    void CloseEditor() { _editMark = null; MarkEditor.IsVisible = false; }

    void BuildEditSwatches()
    {
        EditSwatches.Children.Clear();
        EditSwatches.Children.Add(new StackPanel { Width = 240, Children = { ColorChoice(h => { _editColor = h; BuildEditSwatches(); }, _editColor) } });
        var rgb = BuildRgbPicker(h => { _editColor = h; BuildEditSwatches(); }, out var open, out var apply);
        EditSwatches.Children.Add(new StackPanel { Width = 240, Spacing = 4, Margin = new(0, 6, 0, 0), Children = { open, rgb, apply } });
    }

    async Task SaveEditedMarkAsync()
    {
        if (_editMark is not { } m) return;
        var folders = FolderTree(_vault.GetFolders());
        long? folder = MarkFolderBox.SelectedIndex > 0 && MarkFolderBox.SelectedIndex <= folders.Count ? folders[MarkFolderBox.SelectedIndex - 1].Id : null;
        if (m.Ref.Book == _curBook && m.Ref.Chapter == _curChapter) PushUndo();
        _vault.UpdateMark(m.Id, _editColor, MarkNoteBox.Text ?? "", folder, (MarkTagsBox.Text ?? "").Split(',', ';'));
        RefreshCollection();
        LoadEditor(m.Id);
        RefreshCells();
        await Task.CompletedTask;
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
}
