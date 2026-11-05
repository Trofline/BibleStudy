using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace BibleStudy.App;

/// <summary>
/// Several marks at once in the explorer: choose them (Ctrl/Shift+click, Ctrl+A) and give all of them a new color or a tag, or take a tag away.
/// Every change can be taken back with Ctrl+Z.
/// </summary>
public partial class MainWindow
{
    List<long> MarkIds(List<ExItem> items) => items.Where(i => i.Kind == ExKind.Mark).Select(i => i.Id).ToList();

    MenuItem ColorMenu(List<ExItem> items, Control anchor)
    {
        var ids = MarkIds(items);
        return MenuAction(ids.Count > 1 ? L("Farbe von {0} Markierungen ändern …", ids.Count) : L("Farbe ändern …"), () =>
        {
            var fly = new Flyout();
            fly.Content = new StackPanel
            {
                Spacing = 8, MaxWidth = 300,
                Children = { new TextBlock { Text = L("Neue Farbe"), FontWeight = FontWeight.SemiBold }, ColorChoice(hex => { fly.Hide(); ExRecolor(ids, hex); }) },
            };
            fly.ShowAt(anchor);
        }, ids.Count > 0);
    }

    void ExRecolor(List<long> ids, string hex)
    {
        var old = _vault.GetMarks().Where(m => ids.Contains(m.Id)).Select(m => (m.Id, m.Color)).ToList();
        if (old.Count == 0) return;
        _vault.RecolorMarks(ids, hex);
        ExRemember(L("Farbe von {0} Markierungen", old.Count), () => { foreach (var g in old.GroupBy(o => o.Color)) _vault.RecolorMarks(g.Select(o => o.Id), g.Key); }, true);
        _exSel.Clear();
        AfterExplorerChange();
        RefreshCells();
        ExNote(old.Count == 1 ? L("Farbe geändert.") : L("{0} Markierungen haben jetzt dieselbe Farbe.", old.Count), true);
    }

    MenuItem TagMenu(List<ExItem> items, Control anchor)
    {
        var ids = MarkIds(items);
        var menu = new MenuItem { Header = L("Tags"), IsEnabled = ids.Count > 0 };
        if (ids.Count == 0) { menu.Items.Add(new MenuItem()); return menu; }
        menu.Items.Add(MenuAction(ids.Count > 1 ? L("Tag zu {0} Markierungen hinzufügen …", ids.Count) : L("Tag hinzufügen …"), () => AskTag(anchor, ids)));
        var marks = _vault.GetMarks().Where(m => ids.Contains(m.Id)).ToList();
        var present = marks.SelectMany(m => m.Tags).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(t => t, StringComparer.CurrentCultureIgnoreCase).ToList();
        if (present.Count > 0)
        {
            var remove = new MenuItem { Header = L("Tag entfernen") };
            foreach (var t in present)
            {
                var n = marks.Count(m => m.Tags.Contains(t, StringComparer.OrdinalIgnoreCase));
                remove.Items.Add(MenuAction($"#{t}" + (ids.Count > 1 ? "  (" + n + ")" : ""), () => ExTag(ids, t, add: false)));
            }
            menu.Items.Add(remove);
        }
        return menu;
    }

    /// <summary>A small box for the tag: type a new one or click one that is used already.</summary>
    void AskTag(Control anchor, List<long> ids)
    {
        var box = new TextBox { Width = 240, PlaceholderText = L("Tag, z. B. Gnade") };
        var ok = new Button { Content = L("Hinzufügen"), Classes = { "primary" }, HorizontalAlignment = HorizontalAlignment.Left };
        var known = new WrapPanel { MaxWidth = 300 };
        var fly = new Flyout();
        void Apply(string tag)
        {
            tag = tag.Trim().TrimStart('#');
            if (tag.Length == 0) return;
            fly.Hide();
            ExTag(ids, tag, add: true);
        }
        foreach (var (tag, _) in _vault.GetTags().OrderByDescending(t => t.Count).Take(16))
        {
            var chip = new Button { Content = "#" + tag, Classes = { "ghost" }, Padding = new(8, 3), Margin = new(0, 0, 6, 6), FontSize = 12.5 };
            chip.Click += (_, _) => Apply(tag);
            known.Children.Add(chip);
        }
        fly.Content = new StackPanel
        {
            Spacing = 8,
            Children = { new TextBlock { Text = ids.Count > 1 ? L("Tag für {0} Markierungen", ids.Count) : L("Tag hinzufügen"), FontWeight = FontWeight.SemiBold }, box, known, ok },
        };
        known.IsVisible = known.Children.Count > 0;
        ok.Click += (_, _) => Apply(box.Text ?? "");
        box.KeyDown += (_, e) => { if (e.Key == Key.Enter) Apply(box.Text ?? ""); };
        fly.Opened += (_, _) => box.Focus();
        fly.ShowAt(anchor);
    }

    void ExTag(List<long> ids, string tag, bool add)
    {
        // only the marks that really change are taken back (a mark that had the tag before keeps it)
        var change = _vault.GetMarks().Where(m => ids.Contains(m.Id) && m.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase) != add).ToList();
        if (change.Count == 0) { ExNote(add ? L("Alle haben den Tag „{0}“ schon.", tag) : L("Keine hat den Tag „{0}“.", tag), false); return; }
        // a tag taken away is taken away as it is written there (the case of the letters may differ)
        if (add) _vault.TagMarks(change.Select(m => m.Id), tag, true);
        else foreach (var m in change) _vault.TagMarks([m.Id], m.Tags.First(t => t.Equals(tag, StringComparison.OrdinalIgnoreCase)), false);
        var undo = change.Select(m => (m.Id, Tag: add ? tag : m.Tags.First(t => t.Equals(tag, StringComparison.OrdinalIgnoreCase)))).ToList();
        ExRemember(add ? L("Tag „{0}“ vergeben", tag) : L("Tag „{0}“ entfernt", tag), () => { foreach (var (id, t) in undo) _vault.TagMarks([id], t, !add); }, false);
        _exSel.Clear();
        AfterExplorerChange();
        ExNote(add ? L("„{0}“ an {1} Markierungen vergeben.", tag, change.Count) : L("„{0}“ bei {1} Markierungen entfernt.", tag, change.Count), true);
    }
}
