using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using BibleStudy.Core.Models;

namespace BibleStudy.App;

/// <summary>One chapter of the barcode and how many hits it holds.</summary>
public sealed record BarcodeCell(string Book, int Chapter, int Count);

/// <summary>
/// The word barcode: every chapter of the Bible is one thin stripe, in Bible order (Old Testament above, New Testament below); the more hits a
/// chapter holds, the stronger the stripe. You see at a glance where a word clusters. Hovering names the chapter, a click opens it.
/// </summary>
sealed class ChapterBarcode : Control
{
    public event Action<string, int>? ChapterClicked;

    sealed record Strip(string Title, List<BarcodeCell> Cells);

    const double Left = 4, TitleH = 16, StripH = 34, LabelsH = 30, Gap = 6;
    List<Strip> _strips = [];
    Func<string, string> _short = b => b, _full = b => b;
    int _max = 1;
    (int Strip, int Cell)? _hover;

    public ChapterBarcode() { ClipToBounds = true; Height = 80; }

    /// <summary>The cells must be in Bible order; chapters without hits are cells with a count of 0.</summary>
    public void SetData(IReadOnlyList<BarcodeCell> cells, Func<string, string> shortName, Func<string, string> fullName)
    {
        _short = shortName; _full = fullName;
        var ot = cells.Where(c => BookCategories.IsOldTestament(BookCategories.Of(c.Book))).ToList();
        var nt = cells.Where(c => !BookCategories.IsOldTestament(BookCategories.Of(c.Book))).ToList();
        _strips = [];
        if (ot.Count > 0) _strips.Add(new Strip(L("Altes Testament"), ot));
        if (nt.Count > 0) _strips.Add(new Strip(nt.All(c => BookCategories.IsNewTestament(BookCategories.Of(c.Book))) ? L("Neues Testament") : L("Weitere Bücher"), nt));
        _max = Math.Max(1, cells.Select(c => c.Count).DefaultIfEmpty(0).Max());
        Height = Math.Max(80, _strips.Count * (TitleH + StripH + LabelsH + Gap));
        _hover = null;
        InvalidateVisual();
    }

    double StripTop(int s) => s * (TitleH + StripH + LabelsH + Gap) + TitleH;

    (int, int)? CellAt(Point p)
    {
        for (var s = 0; s < _strips.Count; s++)
        {
            var top = StripTop(s);
            if (p.Y < top || p.Y > top + StripH) continue;
            var w = (Bounds.Width - 2 * Left) / _strips[s].Cells.Count;
            var i = (int)Math.Floor((p.X - Left) / w);
            return i >= 0 && i < _strips[s].Cells.Count ? (s, i) : null;
        }
        return null;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        var at = CellAt(e.GetPosition(this));
        if (at == _hover) return;
        _hover = at;
        if (at is (var s, var i))
        {
            var c = _strips[s].Cells[i];
            ToolTip.SetTip(this, $"{_full(c.Book)} {c.Chapter} · {c.Count}×");
            Cursor = new Cursor(c.Count > 0 ? StandardCursorType.Hand : StandardCursorType.Arrow);
        }
        else { ToolTip.SetTip(this, null); Cursor = new Cursor(StandardCursorType.Arrow); }
        InvalidateVisual();
    }

    protected override void OnPointerExited(PointerEventArgs e) { _hover = null; InvalidateVisual(); }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (CellAt(e.GetPosition(this)) is (var s, var i) && _strips[s].Cells[i] is { Count: > 0 } c) ChapterClicked?.Invoke(c.Book, c.Chapter);
    }

    public override void Render(DrawingContext dc)
    {
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(Bounds.Size));
        var muted = (this.FindResource("BsMuted") as IBrush) ?? Brushes.Gray;
        var text = (this.FindResource("BsText") as IBrush) ?? Brushes.White;
        var accent = (this.FindResource("BsAccent") as ISolidColorBrush)?.Color ?? Colors.Gold;
        var tint = (text as ISolidColorBrush)?.Color ?? Colors.White;

        for (var s = 0; s < _strips.Count; s++)
        {
            var strip = _strips[s];
            var top = StripTop(s);
            var w = (Bounds.Width - 2 * Left) / strip.Cells.Count;
            dc.DrawText(new FormattedText(strip.Title, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Typeface.Default, 11, muted), new Point(Left, top - TitleH + 1));

            // alternating bands mark the books; the labels sit under them
            var bookIndex = 0;
            var rowEnd = new double[] { -100, -100 };   // where the last label of each of the two rows ends
            for (var i = 0; i < strip.Cells.Count;)
            {
                var j = i;
                while (j < strip.Cells.Count && strip.Cells[j].Book == strip.Cells[i].Book) j++;
                var x0 = Left + i * w; var width = (j - i) * w;
                if (bookIndex % 2 == 0) dc.DrawRectangle(new SolidColorBrush(tint, 0.07), null, new Rect(x0, top, width, StripH));
                var name = new FormattedText(_short(strip.Cells[i].Book), CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Typeface.Default, 10, muted);
                // neighbours alternate between two rows; a label that would touch the previous one in its row is left out
                var lx = x0 + width / 2 - name.Width / 2;
                var row = rowEnd[bookIndex % 2] + 3 <= lx ? bookIndex % 2 : rowEnd[1 - bookIndex % 2] + 3 <= lx ? 1 - bookIndex % 2 : -1;
                if (row >= 0 && lx >= 0)
                {
                    dc.DrawText(name, new Point(lx, top + StripH + 3 + row * 13));
                    rowEnd[row] = lx + name.Width;
                }
                bookIndex++; i = j;
            }

            for (var i = 0; i < strip.Cells.Count; i++)
            {
                var c = strip.Cells[i];
                var x = Left + i * w;
                var hot = _hover == (s, i);
                if (c.Count == 0)
                {
                    dc.DrawRectangle(new SolidColorBrush(tint, hot ? 0.45 : 0.12), null, new Rect(x, top + StripH * 0.7, Math.Max(0.5, w - (w > 3 ? 0.6 : 0)), StripH * 0.3));
                    continue;
                }
                var strength = 0.35 + 0.65 * Math.Sqrt((double)c.Count / _max);
                dc.DrawRectangle(new SolidColorBrush(accent, hot ? 1 : strength), null, new Rect(x, top, Math.Max(1, w - (w > 4 ? 0.8 : 0)), StripH));
            }
        }
        if (_strips.Count == 0) dc.DrawText(new FormattedText(L("Keine Treffer."), CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Typeface.Default, 12, muted), new Point(Left, 8));
    }
}
