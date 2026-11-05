using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using BibleStudy.Core.Models;
using BibleStudy.Core.Storage;

namespace BibleStudy.App;

/// <summary>
/// Selecting in the Bible text: text over several verses (from the middle of one verse into the middle of the next), whole verses with
/// Ctrl (single) and Shift (up to here), no blank at the two ends of a selection, and a selection that stays when the mark button is pressed.
/// The built-in selection of a text block cannot leave its verse and disappears with the focus, so the program keeps its own and paints it.
/// </summary>
public partial class MainWindow
{
    sealed record SelPart(int Verse, int Start, int End);

    /// <summary>Selected text: one part per verse (blanks at the ends cut off), all in the text of one translation.</summary>
    sealed class TextSel
    {
        public string TranslationId = "";
        public int Col;
        public List<SelPart> Parts = [];
    }

    sealed class DragState
    {
        public CellRef Anchor = null!;
        public int AnchorRaw;
        public Point Start, Last;
        public bool Moved;
        public List<CellRef> Cells = [];
        public ScrollViewer? Scroller;
        public DispatcherTimer? Timer;
    }

    TextSel? _sel;
    DragState? _drag;
    readonly HashSet<int> _selVerses = [];
    int? _anchorVerse;
    readonly Dictionary<string, Chapter> _markSources = [];

    void InitSelection()
    {
        ReaderScroll.AddHandler(PointerMovedEvent, OnSelPointerMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
        ReaderScroll.AddHandler(PointerReleasedEvent, OnSelPointerReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
        ReaderScroll.AddHandler(PointerCaptureLostEvent, (_, _) => EndDrag(false), RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(KeyDownEvent, OnSelKeyDown, RoutingStrategies.Tunnel);
        InitQuickMark();
    }

    // ---- verses chosen with Ctrl and Shift ------------------------------------------------------------------------------

    /// <summary>Ctrl+click adds or takes away a verse, Shift+click chooses all verses from the one clicked last up to this one.</summary>
    void SelectVerseWithKeys(int verse, KeyModifiers mods)
    {
        if (mods.HasFlag(KeyModifiers.Shift) && _anchorVerse is int anchor)
        {
            var (a, b) = (Math.Min(anchor, verse), Math.Max(anchor, verse));
            var numbers = _verseBoxes.Keys.Where(n => n >= a && n <= b).ToList();
            if (!mods.HasFlag(KeyModifiers.Control)) _selVerses.Clear();
            foreach (var n in numbers) _selVerses.Add(n);
            FinishVerseChoice(verse);
        }
        else if (mods.HasFlag(KeyModifiers.Control))
        {
            if (_selVerses.Count == 0 && _selected is int current) _selVerses.Add(current);
            if (!_selVerses.Remove(verse)) { _selVerses.Add(verse); _anchorVerse = verse; }
            FinishVerseChoice(verse);
        }
        else SelectVerse(verse);
    }

    void FinishVerseChoice(int verse)
    {
        // the side panel (note, cross references …) follows the verse clicked last, as before
        var keep = _selVerses.ToList();
        var anchor = _anchorVerse;
        _selected = verse;
        SelectVerse(verse);
        _selVerses.Clear(); foreach (var n in keep) _selVerses.Add(n);
        _anchorVerse = anchor;
        if (_selVerses.Count == 0) { _selected = null; _anchorVerse = null; }
        Paint();
        StatusText.Text = _selVerses.Count > 1 ? L("{0} Verse gewählt · Farbe über den Marker-Knopf", _selVerses.Count) : "";
    }

    // ---- selecting text -------------------------------------------------------------------------------------------------

    /// <summary>The offset in the plain verse text for a position in the cell (blocks that are no text of the verse count for the following text).</summary>
    static int RawAt(CellInfo c, int p)
    {
        foreach (var r in c.Runs)
        {
            if (p <= r.Tb) return r.Raw;
            if (p < r.Tb + r.Len) return r.Raw + (p - r.Tb);
        }
        return c.RawText.Length;
    }

    /// <summary>The other way round: where in the cell a text offset of the verse is shown.</summary>
    static int TbAt(CellInfo c, int raw)
    {
        foreach (var r in c.Runs) if (raw >= r.Raw && raw <= r.Raw + r.Len) return r.Tb + (raw - r.Raw);
        return c.Runs.Count > 0 ? c.Runs[^1].Tb + c.Runs[^1].Len : 0;
    }

    static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c is '\'' or '’';

    void BeginTextSelection(PointerPressedEventArgs e, SelectableTextBlock tb, CellInfo info)
    {
        var cref = _cells.FirstOrDefault(c => ReferenceEquals(c.Tb, tb));
        if (cref is null || cref.Data.Foreign) return;
        var caret = tb.TextLayout.HitTestPoint(e.GetPosition(tb)).TextPosition;
        var raw = RawAt(info, caret);
        e.Handled = true;   // our own selection takes over
        tb.Focus();
        if (e.ClickCount >= 3)   // the whole verse
        {
            SetSelectionFrom(cref, 0, cref, info.RawText.Length, ColCells(cref));
            OnSelectionMade();
            return;
        }
        if (e.ClickCount == 2)   // one word
        {
            var t = info.RawText;
            if (t.Length > 0)
            {
                var i = Math.Clamp(raw, 0, t.Length - 1);
                if (!IsWordChar(t[i]) && i > 0 && IsWordChar(t[i - 1])) i--;
                if (IsWordChar(t[i]))
                {
                    int s = i, en = i + 1;
                    while (s > 0 && IsWordChar(t[s - 1])) s--;
                    while (en < t.Length && IsWordChar(t[en])) en++;
                    SetSelectionFrom(cref, s, cref, en, ColCells(cref));
                    OnSelectionMade();
                }
            }
            return;
        }
        var d = new DragState { Anchor = cref, AnchorRaw = raw, Start = e.GetPosition(this), Last = e.GetPosition(this), Cells = ColCells(cref) };
        d.Scroller = _independent && cref.Data.Col < _colScrolls.Count ? _colScrolls[cref.Data.Col] : ReaderScroll;
        _drag = d;
        e.Pointer.Capture(ReaderScroll);
    }

    List<CellRef> ColCells(CellRef c) => [.. _cells.Where(x => !x.Data.Foreign && x.Data.Col == c.Data.Col && x.Data.T.Id == c.Data.T.Id && x.Tb.IsAttachedToVisualTree()).OrderBy(x => x.Data.Number)];

    void OnSelPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_drag is not { } d) return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) { EndDrag(false); return; }
        var p = e.GetPosition(this);
        d.Last = p;
        if (!d.Moved && Math.Abs(p.X - d.Start.X) + Math.Abs(p.Y - d.Start.Y) < 4) return;
        if (!d.Moved)
        {
            d.Moved = true;
            d.Timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
            d.Timer.Tick += (_, _) => DragTick(d);
            d.Timer.Start();
        }
        e.Handled = true;
        UpdateDragSelection(d);
    }

    /// <summary>Near the upper or lower edge the page scrolls on by itself, so a selection over many verses is possible.</summary>
    void DragTick(DragState d)
    {
        if (d.Scroller is not { } sc || sc.TranslatePoint(default, this) is not { } top) return;
        var bottom = top.Y + sc.Bounds.Height;
        double dy = 0;
        if (d.Last.Y < top.Y + 28) dy = -Math.Min(40, 4 + (top.Y + 28 - d.Last.Y) / 3);
        else if (d.Last.Y > bottom - 28) dy = Math.Min(40, 4 + (d.Last.Y - (bottom - 28)) / 3);
        if (dy == 0) return;
        var max = Math.Max(0, sc.Extent.Height - sc.Viewport.Height);
        sc.Offset = new Vector(sc.Offset.X, Math.Clamp(sc.Offset.Y + dy, 0, max));
        UpdateDragSelection(d);
    }

    void OnSelPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_drag is null) return;
        e.Handled = true;
        EndDrag(true);
    }

    void EndDrag(bool finished)
    {
        if (_drag is not { } d) return;
        _drag = null;
        d.Timer?.Stop();
        try { ((IInputElement?)ReaderScroll).Focus(); } catch { /* no focus change needed */ }
        if (finished && d.Moved && _sel is { Parts.Count: > 0 }) OnSelectionMade();
        else if (!d.Moved)
        {
            ClearTextSelection(true);
            if (finished) SelectMarkAt(d.Anchor, d.AnchorRaw);   // a plain click on marked words opens the colors for that mark: change it or take it away
        }
    }

    /// <summary>A click (no dragging) on words that are marked: the whole mark is selected and the colors show over it, so it can be recolored or removed without marking the words again.</summary>
    void SelectMarkAt(CellRef cell, int raw)
    {
        if (!_settings.SelectionPopup || cell.Tb.Tag is not CellInfo info) return;
        var hit = CellMarks(cell.Data, info.RawText).Where(x => !x.Shared && x.Mark.Id >= 0 && x.Start <= raw && raw < x.End).OrderBy(x => x.End - x.Start).FirstOrDefault();
        if (hit is null) return;
        var m = hit.Mark;
        var sel = new TextSel { TranslationId = cell.Data.T.Id, Col = cell.Data.Col };
        foreach (var c in ColCells(cell).Where(c => m.Covers(c.Data.Number)))
            if (c.Tb.Tag is CellInfo ci && MarkLogic.PartIn(m, c.Data.Number, ci.RawText.Length) is { } p) sel.Parts.Add(new SelPart(c.Data.Number, p.Start, p.End));
        if (sel.Parts.Count == 0) return;
        PaintSelection(sel);
        ShowSelectionPopup();
    }

    /// <summary>The cell under a point of the window and the offset in its verse; above or below the cells the first or last position.</summary>
    (CellRef Cell, int Raw)? HeadAt(Point p, DragState d)
    {
        CellRef? best = null; var bestDist = double.MaxValue; var side = 0;
        foreach (var c in d.Cells)
        {
            if (!c.Tb.IsAttachedToVisualTree() || c.Tb.TranslatePoint(default, this) is not { } topLeft) continue;
            var h = c.Tb.Bounds.Height;
            var dist = p.Y < topLeft.Y ? topLeft.Y - p.Y : p.Y > topLeft.Y + h ? p.Y - topLeft.Y - h : 0;
            if (dist < bestDist) { bestDist = dist; best = c; side = p.Y < topLeft.Y ? -1 : p.Y > topLeft.Y + h ? 1 : 0; }
        }
        if (best is null) return null;
        var info = (CellInfo)best.Tb.Tag!;
        if (side < 0) return (best, 0);
        if (side > 0) return (best, info.RawText.Length);
        var local = this.TranslatePoint(p, best.Tb) ?? default;
        return (best, RawAt(info, best.Tb.TextLayout.HitTestPoint(local).TextPosition));
    }

    void UpdateDragSelection(DragState d)
    {
        if (HeadAt(d.Last, d) is not { } head) return;
        SetSelectionFrom(d.Anchor, d.AnchorRaw, head.Cell, head.Raw, d.Cells);
    }

    /// <summary>Selects from one point to another (in either direction): one part per verse, the blanks at the two ends cut off.</summary>
    void SetSelectionFrom(CellRef a, int aRaw, CellRef h, int hRaw, List<CellRef> cells)
    {
        var forward = a.Data.Number < h.Data.Number || (a.Data.Number == h.Data.Number && aRaw <= hRaw);
        var (first, fr, last, lr) = forward ? (a, aRaw, h, hRaw) : (h, hRaw, a, aRaw);
        if (_settings.SelectWholeWords)   // the ends move out to the borders of the words they lie in
        {
            var ft = ((CellInfo)first.Tb.Tag!).RawText; var lt = ((CellInfo)last.Tb.Tag!).RawText;
            while (fr > 0 && fr < ft.Length && IsWordChar(ft[fr - 1]) && IsWordChar(ft[fr])) fr--;
            while (lr > 0 && lr < lt.Length && IsWordChar(lt[lr - 1]) && IsWordChar(lt[lr])) lr++;
        }
        var sel = new TextSel { TranslationId = a.Data.T.Id, Col = a.Data.Col };
        foreach (var c in cells.Where(c => c.Data.Number >= first.Data.Number && c.Data.Number <= last.Data.Number))
        {
            var text = ((CellInfo)c.Tb.Tag!).RawText;
            var s = c.Data.Number == first.Data.Number ? fr : 0;
            var e = c.Data.Number == last.Data.Number ? lr : text.Length;
            if (MarkLogic.Trim(text, s, e) is { } t) sel.Parts.Add(new SelPart(c.Data.Number, t.Start, t.End));
        }
        PaintSelection(sel.Parts.Count == 0 ? null : sel);
    }

    /// <summary>Draws the selection: only the cells whose part changed are drawn again.</summary>
    void PaintSelection(TextSel? next)
    {
        var old = _sel; _sel = next;
        var verses = new HashSet<int>();
        if (old is not null) foreach (var p in old.Parts) verses.Add(p.Verse);
        if (next is not null) foreach (var p in next.Parts) verses.Add(p.Verse);
        foreach (var c in _cells.Where(c => !c.Data.Foreign && verses.Contains(c.Data.Number)).ToList())
        {
            SelPart? Of(TextSel? s) => s is not null && s.TranslationId == c.Data.T.Id && s.Col == c.Data.Col ? s.Parts.FirstOrDefault(p => p.Verse == c.Data.Number) : null;
            var before = Of(old); var after = Of(next);
            if (before == after) continue;
            FillCell(c.Tb, c.Data, after is null ? null : (after.Start, after.End));
        }
        _hoverSpan = null;
    }

    /// <summary>Takes the selection away again (and the color bar over it).</summary>
    void ClearTextSelection(bool paint)
    {
        HideSelectionPopup();
        _normalMarking = false;
        if (_sel is null) return;
        if (paint) PaintSelection(null); else _sel = null;
    }

    string SelectedText()
    {
        if (_sel is not { } s) return "";
        var parts = new List<string>();
        foreach (var p in s.Parts)
            if (_cells.FirstOrDefault(c => c.Data.Number == p.Verse && c.Data.T.Id == s.TranslationId && c.Data.Col == s.Col)?.Tb.Tag is CellInfo info && p.End <= info.RawText.Length)
                parts.Add(info.RawText[p.Start..p.End]);
        return string.Join(" ", parts);
    }

    /// <summary>The selection is made: mark it at once (quick marking), show the colors over it, or just say what is selected.</summary>
    void OnSelectionMade()
    {
        if (_sel is not { Parts.Count: > 0 }) return;
        var text = SelectedText();
        if (string.IsNullOrWhiteSpace(text)) { ClearTextSelection(true); return; }
        if (_settings.QuickMark) { _ = ApplyColorAsync(QuickColor()); return; }
        StatusText.Text = L("Ausgewählt: „{0}“ · Farbe über den Marker-Knopf wählen", Shorten(text, 60));
        if (_settings.SelectionPopup) ShowSelectionPopup();
    }

    // ---- keys ---------------------------------------------------------------------------------------------------------------

    bool TextFieldHasFocus() => TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is TextBox;

    void OnSelKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled) return;
        var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control) && !e.KeyModifiers.HasFlag(KeyModifiers.Shift) && !e.KeyModifiers.HasFlag(KeyModifiers.Alt);
        if (e.Key == Key.Escape && (_sel is not null || _selVerses.Count > 1))
        {
            ClearTextSelection(true);
            if (_selVerses.Count > 1 && _selected is int keep) { SelectVerse(keep); }
            e.Handled = true;
        }
        else if (ctrl && e.Key == Key.C && _sel is not null && !TextFieldHasFocus())
        {
            var text = SelectedText();
            if (text.Length > 0 && TopLevel.GetTopLevel(this)?.Clipboard is { } cb) { _ = cb.SetTextAsync(text); StatusText.Text = L("Kopiert: „{0}“", Shorten(text, 60)); }
            e.Handled = true;
        }
        else if (ctrl && e.Key == Key.Z && !TextFieldHasFocus() && _markUndo.Count > 0)
        {
            e.Handled = true;
            _ = UndoMarksAsync();
        }
    }

    // ---- the colors over the selected text ----------------------------------------------------------------------------

    Popup? _selPopup;

    /// <summary>Set when the user asked for the normal colors inside a marked verse (instead of the sub-mark); ends with the next selection or mark.</summary>
    bool _normalMarking;

    EventHandler<PointerPressedEventArgs>? _selPopupOutside;

    void HideSelectionPopup()
    {
        if (_selPopupOutside is { } h) { RemoveHandler(PointerPressedEvent, h); _selPopupOutside = null; }
        if (_selPopup is not { } p) return;
        _selPopup = null;
        p.IsOpen = false;
        ReaderHost.Children.Remove(p);
    }

    /// <summary>The colors over the selected text, or – with <paramref name="wholeVerse"/> (a click on the verse number) – over the start of that verse, to mark all of it.</summary>
    void ShowSelectionPopup(int? wholeVerse = null)
    {
        HideSelectionPopup();
        CellRef? cref; Rect rect;
        if (wholeVerse is int verse)
        {
            cref = _cells.FirstOrDefault(c => c.Data.Number == verse && !c.Data.Foreign && c.Data.Col == 0) ?? _cells.FirstOrDefault(c => c.Data.Number == verse && !c.Data.Foreign);
            if (cref is null) return;
            rect = cref.Tb.TextLayout.HitTestTextPosition(0);
        }
        else
        {
            if (_sel is not { Parts.Count: > 0 } sel) return;
            var last = sel.Parts[^1];
            cref = _cells.FirstOrDefault(c => c.Data.Number == last.Verse && c.Data.T.Id == sel.TranslationId && c.Data.Col == sel.Col && !c.Data.Foreign);
            if (cref is null || cref.Tb.Tag is not CellInfo info) return;
            rect = cref.Tb.TextLayout.HitTestTextPosition(TbAt(info, last.End));
        }
        void Pick(string hex) { HideSelectionPopup(); _ = ApplyColorAsync(hex); }
        var colors = new WrapPanel { MaxWidth = 360 };
        // the first choice is always "no color": takes the mark away again, so no trip to the top right is needed
        var clear = new Border { Width = 24, Height = 24, CornerRadius = new(12), Margin = new(0, 0, 6, 6), BorderThickness = new(1), Cursor = new Cursor(StandardCursorType.Hand), Child = new TextBlock { Text = "✕", FontSize = 12, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center } };
        clear.Bind(Border.BorderBrushProperty, this.GetResourceObservable("BsMuted"));
        ToolTip.SetTip(clear, L("Markierung entfernen"));
        clear.PointerPressed += (_, _) => { HideSelectionPopup(); _ = RemoveMarksAsync(); };
        colors.Children.Add(clear);
        var current = wholeVerse is int cv && _wholeMarks.TryGetValue(cv, out var curHex) ? curHex : null;   // a verse that is marked already shows its color
        if (wholeVerse is null && InsideMarkedVerse())
        {
            // inside a marked verse there is only one thing to do: the sub-mark, in its own color
            var sub = new Button { Classes = { "tool" }, Width = double.NaN, MinWidth = 150, Height = 28, Padding = new(10, 0), Margin = new(0, -2, 6, 0), Content = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 8, Children = { new Border { Width = 14, Height = 14, CornerRadius = new(7), Background = new SolidColorBrush(MarkColor(SubMarkColor())) }, new TextBlock { Text = L("Untermarkierung"), VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center } } } };
            ToolTip.SetTip(sub, L("Hebt den wichtigsten Teil im markierten Vers hervor"));
            sub.Click += (_, _) => Pick(SubMarkColor());
            colors.Children.Add(sub);
        }
        else foreach (var hex in PresetColors.Concat(_settings.CustomColors).Take(14)) colors.Children.Add(Swatch(hex, Pick, current is not null && string.Equals(current, hex, StringComparison.OrdinalIgnoreCase), 24));
        var more = new Button { Content = "…", Classes = { "tool" }, Width = 28, Height = 28, Margin = new(0, -2, 0, 0) };
        ToolTip.SetTip(more, L("Weitere Farben und Optionen"));
        // in a marked verse the "…" leads to the normal colors (for an ordinary mark instead of a sub-mark); elsewhere to all colors and options
        more.Click += (_, _) =>
        {
            HideSelectionPopup();
            if (wholeVerse is null && InsideMarkedVerse()) { _normalMarking = true; ShowSelectionPopup(); }
            else ShowColorFlyout();
        };
        colors.Children.Add(more);
        var card = new Border { Padding = new(10, 8, 4, 2), CornerRadius = new(10), BorderThickness = new(1), Child = colors };
        card.Bind(Border.BackgroundProperty, this.GetResourceObservable("BsSurface"));
        card.Bind(Border.BorderBrushProperty, this.GetResourceObservable("BsBorder"));
        var popup = new Popup
        {
            PlacementTarget = cref.Tb, Placement = PlacementMode.Top, PlacementRect = new Rect(rect.X, rect.Y, 1, Math.Max(1, rect.Height)),
            // not the built-in light dismiss: it swallows the click that closes it, so the next verse had to be clicked twice.
            // Ours closes the popup and lets the same click go on.
            IsLightDismissEnabled = false, Child = card,
        };
        _selPopupOutside = (_, e) =>
        {
            if (e.Source is Visual src && (ReferenceEquals(src, card) || src.GetVisualAncestors().Contains(card))) return;
            HideSelectionPopup();
        };
        AddHandler(PointerPressedEvent, _selPopupOutside, RoutingStrategies.Tunnel, true);
        popup.Closed += (_, _) => { if (ReferenceEquals(_selPopup, popup)) _selPopup = null; ReaderHost.Children.Remove(popup); };
        _selPopup = popup;
        ReaderHost.Children.Add(popup);
        popup.IsOpen = true;
    }

    // ---- quick marking -------------------------------------------------------------------------------------------------

    string QuickColor() => NormalizeHex(_settings.QuickMarkColor) ?? _settings.DefaultColor;

    void InitQuickMark()
    {
        QuickMarkBtn.IsChecked = _settings.QuickMark;
        UpdateQuickUi();
        QuickMarkBtn.IsCheckedChanged += (_, _) =>
        {
            _settings.QuickMark = QuickMarkBtn.IsChecked == true; SaveSettings(); UpdateQuickUi();
            StatusText.Text = _settings.QuickMark ? L("Schnellmarkieren an: Was du im Text auswählst, wird sofort markiert.") : L("Schnellmarkieren aus.");
        };
        QuickColorBtn.Click += (_, _) => ShowQuickColorFlyout();
    }

    void UpdateQuickUi()
    {
        QuickDot.Background = new SolidColorBrush(MarkColor(QuickColor()));
        var name = ColorName(QuickColor());
        ToolTip.SetTip(QuickMarkBtn, L("Schnellmarkieren: Was du im Text auswählst, wird sofort in der gewählten Farbe markiert (Strg+Z macht es rückgängig).") + (name.Length > 0 ? "\n" + name : ""));
    }

    void ShowQuickColorFlyout()
    {
        var flyout = new Flyout { Placement = PlacementMode.BottomEdgeAlignedRight };
        void Pick(string hex) { _settings.QuickMarkColor = hex; SaveSettings(); UpdateQuickUi(); flyout.Hide(); }
        var title = new TextBlock { Text = L("Farbe fürs Schnellmarkieren"), FontWeight = FontWeight.SemiBold };
        var picker = BuildRgbPicker(Pick, out var custom, out var apply);
        flyout.Content = new StackPanel { Spacing = 8, Width = 280, Children = { title, ColorChoice(Pick, QuickColor()), custom, picker, apply } };
        flyout.ShowAt(QuickColorBtn);
    }
}
