using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using BibleStudy.Core.Models;

namespace BibleStudy.App;

/// <summary>The references between the Old and the New Testament as a picture (a window of its own); a click on a book fills the search with its references.</summary>
public partial class MainWindow
{
    Window? _refNetWindow;

    async Task ShowRefNetAsync()
    {
        if (_refNetWindow is { } open) { open.Activate(); return; }
        var view = new RefNetView();
        var hint = new TextBlock { Classes = { "caption" }, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        var status = new TextBlock { Text = L("Einmalig: Verweise des Neuen Testaments werden geladen …"), Classes = { "caption" }, TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 6) };
        var strength = new ComboBox { ItemsSource = new[] { L("ab 20 Stimmen"), L("ab 50 Stimmen"), L("ab 100 Stimmen") }, SelectedIndex = 0, Width = 160 };
        var thresholds = new[] { 20, 50, 100 };
        var top = new DockPanel { Margin = new(16, 12, 16, 4) };
        var pill = new Border { Classes = { "pill" }, Child = strength };
        DockPanel.SetDock(pill, Dock.Right);
        top.Children.Add(pill);
        top.Children.Add(new StackPanel { Children = { new TextBlock { Text = L("Verweise zwischen Altem und Neuem Testament"), FontSize = 18, FontWeight = FontWeight.SemiBold }, new TextBlock { Text = L("Jede Linie verbindet ein AT-Buch (links) mit einem NT-Buch (rechts); je dicker, desto mehr Querverweise. Klick auf ein Buch: seine Verse in der Suche."), Classes = { "caption" }, TextWrapping = TextWrapping.Wrap } } });
        var source = new TextBlock { Text = "Querverweise © OpenBible.info, CC BY 4.0 (openbible.info/labs/cross-references)", Classes = { "caption" }, Margin = new(16, 4, 16, 8) };
        var bottom = new StackPanel { Children = { status, hint, source } };
        var dock = new DockPanel { Background = Res("BsBackground") };
        DockPanel.SetDock(top, Dock.Top); DockPanel.SetDock(bottom, Dock.Bottom);
        dock.Children.Add(top); dock.Children.Add(bottom); dock.Children.Add(view);

        var win = new Window { Title = L("Verweis-Netz AT ↔ NT"), Width = 980, Height = 820, Content = dock, Icon = Icon, WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = Res("BsBackground") };
        _refNetWindow = win;
        win.Closed += (_, _) => _refNetWindow = null;
        view.Hint += text => hint.Text = text;
        view.BookClicked += async id =>
        {
            var book = _books.FirstOrDefault(b => b.Id == id);
            if (book is null) return;
            SetPanelOpen(true);
            OpenWork(NavGroups.First(g => g.Name == "Suchen"));
            SearchBox.Text = $"Bezug:\"{book.CommonName} 1-{book.NumberOfChapters}\"";
            await SearchAsync();
            Activate();
        };
        win.Show(this);

        List<(VerseRef Source, CrossRef Cross)> all;
        try { all = await NtCrossRefsAsync(); }
        catch (Exception ex) { status.Text = L("Fehler: ") + ex.Message; return; }
        var ot = _books.Where(b => BookCategories.IsOldTestament(BookCategories.Of(b.Id))).Select(b => (b.Id, b.CommonName)).ToList();
        var nt = _books.Where(b => BookCategories.IsNewTestament(BookCategories.Of(b.Id))).Select(b => (b.Id, b.CommonName)).ToList();
        void Draw()
        {
            var min = thresholds[Math.Max(0, strength.SelectedIndex)];
            var count = new Dictionary<(string, string), int>();
            foreach (var (src, x) in all)
                if (x.Score >= min && BookCategories.IsOldTestament(BookCategories.Of(x.Target.Book)))
                    count[(src.Book, x.Target.Book)] = count.GetValueOrDefault((src.Book, x.Target.Book)) + 1;
            view.SetData(ot, nt, count);
        }
        status.IsVisible = false;
        strength.SelectionChanged += (_, _) => Draw();
        Draw();
    }
}
