using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace BibleStudy.App;

/// <summary>
/// Tidier settings: the sections stay the cards they were, but the long ones (shortcuts, sources and licenses, plugins …) are folded
/// at the start with a click on the title to open them, and a list on the right jumps to a section (and opens it).
/// </summary>
public partial class MainWindow
{
    /// <summary>Positions of the sections (in the order of the page) that start folded.</summary>
    static readonly HashSet<int> FoldedSettings = [3, 8, 9, 10, 11, 13];

    void InitSettingsLayout()
    {
        var cards = SettingsStack.Children.OfType<Border>()
            .Where(b => b.Classes.Contains("card") && b.Child is StackPanel sp && sp.Children.Count > 0 && sp.Children[0] is TextBlock tb && tb.Classes.Contains("eyebrow"))
            .ToList();
        for (var i = 0; i < cards.Count; i++)
        {
            var card = cards[i];
            var panel = (StackPanel)card.Child!;
            var title = (TextBlock)panel.Children[0];
            panel.Children.RemoveAt(0);
            card.Child = null;   // the panel moves into a new container, so it must leave the card first

            var chevron = new TextBlock { Text = "▾", FontSize = 14, VerticalAlignment = VerticalAlignment.Center, Opacity = 0.7 };
            var head = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Cursor = new Cursor(StandardCursorType.Hand), Background = Brushes.Transparent };
            head.Children.Add(title);
            Grid.SetColumn(chevron, 1);
            head.Children.Add(chevron);
            var body = new Border { Child = panel, Margin = new(0, 10, 0, 0) };
            var wrap = new StackPanel { Children = { head, body } };
            card.Child = wrap;

            void Set(bool open) { body.IsVisible = open; chevron.Text = open ? "▾" : "▸"; }
            Set(!FoldedSettings.Contains(i));
            head.PointerPressed += (_, e) => { if (e.GetCurrentPoint(head).Properties.IsLeftButtonPressed) Set(!body.IsVisible); };

            var name = title.Text ?? "";
            var jump = new Button { Content = name, Classes = { "ghost" }, FontSize = 11, HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new(8, 4) };
            jump.Click += (_, _) =>
            {
                Set(true);
                Dispatcher.UIThread.Post(() => card.BringIntoView(), DispatcherPriority.Background);
            };
            SettingsJump.Children.Add(jump);
        }
    }
}
