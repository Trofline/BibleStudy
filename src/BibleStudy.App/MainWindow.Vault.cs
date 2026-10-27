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
    const int SettingsTab = 9;
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
        CloseEditor(); RefreshLists(); RefreshQueue(); RefreshTodos(); RefreshVaultUi();
        if (Home.IsVisible) Home.Show(_books, _read, Home.OpenBook);
        await ShowChapterAsync(_selected);
    }

    async Task CreateVaultAsync()
    {
        var name = NewVaultBox.Text?.Trim() ?? "";
        if (name.Length == 0 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) { VaultInfo.Text = "Ungültiger Name (keine Sonderzeichen wie \\ / : * ? \" < > |)."; return; }
        NewVaultBox.Text = "";
        await SwitchVaultAsync(name);
    }

    void RefreshVaultUi()
    {
        _vaultUiLoading = true;
        VaultBox.ItemsSource = VaultNames();
        VaultBox.SelectedItem = _vault.Name;
        _vaultUiLoading = false;
        VaultInfo.Text = $"Aktiver Vault: {_vault.Name}";
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
            _vault.AddFolder(NewFolderBox.Text); NewFolderBox.Text = ""; RefreshCollection();
        };
        MarksList.SelectionChanged += async (_, _) =>
        {
            if (MarksList.SelectedItem is not Entry { Id: long id } e) return;
            await GoToAsync(e.Ref.Book, e.Ref.Chapter, e.Ref.Verse);
            LoadEditor(id);
        };
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
            if (_selected is int v) AddToQueue([new VerseRef(_curBook, _curChapter, v)]); else StatusText.Text = "Erst einen Vers anklicken.";
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
        StatusText.Text = $"Ausgewählt: „{Shorten(text, 60)}“ · Farbe über den Marker-Knopf wählen";
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
        else { StatusText.Text = "Erst Text auswählen oder einen Vers anklicken."; return; }
        RefreshCollection();
        await ShowChapterAsync(_selected);
    }

    async Task RemoveMarksAsync()
    {
        IEnumerable<Mark> victims = _selection is { } s
            ? _chapterMarks.Where(m => !m.WholeVerse && m.Ref.Verse == s.Verse && m.TranslationId == s.TranslationId && m.Start < s.End && m.End > s.Start)
            : _selected is int v ? _chapterMarks.Where(m => m.Ref.Verse == v) : [];
        var list = victims.ToList();
        if (list.Count == 0) { StatusText.Text = "Nichts zum Entfernen ausgewählt."; return; }
        foreach (var m in list) _vault.DeleteMark(m.Id);
        _selection = null;
        RefreshCollection();
        await ShowChapterAsync(_selected);
    }

    /// <summary>Quick save of the whole clicked verse in the default color; again removes it.</summary>
    void ToggleBookmark()
    {
        if (_selected is not int v) { StatusText.Text = "Erst einen Vers anklicken."; return; }
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

    void ShowColorFlyout()
    {
        var flyout = new Flyout { Placement = PlacementMode.BottomEdgeAlignedRight };
        void Pick(string hex) { flyout.Hide(); _ = ApplyColorAsync(hex); }
        var title = new TextBlock
        {
            FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap,
            Text = _selection is { } s ? $"Auswahl: „{Shorten(s.Text)}“" : _selected is int v ? $"Ganzer Vers {v}" : "Erst Text auswählen oder einen Vers anklicken",
        };
        var swatches = new WrapPanel();
        foreach (var hex in PresetColors.Concat(_settings.CustomColors)) swatches.Children.Add(Swatch(hex, Pick));
        var hexBox = new TextBox { Watermark = "#RRGGBB", Width = 110 };
        var error = new TextBlock { Classes = { "caption" } };
        var add = new Button { Content = "Eigene Farbe" };
        add.Click += (_, _) =>
        {
            if (NormalizeHex(hexBox.Text) is not { } hex) { error.Text = "Bitte eine Farbe wie #FFD54F eingeben."; return; }
            if (!_settings.CustomColors.Contains(hex)) { _settings.CustomColors.Add(hex); SaveSettings(); }
            Pick(hex);
        };
        var remove = new Button { Content = "Markierung entfernen" };
        remove.Click += (_, _) => { flyout.Hide(); _ = RemoveMarksAsync(); };
        flyout.Content = new StackPanel
        {
            Spacing = 8, Width = 260,
            Children = { title, swatches, new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { hexBox, add } }, error, remove },
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
        FolderFilterBox.ItemsSource = new List<object> { "Alle Ordner", "Ohne Ordner" }.Concat(folders).ToList();
        FolderFilterBox.SelectedIndex = keepNoFolder ? 1 : keepFolder is long id && folders.FindIndex(f => f.Id == id) is var i and >= 0 ? i + 2 : 0;
        TagFilterBox.ItemsSource = new List<string> { "Alle Tags" }.Concat(tags).ToList();
        TagFilterBox.SelectedIndex = keepTag is not null && tags.IndexOf(keepTag) is var j and >= 0 ? j + 1 : 0;
        _vaultUiLoading = false;
        DeleteFolderBtn.IsVisible = FolderFilterBox.SelectedItem is Folder;
        ApplyCollectionFilter();
    }

    void ApplyCollectionFilter()
    {
        long? folder = (FolderFilterBox.SelectedItem as Folder)?.Id;
        var marks = _vault.GetMarks(folderId: folder, noFolder: FolderFilterBox.SelectedIndex == 1,
            tag: TagFilterBox.SelectedIndex > 0 ? TagFilterBox.SelectedItem as string : null);
        MarksList.ItemsSource = marks.Select(m => new Entry(m.Ref, MarkDisplay(m), m.Id, m.Color)).ToList();
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
        MarkEditTitle.Text = Label(m.Ref) + (m.WholeVerse ? " (ganzer Vers)" : $" „{Shorten(m.Text)}“");
        BuildEditSwatches();
        MarkNoteBox.Text = m.Note;
        MarkTagsBox.Text = string.Join(", ", m.Tags);
        var folders = _vault.GetFolders();
        MarkFolderBox.ItemsSource = new List<object> { "(kein Ordner)" }.Concat(folders).ToList();
        MarkFolderBox.SelectedIndex = m.FolderId is long fid ? folders.FindIndex(f => f.Id == fid) + 1 : 0;
    }

    void CloseEditor() { _editMark = null; MarkEditor.IsVisible = false; }

    void BuildEditSwatches()
    {
        EditSwatches.Children.Clear();
        var all = PresetColors.Concat(_settings.CustomColors).ToList();
        if (!all.Contains(_editColor)) all.Add(_editColor);
        foreach (var hex in all) EditSwatches.Children.Add(Swatch(hex, h => { _editColor = h; BuildEditSwatches(); }, hex == _editColor));
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
        QueueInfo.Text = $"{list.Count} hinzugefügt · {_vault.GetQueue().Count} in der Warteschlange";
    }

    void RefreshQueue()
    {
        var items = _vault.GetQueue();
        QueueList.ItemsSource = items.Select(q => new Entry(q.Ref, q.Label, q.Id)).ToList();
        QueueInfo.Text = items.Count == 0 ? "Leer. Verse oder Querverweise hinzufügen und nacheinander abarbeiten." : $"{items.Count} in der Warteschlange";
    }

    async Task QueueNextAsync()
    {
        if (_vault.GetQueue().FirstOrDefault() is not { } first) { QueueInfo.Text = "Die Warteschlange ist leer."; return; }
        _vault.RemoveFromQueue(first.Id);
        RefreshQueue();
        QueueInfo.Text = $"Jetzt: {first.Label} · noch {_vault.GetQueue().Count} offen";
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
        foreach (var t in _vault.GetTodos())
        {
            var id = t.Id;
            var label = new TextBlock { Text = t.Text, TextWrapping = TextWrapping.Wrap, Opacity = t.Done ? 0.55 : 1, TextDecorations = t.Done ? TextDecorations.Strikethrough : null };
            var check = new CheckBox { IsChecked = t.Done, Content = label, VerticalAlignment = VerticalAlignment.Center };
            check.IsCheckedChanged += (_, _) => { _vault.SetTodoDone(id, check.IsChecked == true); RefreshTodos(); };
            var delete = new Button { Classes = { "tool" }, Width = 28, Height = 28, Content = new Icon { Data = Geometry.Parse("M18 6L6 18 M6 6l12 12"), IconSize = 14 } };
            ToolTip.SetTip(delete, "Aufgabe löschen");
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
