using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;

namespace BibleStudy.App;

/// <summary>
/// Choosing in the explorer as in the gallery of a phone: "Auswählen" turns every row into a box to tick (a tap ticks or unticks, nothing opens),
/// a bar below says how many are chosen and does one thing with all of them: move, color, tags, a reading path, delete. Choosing starts only
/// with that button: a finger that rests on a row while scrolling must never start it (it did, and scrolling chose verses). A tap on a row
/// opens it, a long press shows its menu. On a computer Ctrl and Shift keep working as before.
/// </summary>
public partial class MainWindow
{
    bool _exPick;
    Button _exPickBtn = null!;
    Border _exPickBar = null!;
    TextBlock _exPickCount = null!;
    Button _exPickMove = null!, _exPickMore = null!, _exPickDelete = null!, _exPickPath = null!, _exPickAll = null!;

    void BuildExplorerPick()
    {
        _exPickBtn = new Button { Content = L("Auswählen"), Margin = new(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        ToolTip.SetTip(_exPickBtn, L("Mehrere Einträge antippen und dann für alle auf einmal verschieben, färben, löschen oder zu einem Lesepfad machen"));
        _exPickBtn.Click += (_, _) => SetExPick(!_exPick);

        _exPickCount = new TextBlock { VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeight.SemiBold, Margin = new(4, 0, 10, 0) };
        Button Action(string text, string tip)
        {
            var b = new Button { Content = text, Margin = new(0, 0, 6, 6), VerticalAlignment = VerticalAlignment.Center };
            ToolTip.SetTip(b, tip);
            return b;
        }
        _exPickAll = Action(L("Alle"), L("Alle Einträge dieser Liste wählen (noch einmal: keinen)"));
        _exPickAll.Click += (_, _) =>
        {
            var everything = _exSel.Count == _exRows.Count && _exRows.Count > 0;
            _exSel.Clear();
            if (!everything) _exSel.AddRange(_exRows);
            ExShowSelection();
        };
        _exPickMove = Action(L("Verschieben"), L("In einen Ordner verschieben"));
        _exPickMove.Click += (_, _) => ShowAsMenu(_exPickMove, MoveMenu(_exSel.ToList()));
        _exPickPath = Action(L("Lesepfad"), L("Die gewählten Verse in der Reihenfolge, in der du sie angetippt hast, zu einem Lesepfad machen"));
        _exPickPath.Click += (_, _) =>
        {
            // no path yet: a new one at once; else onto the path used last, onto another, or a new one
            var items = _exSel.ToList();
            if (ReadingPaths().Count == 0) { PathFromExplorer(items, _exPickPath); return; }
            var fly = new MenuFlyout();
            FillItemPathMenu(fly.Items, items.Count == 1 ? items[0] : null, items, _exPickPath);
            fly.ShowAt(_exPickPath);
        };
        _exPickMore = Action(L("Mehr …"), L("Farbe, Tags, in einen anderen Vault"));
        _exPickMore.Click += (_, _) =>
        {
            var items = _exSel.ToList();
            var fly = new MenuFlyout();
            fly.Items.Add(ColorMenu(items, _exPickMore));
            fly.Items.Add(TagMenu(items, _exPickMore));
            fly.Items.Add(OtherVaultMenu(items));
            if (IsPhone) { fly.Items.Add(new Separator()); fly.Items.Add(MenuAction(L("Löschen"), () => ExDelete(_exSel.ToList()))); }   // a phone: one button less in the bar
            fly.ShowAt(_exPickMore);
        };
        _exPickDelete = Action(L("Löschen"), L("In den Papierkorb legen (Rückgängig geht)"));
        _exPickDelete.Click += (_, _) => ExDelete(_exSel.ToList());
        var done = new Button { Content = L("Fertig"), Classes = { "primary" }, Margin = new(0, 0, 0, 6), VerticalAlignment = VerticalAlignment.Center };
        done.Click += (_, _) => SetExPick(false);
        var actions = new WrapPanel { Children = { _exPickAll, _exPickCount, _exPickMove, _exPickPath, _exPickMore } };
        if (!IsPhone) actions.Children.Add(_exPickDelete);
        var dock = new DockPanel { Children = { done, actions } };
        DockPanel.SetDock(done, Dock.Right);
        _exPickBar = new Border { CornerRadius = new(12), Padding = IsPhone ? new(8, 6, 8, 0) : new(10, 8, 10, 2), Margin = IsPhone ? new(6, 4, 6, 0) : new(14, 8, 14, 0), BorderThickness = new(1), IsVisible = false, Child = dock };
        _exPickBar.Bind(Border.BackgroundProperty, this.GetResourceObservable("BsSurface2"));
        _exPickBar.Bind(Border.BorderBrushProperty, this.GetResourceObservable("BsAccent"));
    }

    /// <summary>A menu item with a list under it shown as a menu of its own (the items move over).</summary>
    static void ShowAsMenu(Control anchor, MenuItem source)
    {
        var fly = new MenuFlyout();
        var items = source.Items.OfType<object>().ToList();
        source.Items.Clear();
        foreach (var i in items) fly.Items.Add(i);
        fly.ShowAt(anchor);
    }

    void SetExPick(bool on, ExItem? with = null)
    {
        _exPick = on;
        _exSel.Clear();
        if (on && with is not null) { _exSel.Add(with); _exAnchor = with; }
        _exPickBtn.Classes.Set("primary", on);
        _exPickBtn.Content = on ? L("Fertig") : L("Auswählen");
        FillExplorerList();
    }

    /// <summary>A tap on a row while choosing: ticked or unticked; the order of ticking is kept (a reading path follows it).</summary>
    void ExPickToggle(ExItem item)
    {
        if (_exSel.RemoveAll(s => SameItem(s, item)) == 0) _exSel.Add(item);
        ExShowSelection();
    }

    void UpdateExPickBar()
    {
        if (_exPickBar is null) return;
        _exPickBar.IsVisible = _exPick && !_exInTrash;
        _exPickBtn.IsVisible = !_exInTrash && !(IsPhone && _exPick);   // on a phone the bar below has its own "Fertig": not two of them
        if (!_exPick) return;
        var n = _exSel.Count;
        _exPickCount.Text = n == 0 ? L("Nichts gewählt") : n == 1 ? L("1 gewählt") : L("{0} gewählt", n);
        var own = _exSel.Where(i => !Foreign(i)).ToList();
        _exPickMove.IsEnabled = _exPickDelete.IsEnabled = own.Count > 0;
        _exPickMore.IsEnabled = IsPhone ? own.Count > 0 : own.Any(i => i.Kind == ExKind.Mark);
        _exPickPath.IsEnabled = own.Any(i => i.Kind is ExKind.Mark or ExKind.Note);
        _exPickAll.Content = n == _exRows.Count && n > 0 ? L("Keine") : L("Alle");
    }

    /// <summary>The box to tick at the start of a row (only while choosing).</summary>
    Border PickBox(ExItem item)
    {
        var tick = new TextBlock { Text = "✓", FontSize = 11, FontWeight = FontWeight.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.White };
        var box = new Border { Width = 18, Height = 18, CornerRadius = new(9), BorderThickness = new(1.5), Child = tick, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };
        void Paint()
        {
            var on = _exSel.Any(s => SameItem(s, item));
            tick.IsVisible = on;
            if (on) { box.Bind(Border.BackgroundProperty, this.GetResourceObservable("BsAccent")); box.Bind(Border.BorderBrushProperty, this.GetResourceObservable("BsAccent")); }
            else { box.Background = Brushes.Transparent; box.Bind(Border.BorderBrushProperty, this.GetResourceObservable("BsMuted")); }
        }
        box.Tag = (Action)Paint;
        Paint();
        return box;
    }
}
