using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using BibleStudy.Core.Search;

namespace BibleStudy.App;

/// <summary>
/// The help page was one long wall of text. Each heading becomes a row that opens its paragraphs, and a search field above opens
/// every section in which the words occur. The texts themselves stay where they are (in the window definition, translated as before).
/// </summary>
public partial class MainWindow
{
    void InitHelpPage()
    {
        if (TourBtn.Parent is not StackPanel page) return;
        var start = page.Children.IndexOf(TourBtn) + 1;
        var sections = new List<(Button Head, StackPanel Body, string Text)>();
        StackPanel? body = null;
        foreach (var c in page.Children.Skip(start).ToList())
        {
            page.Children.Remove(c);
            if (c is TextBlock { FontWeight: FontWeight.SemiBold } head)
            {
                body = new StackPanel { Spacing = 8, Margin = new(22, 2, 0, 8), IsVisible = false };
                var title = head.Text ?? "";
                var button = new Button { Classes = { "ghost" }, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new(6, 6), FontWeight = FontWeight.SemiBold, Content = "▸  " + title };
                var b = body;
                button.Click += (_, _) => { b.IsVisible = !b.IsVisible; button.Content = (b.IsVisible ? "▾  " : "▸  ") + title; };
                sections.Add((button, body, title));
            }
            else if (body is not null) body.Children.Add(c);
            else page.Children.Add(c);
        }
        var search = new TextBox { PlaceholderText = L("In der Hilfe suchen …"), Margin = new(0, 4, 0, 4) };
        page.Children.Add(search);
        foreach (var (head, b, _) in sections) { page.Children.Add(head); page.Children.Add(b); }
        string Fold(string s) => SearchQuery.Fold(s, true);
        var texts = sections.Select(s => Fold(s.Text + " " + string.Join(" ", s.Body.Children.OfType<TextBlock>().Select(t => t.Text)))).ToList();
        search.TextChanged += (_, _) =>
        {
            var words = Fold(search.Text?.Trim() ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
            for (var i = 0; i < sections.Count; i++)
            {
                var (head, b, title) = sections[i];
                var hit = words.Length == 0 || words.All(w => texts[i].Contains(w, StringComparison.Ordinal));
                head.IsVisible = hit;
                b.IsVisible = hit && words.Length > 0;
                head.Content = (b.IsVisible ? "▾  " : "▸  ") + title;
            }
        };
    }
}
