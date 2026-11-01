using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;

namespace BibleStudy.App;

/// <summary>
/// Tidier settings: every section is a fold-out (only the first is open at the start) and a slim list on the right jumps to a section
/// and opens it.
/// </summary>
public partial class MainWindow
{
    void InitSettingsLayout()
    {
        var cards = SettingsStack.Children.OfType<Border>()
            .Where(b => b.Classes.Contains("card") && b.Child is StackPanel sp && sp.Children.Count > 0 && sp.Children[0] is TextBlock tb && tb.Classes.Contains("eyebrow"))
            .ToList();
        var expanders = new List<Expander>();
        foreach (var card in cards)
        {
            var panel = (StackPanel)card.Child!;
            var title = (TextBlock)panel.Children[0];
            panel.Children.RemoveAt(0);
            var header = new TextBlock { Text = title.Text, Classes = { "eyebrow" }, VerticalAlignment = VerticalAlignment.Center };
            var ex = new Expander { Header = header, Content = panel, IsExpanded = expanders.Count == 0, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new(0, 6, 0, 0) };
            card.Child = ex;
            expanders.Add(ex);

            var name = title.Text ?? "";
            var shortName = name.Length > 12 ? name[..11] + "…" : name;
            var jump = new Button { Content = shortName, Classes = { "ghost" }, FontSize = 11, HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new(8, 4) };
            ToolTip.SetTip(jump, name);
            jump.Click += (_, _) =>
            {
                foreach (var other in expanders) if (other != ex) other.IsExpanded = false;
                ex.IsExpanded = true;
                Dispatcher.UIThread.Post(() => card.BringIntoView(), DispatcherPriority.Background);
            };
            SettingsJump.Children.Add(jump);
        }
    }
}
