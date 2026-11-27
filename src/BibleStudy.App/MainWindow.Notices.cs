using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace BibleStudy.App;

/// <summary>
/// The small notices (phone: above the bar at the bottom, desktop: bottom right) that go again after a few seconds. Several at the same time
/// lie on top of each other: the newest one at the bottom, the older ones move up. Each one goes away on its own, with a tap or with a swipe to the side.
/// Green for what went well (connected, synchronised), blue for a plain note, violet for a message or suggestion of the program.
/// What was shown is kept for the time the program runs, so it can be read again in the message center.
/// </summary>
public partial class MainWindow
{
    const string NoticeGood = "#2F7D4F", NoticeInfo = "#3B5BDB", NoticeMessage = "#7048E8";
    const int MaxNotices = 4;

    StackPanel? _notices;
    Panel? _noticeHost;
    DateTime _noticeShownAt;
    readonly Dictionary<Border, IDisposable> _noticeTimers = [];

    /// <summary>A notice of the program shown during this run (newest last), for the list in the message center.</summary>
    sealed record NoticeLogEntry(DateTime At, string Title, string Text, string Color);
    readonly List<NoticeLogEntry> _noticeLog = [];

    Border? Notify(string title, string text, bool good = false, string? actionLabel = null, Action? action = null, TimeSpan? stay = null) =>
        ShowNotice(title, text, good ? NoticeGood : NoticeInfo, good ? "✓  " : "", actionLabel, action, stay);

    /// <summary>A message or a suggestion of the program: looks like the other notices, in violet, and stays a little longer.</summary>
    Border? NotifyMessage(string title, string text, string? actionLabel = null, Action? action = null, string? secondLabel = null, Action? second = null) =>
        ShowNotice(title, text, NoticeMessage, "💡  ", actionLabel, action, TimeSpan.FromSeconds(actionLabel is null ? 10 : 25), secondLabel, second);

    bool EnsureNoticeHost()
    {
        if (_notices is not null) return true;
        // the notices lie in a grid over the whole content (the content is wrapped once)
        if (TopBar.Parent is not DockPanel dock) return false;
        Panel host;
        if (dock.Parent is Grid g) host = g;
        else
        {
            host = new Grid();
            if (dock.Parent is Panel p) { var at = p.Children.IndexOf(dock); p.Children.RemoveAt(at); host.Children.Add(dock); p.Children.Insert(at, host); }
            else if (dock.Parent is ContentControl cc) { cc.Content = null; host.Children.Add(dock); cc.Content = host; }
            else return false;
        }
        _noticeHost = host;
        _notices = new StackPanel { Spacing = 8, MaxWidth = 340, ZIndex = 1000, VerticalAlignment = VerticalAlignment.Bottom };
        host.Children.Add(_notices);
        return true;
    }

    Border? ShowNotice(string title, string text, string color, string mark, string? actionLabel, Action? action, TimeSpan? stay,
        string? secondLabel = null, Action? second = null)
    {
        if (!EnsureNoticeHost()) return null;
        var stack = _notices!;
        stack.HorizontalAlignment = _phone ? HorizontalAlignment.Center : HorizontalAlignment.Right;
        stack.Margin = _phone ? new Thickness(16, 0, 16, 78) : new Thickness(0, 0, 16, 36);

        // the same notice again (a second "connected", a suggestion shown again): the old one makes room, so it is not there twice
        foreach (var old in stack.Children.OfType<Border>().Where(b => b.Tag as string == title).ToList()) HideNotice(old);

        var card = new Border
        {
            CornerRadius = new CornerRadius(10), Padding = new Thickness(12, 8), BoxShadow = BoxShadows.Parse("0 2 10 0 #38000000"),
            Background = new SolidColorBrush(Color.Parse(color)), Tag = title, RenderTransform = new TranslateTransform(), Opacity = 0,
            Transitions = new Transitions { new DoubleTransition { Property = Visual.OpacityProperty, Duration = TimeSpan.FromMilliseconds(180) } },
        };
        var body = new StackPanel { Spacing = 2 };
        body.Children.Add(new TextBlock { Text = mark + title, FontWeight = FontWeight.SemiBold, FontSize = 13, Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap });
        if (text.Length > 0) body.Children.Add(new TextBlock { Text = text, FontSize = 12, Foreground = new SolidColorBrush(Color.Parse("#E8FFFFFF")), TextWrapping = TextWrapping.Wrap });
        if ((actionLabel is not null && action is not null) || (secondLabel is not null && second is not null))
        {
            // a notice that asks for a step (open SideStore, start the tour …) has its buttons; it stays longer, so there is time to read and tap
            var row = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
            Button Make(string label, Action run, bool main)
            {
                var b = new Button
                {
                    Content = label, Margin = new Thickness(0, 0, 6, 0), HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(12, 6), MinHeight = 36, FontSize = 13,
                    Background = main ? Brushes.White : new SolidColorBrush(Color.Parse("#33FFFFFF")), Foreground = main ? Brushes.Black : Brushes.White,
                };
                b.Click += (_, _) => { HideNotice(card); run(); };
                return b;
            }
            if (actionLabel is not null && action is not null) row.Children.Add(Make(actionLabel, action, true));
            if (secondLabel is not null && second is not null) row.Children.Add(Make(secondLabel, second, false));
            body.Children.Add(row);
        }
        card.Child = body;

        // a swipe to the side moves it with the finger and takes it away; a tap takes it away too
        double startX = 0; var down = false;
        card.PointerPressed += (_, e) => { down = true; startX = e.GetPosition(_noticeHost).X; card.Transitions = null; e.Pointer.Capture(card); e.Handled = true; };
        card.PointerMoved += (_, e) =>
        {
            if (!down || card.RenderTransform is not TranslateTransform t) return;
            t.X = e.GetPosition(_noticeHost).X - startX;
            card.Opacity = Math.Max(0.2, 1 - Math.Abs(t.X) / 200);
        };
        card.PointerReleased += (_, e) =>
        {
            if (!down) return;
            down = false; e.Pointer.Capture(null);
            var dx = e.GetPosition(_noticeHost).X - startX;
            if (Math.Abs(dx) is < 8 or > 50) HideNotice(card);
            else { if (card.RenderTransform is TranslateTransform t) t.X = 0; card.Opacity = 1; }
        };
        // while the mouse rests on it, it stays (there is time to read it to the end); afterwards it goes a few seconds later
        card.PointerEntered += (_, _) => { if (_noticeTimers.Remove(card, out var t)) t.Dispose(); };
        card.PointerExited += (_, _) => { if (card.Parent is not null && !_noticeTimers.ContainsKey(card)) _noticeTimers[card] = DispatcherTimer.RunOnce(() => HideNotice(card), TimeSpan.FromSeconds(4)); };

        stack.Children.Add(card);
        while (stack.Children.Count > MaxNotices && stack.Children[0] is Border oldest) HideNotice(oldest);
        Dispatcher.UIThread.Post(() => card.Opacity = 1, DispatcherPriority.Background);   // a soft fade in
        _noticeShownAt = DateTime.UtcNow;
        _noticeTimers[card] = DispatcherTimer.RunOnce(() => HideNotice(card),
            stay ?? TimeSpan.FromSeconds(action is not null ? 30 : text.Length > 60 ? 7 : 4));

        _noticeLog.Add(new(DateTime.Now, title, text, color));
        if (_noticeLog.Count > 50) _noticeLog.RemoveAt(0);
        return card;
    }

    /// <summary>Takes this notice away (null: does nothing, so a caller may keep the result of <see cref="Notify"/> without checking it).</summary>
    void HideNotice(Border? card)
    {
        if (card is null) return;
        if (_noticeTimers.Remove(card, out var t)) t.Dispose();
        _notices?.Children.Remove(card);
    }
}
