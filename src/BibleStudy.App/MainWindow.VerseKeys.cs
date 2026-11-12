using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace BibleStudy.App;

/// <summary>
/// Reading with the keyboard. Without a chosen verse ↑/↓ scroll the text; with one they move to the verse above or below, Shift+↑/↓
/// add verses to the choice. ←/→ go to the previous or next chapter. Space shows the colors for the chosen verses (or the chosen text);
/// in the colors the arrow keys pick one, Enter or Space takes it, Esc closes them and the arrows move between verses again.
/// Esc without colors open takes the verse choice away.
/// </summary>
public partial class MainWindow
{
    List<(Control Item, Action Run)> _popupChoices = [];
    int _popupIndex = -1, _popupStart;
    readonly Dictionary<Border, (Thickness Thickness, IBrush? Brush)> _popupLooks = [];

    /// <summary>True when the key was used here (called first by the window's key handler).</summary>
    bool HandleVerseKeys(KeyEventArgs e)
    {
        if (e.Handled || _capturing is not null) return false;
        var mods = e.KeyModifiers;
        if (_selPopup is not null && _popupChoices.Count > 0 && mods == KeyModifiers.None)
        {
            switch (e.Key)
            {
                case Key.Left: MovePopupChoice(-1); break;
                case Key.Right: MovePopupChoice(1); break;
                case Key.Up: MovePopupRow(-1); break;
                case Key.Down: MovePopupRow(1); break;
                case Key.Enter or Key.Space:
                    var i = _popupIndex >= 0 ? _popupIndex : _popupStart;
                    if (i >= 0 && i < _popupChoices.Count) _popupChoices[i].Run();
                    break;
                case Key.Escape: HideSelectionPopup(); break;
                default: return false;
            }
            e.Handled = true;
            return true;
        }
        if (mods is not (KeyModifiers.None or KeyModifiers.Shift) || !VerseKeysApply(e)) return false;
        var shift = mods == KeyModifiers.Shift;
        switch (e.Key)
        {
            case Key.Up or Key.Down:
            {
                var down = e.Key == Key.Down;
                if (_selected is not int cur)
                {
                    if (shift) { if (FirstVisibleVerse() is int first) { SelectVerse(first); KeepVerseVisible(first); FocusVerse(first); } }
                    else LineScroll(down ? 1 : -1);
                    break;
                }
                // with Shift the choice grows or shrinks from the verse it started at; the verse moved to is the end of the choice
                if (NeighbourVerse(cur, down) is not int next) break;
                if (shift) SelectVerseWithKeys(next, KeyModifiers.Shift);
                else SelectVerse(next);
                KeepVerseVisible(next);
                FocusVerse(next);
                break;
            }
            case Key.Left when !shift: _ = StepChapterAsync(-1); break;
            case Key.Right when !shift: NextBtn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); break;   // as the button: "mark as read when going on" counts too
            case Key.Space when !shift:
                if (FocusedControl() is Button or ToggleButton or CheckBox) return false;   // Space on a focused button presses it
                if (_sel is { Parts.Count: > 0 }) ShowSelectionPopup();
                else if (_selected is int v) ShowSelectionPopup(_selVerses.Count > 0 ? _selVerses.Min() : v);
                else { if (FirstVisibleVerse() is int first) { SelectVerse(first); KeepVerseVisible(first); FocusVerse(first); } break; }
                if (_selPopup is not null && _popupChoices.Count > 0) { _popupIndex = _popupStart; PaintPopupChoice(); }
                break;
            case Key.Escape when !shift && _selected is not null && _sel is null && _selVerses.Count <= 1:
                _selected = null; _selVerses.Clear(); _anchorVerse = null; Paint();
                ThreadPane.SelectedVerse = null;
                break;
            default: return false;
        }
        e.Handled = true;
        return true;
    }

    /// <summary>The verse chosen with the keys gets the focus, as after a click: Space then opens the colors, not the button clicked last.</summary>
    void FocusVerse(int verse) => _cells.FirstOrDefault(c => c.Data.Number == verse && !c.Data.Foreign && c.Data.Col == 0)?.Tb.Focus();

    object? FocusedControl() => TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();

    /// <summary>The reading keys apply while the text is shown and nothing else wants the keys (a text field, a list, the side panel, the right side of the split view).</summary>
    bool VerseKeysApply(KeyEventArgs e)
    {
        if (!ReaderHost.IsVisible || Home.IsVisible || _curBook == "" || _verseBoxes.Count == 0 || _settings.FocusMode && e.Key is Key.Space) return false;
        if (_activeWork is not null || _settings.SplitView && !Panes.Contains(ReaderPane)) return false;   // a tab covers the text, or the text is in no area
        var focused = FocusedControl();
        if (focused is TextBox or ComboBox or ListBoxItem or ListBox or Slider or MenuItem or TabItem or NumericUpDown or TreeViewItem) return false;
        if (focused is Visual v && (v.GetVisualAncestors().Contains(SidePanel) || v.GetVisualAncestors().Contains(WorkHost) || (v.GetVisualAncestors().Contains(MainArea) && !v.GetVisualAncestors().Contains(LeftArea)))) return false;
        return true;
    }

    int? NeighbourVerse(int verse, bool down)
    {
        var keys = _verseBoxes.Keys;
        return down ? keys.Where(k => k > verse).Cast<int?>().Min() : keys.Where(k => k < verse).Cast<int?>().Max();
    }

    /// <summary>The first verse whose top can be seen in the text.</summary>
    int? FirstVisibleVerse()
    {
        var top = ReaderScroll.Offset.Y;
        foreach (var (n, box) in _verseBoxes.OrderBy(k => k.Key))
            if (box.TranslatePoint(new Point(0, 0), ReaderPanel) is { } p && p.Y + ReaderPanel.Margin.Top + box.Bounds.Height > top) return n;
        return _verseBoxes.Keys.Cast<int?>().Min();
    }

    /// <summary>Scrolls only as far as needed to show the whole verse (moving through the verses must not make the text jump).</summary>
    void KeepVerseVisible(int verse)
    {
        if (_independent) { ScrollToVerse(verse); return; }
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (!_verseBoxes.TryGetValue(verse, out var box) || box.TranslatePoint(new Point(0, 0), ReaderPanel) is not { } p) return;
            var y = p.Y + ReaderPanel.Margin.Top;
            var view = ReaderScroll.Viewport.Height;
            var off = ReaderScroll.Offset.Y;
            const double pad = 40;
            if (y - pad < off) off = Math.Max(0, y - pad);
            else if (y + box.Bounds.Height + pad > off + view) off = Math.Min(Math.Max(0, ReaderScroll.Extent.Height - view), y + box.Bounds.Height + pad - view);
            else return;
            ReaderScroll.Offset = new Vector(ReaderScroll.Offset.X, off);
        }, Avalonia.Threading.DispatcherPriority.Background);
    }

    void LineScroll(int direction)
    {
        void By(ScrollViewer s) => s.Offset = new Vector(s.Offset.X, Math.Clamp(s.Offset.Y + direction * 60, 0, Math.Max(0, s.Extent.Height - s.Viewport.Height)));
        if (_independent) { for (var j = 0; j < _colScrolls.Count; j++) if (!IsForeign(j)) By(_colScrolls[j]); }
        else By(ReaderScroll);
    }

    // ---- the colors with the keyboard -----------------------------------------------------------------------------------

    void MovePopupChoice(int step)
    {
        if (_popupIndex < 0) _popupIndex = _popupStart;
        else _popupIndex = Math.Clamp(_popupIndex + step, 0, _popupChoices.Count - 1);
        PaintPopupChoice();
    }

    /// <summary>↑/↓ in the colors: the color in the row above or below that is nearest to the one chosen (the colors wrap into rows).</summary>
    void MovePopupRow(int dir)
    {
        if (_popupIndex < 0) { _popupIndex = _popupStart; PaintPopupChoice(); return; }
        var cur = _popupChoices[_popupIndex].Item.Bounds;
        var best = -1; var bestScore = double.MaxValue;
        for (var i = 0; i < _popupChoices.Count; i++)
        {
            var b = _popupChoices[i].Item.Bounds;
            var dy = (b.Center.Y - cur.Center.Y) * dir;
            if (dy < 4) continue;   // not in a row further in that direction
            var score = dy * 1000 + Math.Abs(b.Center.X - cur.Center.X);
            if (score < bestScore) { bestScore = score; best = i; }
        }
        if (best >= 0) { _popupIndex = best; PaintPopupChoice(); }
        else if (dir < 0) HideSelectionPopup();   // ↑ in the top row leaves the colors, like Esc
    }

    void PaintPopupChoice()
    {
        var accent = Res("BsAccent") as IBrush ?? Brushes.Goldenrod;
        for (var i = 0; i < _popupChoices.Count; i++)
        {
            var on = i == _popupIndex;
            switch (_popupChoices[i].Item)
            {
                case Border b:
                    if (!_popupLooks.ContainsKey(b)) _popupLooks[b] = (b.BorderThickness, b.BorderBrush);
                    var (thickness, brush) = _popupLooks[b];
                    b.BorderThickness = on ? new Thickness(3) : thickness;
                    b.BorderBrush = on ? accent : brush;
                    b.RenderTransform = on ? new ScaleTransform(1.2, 1.2) : null;
                    break;
                case Button btn: btn.Classes.Set("active", on); break;
            }
        }
        if (_popupIndex >= 0 && _popupIndex < _popupChoices.Count && ToolTip.GetTip(_popupChoices[_popupIndex].Item) is string tip) StatusText.Text = tip + " · " + L("Enter: übernehmen, Esc: schließen");
    }
}
