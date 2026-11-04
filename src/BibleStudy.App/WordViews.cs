using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using BibleStudy.Core.Search;

namespace BibleStudy.App;

/// <summary>A word for the cloud or the star: how strong it is, what to say when the pointer rests on it.</summary>
public sealed record WordItem(string Word, double Weight, string Tip);

static class WordViewPalette
{
    static readonly string[] Colors = ["#F5B700", "#17a7b4", "#eb568f", "#88a00d", "#0593e6", "#cd4805", "#a06aec", "#03ac8f"];
    public static Color At(int i) => Color.Parse(Colors[i % Colors.Length]);
}

/// <summary>
/// A word cloud: the stronger a word, the bigger it is written. The words are laid out when the size is known and again when it changes.
/// Hovering shows the number behind a word, a click asks for it.
/// </summary>
sealed class WordCloudView : Control
{
    public event Action<int>? WordClicked;

    IReadOnlyList<WordItem> _items = [];
    List<CloudPlacement> _placed = [];
    Size _laidOutFor;
    int _hover = -1;

    public WordCloudView() { ClipToBounds = true; MinHeight = 320; }

    /// <summary>How many of the given words did not find room (the caller can mention it).</summary>
    public int Omitted => _items.Count - _placed.Count;

    public void SetWords(IReadOnlyList<WordItem> items)
    {
        _items = items; _hover = -1; _laidOutFor = default;
        InvalidateVisual();
    }

    static (double, double) Measure(string text, double size)
    {
        var f = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface(Typeface.Default.FontFamily, FontStyle.Normal, FontWeight.SemiBold), size, Brushes.Black);
        return (f.Width, f.Height);
    }

    void Layout()
    {
        if (_laidOutFor == Bounds.Size) return;
        _laidOutFor = Bounds.Size;
        _placed = WordCloudLayout.PlaceFitted(_items.Select(i => (i.Word, i.Weight)).ToList(), Bounds.Width, Bounds.Height, Measure);
    }

    int At(Point p)
    {
        for (var i = _placed.Count - 1; i >= 0; i--)
        {
            var w = _placed[i];
            if (p.X >= w.X && p.X <= w.X + w.Width && p.Y >= w.Y && p.Y <= w.Y + w.Height) return i;
        }
        return -1;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        var i = At(e.GetPosition(this));
        if (i == _hover) return;
        _hover = i;
        Cursor = new Cursor(i >= 0 ? StandardCursorType.Hand : StandardCursorType.Arrow);
        ToolTip.SetTip(this, i < 0 ? null : $"{_items[_placed[i].Index].Word} · {_items[_placed[i].Index].Tip}");
        InvalidateVisual();
    }

    protected override void OnPointerExited(PointerEventArgs e) { _hover = -1; InvalidateVisual(); }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (At(e.GetPosition(this)) is var i and >= 0) WordClicked?.Invoke(_placed[i].Index);
    }

    public override void Render(DrawingContext dc)
    {
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(Bounds.Size));
        Layout();
        for (var i = 0; i < _placed.Count; i++)
        {
            var p = _placed[i];
            var color = WordViewPalette.At(p.Index);
            var f = new FormattedText(_items[p.Index].Word, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface(Typeface.Default.FontFamily, FontStyle.Normal, FontWeight.SemiBold), p.FontSize,
                new SolidColorBrush(color, i == _hover ? 1 : 0.88));
            if (i == _hover) f.SetTextDecorations(TextDecorations.Underline);
            dc.DrawText(f, new Point(p.X, p.Y));
        }
    }
}

/// <summary>
/// The neighbours of a word as a star: the word stands in the middle, the words that keep company with it around it, the strongest on the inner
/// ring. Bigger and stronger lines mean a stronger tie.
/// </summary>
sealed class CollocationStarView : Control
{
    public event Action<int>? WordClicked;

    string _center = "";
    IReadOnlyList<WordItem> _items = [];
    readonly List<(Rect Box, int Index)> _boxes = [];
    int _hover = -1;

    public CollocationStarView() { ClipToBounds = true; MinHeight = 420; }

    public void SetData(string center, IReadOnlyList<WordItem> items)
    {
        _center = center; _items = items.Take(24).ToList(); _hover = -1;
        InvalidateVisual();
    }

    int At(Point p)
    {
        for (var i = _boxes.Count - 1; i >= 0; i--) if (_boxes[i].Box.Contains(p)) return _boxes[i].Index;
        return -1;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        var i = At(e.GetPosition(this));
        if (i == _hover) return;
        _hover = i;
        Cursor = new Cursor(i >= 0 ? StandardCursorType.Hand : StandardCursorType.Arrow);
        ToolTip.SetTip(this, i < 0 ? null : $"{_items[i].Word} · {_items[i].Tip}");
        InvalidateVisual();
    }

    protected override void OnPointerExited(PointerEventArgs e) { _hover = -1; InvalidateVisual(); }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (At(e.GetPosition(this)) is var i and >= 0) WordClicked?.Invoke(i);
    }

    public override void Render(DrawingContext dc)
    {
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(Bounds.Size));
        _boxes.Clear();
        var text = (this.FindResource("BsText") as IBrush) ?? Brushes.White;
        var accent = (this.FindResource("BsAccent") as ISolidColorBrush)?.Color ?? Colors.Gold;
        var tint = (text as ISolidColorBrush)?.Color ?? Colors.White;
        var c = new Point(Bounds.Width / 2, Bounds.Height / 2);

        // two rings: the first eight on the inner one, the rest outside; the ellipse follows the shape of the window
        var rx1 = Bounds.Width * 0.22; var ry1 = Bounds.Height * 0.24;
        var rx2 = Bounds.Width * 0.40; var ry2 = Bounds.Height * 0.43;
        var max = _items.Count == 0 ? 1 : Math.Max(1e-9, _items.Max(i => i.Weight));
        var min = _items.Count == 0 ? 0 : _items.Min(i => i.Weight);

        var spots = new List<(int Index, Point At)>();
        var inner = Math.Min(8, _items.Count);
        var outer = _items.Count - inner;
        for (var i = 0; i < _items.Count; i++)
        {
            var onInner = i < inner;
            var n = onInner ? inner : outer;
            var k = onInner ? i : i - inner;
            var angle = -Math.PI / 2 + 2 * Math.PI * k / Math.Max(1, n) + (onInner ? 0 : Math.PI / Math.Max(1, n));
            spots.Add((i, new Point(c.X + (onInner ? rx1 : rx2) * Math.Cos(angle), c.Y + (onInner ? ry1 : ry2) * Math.Sin(angle))));
        }

        // the strongest words are not much stronger than the next ones, so the rank counts as much as the score
        double Share(int i)
        {
            var byScore = max - min < 1e-9 ? 1 : Math.Sqrt((_items[i].Weight - min) / (max - min));
            var byRank = _items.Count <= 1 ? 1 : 1.0 - (double)i / (_items.Count - 1);
            return 0.5 * byScore + 0.5 * byRank;
        }
        foreach (var (i, at) in spots)   // lines first, so the words lie on top
        {
            var share = Share(i);
            dc.DrawLine(new Pen(new SolidColorBrush(accent, 0.15 + 0.5 * share), 0.8 + 3 * share), c, at);
        }
        foreach (var (i, at) in spots)
        {
            var share = Share(i);
            var size = 12 + 11 * share;
            var f = new FormattedText(_items[i].Word, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface(Typeface.Default.FontFamily, FontStyle.Normal, FontWeight.SemiBold), size, text);
            var box = new Rect(at.X - f.Width / 2 - 6, at.Y - f.Height / 2 - 3, f.Width + 12, f.Height + 6);
            _boxes.Add((box, i));
            dc.DrawRectangle(new SolidColorBrush(tint, i == _hover ? 0.18 : 0.08), new Pen(new SolidColorBrush(WordViewPalette.At(i), i == _hover ? 1 : 0.7), 1), box, 8, 8);
            dc.DrawText(f, new Point(box.X + 6, box.Y + 3));
        }

        var cf = new FormattedText(_center, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface(Typeface.Default.FontFamily, FontStyle.Normal, FontWeight.Bold), 20, Brushes.Black);
        var cbox = new Rect(c.X - cf.Width / 2 - 12, c.Y - cf.Height / 2 - 6, cf.Width + 24, cf.Height + 12);
        dc.DrawRectangle(new SolidColorBrush(accent), null, cbox, 12, 12);
        dc.DrawText(cf, new Point(cbox.X + 12, cbox.Y + 6));
    }
}
