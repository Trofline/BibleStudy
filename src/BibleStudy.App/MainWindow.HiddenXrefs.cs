using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using BibleStudy.Core.Models;
using BibleStudy.Core.Storage;

namespace BibleStudy.App;

/// <summary>
/// Printed cross references come from a third party (OpenBible.info, rated by readers, not checked by scholars). When you see that one is wrong
/// or pointless you can hide it for good: it is gone from the text, the cross reference list and the verse overview, in both directions.
/// The decision is saved in your vault and can be taken back under Settings → Sources and data.
/// </summary>
public partial class MainWindow
{
    HashSet<(VerseRef, VerseRef)> _hiddenXrefs = [];

    void RefreshHiddenXrefs()
    {
        _hiddenXrefs = _vault is null ? [] : _vault.GetHiddenXrefs().SelectMany(h => new[] { (h.From, h.To), (h.To, h.From) }).ToHashSet();
    }

    bool XrefHidden(VerseRef from, VerseRef to) => _hiddenXrefs.Contains((from, to));

    /// <summary>The cross references without the ones hidden by the user (compared by the first verse of each target).</summary>
    List<CrossRef> VisibleXrefs(VerseRef from, IEnumerable<CrossRef> refs) => refs.Where(x => !XrefHidden(from, x.Target)).ToList();

    async Task HideXrefAsync(VerseRef from, VerseRef to)
    {
        _vault.HideXref(from, to);
        RefreshHiddenXrefs();
        StatusText.Text = L("{0} → {1} ist ausgeblendet. Zurückholen: Einstellungen → Quellen und Daten.", Label(from), Label(to));
        await ShowChapterAsync(_selected);   // chips, graph threads and the lists draw again
        if (_selected is int v) _ = ShowXrefsAsync(new VerseRef(_curBook, _curChapter, v));
    }

    /// <summary>Adds the menu "hide this cross reference for good" to a control that stands for the reference from <paramref name="from"/> to <paramref name="to"/>.</summary>
    void AttachHideXrefMenu(Control control, VerseRef from, VerseRef to)
    {
        var item = new MenuItem { Header = L("Diesen Querverweis dauerhaft ausblenden") };
        item.Click += async (_, _) => await HideXrefAsync(from, to);
        control.ContextMenu = new ContextMenu { Items = { item } };
    }

    void InitHiddenXrefs()
    {
        RefreshHiddenXrefs();
        HiddenXrefsBtn.Click += (_, _) => ShowHiddenXrefs();
        // the list of cross references in the side panel: right click on an entry
        XrefList.AddHandler(PointerPressedEvent, (object? _, PointerPressedEventArgs e) =>
        {
            if (!e.GetCurrentPoint(XrefList).Properties.IsRightButtonPressed || _selected is not int verse) return;
            for (var c = e.Source as Avalonia.Visual; c is not null; c = c.GetVisualParent())
                if (c is Control { DataContext: Entry entry })
                {
                    var from = new VerseRef(_curBook, _curChapter, verse);
                    var menu = new MenuFlyout();
                    var hide = new MenuItem { Header = L("Diesen Querverweis dauerhaft ausblenden") };
                    hide.Click += async (_, _) => await HideXrefAsync(from, entry.Ref);
                    menu.Items.Add(hide);
                    menu.ShowAt((Control)c, true);
                    e.Handled = true;
                    return;
                }
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    void ShowHiddenXrefs()
    {
        var hidden = _vault.GetHiddenXrefs();
        var panel = new StackPanel { Spacing = 6, Width = 360 };
        panel.Children.Add(new TextBlock { Text = hidden.Count == 0 ? L("Du hast keinen Querverweis ausgeblendet.") : L("Von dir ausgeblendete Querverweise:"), TextWrapping = TextWrapping.Wrap, FontWeight = FontWeight.SemiBold });
        var fly = new Flyout { Content = new ScrollViewer { MaxHeight = 360, Content = panel } };
        foreach (var h in hidden)
        {
            var item = h;
            var back = new Button { Content = L("Zurückholen"), Classes = { "tool" }, Width = double.NaN, Padding = new(10, 0), Height = 28 };
            back.Click += async (_, _) =>
            {
                _vault.UnhideXref(item.Id); RefreshHiddenXrefs(); fly.Hide();
                await ShowChapterAsync(_selected);
            };
            var row = new DockPanel { Children = { back, new TextBlock { Text = $"{Label(item.From)}  ↔  {Label(item.To)}", VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap } } };
            DockPanel.SetDock(back, Dock.Right);
            panel.Children.Add(row);
        }
        fly.ShowAt(HiddenXrefsBtn);
    }
}
