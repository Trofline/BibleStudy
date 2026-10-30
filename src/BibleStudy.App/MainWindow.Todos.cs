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
    long? _todoEditing;       // the line that is being edited right now
    long? _todoAddingTo;      // the task that shows an input line for a new sub-task
    bool _todoDoneOpen;

    StackPanel? _todoPopupList;   // the list inside the open to-do popup (null while closed)
    TextBlock? _todoPopupCount;

    void InitTodos()
    {
        TodoClearBtn.Click += (_, _) => { _vault.ClearDoneTodos(); RefreshTodos(); };
        TodoTopBtn.Click += (_, _) => ShowTodoPopup();
    }

    /// <summary>The same checklist as a small window from the top bar: write something, press Enter, tick it off. Reachable from everywhere.</summary>
    void ShowTodoPopup()
    {
        var box = new TextBox { Watermark = L("Neue Aufgabe… (Enter)") };
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
        var title = new TextBlock { Text = L("Meine To-Dos"), FontSize = 17, FontWeight = FontWeight.SemiBold, Margin = new(0, 0, 0, 10) };
        var body = new StackPanel { Width = 420, Children = { title, input, count, new ScrollViewer { Content = list, MaxHeight = Math.Max(200, Bounds.Height - 340), HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled } } };
        var flyout = new Flyout { Content = body, Placement = PlacementMode.BottomEdgeAlignedRight };
        flyout.Closed += (_, _) => { _todoPopupList = null; _todoPopupCount = null; _todoEditing = null; _todoAddingTo = null; };
        _todoPopupList = list; _todoPopupCount = count;
        RefreshTodos();
        flyout.ShowAt(TodoTopBtn);
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

    void RefreshTodos()
    {
        var all = _vault.GetTodos();
        var ids = all.Select(t => t.Id).ToHashSet();
        var kids = all.Where(t => t.ParentId is long p && ids.Contains(p)).ToLookup(t => t.ParentId!.Value);
        var tops = all.Where(t => t.ParentId is not long p || !ids.Contains(p)).ToList();
        var open = tops.Where(t => !t.Done).ToList();
        var finished = tops.Where(t => t.Done).ToList();

        var done = all.Count(t => t.Done);
        TodoBar.Maximum = Math.Max(1, all.Count); TodoBar.Value = done;
        TodoCount.Text = all.Count == 0 ? "" : L("{0} von {1} erledigt", done, all.Count);
        TodoClearBtn.IsVisible = done > 0;

        if (_todoPopupCount is not null) _todoPopupCount.Text = TodoCount.Text;
        FillTodos(TodoPanel, all, kids, open, finished);
        if (_todoPopupList is not null) FillTodos(_todoPopupList, all, kids, open, finished);
    }

    void FillTodos(Panel panel, List<TodoItem> all, ILookup<long, TodoItem> kids, List<TodoItem> open, List<TodoItem> finished)
    {
        panel.Children.Clear();
        if (all.Count == 0)
        {
            panel.Children.Add(new TextBlock { Text = L("Noch keine Aufgaben. Schreibe oben eine hinein, z. B. „Römer 8 vergleichen“; auf Wunsch hängt sie den angeklickten Vers an und ein Datum."), TextWrapping = TextWrapping.Wrap, Opacity = 0.65, Margin = new(2, 8) });
            return;
        }
        for (var i = 0; i < open.Count; i++) AddTodoWithKids(panel, open[i], kids, i, open.Count);
        if (finished.Count > 0)
        {
            var list = new StackPanel { Spacing = 4 };
            for (var i = 0; i < finished.Count; i++) AddTodoWithKids(list, finished[i], kids, i, finished.Count);
            var expander = new Expander { Header = L("Erledigt ({0})", finished.Count), IsExpanded = _todoDoneOpen, Content = list, Margin = new(0, 10, 0, 0) };
            expander.PropertyChanged += (_, e) => { if (e.Property == Expander.IsExpandedProperty) _todoDoneOpen = expander.IsExpanded; };
            panel.Children.Add(expander);
        }
    }

    void AddTodoWithKids(Panel into, TodoItem t, ILookup<long, TodoItem> kids, int index, int count)
    {
        into.Children.Add(TodoRow(t, false, index, count));
        var mine = kids[t.Id].ToList();
        for (var i = 0; i < mine.Count; i++) into.Children.Add(TodoRow(mine[i], true, i, mine.Count));
        if (_todoAddingTo == t.Id) into.Children.Add(SubTaskInput(t.Id));
    }

    Control SubTaskInput(long parent)
    {
        var box = new TextBox { Watermark = L("Unteraufgabe, Enter zum Hinzufügen (Esc: abbrechen)"), Margin = new(34, 0, 0, 2) };
        box.AttachedToVisualTree += (_, _) => box.Focus();
        box.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { e.Handled = true; _todoAddingTo = null; RefreshTodos(); }
            else if (e.Key == Key.Enter) { e.Handled = true; if (!string.IsNullOrWhiteSpace(box.Text)) _vault.AddTodo(box.Text, null, parent); RefreshTodos(); }   // stays open for the next one
        };
        return box;
    }

    Control TodoRow(TodoItem t, bool child, int index, int count)
    {
        var id = t.Id;
        var today = DateOnly.FromDateTime(DateTime.Today);
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new(child ? 28 : 0, 0, 0, 0) };

        Control left;
        if (_todoEditing == id)
        {
            var edit = new TextBox { Text = t.Text, AcceptsReturn = false };
            edit.AttachedToVisualTree += (_, _) => { edit.Focus(); edit.SelectAll(); };
            void Finish(bool save)
            {
                if (_todoEditing != id) return;
                _todoEditing = null;
                if (save) _vault.SetTodoText(id, edit.Text ?? "");
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
            check.IsCheckedChanged += (_, _) => { _vault.SetTodoDone(id, check.IsChecked == true); RefreshTodos(); };
            left = check;
        }
        row.Children.Add(left);

        var tools = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        if (t.Due is { } due && !t.Done)
        {
            var late = due < today;
            var text = due == today ? L("heute") : due == today.AddDays(1) ? L("morgen") : due.ToString("dd.MM.yyyy");
            var badge = new TextBlock { Text = text, FontSize = 12, FontWeight = late ? FontWeight.SemiBold : FontWeight.Normal, Foreground = late ? new SolidColorBrush(Color.Parse("#E5484D")) : Res("BsMuted"), VerticalAlignment = VerticalAlignment.Center };
            if (late) ToolTip.SetTip(badge, L("Überfällig"));
            tools.Children.Add(badge);
        }
        else if (t.Due is { } doneDue) tools.Children.Add(new TextBlock { Text = doneDue.ToString("dd.MM.yyyy"), FontSize = 12, Opacity = 0.5, VerticalAlignment = VerticalAlignment.Center });
        if (t.Ref is { } r)
        {
            var link = new Button { Content = Label(r), FontSize = 12, Padding = new(8, 2) };
            link.Click += async (_, _) => await GoToAsync(r.Book, r.Chapter, r.Verse);
            tools.Children.Add(link);
        }

        var more = new Button { Classes = { "tool" }, Width = 28, Height = 28, Content = new Icon { Data = Geometry.Parse("M5 12h.01 M12 12h.01 M19 12h.01"), IconSize = 16 } };
        ToolTip.SetTip(more, L("Mehr: bearbeiten, Unteraufgabe, Datum, verschieben"));
        var menu = new ContextMenu();
        MenuItem Item(string header, Action act) { var m = new MenuItem { Header = header }; m.Click += (_, _) => act(); menu.Items.Add(m); return m; }
        Item(L("Bearbeiten"), () => { _todoEditing = id; RefreshTodos(); });
        if (!child) Item(L("Unteraufgabe hinzufügen"), () => { _todoAddingTo = id; RefreshTodos(); });
        Item(L("Fälligkeitsdatum …"), () => ChooseTodoDue(more, t));
        if (index > 0) Item(L("Nach oben"), () => { _vault.MoveTodo(id, -1); RefreshTodos(); });
        if (index < count - 1) Item(L("Nach unten"), () => { _vault.MoveTodo(id, 1); RefreshTodos(); });
        if (t.Ref is null && _selected is int sel) Item(L("Angeklickten Vers anhängen"), () => { _vault.SetTodoRef(id, new VerseRef(_curBook, _curChapter, sel)); RefreshTodos(); });
        more.Click += (_, _) => menu.Open(more);
        row.ContextMenu = menu;
        tools.Children.Add(more);

        var delete = new Button { Classes = { "tool" }, Width = 28, Height = 28, Content = new Icon { Data = Geometry.Parse("M18 6L6 18 M6 6l12 12"), IconSize = 14 } };
        ToolTip.SetTip(delete, child ? L("Unteraufgabe löschen") : L("Aufgabe löschen (mit ihren Unteraufgaben)"));
        delete.Click += (_, _) => { _vault.DeleteTodo(id); RefreshTodos(); };
        tools.Children.Add(delete);

        Grid.SetColumn(tools, 1);
        row.Children.Add(tools);
        return row;
    }

    void ChooseTodoDue(Control anchor, TodoItem t)
    {
        var calendar = new Calendar { SelectedDate = t.Due is { } d ? d.ToDateTime(TimeOnly.MinValue) : null };
        var none = new Button { Content = L("Kein Datum"), HorizontalAlignment = HorizontalAlignment.Left };
        var flyout = new Flyout { Content = new StackPanel { Spacing = 6, Children = { calendar, none } } };
        calendar.SelectedDatesChanged += (_, _) =>
        {
            if (calendar.SelectedDate is not { } picked) return;
            flyout.Hide(); _vault.SetTodoDue(t.Id, DateOnly.FromDateTime(picked.Date)); RefreshTodos();
        };
        none.Click += (_, _) => { flyout.Hide(); _vault.SetTodoDue(t.Id, null); RefreshTodos(); };
        flyout.ShowAt(anchor);
    }
}
