using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace BibleStudy.App;

/// <summary>
/// Small screens (a laptop with 125–150 % scaling is only about 900–1100 units wide): what does not fit in the top bar and the reading bar
/// does not squeeze behind its neighbours or disappear, it moves behind a "…" button. First the labels of the reading bar give way (the icons
/// and their tooltips stay), then the least needed buttons, in a fixed order. Everything stays one click away.
/// </summary>
public partial class MainWindow
{
    /// <summary>A button that may move into the "…" menu. <paramref name="Allowed"/> says whether it is shown at all when there is room (Easy mode hides some).</summary>
    sealed record Spill(Control Item, Func<bool> Allowed, Control? With = null, bool Silent = false);

    Spill[] _topSpill = [], _readSpill = [];
    readonly List<Spill> _topHidden = [], _readHidden = [];
    readonly List<TextBlock> _readLabels = [];
    readonly List<Border> _readPills = [];

    /// <summary>Shows everything that is allowed, then hides items in order until <paramref name="fits"/> says the rest fits.</summary>
    void RunSpill(Button more, Spill[] order, List<Spill> hidden, Func<bool> fits)
    {
        hidden.Clear();
        foreach (var s in order) { var on = s.Allowed(); s.Item.IsVisible = on; if (s.With is not null) s.With.IsVisible = on; }
        more.IsVisible = false;
        if (fits()) return;
        more.IsVisible = true;
        foreach (var s in order)
        {
            if (!s.Item.IsVisible) continue;
            s.Item.IsVisible = false; if (s.With is not null) s.With.IsVisible = false;
            hidden.Add(s);
            if (fits()) break;
        }
        if (hidden.All(h => h.Silent)) more.IsVisible = false;   // only decoration moved away: nothing to list
    }

    /// <summary>Where a menu of a button opens: at the button, or at the "…" button when it was moved there.</summary>
    Control Anchor(Control c)
    {
        if (c.IsEffectivelyVisible) return c;
        if (_topHidden.Any(s => s.Item == c || s.With == c)) return TopMoreBtn;
        if (_readHidden.Any(s => s.Item == c || s.With == c)) return ReadMoreBtn;
        return c;
    }

    void ShowSpill(Button more, List<Spill> hidden)
    {
        var flyout = new Flyout { Placement = PlacementMode.BottomEdgeAlignedRight };
        var list = new StackPanel { Spacing = 2, MinWidth = 210 };
        foreach (var s in hidden.Where(h => !h.Silent))
        {
            var item = s.Item;
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            var icon = item.GetLogicalDescendants().OfType<Icon>().FirstOrDefault();
            if (icon is not null) row.Children.Add(new Icon { Data = icon.Data, IconSize = 17, VerticalAlignment = VerticalAlignment.Center });
            else row.Children.Add(new Border { Width = 17 });
            row.Children.Add(new TextBlock { Text = (item is ToggleButton { IsChecked: true } ? "✓  " : "") + SpillLabel(item), VerticalAlignment = VerticalAlignment.Center });
            var entry = new Button { Classes = { "toolx" }, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left, Content = row };
            entry.Click += (_, _) => { flyout.Hide(); Dispatcher.UIThread.Post(() => Activate(item), DispatcherPriority.Background); };
            list.Children.Add(entry);
        }
        flyout.Content = list;
        flyout.ShowAt(more);
    }

    /// <summary>The text of a moved button: its own label, or the start of its tooltip (those are translated already).</summary>
    static string SpillLabel(Control c)
    {
        var label = c.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text).FirstOrDefault(t => !string.IsNullOrWhiteSpace(t));
        if (label is not null) return label;
        var tip = ToolTip.GetTip(c) as string ?? "";
        var cut = tip.IndexOfAny([':', '(', ',']);
        return (cut > 0 ? tip[..cut] : tip).Trim();
    }

    /// <summary>What a click on the button would do. Switches that change by a state need the state changed, the others just get the click.</summary>
    void Activate(Control c)
    {
        if (c == StudyBtn || c == QuickMarkBtn) ((ToggleButton)c).IsChecked = ((ToggleButton)c).IsChecked != true;
        else c.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }

    /// <summary>A hidden or shown part only marks its direct parent as changed; the measuring done here by hand needs the whole branch to be measured again.</summary>
    static void Remeasure(Visual v)
    {
        foreach (var child in v.GetVisualChildren()) Remeasure(child);
        (v as Layoutable)?.InvalidateMeasure();
    }

    // ---- reading bar -------------------------------------------------------------------------------------------------------

    void InitReadBarFit()
    {
        foreach (var b in ReadBar.GetLogicalDescendants().OfType<ContentControl>().Where(b => b == ReadBtn || b.Classes.Contains("toolx")))
            if (b.Content is StackPanel sp) _readLabels.AddRange(sp.Children.OfType<TextBlock>());
        _readPills.AddRange(ReadBar.GetLogicalDescendants().OfType<Border>().Where(b => b.Classes.Contains("pill")));
        // the first one leaves first
        _readSpill =
        [
            new(ParallelBtn, () => !_settings.EasyMode), new(FamilyBtn, () => true), new(TimelineBtn, () => true), new(MapBtn, () => true),
            new(GraphBtn, () => !_settings.EasyMode), new(BookmarkBtn, () => true), new(QuickMarkBtn, () => true, QuickColorBtn),
            new(AudioBtn, () => true), new(SpeakBtn, () => true), new(StudyBtn, () => !_settings.EasyMode), new(MarkBtn, () => true), new(ViewBtn, () => true),
        ];
        ReadBar.PropertyChanged += (_, e) => { if (e.Property == BoundsProperty || e.Property == IsVisibleProperty) FitReadBar(); };
        ReadMoreBtn.Click += (_, _) => ShowSpill(ReadMoreBtn, _readHidden);
        FitReadBar();
    }

    void FitReadBar()
    {
        if (ReadBar.Child is not DockPanel dock || ReadBar.Bounds.Width <= 0) return;
        var available = ReadBar.Bounds.Width - ReadBar.Padding.Left - ReadBar.Padding.Right;
        bool Fits()
        {
            double sum = 0;
            Remeasure(dock);
            foreach (var child in dock.Children)
            {
                if (!child.IsVisible) continue;
                child.Measure(Size.Infinity);
                sum += child.DesiredSize.Width + child.Margin.Left + child.Margin.Right;
            }
            return sum <= available;
        }
        void Labels(bool on) { foreach (var t in _readLabels) t.IsVisible = on; }
        // a group whose buttons all moved away leaves nothing behind (its frame would stay as a little stub)
        void Frames() { foreach (var p in _readPills) p.IsVisible = p.Child is not Panel g || g.Children.Any(c => c.IsVisible); }
        Labels(true);
        RunSpill(ReadMoreBtn, _readSpill, _readHidden, () => true);
        Frames();
        if (Fits()) return;
        Labels(false);   // icons only: the tooltips say what they are
        foreach (var p in _readPills) p.IsVisible = true;
        RunSpill(ReadMoreBtn, _readSpill, _readHidden, () => { Frames(); return Fits(); });
        Frames();
    }
}
