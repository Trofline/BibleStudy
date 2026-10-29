using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using BibleStudy.Core.Models;
using BibleStudy.Core.Storage;

namespace BibleStudy.App;

/// <summary>
/// The notes tab as a tree: your folders first, then the notes without a folder grouped by Bible book. Searchable, groups fold in and out,
/// a note moves into a folder with the folder box beside the editor or with a right click.
/// </summary>
public partial class MainWindow
{
    readonly HashSet<string> _noteOpen = [];
    bool _notesTouched;
    bool _noteFolderLoading;

    void InitNotes()
    {
        NotesSearchBox.TextChanged += (_, _) => RefreshNotesTree();
        NotesFolderBtn.Click += (_, _) => { NewNoteFolderBox.IsVisible = !NewNoteFolderBox.IsVisible; if (NewNoteFolderBox.IsVisible) NewNoteFolderBox.Focus(); };
        NewNoteFolderBox.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter || string.IsNullOrWhiteSpace(NewNoteFolderBox.Text)) return;
            var id = _vault.AddFolder(NewNoteFolderBox.Text);
            NewNoteFolderBox.Text = ""; NewNoteFolderBox.IsVisible = false;
            _noteOpen.Add("f:" + id); _notesTouched = true;
            RefreshCollection();
        };
        NotesMoreBtn.Click += (_, _) =>
        {
            var menu = new MenuFlyout();
            var open = new MenuItem { Header = L("Alle aufklappen") };
            open.Click += (_, _) => { _notesTouched = true; foreach (var k in AllNoteGroupKeys()) _noteOpen.Add(k); RefreshNotesTree(); };
            var close = new MenuItem { Header = L("Alle zuklappen") };
            close.Click += (_, _) => { _notesTouched = true; _noteOpen.Clear(); RefreshNotesTree(); };
            menu.Items.Add(open); menu.Items.Add(close);
            menu.ShowAt(NotesMoreBtn);
        };
        NoteFolderBox.SelectionChanged += (_, _) =>
        {
            if (_noteFolderLoading || _noteRef is not { } r) return;
            if (_vault.GetNotes().Any(n => n.Ref == r)) { _vault.SetNoteFolder(r, SelectedNoteFolder()); RefreshNotesTree(); }
        };
        RefreshNotesTree();
    }

    long? SelectedNoteFolder() => (NoteFolderBox.SelectedItem as Folder)?.Id;

    IEnumerable<string> AllNoteGroupKeys()
    {
        foreach (var f in _vault.GetFolders()) yield return "f:" + f.Id;
        foreach (var b in _vault.GetNotes().Select(n => n.Ref.Book).Distinct()) yield return "b:" + b;
    }

    /// <summary>Fills the folder box beside the editor for the verse that is shown (called when the note editor changes verse).</summary>
    void LoadNoteFolderBox(VerseRef r)
    {
        _noteFolderLoading = true;
        var folders = _vault.GetFolders();
        NoteFolderBox.ItemsSource = new List<object> { L("(kein Ordner)") }.Concat(folders).ToList();
        var current = _vault.GetNotes().FirstOrDefault(n => n.Ref == r)?.FolderId;
        NoteFolderBox.SelectedIndex = current is long id && folders.FindIndex(f => f.Id == id) is var i and >= 0 ? i + 1 : 0;
        _noteFolderLoading = false;
    }

    static bool NoteMatches(string label, string text, string[] words) => words.All(w => label.Contains(w, StringComparison.CurrentCultureIgnoreCase) || text.Contains(w, StringComparison.CurrentCultureIgnoreCase));

    void RefreshNotesTree()
    {
        if (_vault is null) return;
        var all = _vault.GetNotes();
        var query = NotesSearchBox.Text?.Trim() ?? "";
        var words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var shown = words.Length == 0 ? all : all.Where(n => NoteMatches(Label(n.Ref), n.Text, words)).ToList();
        var folders = _vault.GetFolders().OrderBy(f => f.Name, Comparer<string>.Create(NaturalCompare)).ToList();
        NotesEmpty.IsVisible = all.Count == 0;
        NotesCount.Text = all.Count == 0 ? "" : words.Length > 0 ? L("{0} von {1} Notizen", shown.Count, all.Count) : L("{0} Notizen", all.Count);
        NotesTree.Children.Clear();

        // few notes: everything open; many: folders and the book you are reading, until the user decides
        if (!_notesTouched)
        {
            _noteOpen.Clear();
            foreach (var f in folders) _noteOpen.Add("f:" + f.Id);
            if (all.Count <= 40) foreach (var b in all.Select(n => n.Ref.Book).Distinct()) _noteOpen.Add("b:" + b);
            else _noteOpen.Add("b:" + _curBook);
        }
        var searching = words.Length > 0;

        void Group(string key, string title, int count, Action<StackPanel> fill, MenuFlyout? menu = null)
        {
            var open = searching || _noteOpen.Contains(key);
            var head = new Button { Classes = { "ghost" }, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new(6, 5), Margin = new(0, 2, 0, 0), Content = $"{(open ? "▾" : "▸")}  {title}   ·   {count}", FontWeight = FontWeight.SemiBold };
            head.Click += (_, _) => { _notesTouched = true; if (!_noteOpen.Remove(key)) _noteOpen.Add(key); RefreshNotesTree(); };
            if (menu is not null) head.ContextFlyout = menu;
            NotesTree.Children.Add(head);
            if (!open) return;
            var body = new StackPanel { Spacing = 1, Margin = new(14, 0, 0, 4) };
            fill(body);
            NotesTree.Children.Add(body);
        }

        foreach (var f in folders)
        {
            var inFolder = shown.Where(n => n.FolderId == f.Id).ToList();
            if (inFolder.Count == 0 && searching) continue;
            var folder = f;
            var menu = new MenuFlyout();
            var delete = new MenuItem { Header = L("Ordner löschen (die Notizen bleiben)") };
            delete.Click += (_, _) => { _vault.DeleteFolder(folder.Id); RefreshCollection(); };
            menu.Items.Add(delete);
            Group("f:" + f.Id, f.Name, inFolder.Count, body => { foreach (var n in inFolder) body.Children.Add(NoteRow(n, folders)); }, menu);
        }
        var folderIds = folders.Select(f => f.Id).ToHashSet();
        var loose = shown.Where(n => n.FolderId is not long id || !folderIds.Contains(id)).ToList();
        foreach (var g in loose.GroupBy(n => n.Ref.Book).OrderBy(g => BookOrder(g.Key)))
        {
            var book = g.Key;
            Group("b:" + book, BookName(book), g.Count(), body => { foreach (var n in g) body.Children.Add(NoteRow(n, folders)); });
        }
        if (all.Count > 0 && shown.Count == 0) NotesTree.Children.Add(new TextBlock { Text = L("Keine Notiz passt zur Suche."), Classes = { "caption" }, Margin = new(2, 8) });
    }

    Control NoteRow(Note n, List<Folder> folders)
    {
        var text = n.Text.ReplaceLineEndings(" ").Trim();
        var title = new TextBlock { Text = Label(n.Ref), FontWeight = FontWeight.SemiBold, FontSize = 12.5 };
        title.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("BsAccent"));
        var snippet = new TextBlock { Text = Shorten(text, 140), TextWrapping = TextWrapping.Wrap, MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis, FontSize = 12.5, Opacity = 0.9 };
        var row = new Border { Padding = new(8, 5), CornerRadius = new(8), Cursor = new Cursor(StandardCursorType.Hand), Background = Brushes.Transparent, Child = new StackPanel { Children = { title, snippet } } };
        if (_noteRef == n.Ref) row.Bind(Border.BackgroundProperty, this.GetResourceObservable("BsSurface2"));
        else
        {
            row.PointerEntered += (_, _) => row.Bind(Border.BackgroundProperty, this.GetResourceObservable("BsSurface2"));
            row.PointerExited += (_, _) => { row.ClearValue(Border.BackgroundProperty); row.Background = Brushes.Transparent; };
        }
        row.PointerPressed += async (_, e) =>
        {
            if (e.GetCurrentPoint(row).Properties.IsLeftButtonPressed) await GoToAsync(n.Ref.Book, n.Ref.Chapter, n.Ref.Verse);
        };

        var menu = new MenuFlyout();
        var move = new MenuItem { Header = L("In Ordner verschieben") };
        var none = new MenuItem { Header = L("(kein Ordner)") };
        none.Click += (_, _) => { _vault.SetNoteFolder(n.Ref, null); RefreshNotesTree(); if (_noteRef == n.Ref) LoadNoteFolderBox(n.Ref); };
        move.Items.Add(none);
        foreach (var f in folders)
        {
            var id = f.Id; var item = new MenuItem { Header = f.Name };
            item.Click += (_, _) => { _vault.SetNoteFolder(n.Ref, id); _noteOpen.Add("f:" + id); RefreshNotesTree(); if (_noteRef == n.Ref) LoadNoteFolderBox(n.Ref); };
            move.Items.Add(item);
        }
        var delete = new MenuItem { Header = L("Notiz löschen") };
        delete.Click += async (_, _) =>
        {
            _vault.SetNote(n.Ref, "");
            if (_noteRef == n.Ref) { NoteEditor.Text = _noteLoaded = ""; }
            RefreshLists();
            await ShowChapterAsync(_selected);
        };
        menu.Items.Add(move); menu.Items.Add(new Separator()); menu.Items.Add(delete);
        row.ContextFlyout = menu;
        return row;
    }
}
