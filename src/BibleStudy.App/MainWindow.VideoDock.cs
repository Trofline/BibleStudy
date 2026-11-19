using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace BibleStudy.App;

/// <summary>
/// The pictures of the others in a call: a small field above the reading text that stays in view (four sizes, four corners), and the same
/// pictures large in a window of their own (always on top if wanted, full screen with a double click).
/// </summary>
public partial class MainWindow
{
    readonly Dictionary<string, (string Name, Color Ink)> _tileInfo = [];
    Border? _dock;
    StackPanel? _dockTiles;
    bool _dockWanted;
    int _dockSize = 1, _dockCorner;
    static readonly double[] DockWidths = [140, 220, 320, 440];

    /// <summary>One picture with its name; the image is the first child of the grid. A width makes it a fixed size, otherwise it fills its cell.</summary>
    static Border VideoBox(string id, string name, Color ink, double? width, Bitmap? bmp)
    {
        var image = new Image { Stretch = Stretch.Uniform, Source = bmp };
        if (width is double w) { image.Width = w; image.Height = w * 0.75; }
        var label = new Border { Background = new SolidColorBrush(ink), CornerRadius = new(4), Padding = new(6, 2), Margin = new(6), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom, Child = new TextBlock { Text = name, Foreground = Brushes.White, FontSize = 12 } };
        return new Border { Tag = id, Margin = new(4), VerticalAlignment = VerticalAlignment.Center, BorderThickness = new(2), BorderBrush = new SolidColorBrush(ink), CornerRadius = new(8), ClipToBounds = true, Background = Brushes.Black, Child = new Grid { Children = { image, label } } };
    }

    static Image VideoImage(Border box) => (Image)((Grid)box.Child!).Children[0];

    void ToggleDock() { _dockWanted = !_dockWanted; RefreshDock(); }

    /// <summary>Shows, fills or hides the field. It is shown when wanted, while there are pictures and while they are not in the window of their own.</summary>
    void RefreshDock()
    {
        var show = _dockWanted && !AppPlatform.IsMobile && _bigVideo is null && _tiles.Count > 0;
        if (!show) { if (_dock?.Parent is Panel old) old.Children.Remove(_dock); return; }
        if (_dock is null) BuildDock();
        _dockTiles!.Children.Clear();
        foreach (var (id, t) in _tiles)
        {
            var info = _tileInfo.TryGetValue(id, out var i) ? i : (Name: "", Ink: Color.Parse("#30A46C"));
            _dockTiles.Children.Add(VideoBox(id, info.Name, info.Ink, DockWidths[_dockSize], t.Image.Source as Bitmap));
        }
        PlaceDock();
        if (_dock!.Parent is null) ReaderHost.Children.Add(_dock);
    }

    void BuildDock()
    {
        Button Tool(string glyph, string tip, Action click)
        {
            var b = new Button { Content = glyph, Classes = { "ghost" }, Padding = new(8, 2), MinWidth = 0 };
            ToolTip.SetTip(b, tip);
            b.Click += (_, _) => click();
            return b;
        }
        _dockTiles = new StackPanel { Spacing = 2 };
        var bar = new StackPanel
        {
            Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
            Children =
            {
                Tool("−", L("Kleiner"), () => { _dockSize = Math.Max(0, _dockSize - 1); RefreshDock(); }),
                Tool("+", L("Größer"), () => { _dockSize = Math.Min(DockWidths.Length - 1, _dockSize + 1); RefreshDock(); }),
                Tool("⇄", L("In die nächste Ecke"), () => { _dockCorner = (_dockCorner + 1) % 4; PlaceDock(); }),
                Tool("⧉", L("Als eigenes Fenster"), ShowBigVideo),
                Tool("✕", L("Nicht mehr festhalten"), () => { _dockWanted = false; RefreshDock(); }),
            },
        };
        _dock = new Border
        {
            CornerRadius = new(10), Padding = new(4), BorderThickness = new(1), ZIndex = 55,
            BoxShadow = BoxShadows.Parse("0 4 18 0 #33000000"),
            Child = new StackPanel { Children = { bar, _dockTiles } },
        };
        _dock.Bind(Border.BackgroundProperty, this.GetResourceObservable("BsSurface2"));
        _dock.Bind(Border.BorderBrushProperty, this.GetResourceObservable("BsBorder"));
    }

    /// <summary>One of the four corners of the reading area (the first is at the top right, where nothing of the text is hidden at the start of a line).</summary>
    void PlaceDock()
    {
        if (_dock is null) return;
        _dock.HorizontalAlignment = _dockCorner is 0 or 1 ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        _dock.VerticalAlignment = _dockCorner is 0 or 3 ? VerticalAlignment.Top : VerticalAlignment.Bottom;
        _dock.Margin = new(14, 10, 22, _dockCorner is 1 or 2 ? 22 : 10);
        Grid.SetRow(_dock, 1); Grid.SetColumn(_dock, 0);
    }

    void UpdateDock(string id, Bitmap bmp)
    {
        if (!_dockWanted) return;
        if (_dockTiles?.Children.FirstOrDefault(c => c.Tag as string == id) is Border box && _dock?.Parent is not null) VideoImage(box).Source = bmp;
        else RefreshDock();   // a new picture
    }

    /// <summary>All pictures large in a window of their own (on a computer).</summary>
    void ShowBigVideo()
    {
        if (AppPlatform.IsMobile) return;
        if (_bigVideo is { IsVisible: true }) { _bigVideo.Activate(); return; }
        _bigGrid = new UniformGrid { Margin = new(8) };
        foreach (var (id, t) in _tiles)
        {
            var info = _tileInfo.TryGetValue(id, out var i) ? i : (Name: "", Ink: Color.Parse("#30A46C"));
            _bigGrid.Children.Add(VideoBox(id, info.Name, info.Ink, null, t.Image.Source as Bitmap));
        }
        var win = new Window { Title = L("Zusammen studieren · Video"), Width = 900, Height = 600, MinWidth = 320, MinHeight = 240, Background = Brushes.Black };
        var top = new Button { Content = L("Immer im Vordergrund"), Margin = new(8, 6, 4, 0) };
        top.Click += (_, _) => { win.Topmost = !win.Topmost; top.Classes.Set("primary", win.Topmost); };
        var back = new Button { Content = L("Zurück ins Programm"), Margin = new(4, 6, 8, 0) };
        back.Click += (_, _) => win.Close();
        var tip = new TextBlock { Text = L("Doppelklick: Vollbild"), Opacity = 0.6, VerticalAlignment = VerticalAlignment.Center, Margin = new(8, 6, 0, 0) };
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Children = { top, back, tip } };
        DockPanel.SetDock(bar, Dock.Top);
        win.Content = new DockPanel { Children = { bar, _bigGrid } };
        win.DoubleTapped += (_, e) => { if (e.Source is not Button) win.WindowState = win.WindowState == WindowState.FullScreen ? WindowState.Normal : WindowState.FullScreen; };
        win.Closed += (_, _) => { _bigVideo = null; _bigGrid = null; RefreshDock(); };
        _bigVideo = win;
        RefreshDock();   // the field gives way to the window
        win.Show(this);
    }

    void UpdateBigVideo(string id, string name, Color ink, Bitmap bmp)
    {
        if (_bigGrid is null) return;
        if (_bigGrid.Children.FirstOrDefault(c => c.Tag as string == id) is not Border box)
        {
            box = VideoBox(id, name, ink, null, null);
            _bigGrid.Children.Add(box);
        }
        VideoImage(box).Source = bmp;
    }
}
