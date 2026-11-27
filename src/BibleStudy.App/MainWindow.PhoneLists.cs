using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace BibleStudy.App;

/// <summary>
/// The lists of the documents and of the notes on a phone. On a computer the part above (search, filters, the note editor) stays put and the list
/// scrolls below it. On a phone that part took nearly the whole screen: the list had a strip of two rows at the bottom, and the welcome card
/// ran out under the tab bar. On a phone everything scrolls together, so the list gets the whole screen as soon as you scroll down; the
/// categories of the documents stand in one row that slides sideways.
/// </summary>
public partial class MainWindow
{
    readonly Dictionary<DockPanel, List<Control>> _scrolledAsOne = [];
    ScrollViewer? _docCatScroll;

    void ApplyPhoneLists(bool phone)
    {
        ScrollAsOne(DocListPanel, DocTreeScroll, phone);
        ScrollAsOne(NotesPanel, NotesScroll, phone);
        DocSubtitle.IsVisible = DocCatLabel.IsVisible = !phone;

        // the categories: on a phone one row that slides sideways (they took four rows of the screen)
        if (phone && _docCatScroll is null && DocCategoryChips.Parent is Panel host)
        {
            var at = host.Children.IndexOf(DocCategoryChips);
            host.Children.Remove(DocCategoryChips);
            _docCatScroll = new ScrollViewer
            {
                Content = DocCategoryChips,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            };
            host.Children.Insert(at, _docCatScroll);
        }
        else if (!phone && _docCatScroll is { Parent: Panel back } sv)
        {
            var at = back.Children.IndexOf(sv);
            sv.Content = null;
            back.Children.Remove(sv);
            back.Children.Insert(at, DocCategoryChips);
            _docCatScroll = null;
        }
    }

    /// <summary>
    /// On a phone: what stands docked above <paramref name="scroll"/> in <paramref name="panel"/> moves into the scrolled area, above the list.
    /// Back on a wide screen it goes back to its place.
    /// </summary>
    void ScrollAsOne(DockPanel panel, ScrollViewer scroll, bool phone)
    {
        if (phone && !_scrolledAsOne.ContainsKey(panel))
        {
            if (scroll.Content is not Control list) return;
            var above = panel.Children.Where(c => c != scroll).ToList();
            foreach (var c in above) panel.Children.Remove(c);
            scroll.Content = null;
            var all = new StackPanel();
            foreach (var c in above) all.Children.Add(c);
            all.Children.Add(list);
            scroll.Content = all;
            _scrolledAsOne[panel] = above;
        }
        else if (!phone && _scrolledAsOne.Remove(panel, out var above))
        {
            if (scroll.Content is not StackPanel all) return;
            var list = all.Children.Last();
            all.Children.Clear();
            scroll.Content = list;
            panel.Children.Remove(scroll);
            foreach (var c in above) panel.Children.Add(c);
            panel.Children.Add(scroll);
        }
    }
}
