using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using BibleStudy.Core;

namespace BibleStudy.App;

/// <summary>
/// With the word study on, hovering a word lights up the words of the chapter that belong to it: the same Strong number and the same word (purple),
/// the same Strong number but another word (green), the same word but another number (blue). A word can be pinned from its right-click menu, so the
/// marks stay and can be stepped through.
/// </summary>
public partial class MainWindow
{
    enum SameKind { None, Both, Strong, Word }

    readonly List<SelectableTextBlock> _wordCells = [];
    readonly List<WordSpan> _pinned = [];
    WordSpan? _hoverSpan;
    int _markCursor = -1;

    static string NormWord(string w) => new string(w.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();

    static string PinKey(WordSpan s) => string.Join(",", s.Strongs.Order()) + "|" + NormWord(s.Word);

    static SameKind Compare(WordSpan target, WordSpan s)
    {
        if (s.Strongs.Length == 0) return SameKind.None;
        var strong = target.Strongs.Intersect(s.Strongs, StringComparer.OrdinalIgnoreCase).Any();
        var word = NormWord(s.Word).Length > 0 && NormWord(target.Word) == NormWord(s.Word);
        return strong && word ? SameKind.Both : strong ? SameKind.Strong : word ? SameKind.Word : SameKind.None;
    }

    bool Wanted(SameKind k) => k switch { SameKind.Both => _settings.MarkBoth, SameKind.Strong => _settings.MarkStrong, SameKind.Word => _settings.MarkWord, _ => false };

    IBrush KindBrush(SameKind k) => HighlightBrush(k switch { SameKind.Both => "#8B5CF6", SameKind.Strong => "#2FA36B", _ => "#3B82F6" });

    /// <summary>Paints the background of every word of the chapter: its own highlight, or the color of how it relates to the pinned and the hovered word.</summary>
    void ApplyWordMarks()
    {
        _wordCells.RemoveAll(c => !c.IsAttachedToVisualTree());
        var targets = new List<WordSpan>(_pinned);
        if (_hoverSpan is not null) targets.Add(_hoverSpan);
        foreach (var cell in _wordCells)
        {
            if (cell.Tag is not CellInfo info) continue;
            foreach (var s in info.Spans)
            {
                if (s.Run is null || s.Strongs.Length == 0) continue;
                var brush = s.Base;
                foreach (var t in targets)
                    if (Compare(t, s) is var k && Wanted(k)) brush = KindBrush(k);   // the hovered word comes last and wins
                if (!ReferenceEquals(s.Run.Background, brush)) s.Run.Background = brush;
            }
        }
    }

    WordSpan? SpanUnder(PointerEventArgs e)
    {
        if (e.Source is not SelectableTextBlock { Tag: CellInfo cell } tb) return null;
        var hit = tb.TextLayout.HitTestPoint(e.GetPosition(tb)).TextPosition;
        return cell.Spans.FirstOrDefault(s => s.Note is null && s.Strongs.Length > 0 && hit >= s.Start && hit < s.End);
    }

    void OnWordHover(SelectableTextBlock tb, PointerEventArgs? e)
    {
        if (StudyBtn.IsChecked != true) return;
        var span = e is null ? null : SpanUnder(e);
        if (ReferenceEquals(span, _hoverSpan)) return;
        _hoverSpan = span;
        ApplyWordMarks();
        _ = SetWordTipAsync(tb, span);
    }

    /// <summary>The Strong numbers of the word at once, the dictionary entry and the count when they are there.</summary>
    async Task SetWordTipAsync(Control tb, WordSpan? span)
    {
        if (span is null) { ToolTip.SetTip(tb, null); return; }
        ToolTip.SetTip(tb, string.Join(" · ", span.Strongs));
        try
        {
            var lines = new List<string>();
            foreach (var id in span.Strongs.Take(2))
            {
                var e = await _strongs.LookupAsync(id);
                if (!ReferenceEquals(_hoverSpan, span)) return;
                var line = id + (e is null ? "" : $"  {e.Lemma}  {e.Translit}");
                if (e is not null && e.Definition.Length > 0) line += "\n" + Shorten(Mean(e.Definition), 80);
                if (_analyzer is not null && WordAnalyzer.Normalize(id) is { } n && _analyzer.Count(n) is > 0 and var c) line += "\n" + L("{0} Mal in der Bibel", c);
                lines.Add(line);
            }
            if (ReferenceEquals(_hoverSpan, span)) ToolTip.SetTip(tb, string.Join("\n\n", lines));
        }
        catch { /* the numbers alone are shown */ }
    }

    // ---- pinning and stepping through the marks (right-click on a word) ---------------------------------------------------

    void AddWordMenuItems(MenuFlyout menu, WordSpan w)
    {
        void Item(string text, Action act) { var i = new MenuItem { Header = text }; i.Click += (_, _) => act(); menu.Items.Add(i); }
        void Toggle(string text, bool on, Action<bool> set)
        {
            var i = new MenuItem { Header = text, ToggleType = MenuItemToggleType.CheckBox, IsChecked = on };
            i.Click += (_, _) => { set(i.IsChecked); SaveSettings(); ApplyWordMarks(); };
            menu.Items.Add(i);
        }
        var word = w.Word.Trim();
        var ids = string.Join(" · ", w.Strongs);
        var pinned = _pinned.Any(p => PinKey(p) == PinKey(w));
        Item(pinned ? L("Hervorhebung lösen: „{0}“", word) : L("Hervorhebung fixieren: „{0}“", word), () =>
        {
            if (pinned) _pinned.RemoveAll(p => PinKey(p) == PinKey(w)); else _pinned.Add(w);
            _markCursor = -1;
            ApplyWordMarks();
            StatusText.Text = _pinned.Count == 0 ? L("Keine fixierten Hervorhebungen.") : L("{0} Wörter in diesem Kapitel markiert. Rechtsklick auf ein Wort: nächstes markiertes Wort.", MarkedSpans().Count);
        });
        if (_pinned.Count > 0)
        {
            Item(L("Nächstes markiertes Wort"), () => StepMark(1));
            Item(L("Vorheriges markiertes Wort"), () => StepMark(-1));
            Item(L("Alle Hervorhebungen lösen"), () => { _pinned.Clear(); _markCursor = -1; ApplyWordMarks(); });
        }
        menu.Items.Add(new Separator());
        Item(L("{0} nachschlagen", ids), () => _ = ShowStudyAsync(w));
        Item(L("Vorkommen als Diagramm"), () => _ = ShowWordChartAsync(w.Strongs[0], w.Word));
        Toggle(L("Strong und Wort gleich (lila)"), _settings.MarkBoth, v => _settings.MarkBoth = v);
        Toggle(L("Nur Strong gleich (grün)"), _settings.MarkStrong, v => _settings.MarkStrong = v);
        Toggle(L("Nur Wort gleich (blau)"), _settings.MarkWord, v => _settings.MarkWord = v);
        menu.Items.Add(new Separator());
    }

    /// <summary>The marked words of the chapter in reading order (cell, span).</summary>
    List<(SelectableTextBlock Cell, WordSpan Span)> MarkedSpans()
    {
        var list = new List<(SelectableTextBlock, WordSpan)>();
        if (_pinned.Count == 0) return list;
        foreach (var cell in _wordCells.Where(c => c.IsAttachedToVisualTree()))
            if (cell.Tag is CellInfo info)
                foreach (var s in info.Spans.OrderBy(x => x.Start))
                    if (s.Run is not null && _pinned.Any(p => Wanted(Compare(p, s)))) list.Add((cell, s));
        return list;
    }

    void StepMark(int dir)
    {
        var marked = MarkedSpans();
        if (marked.Count == 0) { StatusText.Text = L("Keine markierten Wörter in diesem Kapitel."); return; }
        _markCursor = ((_markCursor + dir) % marked.Count + marked.Count) % marked.Count;
        var (cell, span) = marked[_markCursor];
        var r = cell.TextLayout.HitTestTextPosition(span.Start);
        cell.BringIntoView(new Rect(r.X, Math.Max(0, r.Y - 70), 20, r.Height + 140));
        StatusText.Text = L("Markiertes Wort {0} von {1}", _markCursor + 1, marked.Count);
    }
}
