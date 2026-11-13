using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Platform;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Platform;
using Avalonia.Platform.Storage;

namespace BibleStudy.App;

#if ANDROID
/// <summary>
/// On Android there are no windows: the main window is the one view of the app. This gives it the members of a window the program
/// uses, so that the same code runs; what a phone has not (position, size, a title bar) does nothing there.
/// </summary>
public class AppWindow : UserControl
{
    public string? Title { get; set; }
    public WindowState WindowState { get; set; } = WindowState.Maximized;
    public PixelPoint Position { get; set; }
    public WindowIcon? Icon { get; set; }
    public bool Topmost { get; set; }
    public bool ShowInTaskbar { get; set; }
    public bool CanResize { get; set; }
    public bool ShowActivated { get; set; }
    public SizeToContent SizeToContent { get; set; }
    public WindowStartupLocation WindowStartupLocation { get; set; }
    public WindowDecorations WindowDecorations { get; set; }
    public double RenderScaling => TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
    public bool IsActive => true;

    public IStorageProvider StorageProvider => TopLevel.GetTopLevel(this)?.StorageProvider ?? throw new InvalidOperationException("not shown yet");
    public IClipboard? Clipboard => TopLevel.GetTopLevel(this)?.Clipboard;
    public ILauncher Launcher => TopLevel.GetTopLevel(this)?.Launcher ?? throw new InvalidOperationException("not shown yet");
    public IInsetsManager? InsetsManager => TopLevel.GetTopLevel(this)?.InsetsManager;
    public Screens? Screens => null;
    public IFocusManager? FocusManager => TopLevel.GetTopLevel(this)?.FocusManager;
    /// <summary>The pages open over the app: on a desktop the windows the main window owns.</summary>
    public IReadOnlyList<Window> OwnedWindows => Pages?.Windows ?? [];

    public static WindowClosingEventArgs ClosingArgs(WindowCloseReason reason) =>
        (WindowClosingEventArgs)Activator.CreateInstance(typeof(WindowClosingEventArgs), System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public, null, [reason, false], null)!;

    public event EventHandler? Opened;
    public event EventHandler? Closed;
    public event EventHandler<WindowClosingEventArgs>? Closing;
    public event EventHandler? Activated;
    public event EventHandler? Deactivated;
    public event EventHandler<PixelPointEventArgs>? PositionChanged;
    public event EventHandler? Resized;

    public void Show() { }
    public void Activate() { }
    public void Hide() { }
    public void BeginMoveDrag(Avalonia.Input.PointerPressedEventArgs e) { }
    public void BeginResizeDrag(WindowEdge edge, Avalonia.Input.PointerPressedEventArgs e) { }

    /// <summary>The app goes to the background (Android ends it when it needs the memory).</summary>
    public void Close() => Closed?.Invoke(this, EventArgs.Empty);

    /// <summary>The layer over the app where the program's other windows lie as pages (see <see cref="Window"/>).</summary>
    public static PageLayer? Pages { get; private set; }

    /// <summary>Asked first when the back key is pressed (after the pages): true when it did something (closed a panel, went back).</summary>
    public Func<bool>? BackHandler { get; set; }

    bool _opened;

    /// <summary>The size the desktop window asks for (Width 1280, MinWidth 850 …) never applies to the screen of a phone.</summary>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == WidthProperty || change.Property == HeightProperty) { if (!double.IsNaN((double)change.NewValue!)) SetCurrentValue(change.Property, double.NaN); }
        else if (change.Property == MinWidthProperty || change.Property == MinHeightProperty) { if ((double)change.NewValue! != 0) SetCurrentValue(change.Property, 0d); }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (_opened) return;
        _opened = true;
        // the content from the XAML goes below a layer for the pages
        if (Pages is null && Content is Control body)
        {
            Content = null;
            Pages = new PageLayer();
            Content = new Panel { Children = { body, Pages } };
        }
        if (TopLevel.GetTopLevel(this) is { } top)
            top.BackRequested += (_, args) =>
            {
                if (Pages?.CloseTop() == true || BackHandler?.Invoke() == true) args.Handled = true;
            };
        Opened?.Invoke(this, EventArgs.Empty);
        Activated?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Called by the activity when Android stops the app: the same as closing the window on a desktop.</summary>
    public void RaiseClosing()
    {
        Closing?.Invoke(this, ClosingArgs(WindowCloseReason.ApplicationShutdown));
        Closed?.Invoke(this, EventArgs.Empty);
    }
}
#else
/// <summary>The main window; on Android (see the other half of this file) a plain view with the same members.</summary>
public class AppWindow : Window
{
}
#endif
