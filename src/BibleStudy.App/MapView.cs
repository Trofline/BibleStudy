using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using BibleStudy.Core.Models;

namespace BibleStudy.App;

/// <summary>Map of the places of a chapter or a whole book: places as dots, the order of mention as a dashed route.</summary>
public sealed class MapView : UserControl
{
    /// <summary>Book id, chapter, verse.</summary>
    public event Action<string, int, int>? OpenVerse;
    public event Action? CloseRequested;

    public Func<Task<WorldMap>>? MapProvider { get; set; }
    public Func<string, int, Task<IReadOnlyList<MapPlace>>>? PlacesProvider { get; set; }

    readonly MapCanvas _canvas = new();
    readonly ToggleButton _chapterBtn = new() { Content = "Kapitel", IsChecked = true };
    readonly ToggleButton _bookBtn = new() { Content = "Ganzes Buch" };
    readonly CheckBox _routeBox = new() { Content = "Weg in Reihenfolge der Erwähnung", IsChecked = true };
    readonly TextBlock _title = new() { FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock _hint = new() { FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    readonly TextBlock _detailTitle = new() { FontWeight = FontWeight.SemiBold, FontSize = 15 };
    readonly TextBlock _detailInfo = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap };
    readonly WrapPanel _detailChips = new();
    readonly Border _detail = new() { CornerRadius = new(12), Padding = new(14, 10), Margin = new(16, 0, 16, 8), IsVisible = false, BorderThickness = new(1) };

    string _book = "", _bookName = "";
    int _chapter, _chapters;
    WorldMap? _map;
    readonly Dictionary<string, List<(int Chapter, int Verse)>> _mentions = [];
    int _generation;

    public MapView()
    {
        _hint.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("BsMuted"));
        _detailInfo.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("BsMuted"));
        _detailTitle.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("BsAccent"));
        _detail.Bind(Border.BackgroundProperty, this.GetResourceObservable("BsSurface"));
        _detail.Bind(Border.BorderBrushProperty, this.GetResourceObservable("BsBorder"));
        _detail.Child = new StackPanel { Children = { _detailTitle, _detailInfo, _detailChips } };

        var back = new Button { Content = "← Zum Text" };
        back.Click += (_, _) => CloseRequested?.Invoke();
        var strip = new WrapPanel { Margin = new(16, 10, 16, 6) };
        void Add(Control c, double right = 10) { c.Margin = new(0, 0, right, 4); c.VerticalAlignment = VerticalAlignment.Center; strip.Children.Add(c); }
        Add(back, 16); Add(_chapterBtn, 4); Add(_bookBtn, 16); Add(_routeBox, 16); Add(_title);
        var hintRow = new Border { Height = 24, Padding = new(16, 0), Child = _hint };

        var source = new TextBlock { FontSize = 11, Margin = new(16, 4, 16, 8), Text = $"{WorldMap.Attribution} · {Core.Providers.HelloAoProvider.PlacesAttribution}", TextWrapping = TextWrapping.Wrap };
        source.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("BsMuted"));
        var bottom = new StackPanel { Children = { _detail, source } };

        var dock = new DockPanel();
        DockPanel.SetDock(strip, Dock.Top); DockPanel.SetDock(hintRow, Dock.Top); DockPanel.SetDock(bottom, Dock.Bottom);
        dock.Children.Add(strip); dock.Children.Add(hintRow); dock.Children.Add(bottom); dock.Children.Add(_canvas);
        Content = dock;

        _chapterBtn.Click += async (_, _) => { _chapterBtn.IsChecked = true; _bookBtn.IsChecked = false; await LoadAsync(); };
        _bookBtn.Click += async (_, _) => { _bookBtn.IsChecked = true; _chapterBtn.IsChecked = false; await LoadAsync(); };
        _routeBox.IsCheckedChanged += (_, _) => _canvas.ShowRoute = _routeBox.IsChecked == true;
        _canvas.Hovered += text => _hint.Text = text;
        _canvas.Selected += ShowDetail;
    }

    public async Task OpenAsync(string book, string bookName, int chapter, int chapters)
    {
        _book = book; _bookName = bookName; _chapter = chapter; _chapters = chapters;
        _detail.IsVisible = false;
        await LoadAsync();
    }

    async Task LoadAsync()
    {
        var generation = ++_generation;
        var wholeBook = _bookBtn.IsChecked == true;
        _title.Text = wholeBook ? _bookName : $"{_bookName} {_chapter}";
        try
        {
            if (_map is null)
            {
                _hint.Text = "Karte wird geladen (einmalig, danach offline) …";
                _map = await MapProvider!();
                _canvas.SetMap(_map);
            }
            _hint.Text = wholeBook ? "Orte des Buches werden geladen …" : "";
            _mentions.Clear();
            var places = new List<MapPlace>();
            var seen = new HashSet<string>();
            var chapters = wholeBook ? Enumerable.Range(1, _chapters).ToList() : [_chapter];
            using var gate = new SemaphoreSlim(8);
            var results = await Task.WhenAll(chapters.Select(async c =>
            {
                await gate.WaitAsync();
                try { return (Chapter: c, Places: await PlacesProvider!(_book, c)); }
                catch (HttpRequestException) { return (Chapter: c, Places: (IReadOnlyList<MapPlace>)[]); }
                finally { gate.Release(); }
            }));
            if (generation != _generation) return; // the user switched scope meanwhile
            foreach (var (chapter, list) in results.OrderBy(r => r.Chapter))
                foreach (var place in list)
                {
                    if (!_mentions.TryGetValue(place.Id, out var mentions)) _mentions[place.Id] = mentions = [];
                    mentions.AddRange(place.Verses.Select(v => (chapter, v)));
                    if (seen.Add(place.Id)) places.Add(place);
                }
            _canvas.SetPlaces(places);
            _hint.Text = places.Count == 0 ? "Keine Orte mit Koordinaten in diesem Abschnitt." : $"{places.Count} Orte · Mausrad: zoomen · ziehen: verschieben · Ort anklicken: Stellen";
        }
        catch (Exception ex) { _hint.Text = "Karte nicht verfügbar: " + ex.Message; }
    }

    void ShowDetail(MapPlace? place)
    {
        if (place is null) { _detail.IsVisible = false; return; }
        _detailTitle.Text = place.Name;
        _detailInfo.Text = $"{(string.IsNullOrEmpty(place.FeatureType) ? "Ort" : place.FeatureType)} · {place.Latitude.ToString("0.00", CultureInfo.InvariantCulture)}°, {place.Longitude.ToString("0.00", CultureInfo.InvariantCulture)}°";
        _detailChips.Children.Clear();
        foreach (var (chapter, verse) in _mentions.GetValueOrDefault(place.Id, []).Take(30))
        {
            var chip = new Button { Content = $"{_bookName} {chapter},{verse}", FontSize = 12, Padding = new(8, 2), Margin = new(0, 6, 6, 0) };
            var (c, v) = (chapter, verse);
            chip.Click += (_, _) => OpenVerse?.Invoke(_book, c, v);
            _detailChips.Children.Add(chip);
        }
        _detail.IsVisible = true;
    }
}

/// <summary>Mercator map drawn from Natural Earth outlines; wheel zooms, dragging pans.</summary>
sealed class MapCanvas : Control
{
    public event Action<string>? Hovered;
    public event Action<MapPlace?>? Selected;

    static readonly IBrush Ocean = new SolidColorBrush(Color.Parse("#0d1b2a"));
    static readonly IBrush Land = new SolidColorBrush(Color.Parse("#24303d"));
    static readonly Pen Coast = new(new SolidColorBrush(Color.Parse("#3a4a5c")), 0.6);

    public MapCanvas() => ClipToBounds = true; // the map is larger than the window

    StreamGeometry? _land, _lakes, _rivers;
    IReadOnlyList<MapPlace> _places = [];
    int _hover = -1, _selected = -1;
    double _cx = 35, _cy = MercY(31.5), _scale = 20;
    Point? _dragFrom; Vector _dragCenter; bool _dragged;
    bool _showRoute = true;

    public bool ShowRoute { get => _showRoute; set { _showRoute = value; InvalidateVisual(); } }

    static double MercY(double lat) => 180 / Math.PI * Math.Log(Math.Tan(Math.PI / 4 + Math.Clamp(lat, -85, 85) * Math.PI / 360));

    static StreamGeometry Build(IEnumerable<double[]> shapes, bool closed)
    {
        var g = new StreamGeometry();
        using var ctx = g.Open();
        foreach (var s in shapes)
        {
            if (s.Length < 4) continue;
            ctx.BeginFigure(new Point(s[0], MercY(s[1])), closed);
            for (var i = 2; i < s.Length; i += 2) ctx.LineTo(new Point(s[i], MercY(s[i + 1])));
            ctx.EndFigure(closed);
        }
        return g;
    }

    public void SetMap(WorldMap map)
    {
        _land = Build(map.Land, true); _lakes = Build(map.Lakes, true); _rivers = Build(map.Rivers, false);
        InvalidateVisual();
    }

    public void SetPlaces(IReadOnlyList<MapPlace> places)
    {
        _places = places; _hover = -1; _selected = -1;
        Selected?.Invoke(null);
        Fit();
        InvalidateVisual();
    }

    void Fit()
    {
        if (_places.Count == 0 || Bounds.Width < 50) return;
        double minX = _places.Min(p => p.Longitude), maxX = _places.Max(p => p.Longitude), minY = _places.Min(p => MercY(p.Latitude)), maxY = _places.Max(p => MercY(p.Latitude));
        _cx = (minX + maxX) / 2; _cy = (minY + maxY) / 2;
        var spanX = Math.Max(maxX - minX, 0.5) * 1.35; var spanY = Math.Max(maxY - minY, 0.5) * 1.35;
        _scale = Math.Clamp(Math.Min(Bounds.Width / spanX, Bounds.Height / spanY), 4, 250);
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e) { base.OnSizeChanged(e); Fit(); }

    Point ToScreen(MapPlace p) => new((p.Longitude - _cx) * _scale + Bounds.Width / 2, -(MercY(p.Latitude) - _cy) * _scale + Bounds.Height / 2);

    static IBrush ColorOf(string type) => new SolidColorBrush(Color.Parse(type switch
    {
        "City" or "Town" => "#f2c14e", "Region" or "Country" => "#8ab4f8", "Mountain" or "Hill" => "#c58a5a",
        "River" or "Sea" or "Lake" or "Water" or "Wadi" => "#5bc0de", _ => "#d98cb3",
    }));

    public override void Render(DrawingContext dc)
    {
        dc.DrawRectangle(Ocean, null, new Rect(Bounds.Size));
        if (_land is not null)
        {
            var m = Matrix.CreateTranslation(-_cx, -_cy) * Matrix.CreateScale(_scale, -_scale) * Matrix.CreateTranslation(Bounds.Width / 2, Bounds.Height / 2);
            using (dc.PushTransform(m))
            {
                dc.DrawGeometry(Land, new Pen(Coast.Brush, 0.7 / _scale), _land);
                dc.DrawGeometry(Ocean, null, _lakes!);
                dc.DrawGeometry(null, new Pen(new SolidColorBrush(Color.Parse("#2f6f9f")), 1.2 / _scale), _rivers!);
            }
        }
        var accent = this.TryFindResource("BsAccent", ActualThemeVariant, out var a) && a is IBrush ab ? ab : Brushes.Gold;
        var text = this.TryFindResource("BsText", ActualThemeVariant, out var t) && t is IBrush tb ? tb : Brushes.White;
        if (_showRoute && _places.Count > 1)
        {
            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                ctx.BeginFigure(ToScreen(_places[0]), false);
                for (var i = 1; i < _places.Count; i++) ctx.LineTo(ToScreen(_places[i]));
                ctx.EndFigure(false);
            }
            dc.DrawGeometry(null, new Pen(accent, 1.8, new DashStyle([4, 3], 0)), geometry);
        }
        for (var i = 0; i < _places.Count; i++)
        {
            var p = ToScreen(_places[i]);
            if (p.X < -20 || p.Y < -20 || p.X > Bounds.Width + 20 || p.Y > Bounds.Height + 20) continue;
            var radius = i == _selected ? 8 : i == _hover ? 7 : 5;
            dc.DrawEllipse(ColorOf(_places[i].FeatureType), new Pen(i == _selected ? text : Brushes.Black, i == _selected ? 2 : 1), p, radius, radius);
            if (_places.Count <= 40 || i == _hover || i == _selected || _scale > 40)
            {
                var label = new FormattedText($"{(_places.Count <= 60 ? (i + 1) + " " : "")}{_places[i].Name}", CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Typeface.Default, 11, text);
                dc.DrawRectangle(new SolidColorBrush(Color.Parse("#0d1b2a"), 0.65), null, new Rect(p.X + 8, p.Y - label.Height / 2 - 1, label.Width + 6, label.Height + 2));
                dc.DrawText(label, new Point(p.X + 11, p.Y - label.Height / 2));
            }
        }
        if (_places.Count == 0)
            dc.DrawText(new FormattedText("Keine Orte mit Koordinaten in diesem Abschnitt.", CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Typeface.Default, 14, text), new Point(24, 20));
    }

    int HitTest(Point p)
    {
        for (var i = _places.Count - 1; i >= 0; i--) { var s = ToScreen(_places[i]); if (Math.Abs(s.X - p.X) <= 9 && Math.Abs(s.Y - p.Y) <= 9) return i; }
        return -1;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        var p = e.GetPosition(this);
        if (_dragFrom is { } from && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            if (Math.Abs(p.X - from.X) + Math.Abs(p.Y - from.Y) > 3) _dragged = true;
            if (_dragged)
            {
                _cx = _dragCenter.X - (p.X - from.X) / _scale; _cy = _dragCenter.Y + (p.Y - from.Y) / _scale;
                InvalidateVisual();
            }
            return;
        }
        var i = HitTest(p);
        if (i == _hover) return;
        _hover = i;
        Cursor = i >= 0 ? new Cursor(StandardCursorType.Hand) : null;
        Hovered?.Invoke(i < 0 ? "" : $"{_places[i].Name} · {_places[i].FeatureType}");
        InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        _dragFrom = e.GetPosition(this); _dragCenter = new Vector(_cx, _cy); _dragged = false;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        if (_dragFrom is { } from && !_dragged)
        {
            _selected = HitTest(from);
            Selected?.Invoke(_selected >= 0 ? _places[_selected] : null);
            InvalidateVisual();
        }
        _dragFrom = null;
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        var p = e.GetPosition(this);
        var before = (X: _cx + (p.X - Bounds.Width / 2) / _scale, Y: _cy - (p.Y - Bounds.Height / 2) / _scale);
        _scale = Math.Clamp(_scale * (e.Delta.Y > 0 ? 1.25 : 1 / 1.25), 2, 3000);
        _cx = before.X - (p.X - Bounds.Width / 2) / _scale; _cy = before.Y + (p.Y - Bounds.Height / 2) / _scale; // keep the point under the cursor
        InvalidateVisual();
        e.Handled = true;
    }
}
