using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using BibleStudy.Core.Models;

namespace BibleStudy.App;

/// <summary>
/// Timeline of the Bible with a coupled map: a ruler over all events, the event list, and for the chosen event its people,
/// passages and the places drawn on the map. Data: Theographic (CC BY-SA 4.0).
/// </summary>
public sealed class TimelineView : UserControl
{
    public event Action? CloseRequested;
    /// <summary>Book id, chapter, verse.</summary>
    public event Action<string, int, int>? OpenVerse;

    public Func<Task<IReadOnlyList<TimelineEvent>>>? EventsProvider { get; set; }
    public Func<string, Task<EventDetail>>? DetailProvider { get; set; }
    public Func<string, Task<MapPlace?>>? PlaceProvider { get; set; }
    public Func<Task<WorldMap>>? MapProvider { get; set; }
    public Func<string, string>? BookName { get; set; }

    static readonly (string Name, int From, int To)[] Eras =
    [
        ("Alles", int.MinValue, int.MaxValue), ("Urgeschichte", int.MinValue, -2100), ("Erzväter", -2100, -1500), ("Exodus bis Richter", -1500, -1050),
        ("Königreich", -1050, -586), ("Exil und Rückkehr", -586, -400), ("Jesus", -10, 34), ("Urgemeinde", 34, 100),
    ];

    readonly Ruler _ruler = new();
    readonly ListBox _list = new();
    readonly MapCanvas _canvas = new();
    readonly WrapPanel _eras = new() { Margin = new(16, 0, 16, 4) };
    readonly TextBlock _title = new() { FontSize = 18, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap };
    readonly TextBlock _meta = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap };
    readonly WrapPanel _chips = new();
    readonly TextBlock _people = new() { TextWrapping = TextWrapping.Wrap, Margin = new(0, 4, 0, 0) };
    readonly TextBlock _placesText = new() { TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 6) };
    readonly TextBlock _hint = new() { FontSize = 12, Margin = new(16, 2, 16, 6), TextWrapping = TextWrapping.Wrap };
    readonly TextBlock _source = new() { FontSize = 11, Margin = new(16, 4, 16, 8), TextWrapping = TextWrapping.Wrap };

    IReadOnlyList<TimelineEvent> _all = [];
    List<TimelineEvent> _shown = [];
    int _era;
    bool _mapLoaded;
    int _generation;

    public TimelineView()
    {
        _meta.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("BsMuted"));
        _hint.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("BsMuted"));
        _source.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("BsMuted"));
        _title.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("BsAccent"));

        var back = new Button { Content = L("← Zum Text"), Margin = new(16, 10, 12, 6) };
        back.Click += (_, _) => CloseRequested?.Invoke();
        var head = new StackPanel { Orientation = Orientation.Horizontal, Children = { back, new TextBlock { Text = L("Zeitleiste"), FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center } } };
        for (var i = 0; i < Eras.Length; i++)
        {
            var k = i;
            var b = new Button { Classes = { "seg" }, Content = Eras[i].Name };
            b.Click += (_, _) => { _era = k; ApplyEra(); };
            _eras.Children.Add(b);
        }
        _ruler.Picked += e => Select(e, fromRuler: true);
        _list.SelectionChanged += (_, _) => { if (_list.SelectedIndex is >= 0 and var i && i < _shown.Count) Select(_shown[i], fromRuler: false); };

        var right = new DockPanel();
        var info = new StackPanel { Margin = new(16, 8, 16, 4), Spacing = 4, Children = { _title, _meta, _people, _placesText, _chips } };
        DockPanel.SetDock(info, Dock.Top);
        right.Children.Add(info);
        var mapBox = new Border { Margin = new(16, 4, 16, 4), CornerRadius = new(12), ClipToBounds = true, Child = _canvas, MinHeight = 240 };
        right.Children.Add(mapBox);


        var body = new Grid { ColumnDefinitions = new("340,*") };
        body.Children.Add(new Border { Child = _list, Margin = new(8, 0, 0, 0) });
        Grid.SetColumn(right, 1);
        body.Children.Add(right);

        var root = new DockPanel();
        foreach (var c in new Control[] { head, _eras, _ruler }) { DockPanel.SetDock(c, Dock.Top); root.Children.Add(c); }
        DockPanel.SetDock(_hint, Dock.Top); root.Children.Add(_hint);
        _source.Text = $"{Core.Providers.HelloAoProvider.TimelineAttribution} · {WorldMap.Attribution}";
        DockPanel.SetDock(_source, Dock.Bottom); root.Children.Add(_source);
        root.Children.Add(body);
        Content = root;
    }

    public async Task OpenAsync(string? focusBook = null, int focusChapter = 0)
    {
        if (_all.Count == 0)
        {
            _hint.Text = L("Zeitleiste wird geladen (einmalig, danach offline) …");
            try { _all = await EventsProvider!(); }
            catch (Exception ex) { _hint.Text = L("Zeitleiste nicht verfügbar: ") + ex.Message; return; }
        }
        ApplyEra();
        _hint.Text = L("Ereignis in der Leiste oder in der Liste wählen: Personen, Stellen und Orte erscheinen rechts, die Orte auch auf der Karte.");
        if (!_mapLoaded && MapProvider is not null)
        {
            try { _canvas.SetMap(await MapProvider()); _mapLoaded = true; } catch { /* map stays empty */ }
        }
    }

    void ApplyEra()
    {
        var (_, from, to) = Eras[_era];
        _shown = _all.Where(e => e.Year >= from && e.Year <= to).ToList();
        _list.ItemsSource = _shown.Select(e => $"{YearText(e.Year)}   {e.Name}").ToList();
        _ruler.SetEvents(_shown);
        foreach (var b in _eras.Children.OfType<Button>()) b.Classes.Set("active", b.Content as string == Eras[_era].Name);
    }

    static string YearText(int y) => y < 0 ? $"{-y} v. Chr." : y == 0 ? "0" : $"{y} n. Chr.";

    async void Select(TimelineEvent e, bool fromRuler)
    {
        var generation = ++_generation;
        _ruler.Selected = e;
        if (fromRuler) { var i = _shown.IndexOf(e); if (i >= 0) { _list.SelectedIndex = i; _list.ScrollIntoView(i); return; } }
        _title.Text = e.Name;
        _meta.Text = L("{0} (Näherungswert)", YearText(e.Year));
        _chips.Children.Clear(); _people.Text = ""; _placesText.Text = "";
        _canvas.SetPlaces([]);
        try
        {
            var d = await DetailProvider!(e.Id);
            if (generation != _generation) return;
            _meta.Text = L("{0} (Näherungswert){1}", YearText(d.Year), (d.Duration.Length > 0 ? " · Dauer " + d.Duration : ""));
            _people.Text = d.Participants.Count > 0 ? L("Beteiligt: ") + string.Join(", ", d.Participants.Select(p => p.Name)) : "";
            _placesText.Text = d.Locations.Count > 0 ? L("Orte: ") + string.Join(", ", d.Locations.Select(p => p.Name)) : "";
            foreach (var (r, end) in d.References.Take(24))
            {
                var label = $"{BookName?.Invoke(r.Book) ?? r.Book} {r.Chapter},{r.Verse}{(end > r.Verse ? "–" + end : "")}";
                var chip = new Button { Content = label, FontSize = 12, Padding = new(8, 2), Margin = new(0, 0, 6, 6) };
                var rr = r;
                chip.Click += (_, _) => OpenVerse?.Invoke(rr.Book, rr.Chapter, rr.Verse);
                _chips.Children.Add(chip);
            }
            var places = (await Task.WhenAll(d.Locations.Select(async l => { try { return await PlaceProvider!(l.Id); } catch { return null; } }))).Where(p => p is not null).Select(p => p!).ToList();
            if (generation != _generation) return;
            _canvas.SetPlaces(places);
        }
        catch (Exception ex) { _meta.Text += L(" · Details nicht verfügbar: ") + ex.Message; }
    }

    /// <summary>Ruler over the time axis; every event is a tick, the chosen one is highlighted.</summary>
    sealed class Ruler : Control
    {
        public event Action<TimelineEvent>? Picked;
        List<TimelineEvent> _events = [];
        TimelineEvent? _selected;
        public TimelineEvent? Selected { get => _selected; set { _selected = value; InvalidateVisual(); } }

        public Ruler() { Height = 64; Margin = new(16, 0, 16, 4); Cursor = new Cursor(StandardCursorType.Hand); }

        public void SetEvents(List<TimelineEvent> events) { _events = events; InvalidateVisual(); }

        (double Min, double Max) Range() => _events.Count == 0 ? (0, 1) : (_events.Min(e => e.Year), Math.Max(_events.Max(e => e.Year), _events.Min(e => e.Year) + 1));

        double X(int year) { var (min, max) = Range(); return 8 + (year - min) / (max - min) * (Bounds.Width - 16); }

        public override void Render(DrawingContext dc)
        {
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(Bounds.Size));
            var muted = new SolidColorBrush(Color.Parse("#9a9890"));
            var axisY = Bounds.Height - 20;
            dc.DrawLine(new Pen(muted, 1), new Point(8, axisY), new Point(Bounds.Width - 8, axisY));
            if (_events.Count == 0) return;
            var (min, max) = Range();
            var step = (max - min) > 3000 ? 500 : (max - min) > 1000 ? 200 : (max - min) > 300 ? 50 : 10;
            for (var y = (int)(Math.Ceiling(min / step) * step); y <= max; y += step)
            {
                var x = X(y);
                dc.DrawLine(new Pen(muted, 1), new Point(x, axisY), new Point(x, axisY + 4));
                var t = new FormattedText(y < 0 ? $"{-y}" : $"{y}", CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Typeface.Default, 9, muted);
                dc.DrawText(t, new Point(x - t.Width / 2, axisY + 5));
            }
            foreach (var e in _events)
            {
                var hot = e == _selected;
                var color = Color.Parse(e.Year < -2100 ? "#c58a5a" : e.Year < -1050 ? "#d98cb3" : e.Year < -400 ? "#f2c14e" : e.Year < 100 ? "#5bc0de" : "#8ab4f8");
                var h = hot ? 36 : 8 + Math.Min(e.References, 12) * 1.8;
                var x = X(e.Year);
                dc.DrawLine(new Pen(new SolidColorBrush(color, hot ? 1 : 0.55), hot ? 3 : 1.2), new Point(x, axisY), new Point(x, axisY - h));
            }
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            if (_events.Count == 0) return;
            var x = e.GetPosition(this).X;
            var best = _events.MinBy(ev => Math.Abs(X(ev.Year) - x));
            if (best is not null && Math.Abs(X(best.Year) - x) < 12) Picked?.Invoke(best);
        }
    }
}
