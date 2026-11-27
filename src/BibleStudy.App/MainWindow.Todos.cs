using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using BibleStudy.Core.Models;
using BibleStudy.Core.Storage;

namespace BibleStudy.App;

/// <summary>
/// The checklist: tasks with sub-tasks, an optional due date and verse, your own order, progress, and a place for what is done.
/// </summary>
public partial class MainWindow
{
    string? _todoEditing;      // the line that is being edited right now (vault and id: two vaults can have the same id)
    string? _todoAddingTo;     // the task that shows an input line for a new sub-task
    bool _todoDoneOpen;
    bool _todoAllSyncing;
    CheckBox? _todoPopupAll;

    /// <summary>The to-dos of one vault, sorted into what is open and done; with "all vaults" on there is one group per vault.</summary>
    sealed record TodoGroup(string Name, VaultStore Store, List<TodoItem> All, ILookup<long, TodoItem> Kids, List<TodoItem> Open, List<TodoItem> Finished);

    static string TodoKey(string vault, long id) => vault + "|" + id;

    StackPanel? _todoPopupList;   // the list inside the open to-do popup (null while closed)
    TextBlock? _todoPopupCount;

    void InitTodos()
    {
        TodoClearBtn.Click += (_, _) => { foreach (var g in TodoGroups()) g.Store.ClearDoneTodos(); RefreshTodos(); };   // what is listed, done, goes
        TodoTopBtn.Click += (_, _) => ShowTodoPopup();
        TodoAllVaultsCheck.IsChecked = _settings.TodosAllVaults;
        TodoAllVaultsCheck.IsCheckedChanged += (_, _) => SetTodosAllVaults(TodoAllVaultsCheck.IsChecked == true);
        if (AppPlatform.IsMobile)
        {
            // a phone: the verse and the date for a new to-do show up once something is typed; "all vaults" only where there are several.
            // The list began 300 px down the screen.
            var options = TodoVerseCheck.Parent as Control;
            void Fit()
            {
                if (!IsPhone) { if (options is not null) options.IsVisible = true; TodoAllVaultsCheck.IsVisible = true; return; }
                if (options is not null) options.IsVisible = !string.IsNullOrEmpty(TodoBox.Text) || TodoDue.SelectedDate is not null || TodoVerseCheck.IsChecked == true;
                TodoAllVaultsCheck.IsVisible = VaultNames().Count() > 1 || TodoAllVaultsCheck.IsChecked == true;
            }
            TodoBox.TextChanged += (_, _) => Fit();
            TodoBox.GotFocus += (_, _) => Fit();
            TodoDue.SelectedDateChanged += (_, _) => Fit();
            TodoAllVaultsCheck.AttachedToVisualTree += (_, _) => Fit();
            Fit();
        }
    }

    /// <summary>The same checklist as a small window from the top bar: write something, press Enter, tick it off. Reachable from everywhere.</summary>
    void ShowTodoPopup()
    {
        var box = new TextBox { PlaceholderText = L("Neues To-do… (Enter)") };
        var add = new Button { Content = L("Hinzufügen"), Classes = { "primary" }, Margin = new(8, 0, 0, 0) };
        void Add()
        {
            if (string.IsNullOrWhiteSpace(box.Text)) return;
            _vault.AddTodo(box.Text, null); box.Text = ""; RefreshTodos(); box.Focus();
        }
        add.Click += (_, _) => Add();
        box.KeyDown += (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; Add(); } };
        var input = new DockPanel { Margin = new(0, 0, 0, 10) };
        DockPanel.SetDock(add, Dock.Right);
        input.Children.Add(add); input.Children.Add(box);
        var list = new StackPanel { Spacing = 4 };
        var count = new TextBlock { Classes = { "caption" }, Margin = new(0, 0, 0, 6) };
        var title = new TextBlock { Text = L("Meine To-dos"), FontSize = 17, FontWeight = FontWeight.SemiBold, Margin = new(0, 0, 0, 10) };
        var allVaults = new CheckBox { Content = L("To-dos aller Vaults zeigen"), IsChecked = _settings.TodosAllVaults, Margin = new(0, 0, 0, 8) };
        ToolTip.SetTip(allVaults, L("Zeigt auch die To-dos der anderen Vaults. Sie lassen sich hier genauso abhaken, ändern und löschen; neue To-dos kommen in den aktiven Vault."));
        allVaults.IsCheckedChanged += (_, _) => SetTodosAllVaults(allVaults.IsChecked == true);
        var body = new StackPanel { Width = 420, Children = { title, allVaults, input, count, new ScrollViewer { Content = list, MaxHeight = Math.Max(200, Bounds.Height - 340), HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled } } };
        var flyout = new Flyout { Content = body, Placement = PlacementMode.BottomEdgeAlignedRight };
        flyout.Closed += (_, _) => { _todoPopupList = null; _todoPopupCount = null; _todoPopupAll = null; _todoEditing = null; _todoAddingTo = null; };
        _todoPopupList = list; _todoPopupCount = count; _todoPopupAll = allVaults;
        RefreshTodos();
        flyout.ShowAt(Anchor(TodoTopBtn));
        box.Focus();
    }

    void AddTodo()
    {
        var text = TodoBox.Text?.Trim();
        if (string.IsNullOrEmpty(text)) return;
        VerseRef? r = TodoVerseCheck.IsChecked == true && _selected is int v ? new VerseRef(_curBook, _curChapter, v) : null;
        _vault.AddTodo(text, r, null, TodoDue.SelectedDate is { } d ? DateOnly.FromDateTime(d.Date) : null);
        TodoBox.Text = ""; TodoDue.SelectedDate = null;
        RefreshTodos();
    }

    /// <summary>The vaults whose to-dos show: the active one, and with "all vaults" on every other vault that has any.</summary>
    List<TodoGroup> TodoGroups()
    {
        var names = _settings.TodosAllVaults ? [_vault.Name, .. VaultNames().Where(n => n != _vault.Name && !IsGuestVault(n))] : new List<string> { _vault.Name };
        var groups = new List<TodoGroup>();
        foreach (var name in names)
        {
            try
            {
                var store = StoreOf(name);
                var all = store.GetTodos();
                if (all.Count == 0 && name != _vault.Name) continue;
                var ids = all.Select(t => t.Id).ToHashSet();
                var kids = all.Where(t => t.ParentId is long p && ids.Contains(p)).ToLookup(t => t.ParentId!.Value);
                var tops = all.Where(t => t.ParentId is not long p || !ids.Contains(p)).ToList();
                groups.Add(new TodoGroup(name, store, all, kids, tops.Where(t => !t.Done).ToList(), tops.Where(t => t.Done).ToList()));
            }
            catch { /* a vault that cannot be read just does not show */ }
        }
        return groups;
    }

    /// <summary>"All vaults" for the to-dos on or off (the checkbox of the list and the one of the small window stay equal).</summary>
    void SetTodosAllVaults(bool on)
    {
        if (_todoAllSyncing) return;
        _todoAllSyncing = true;
        try { TodoAllVaultsCheck.IsChecked = on; if (_todoPopupAll is not null) _todoPopupAll.IsChecked = on; }
        finally { _todoAllSyncing = false; }
        if (_settings.TodosAllVaults == on) return;
        _settings.TodosAllVaults = on; SaveSettings();
        RefreshTodos();
    }

    void RefreshTodos()
    {
        var groups = TodoGroups();
        var total = groups.Sum(g => g.All.Count);
        var done = groups.Sum(g => g.All.Count(t => t.Done));
        TodoBar.Maximum = Math.Max(1, total); TodoBar.Value = done;
        TodoCount.Text = total == 0 ? "" : L("{0} von {1} erledigt", done, total);
        if (TodoBar.Parent is Control barRow) barRow.IsVisible = total > 0;   // an empty bar with no to-dos says nothing
        TodoClearBtn.IsVisible = done > 0;

        if (_todoPopupCount is not null) _todoPopupCount.Text = TodoCount.Text;
        FillTodos(TodoPanel, groups);
        if (_todoPopupList is not null) FillTodos(_todoPopupList, groups);
    }

    void FillTodos(Panel panel, List<TodoGroup> groups)
    {
        panel.Children.Clear();
        if (groups.All(g => g.All.Count == 0))
        {
            panel.Children.Add(new TextBlock { Text = L("Noch keine To-dos. Schreibe oben eins hinein, z. B. „Römer 8 vergleichen“; auf Wunsch hängt es den angeklickten Vers an und ein Datum."), TextWrapping = TextWrapping.Wrap, Opacity = 0.65, Margin = new(2, 8) });
            return;
        }
        var named = _settings.TodosAllVaults && groups.Count > 1;   // with several vaults each one has a heading
        foreach (var g in groups.Where(g => g.All.Count > 0))
        {
            if (named) panel.Children.Add(new TextBlock { Text = g.Name, FontWeight = FontWeight.SemiBold, Margin = new(2, panel.Children.Count == 0 ? 0 : 14, 0, 4), Foreground = Res("BsAccent") });
            for (var i = 0; i < g.Open.Count; i++) AddTodoWithKids(panel, g, g.Open[i], i, g.Open.Count);
            if (g.Finished.Count == 0) continue;
            var list = new StackPanel { Spacing = 4 };
            for (var i = 0; i < g.Finished.Count; i++) AddTodoWithKids(list, g, g.Finished[i], i, g.Finished.Count);
            var expander = new Expander { Header = L("Erledigt ({0})", g.Finished.Count), IsExpanded = _todoDoneOpen, Content = list, Margin = new(0, 10, 0, 0) };
            expander.PropertyChanged += (_, e) => { if (e.Property == Expander.IsExpandedProperty) _todoDoneOpen = expander.IsExpanded; };
            panel.Children.Add(expander);
        }
    }

    void AddTodoWithKids(Panel into, TodoGroup g, TodoItem t, int index, int count)
    {
        into.Children.Add(TodoRow(g, t, false, index, count));
        var mine = g.Kids[t.Id].ToList();
        for (var i = 0; i < mine.Count; i++) into.Children.Add(TodoRow(g, mine[i], true, i, mine.Count));
        if (_todoAddingTo == TodoKey(g.Name, t.Id)) into.Children.Add(SubTaskInput(g, t.Id));
    }

    Control SubTaskInput(TodoGroup g, long parent)
    {
        var box = new TextBox { PlaceholderText = L("Teil-To-do, Enter zum Hinzufügen (Esc: abbrechen)"), Margin = new(34, 0, 0, 2) };
        box.AttachedToVisualTree += (_, _) => box.Focus();
        box.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { e.Handled = true; _todoAddingTo = null; RefreshTodos(); }
            else if (e.Key == Key.Enter) { e.Handled = true; if (!string.IsNullOrWhiteSpace(box.Text)) g.Store.AddTodo(box.Text, null, parent); RefreshTodos(); }   // stays open for the next one
        };
        return box;
    }

    Control TodoRow(TodoGroup g, TodoItem t, bool child, int index, int count)
    {
        var id = t.Id;
        var store = g.Store;   // every to-do changes in its own vault
        var key = TodoKey(g.Name, id);
        var today = DateOnly.FromDateTime(DateTime.Today);
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new(child ? 28 : 0, 0, 0, 0) };

        Control left;
        if (_todoEditing == key)
        {
            var edit = new TextBox { Text = t.Text, AcceptsReturn = false };
            edit.AttachedToVisualTree += (_, _) => { edit.Focus(); edit.SelectAll(); };
            void Finish(bool save)
            {
                if (_todoEditing != key) return;
                _todoEditing = null;
                if (save) store.SetTodoText(id, edit.Text ?? "");
                RefreshTodos();
            }
            edit.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter) { e.Handled = true; Finish(true); }
                else if (e.Key == Key.Escape) { e.Handled = true; Finish(false); }
            };
            edit.LostFocus += (_, _) => Finish(true);
            left = edit;
        }
        else
        {
            var label = new TextBlock { Text = t.Text, TextWrapping = TextWrapping.Wrap, Opacity = t.Done ? 0.55 : 1, TextDecorations = t.Done ? TextDecorations.Strikethrough : null };
            var check = new CheckBox { IsChecked = t.Done, Content = label, VerticalAlignment = VerticalAlignment.Center };
            check.IsCheckedChanged += (_, _) => { store.SetTodoDone(id, check.IsChecked == true); RefreshTodos(); };
            left = check;
        }
        row.Children.Add(left);

        var tools = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        // on a phone the date and the verse go on a line of their own below the text: beside it they left the text 150 px, and it broke
        // after nearly every word (and the date stuck to its end)
        var meta = IsPhone ? new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new(30, 2, 0, 4) } : tools;
        if (t.Due is { } due && !t.Done)
        {
            var late = due < today;
            var text = due == today ? L("heute") : due == today.AddDays(1) ? L("morgen") : due.ToString("d", Loc.Culture);
            var badge = new TextBlock { Text = text, FontSize = 12, FontWeight = late ? FontWeight.SemiBold : FontWeight.Normal, Foreground = late ? new SolidColorBrush(Color.Parse("#E5484D")) : Res("BsMuted"), VerticalAlignment = VerticalAlignment.Center };
            if (late) ToolTip.SetTip(badge, L("Überfällig"));
            meta.Children.Add(badge);
        }
        else if (t.Due is { } doneDue) meta.Children.Add(new TextBlock { Text = doneDue.ToString("d", Loc.Culture), FontSize = 12, Opacity = 0.5, VerticalAlignment = VerticalAlignment.Center });
        if (t.Ref is { } r)
        {
            var link = new Button { Content = Label(r), FontSize = 12, Padding = new(8, 2), MinHeight = IsPhone ? 32 : 0, VerticalContentAlignment = VerticalAlignment.Center };
            link.Click += async (_, _) => await GoToAsync(r.Book, r.Chapter, r.Verse);
            meta.Children.Add(link);
        }

        var more = new Button { Classes = { "tool" }, Width = IsPhone ? 36 : 28, Height = IsPhone ? 36 : 28, Content = new Icon { Data = Geometry.Parse("M5 12h.01 M12 12h.01 M19 12h.01"), IconSize = 16 } };
        ToolTip.SetTip(more, L("Mehr: bearbeiten, Teil-To-do, Datum, verschieben"));
        var menu = new MenuFlyout();   // a flyout of its own: a context menu attached to the row cannot be opened on another control
        MenuItem Item(string header, Action act) { var m = new MenuItem { Header = header }; m.Click += (_, _) => act(); menu.Items.Add(m); return m; }
        Item(L("Bearbeiten"), () => { _todoEditing = key; RefreshTodos(); });
        if (!child) Item(L("Teil-To-do hinzufügen"), () => { _todoAddingTo = key; RefreshTodos(); });
        Item(L("Fälligkeitsdatum …"), () => ChooseTodoDue(more, store, t));
        if (index > 0) Item(L("Nach oben"), () => { store.MoveTodo(id, -1); RefreshTodos(); });
        if (index < count - 1) Item(L("Nach unten"), () => { store.MoveTodo(id, 1); RefreshTodos(); });
        if (t.Ref is null && _selected is int sel) Item(L("Angeklickten Vers anhängen"), () => { store.SetTodoRef(id, new VerseRef(_curBook, _curChapter, sel)); RefreshTodos(); });
        more.Click += (_, _) => menu.ShowAt(more);
        row.ContextFlyout = menu;
        tools.Children.Add(more);

        var delete = new Button { Classes = { "tool" }, Width = IsPhone ? 36 : 28, Height = IsPhone ? 36 : 28, Content = new Icon { Data = Geometry.Parse("M18 6L6 18 M6 6l12 12"), IconSize = 14 } };
        ToolTip.SetTip(delete, child ? L("Teil-To-do löschen") : L("To-do löschen (mit seinen Teil-To-dos)"));
        delete.Click += (_, _) => { store.DeleteTodo(id); RefreshTodos(); };
        tools.Children.Add(delete);

        Grid.SetColumn(tools, 1);
        row.Children.Add(tools);
        if (meta != tools && meta.Children.Count > 0)
        {
            row.RowDefinitions = new RowDefinitions("Auto,Auto");
            Grid.SetRow(meta, 1); Grid.SetColumnSpan(meta, 2);
            row.Children.Add(meta);
        }
        return row;
    }

    void ChooseTodoDue(Control anchor, VaultStore store, TodoItem t)
    {
        var calendar = new Calendar { SelectedDate = t.Due is { } d ? d.ToDateTime(TimeOnly.MinValue) : null };
        var none = new Button { Content = L("Kein Datum"), HorizontalAlignment = HorizontalAlignment.Left };
        var flyout = new Flyout { Content = new StackPanel { Spacing = 6, Children = { calendar, none } } };
        calendar.SelectedDatesChanged += (_, _) =>
        {
            if (calendar.SelectedDate is not { } picked) return;
            flyout.Hide(); store.SetTodoDue(t.Id, DateOnly.FromDateTime(picked.Date)); RefreshTodos();
        };
        none.Click += (_, _) => { flyout.Hide(); store.SetTodoDue(t.Id, null); RefreshTodos(); };
        flyout.ShowAt(anchor);
    }
}
