using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;

namespace BibleStudy.App;

/// <summary>
/// The projector view: a verse or a piece of the materials large and calm on a second screen (a projector or a TV), dark with light
/// letters whatever the program looks like; the program's own window stays as it is. Long texts get smaller, so all of it fits.
/// Keys: arrows, space and Page up/down go on or back, B makes the screen black and back, F11 or a double click switches full screen, Esc closes.
/// </summary>
public sealed class ProjectorWindow : Window
{
    readonly TextBlock _title = new() { FontSize = 40, HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center, Foreground = new SolidColorBrush(Color.Parse("#C9A86A")), Margin = new(0, 0, 0, 36), TextWrapping = TextWrapping.Wrap };
    readonly TextBlock _text = new() { FontSize = 66, LineHeight = 96, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Color.Parse("#F3EEE4")) };
    readonly Viewbox _content;

    /// <summary>On (+1) or back (−1), asked for with the keys.</summary>
    public event Action<int>? Step;

    public bool IsBlack { get; private set; }

    public ProjectorWindow(FontFamily font)
    {
        Title = "BibleStudy – Beamer";
        Width = 1100; Height = 680;
        Background = new SolidColorBrush(Color.Parse("#0E0D0B"));
        _text.FontFamily = font; _title.FontFamily = font;
        // the text is laid out on a wide page and the whole page shrinks to the screen: short verses stay large, long passages still fit
        var page = new StackPanel { Width = 1700, Margin = new(80, 60), VerticalAlignment = VerticalAlignment.Center, Children = { _title, _text } };
        _content = new Viewbox { Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, Child = page, Margin = new(40) };
        Content = _content;
        KeyDown += (_, e) =>
        {
            switch (e.Key)
            {
                case Key.Escape: Close(); break;
                case Key.Right or Key.Down or Key.Space or Key.PageDown or Key.Enter: Step?.Invoke(1); break;
                case Key.Left or Key.Up or Key.PageUp or Key.Back: Step?.Invoke(-1); break;
                case Key.B or Key.OemPeriod: ToggleBlack(); break;
                case Key.F11 or Key.F: ToggleFullScreen(); break;
                default: return;
            }
            e.Handled = true;
        };
        DoubleTapped += (_, _) => ToggleFullScreen();
    }

    public string ShownTitle => _title.Text ?? "";

    public void Show(string title, string text)
    {
        _title.Text = title;
        _title.IsVisible = title.Length > 0;
        _text.Text = text;
        if (IsBlack) ToggleBlack();
    }

    /// <summary>A black screen (to talk without the text), and back.</summary>
    public void ToggleBlack()
    {
        IsBlack = !IsBlack;
        _content.IsVisible = !IsBlack;
    }

    public void ToggleFullScreen() => WindowState = WindowState == WindowState.FullScreen ? WindowState.Normal : WindowState.FullScreen;

    /// <summary>Opens on the other screen in full screen when there is one; with only one screen as a window that can be dragged to the projector.</summary>
    public void OpenBeside(Window owner)
    {
        var mine = owner.Screens.ScreenFromWindow(owner);
        var other = owner.Screens.All.FirstOrDefault(s => mine is null || !s.Bounds.Equals(mine.Bounds));
        if (other is not null)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Position = other.WorkingArea.Position + new PixelPoint(40, 40);
            Show();
            WindowState = WindowState.FullScreen;
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Show();
        }
    }
}
