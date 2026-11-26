using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.LogicalTree;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using BibleStudy.Core;
using BibleStudy.Core.Models;
using BibleStudy.Core.Storage;

namespace BibleStudy.App;

/// <summary>
/// Side by side, a column can show a passage of its own (Matthew 24 next to Daniel 9). Its verses are chosen, marked, noted, bookmarked and read aloud
/// here, always with the place of that column; the main text and its choice stay as they are.
/// </summary>
public partial class MainWindow
{
    /// <summary>Per column: its own passage (null = the main text).</summary>
    List<(string Book, int Chapter)?> _colOwn = [];
    /// <summary>Per column: its translation and chapter, as shown.</summary>
    readonly List<(TranslationInfo T, Chapter? C)> _colData = [];
    /// <summary>The verse chosen in a column with a passage of its own.</summary>
    (int Col, int Verse)? _fSel;
    DispatcherTimer? _fLongPress;

    sealed record ForeignState(string Book, int Chapter, List<Mark> Marks, HashSet<int> Bookmarks, HashSet<int> Voice, HashSet<int> Notes);
    readonly Dictionary<int, ForeignState> _colMarkCache = [];

    /// <summary>What the vault has for the passage of a column (read once per drawing).</summary>
    ForeignState? ColState(int col)
    {
        if (_colMarkCache.TryGetValue(col, out var s)) return s;
        if (col <= 0 || col >= _colOwn.Count || _colOwn[col] is not { } own || _vault is null) return null;
        s = new ForeignState(own.Book, own.Chapter, _vault.GetMarks(own.Book, own.Chapter), _vault.GetBookmarkedVerses(own.Book, own.Chapter),
            _vault.GetVoiceNotes(own.Book, own.Chapter).Select(n => n.Ref.Verse).ToHashSet(),
            _vault.GetNotes().Where(n => n.Ref.Book == own.Book && n.Ref.Chapter == own.Chapter).Select(n => n.Ref.Verse).ToHashSet());
        return _colMarkCache[col] = s;
    }

    VerseRef? ColRef(int col, int verse) => col > 0 && col < _colOwn.Count && _colOwn[col] is { } own ? new VerseRef(own.Book, own.Chapter, verse) : null;

    TranslationInfo? ColTranslation(int col) => col >= 0 && col < _colData.Count ? _colData[col].T : null;

    string ColVerseText(int col, int verse) => col >= 0 && col < _colData.Count ? _colData[col].C?.Verses.FirstOrDefault(v => v.Number == verse)?.Text ?? "" : "";

    /// <summary>The marks of a column with a passage of its own that lie in a verse (those made in its translation, and those of the whole verse).</summary>
    List<CellMark> ForeignCellMarks(CellData d, string rawText)
    {
        var list = new List<CellMark>();
        if (ColState(d.Col) is not { } s) return list;
        var core = s.Marks.Any(m => m.WholeVerse && m.Covers(d.Number));
        foreach (var m in s.Marks)
            if (!m.WholeVerse && m.Covers(d.Number) && m.TranslationId == d.T.Id && MarkLogic.PartIn(m, d.Number, rawText.Length) is { } p)
                list.Add(new CellMark(m, p.Start, p.End, core, false));
        return list;
    }

    /// <summary>The boxes of the columns with a passage of their own: the color of a marked verse, and the chosen verse.</summary>
    void PaintForeign()
    {
        var ink = SelectionInk();
        for (var col = 1; col <= _colBoxes.Count; col++)
        {
            if (!IsForeign(col)) continue;
            var marks = ColState(col)?.Marks ?? [];
            foreach (var (n, box) in _colBoxes[col - 1])
            {
                var whole = marks.LastOrDefault(m => m.WholeVerse && m.Covers(n)) is { } wm ? MarkColor(wm.Color) : (Color?)null;
                var chosen = _fSel is { } f && f.Col == col && f.Verse == n;
                if (whole is { } c) box.Background = new SolidColorBrush(c, 0.14);
                else if (chosen) box.Background = new SolidColorBrush(ink, 0.12);
                else box.ClearValue(Border.BackgroundProperty);
                box.BorderBrush = chosen ? new SolidColorBrush(ink, 0.55) : whole is { } c2 ? new SolidColorBrush(c2) : Brushes.Transparent;
            }
        }
    }

    /// <summary>A click (or a finger) on a verse of such a column: it is chosen there; the right button or a long press opens its menu.</summary>
    void OnForeignVersePressed(int col, int verse, Border box, PointerPressedEventArgs e)
    {
        _jumpCol = col;
        var props = e.GetCurrentPoint(box).Properties;
        if (e.Pointer.Type == PointerType.Touch) { StartForeignLongPress(col, verse, box, e); return; }
        if (props.IsRightButtonPressed)
        {
            e.Handled = true;
            ChooseForeign(col, verse);
            ShowForeignVerseMenu(col, verse, box);
            return;
        }
        if (props.IsLeftButtonPressed) ChooseForeign(col, verse);
    }

    void ChooseForeign(int col, int verse)
    {
        _fSel = (col, verse);
        PaintForeign();
        if (ColRef(col, verse) is { } r) StatusText.Text = Label(r);
    }

    void StartForeignLongPress(int col, int verse, Border box, PointerPressedEventArgs e)
    {
        _fLongPress?.Stop();
        var start = e.GetPosition(this); var pointer = e.Pointer;
        var scroll = col < _colScrolls.Count ? _colScrolls[col] : null; var offset = scroll?.Offset.Y ?? 0;
        var timer = _fLongPress = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(550) };
        var moved = false;
        void Cancel() { timer.Stop(); RemoveHandler(PointerReleasedEvent, OnUp); RemoveHandler(PointerMovedEvent, OnMove); }
        void OnUp(object? s, PointerReleasedEventArgs a)
        {
            if (a.Pointer != pointer) return;
            var tap = timer.IsEnabled && !moved;
            Cancel();
            if (tap && Math.Abs((scroll?.Offset.Y ?? 0) - offset) < 8) ChooseForeign(col, verse);   // a short tap chooses the verse
        }
        void OnMove(object? s, PointerEventArgs a)
        {
            if (a.Pointer != pointer) return;
            var p = a.GetPosition(this);
            if (Math.Abs(p.X - start.X) > 12 || Math.Abs(p.Y - start.Y) > 12) { moved = true; Cancel(); }
        }
        AddHandler(PointerReleasedEvent, OnUp, Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerMovedEvent, OnMove, Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
        timer.Tick += (_, _) =>
        {
            Cancel();
            if (Math.Abs((scroll?.Offset.Y ?? 0) - offset) > 8) return;   // the column was scrolled
            ChooseForeign(col, verse);
            ShowForeignVerseMenu(col, verse, box);
        };
        timer.Start();
    }

    /// <summary>The words selected in a verse of such a column, as offsets in its plain text (null when nothing is selected there).</summary>
    (int Start, int End, string Text)? ForeignSelection(Border box)
    {
        var tb = box.GetLogicalDescendants().OfType<SelectableTextBlock>().FirstOrDefault();
        if (tb?.Tag is not CellInfo info || tb.SelectionEnd == tb.SelectionStart) return null;
        var a = RawAt(info, Math.Min(tb.SelectionStart, tb.SelectionEnd)); var b = RawAt(info, Math.Max(tb.SelectionStart, tb.SelectionEnd));
        while (a < b && char.IsWhiteSpace(info.RawText[a])) a++;
        while (b > a && char.IsWhiteSpace(info.RawText[b - 1])) b--;
        return b > a ? (a, b, info.RawText[a..b]) : null;
    }

    void ShowForeignVerseMenu(int col, int verse, Border box)
    {
        if (ColRef(col, verse) is not { } r || ColTranslation(col) is not { } t) return;
        var picked = ForeignSelection(box);
        var menu = new MenuFlyout();
        MenuItem Make(string text, Action act, bool enabled = true) { var i = new MenuItem { Header = text, IsEnabled = enabled }; i.Click += (_, _) => act(); return i; }
        void Item(string text, Action act, bool enabled = true) => menu.Items.Add(Make(text, act, enabled));
        MenuItem Group(string title, params Control[] children) { var g = new MenuItem { Header = SubHeader(title) }; foreach (var c in children) g.Items.Add(c); return g; }
        MenuItem Colors(string title, Action<string> apply)
        {
            var g = new MenuItem { Header = title };
            foreach (var hex in PresetColors)
            {
                var h = hex;
                var i = new MenuItem { Header = ColorName(h) is { Length: > 0 } n ? n : h, Icon = new Border { Width = 14, Height = 14, CornerRadius = new(7), Background = new SolidColorBrush(MarkColor(h)) } };
                i.Click += (_, _) => apply(h);
                g.Items.Add(i);
            }
            return g;
        }
        var state = ColState(col);
        var marksHere = state?.Marks.Where(m => m.Covers(verse) && (m.WholeVerse || m.TranslationId == t.Id)).ToList() ?? [];

        Item(L("{0} – Vers-Übersicht", Label(r)), () => { SetPanelOpen(true); SideTabs.SelectedIndex = GuideTab; _ = ShowGuideAsync(r); });
        Item(L("{0} als Haupttext öffnen", Label(r)), () => _ = GoToAsync(r.Book, r.Chapter, r.Verse));
        menu.Items.Add(new Separator());
        if (picked is { } p)
        {
            Item(L("Markierten Text kopieren"), async () => { if (TopLevel.GetTopLevel(this)?.Clipboard is { } cb) await cb.SetTextAsync(p.Text); });
            menu.Items.Add(Colors(L("Markierten Text markieren"), hex => MarkForeign(col, r, t, hex, p)));
        }
        menu.Items.Add(Colors(L("Vers markieren"), hex => MarkForeign(col, r, t, hex, null)));
        if (marksHere.Count > 0) Item(L("Markierung entfernen"), () => UnmarkForeign(marksHere, r));
        Item(L("Notiz schreiben"), () => ShowForeignNote(r, box));
        Item(_vault.GetBookmarkedVerses(r.Book, r.Chapter).Contains(verse) ? L("Nicht mehr merken") : L("Merken"), () =>
        {
            StatusText.Text = _vault.ToggleBookmark(r, ColVerseText(col, verse)) ? L("{0} gemerkt – zu finden unter „Lesezeichen“ rechts.", Label(r)) : L("{0} nicht mehr gemerkt.", Label(r));
            RefreshBookmarks(); AfterForeignChange();
        });
        foreach (var v in VoiceMenuItems(r, box, Make, Group)) menu.Items.Add(v);
        Item(L("Nur die Stelle kopieren (Strg+C)").Replace(" (Strg+C)", ""), async () => { if (TopLevel.GetTopLevel(this)?.Clipboard is { } cb) { await cb.SetTextAsync(Label(r)); StatusText.Text = L("Stelle kopiert: {0}", Label(r)); } });
        Item(L("Vers mit Stelle kopieren (Strg+Umschalt+C)").Replace(" (Strg+Umschalt+C)", ""), async () =>
        {
            if (TopLevel.GetTopLevel(this)?.Clipboard is not { } cb) return;
            var license = t is { License.PublicDomain: false, LicenseUrl: { Length: > 0 } url } ? "\n" + url : "";
            await cb.SetTextAsync($"{Loc.Quote(ColVerseText(col, verse).Trim())} – {Label(r)} ({t.ShortName}){license}");
            StatusText.Text = L("{0} kopiert.", Label(r));
        });
        Item(L("Vorlesen ab hier"), () => _ = SpeakForeignAsync(col, verse));
        menu.Items.Add(new Separator());
        menu.Items.Add(Group(L("Mehr mit diesem Vers"),
            Make(L("Auf dem Beamer zeigen"), () => _ = ProjectVersesAsync(r, r.Verse)),
            Make(L("Zum Auswendiglernen"), () => { Memory.Add(MemoryKey(r), DateTime.Now); StatusText.Text = L("{0} zum Auswendiglernen hinzugefügt.", Label(r)); }),
            Make(L("Zur Leseliste"), () => AddToQueue([r])),
            Make(L("Zum To-do hinzufügen"), () => { _vault.AddTodo(L("{0} studieren", Label(r)), r); RefreshTodos(); StatusText.Text = L("To-do zu {0} angelegt.", Label(r)); })));
        var study = new List<Control>
        {
            Make(L("Grundtext Wort für Wort"), () => { SetPanelOpen(true); SideTabs.SelectedIndex = GrundtextTab; _ = ShowGrundtextAsync(r); }),
            Make(L("Querverweise"), () => { SetPanelOpen(true); SideTabs.SelectedIndex = 4; _ = ShowXrefsAsync(r); }),
        };
        if (CommentariesOn) study.Add(Make(L("Kommentar zu diesem Vers"), () => OpenCommentary(r)));
        menu.Items.Add(Group(L("Studium"), [.. study]));
        menu.ShowAt(box, showAtPointer: !AppPlatform.IsMobile);
    }

    /// <summary>Marks the whole verse (or the selected words of it) of such a column, in its own place.</summary>
    void MarkForeign(int col, VerseRef r, TranslationInfo t, string hex, (int Start, int End, string Text)? words)
    {
        var target = MarkTargetFolder();
        var before = target is null ? null : MarkIdsHere();
        if (words is { } w) _vault.AddMark(t.Id, r, w.Start, w.End, w.Text, hex, "", target, AutoTags(r), r.Verse);
        else
        {
            var same = ColState(col)?.Marks.FirstOrDefault(m => m.WholeVerse && m.Ref.Verse == r.Verse && m.LastVerse == r.Verse);
            if (same is not null) _vault.UpdateMark(same.Id, hex, same.Note, same.FolderId, same.Tags);   // marked again: only the color changes
            else _vault.AddMark(Mark.AnyTranslation, r, -1, -1, ColVerseText(col, r.Verse).Trim(), hex, "", target, AutoTags(r), r.Verse);
        }
        RefreshCollection();
        AfterForeignChange();
        StatusText.Text = L("Markiert: {0}", words is { } ww ? Loc.Quote(Shorten(ww.Text, 40)) : Label(r));
        if (target is long f && before is not null) AfterMarkedIntoFolder(f, before);
    }

    void UnmarkForeign(List<Mark> marks, VerseRef r)
    {
        foreach (var m in marks) _vault.TrashMark(m.Id);
        RefreshCollection();
        AfterForeignChange();
        StatusText.Text = L("Markierung von {0} entfernt (im Papierkorb).", Label(r));
    }

    /// <summary>After a change in the vault: the columns with a passage of their own are drawn again (marks, star, ♪, ✎).</summary>
    void AfterForeignChange()
    {
        _colMarkCache.Clear();
        RefreshCells();
    }

    /// <summary>A small editor for the note of a verse of such a column (the notes tab keeps the verse of the main text).</summary>
    void ShowForeignNote(VerseRef r, Control anchor)
    {
        var box = new TextBox { Text = _vault.GetNote(r)?.Text ?? "", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 120, MaxHeight = 320, Width = 340, PlaceholderText = L("Deine Notiz …") };
        var save = new Button { Content = L("Speichern"), Classes = { "primary" } };
        var fly = new Flyout { Placement = PlacementMode.Bottom };
        save.Click += (_, _) =>
        {
            _vault.SetNote(r, box.Text ?? "");
            fly.Hide();
            RefreshLists(); AfterForeignChange();
            StatusText.Text = L("Notiz zu {0} gespeichert.", Label(r));
        };
        fly.Content = new StackPanel { Spacing = 8, Children = { new TextBlock { Text = L("Notiz zu ") + Label(r), FontWeight = FontWeight.SemiBold }, box, save } };
        fly.ShowAt(anchor);
        box.Focus();
    }

    /// <summary>Reads such a column aloud from a verse on, in the voice of its translation.</summary>
    async Task SpeakForeignAsync(int col, int verse)
    {
        if (!OperatingSystem.IsWindows() && !AppPlatform.IsMobile) { StatusText.Text = L("Vorlesen ist derzeit nur unter Windows verfügbar."); return; }
        if (_speechMode) StopSpeak();
        if (col >= _colData.Count || _colData[col] is not { C: { } chapter, T: var t }) return;
        await SpeakVersesAsync(chapter.Verses.Where(x => x.Number >= verse).ToList(), t);
    }
}
