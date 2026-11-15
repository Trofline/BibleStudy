#if MOBILE
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Platform.Storage;

namespace BibleStudy.App;

/// <summary>
/// On Android there are no windows. The program opens its smaller windows (settings, word statistics, the projector …) as a
/// <c>Window</c>; in this namespace that name means this class on Android: the same members, but shown as a page over the app,
/// with a bar with its title and a back arrow. The back key of the phone closes the top one. A small window (a question, a form) stays
/// a card in the middle on a tablet; on a phone every one takes the whole screen.
/// </summary>
public class Window : ContentControl
{
    // the size the desktop window would have: used as the largest size of the card, never as a fixed size on a phone
    public new double Width { get; set; } = double.NaN;
    public new double Height { get; set; } = double.NaN;
    public new double MinWidth { get; set; }
    public new double MinHeight { get; set; }
    public new double MaxWidth { get; set; } = double.PositiveInfinity;
    public new double MaxHeight { get; set; } = double.PositiveInfinity;

    /// <summary>Shown as the one view of the app (the terms before the program starts), not as a page over it.</summary>
    public bool IsRootView { get; set; }

    public string? Title { get => _title.Text; set => _title.Text = value; }
    public WindowIcon? Icon { get; set; }
    public SizeToContent SizeToContent { get; set; }
    public bool CanResize { get; set; } = true;
    public bool CanMinimize { get; set; } = true;
    public bool CanMaximize { get; set; } = true;
    public bool Topmost { get; set; }
    public bool ShowInTaskbar { get; set; }
    public bool ShowActivated { get; set; } = true;
    public WindowStartupLocation WindowStartupLocation { get; set; }
    public WindowDecorations WindowDecorations { get; set; }
    public bool ExtendClientAreaToDecorationsHint { get; set; }
    public double ExtendClientAreaTitleBarHeightHint { get; set; }
    public WindowState WindowState { get; set; }
    public PixelPoint Position { get; set; }
    public Window? Owner { get; private set; }
    public bool IsActive => IsShown;
    public bool IsShown => _page is not null;
    public double RenderScaling => TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
    public Screens? Screens => null;
    public IStorageProvider? StorageProvider => TopLevel.GetTopLevel(this)?.StorageProvider;
    public IClipboard? Clipboard => TopLevel.GetTopLevel(this)?.Clipboard;
    public ILauncher? Launcher => TopLevel.GetTopLevel(this)?.Launcher;

    public event EventHandler? Opened;
    public event EventHandler? Closed;
    public event EventHandler<WindowClosingEventArgs>? Closing;
    public event EventHandler? Activated;
    public event EventHandler? Deactivated;
    public event EventHandler<PixelPointEventArgs>? PositionChanged;
    public event EventHandler? Resized;

    readonly TextBlock _title = new() { FontSize = 17, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    Control? _page;
    TaskCompletionSource<object?>? _dialog;
    object? _result;

    public Window()
    {
        // a window has a background of its own; as a page it must cover what is below it
        this.Bind(BackgroundProperty, this.GetResourceObservable("BsBackground"));
    }

    public void Show() => Open(null);
    public void Show(Visual? owner) => Open(owner);
    public Task ShowDialog(Visual owner) => ShowDialog<object?>(owner);

    public async Task<T> ShowDialog<T>(Visual owner)
    {
        _dialog = new TaskCompletionSource<object?>();
        Open(owner);
        var r = await _dialog.Task;
        return r is T t ? t : default!;
    }

    void Open(Visual? owner)
    {
        if (_page is not null) { AppWindow.Pages?.BringToFront(_page); return; }
        Owner = owner as Window;
        if (AppWindow.Pages is not { } pages) return;
        WrapLongText(this);
        _page = Build();
        pages.Add(_page, this);
        Opened?.Invoke(this, EventArgs.Empty);
        Activated?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The page: a bar with the back arrow and the title, below it the content (scrolling where it is taller than the screen).</summary>
    Control Build()
    {
        var back = new Button
        {
            Content = new Icon { Data = Geometry.Parse("M19 12H5 M12 19l-7-7 7-7") },
            Background = Brushes.Transparent, BorderThickness = new Thickness(0), Width = 48, Height = 48, Padding = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center,
        };
        ToolTip.SetTip(back, L("Schließen"));
        back.Click += (_, _) => Close();
        var bar = new DockPanel { Height = 52, Children = { back, _title } };
        // a dark page (the projector) gets a light bar text, else the arrow and the title would vanish on it
        if (Background is ISolidColorBrush { Color: var bg } && (0.299 * bg.R + 0.587 * bg.G + 0.114 * bg.B) < 110)
        {
            _title.Foreground = Brushes.WhiteSmoke;
            back.Foreground = Brushes.WhiteSmoke;
        }
        else { _title.ClearValue(TextBlock.ForegroundProperty); back.ClearValue(ForegroundProperty); }
        _title.Margin = new Thickness(4, 0, 12, 0);
        var barBorder = new Border { Child = bar, BorderThickness = new Thickness(0, 0, 0, 1) };
        barBorder.Bind(Border.BorderBrushProperty, this.GetResourceObservable("BsBorder"));
        DockPanel.SetDock(barBorder, Dock.Top);

        // a window that fits its content: it scrolls instead of cutting off its buttons
        Control body = new FitWidthPanel { Child = this };
        var fits = SizeToContent is SizeToContent.Height or SizeToContent.WidthAndHeight;
        if (fits && Content is not ScrollViewer)
            body = new ScrollViewer { Content = body, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var dock = new DockPanel { Children = { barBorder, body } };

        var card = new Border
        {
            Child = dock, ClipToBounds = true,
            MaxWidth = double.IsNaN(Width) ? double.PositiveInfinity : Math.Max(Width, 360),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = fits ? VerticalAlignment.Center : VerticalAlignment.Stretch,
        };
        card.Bind(Border.BackgroundProperty, this.GetObservable(BackgroundProperty));
        return new Border { Background = new SolidColorBrush(Color.FromArgb(0x99, 0, 0, 0)), Child = card };
    }

    /// <summary>A long label or heading that does not wrap would reach beyond a phone screen (or make the whole page shrink): it wraps.</summary>
    static void WrapLongText(Control root)
    {
        foreach (var tb in Avalonia.LogicalTree.LogicalExtensions.GetLogicalDescendants(root).OfType<TextBlock>())
        {
            if (tb.TextWrapping != TextWrapping.NoWrap || tb.TextTrimming != TextTrimming.None || (tb.Text?.Length ?? 0) < 16) continue;
            if (Avalonia.LogicalTree.LogicalExtensions.GetLogicalAncestors(tb).Any(a => a is Button or MenuItem or TabItem or ComboBoxItem or ListBoxItem)) continue;
            tb.TextWrapping = TextWrapping.Wrap;
        }
    }

    public void Close() => Close(null);

    public void Close(object? result)
    {
        if (_page is null && !IsRootView) return;
        var e = AppWindow.ClosingArgs(WindowCloseReason.WindowClosing);
        Closing?.Invoke(this, e);
        if (e.Cancel) return;
        if (_page is not null) AppWindow.Pages?.Remove(_page);
        if (_page is Border { Child: Border { Child: DockPanel dock } }) dock.Children.Clear();   // the window can be shown again
        if (Parent is FitWidthPanel fit) fit.Child = null;
        if (Parent is ScrollViewer sv) sv.Content = null;
        _page = null;
        _result = result;
        Deactivated?.Invoke(this, EventArgs.Empty);
        Closed?.Invoke(this, EventArgs.Empty);
        _dialog?.TrySetResult(result);
        _dialog = null;
    }

    public void Hide() => Close();
    public void Activate() { if (_page is not null) AppWindow.Pages?.BringToFront(_page); }
    public void BeginMoveDrag(PointerPressedEventArgs e) { }
    public void BeginResizeDrag(WindowEdge edge, PointerPressedEventArgs e) { }
}

static class AndroidVisualExtensions
{
    public static IEnumerable<T> GetVisualDescendantsOf<T>(this Visual v) where T : Visual =>
        Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(v).OfType<T>();
}

/// <summary>The layer over the app in which the windows of <see cref="Window"/> lie as pages, the newest on top.</summary>
public sealed class PageLayer : Panel
{
    readonly List<(Control Page, Window Window)> _open = [];

    public int Count => _open.Count;
    public IReadOnlyList<Window> Windows => _open.Select(o => o.Window).ToList();

    public void Add(Control page, Window window)
    {
        _open.Add((page, window));
        Children.Add(page);
        IsHitTestVisible = true;
    }

    public void Remove(Control page)
    {
        _open.RemoveAll(o => o.Page == page);
        Children.Remove(page);
    }

    public void BringToFront(Control page)
    {
        var i = _open.FindIndex(o => o.Page == page);
        if (i < 0) return;
        var item = _open[i];
        _open.RemoveAt(i); _open.Add(item);
        Children.Remove(page); Children.Add(page);
    }

    /// <summary>The back key: closes the top page; false when there is none.</summary>
    public bool CloseTop()
    {
        if (_open.Count == 0) return false;
        _open[^1].Window.Close();
        return true;
    }
}

/// <summary>
/// A dialog is made for a desktop: its parts have fixed widths of 400, 500 or 640. On a phone they would reach beyond the screen. When the content
/// is wider than the space, it is shown smaller (at least half) instead of cut off; content that adapts to the width is left as it is.
/// </summary>
public sealed class FitWidthPanel : Decorator
{
    double _scale = 1;

    protected override Size MeasureOverride(Size available)
    {
        if (Child is not { } child) return default;
        _scale = 1;
        child.Measure(new Size(available.Width, double.PositiveInfinity));
        if (double.IsInfinity(available.Width) || child.DesiredSize.Width <= available.Width + 0.5)
        {
            child.Measure(available);
            return new Size(Math.Min(child.DesiredSize.Width, available.Width), child.DesiredSize.Height);
        }
        _scale = Math.Max(0.5, available.Width / child.DesiredSize.Width);
        child.Measure(new Size(available.Width / _scale, double.IsInfinity(available.Height) ? double.PositiveInfinity : available.Height / _scale));
        return new Size(available.Width, child.DesiredSize.Height * _scale);
    }

    protected override Size ArrangeOverride(Size final)
    {
        if (Child is not { } child) return final;
        child.RenderTransformOrigin = RelativePoint.TopLeft;
        child.RenderTransform = _scale < 1 ? new ScaleTransform(_scale, _scale) : null;
        child.Arrange(new Rect(0, 0, final.Width / _scale, final.Height / _scale));
        return final;
    }
}
#endif
