using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using BibleStudy.Core.Storage;

namespace BibleStudy.App;

/// <summary>
/// Studying together in a room, the parts that need the very same text on both sides:
/// <list type="bullet">
/// <item>Each side makes a short fingerprint of the chapter text it shows (per translation). Only when the fingerprints are equal the words line up
/// letter for letter, so only then the others' selection (the blue highlighting of selected words) and their highlights are drawn on the exact words.
/// Otherwise the verse frames of before stay.</item>
/// <item>The leader's scrolling is passed on: the followers' page moves along, so the verse in the middle of the leader's window is in the middle of theirs.</item>
/// <item>The leader can lead through a reading path: everyone sees which step it is, the verses of the step are framed in the path's color.</item>
/// </list>
/// All of it is small and throttled (at most four scroll messages a second, the selection at most about seven), since the relay counts messages.
/// </summary>
public partial class MainWindow
{
    static readonly Color SelBlue = Color.Parse("#3390FF");

    /// <summary>The exact selection and highlights of a person, valid only where the fingerprint is the same.</summary>
    sealed record ExactSel(string Book, int Chapter, string Print, List<(int Verse, int Start, int End)> Parts);
    sealed record ExactMarks(string Book, int Chapter, string Print, List<(int Verse, int End, int Start, int Stop, string Color)> Items);
    readonly Dictionary<string, ExactSel> _peerExactSel = [];
    readonly Dictionary<string, ExactMarks> _peerExactMarks = [];
    readonly Dictionary<string, (string Book, int Chapter, string Print)> _peerPrint = [];
    readonly Dictionary<string, string> _printCache = [];

    // the leader's scrolling
    IDisposable? _viewSendTimer;
    DispatcherTimer? _viewGlide;
    string _sentView = "";
    double _viewTarget = -1;
    DateTime _ownScrollUntil;
    (string Book, int Chapter, int Verse, double F, bool End)? _pendingView;
    bool _viewNavigating, _studyWired;
    (string Book, int Chapter)? _leaderView;   // the chapter the leader's scrolling is known for (then a click of the leader does not jump the page here)

    // leading through a reading path
    sealed class Tour { public string Name = "", Color = "", By = ""; public List<StudyPassage> Steps = []; public int Index; }
    Tour? _tour;
    readonly Border _tourBar = new() { CornerRadius = new(8), Padding = new(10, 6), IsVisible = false, Margin = new(0, 4, 0, 4) };

    // ---- the fingerprint of the text ---------------------------------------------------------------------------------------

    /// <summary>A short fingerprint of the open chapter as shown in one translation and column (the plain verse texts, numbered). Equal fingerprints = letter for letter the same text.</summary>
    string ChapterPrint(string translationId, int col)
    {
        var cells = _cells.Where(c => !c.Data.Foreign && c.Data.Col == col && c.Data.T.Id == translationId && c.Tb.Tag is CellInfo).OrderBy(c => c.Data.Number).ToList();
        if (cells.Count == 0) return "";
        var key = $"{translationId}|{col}|{_curBook}|{_curChapter}|{cells.Count}";
        if (_printCache.TryGetValue(key, out var known)) return known;
        var sb = new StringBuilder().Append(_curBook).Append(' ').Append(_curChapter);
        foreach (var c in cells) sb.Append('\n').Append(c.Data.Number).Append(':').Append(((CellInfo)c.Tb.Tag!).RawText);
        var print = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())), 0, 8);
        if (_printCache.Count > 64) _printCache.Clear();
        _printCache[key] = print;
        return print;
    }

    /// <summary>The fingerprint of the translation in the first column (the one the others compare with).</summary>
    string MainPrint() => _cells.FirstOrDefault(c => !c.Data.Foreign && c.Data.Col == 0) is { } first ? ChapterPrint(first.Data.T.Id, 0) : "";

    /// <summary>The cells (translation and column) here whose text has this fingerprint, or null.</summary>
    List<CellRef>? CellsWithPrint(string print)
    {
        if (print.Length == 0) return null;
        foreach (var g in _cells.Where(c => !c.Data.Foreign).GroupBy(c => (c.Data.T.Id, c.Data.Col)).OrderBy(g => g.Key.Col))
            if (ChapterPrint(g.Key.Id, g.Key.Col) == print) return [.. g];
        return null;
    }

    /// <summary>The leader passes on the scrolling for this chapter: the page here already moves with it.</summary>
    bool LeaderScrollsHere(CollabPerson p, string book, int chapter) => _leaderId == p.Id && _leaderView == (book, chapter) && book == _curBook && chapter == _curChapter;

    /// <summary>True when this person reads the open chapter in exactly the same text.</summary>
    bool SameTextAs(CollabPerson p) =>
        _peerPrint.TryGetValue(p.Id, out var pp) && pp.Book == _curBook && pp.Chapter == _curChapter && CellsWithPrint(pp.Print) is not null;

    void NotePrint(CollabPerson p, string book, int chapter, string print)
    {
        if (print.Length == 0) return;
        var had = _peerPrint.TryGetValue(p.Id, out var old) && old == (book, chapter, print);
        _peerPrint[p.Id] = (book, chapter, print);
        if (!had) ShowPeople();
    }

    static string PrintOf(JsonElement root) =>
        root.TryGetProperty("tr", out var e) && e.ValueKind == JsonValueKind.String && e.GetString() is { Length: 16 } s && s.All(Uri.IsHexDigit) ? s.ToUpperInvariant() : "";

    // ---- wiring -------------------------------------------------------------------------------------------------------------

    void InitCollabStudy()
    {
        if (_studyWired) return;
        _studyWired = true;
        ReaderScroll.ScrollChanged += (_, _) => OnLeaderScrolled();
        // the follower's own scrolling wins for a moment (wheel, finger): the leader's next move takes over again after that
        ReaderScroll.AddHandler(PointerWheelChangedEvent, (_, _) => OwnScroll(), RoutingStrategies.Tunnel, handledEventsToo: true);
        ReaderScroll.AddHandler(PointerPressedEvent, (_, e) => { if (e.Pointer.Type == PointerType.Touch) OwnScroll(); }, RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    void OwnScroll()
    {
        _ownScrollUntil = DateTime.UtcNow.AddSeconds(3);
        _viewTarget = -1;
    }

    void ResetCollabStudy()
    {
        _peerExactSel.Clear(); _peerExactMarks.Clear(); _peerPrint.Clear();
        _sentView = ""; _pendingView = null; _viewTarget = -1; _leaderView = null;
        _tour = null;
        UpdateTourBar();
    }

    // ---- the exact selection -------------------------------------------------------------------------------------------------

    IDisposable? _selSendTimer;

    /// <summary>The selection changes while the mouse is dragged: sent at most about seven times a second (the last state always).</summary>
    void CollabSelectionMoved()
    {
        if (!CollabOpen || _selSendTimer is not null) return;
        _selSendTimer = DispatcherTimer.RunOnce(() => { _selSendTimer = null; CollabSendSelection(); }, TimeSpan.FromMilliseconds(150));
    }

    /// <summary>The selected words as offsets in the verse texts, with the fingerprint of the text they are offsets in.</summary>
    (string Print, int[][] Parts) ExactSelection()
    {
        if (_sel is not { Parts.Count: > 0 } s) return ("", []);
        var print = ChapterPrint(s.TranslationId, s.Col);
        return print.Length == 0 ? ("", []) : (print, s.Parts.Take(200).Select(p => new[] { p.Verse, p.Start, p.End }).ToArray());
    }

    void ReadExactSel(CollabPerson p, JsonElement root, string book, int chapter)
    {
        var print = PrintOf(root);
        NotePrint(p, book, chapter, print);
        var parts = new List<(int, int, int)>();
        if (print.Length > 0 && root.TryGetProperty("parts", out var arr) && arr.ValueKind == JsonValueKind.Array)
            foreach (var e in arr.EnumerateArray().Take(200))
            {
                if (e.ValueKind != JsonValueKind.Array || e.GetArrayLength() != 3) continue;
                var n = e.EnumerateArray().Select(x => x.ValueKind == JsonValueKind.Number && x.TryGetInt32(out var i) ? i : -1).ToArray();
                if (n[0] is > 0 and <= 200 && n[1] >= 0 && n[2] > n[1] && n[2] <= 20_000) parts.Add((n[0], n[1], n[2]));
            }
        if (parts.Count > 0) _peerExactSel[p.Id] = new ExactSel(book, chapter, print, parts);
        else _peerExactSel.Remove(p.Id);
    }

    void ReadExactMarks(CollabPerson p, JsonElement root, string book, int chapter)
    {
        var print = PrintOf(root);
        NotePrint(p, book, chapter, print);
        var list = new List<(int, int, int, int, string)>();
        if (print.Length > 0 && root.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
            foreach (var e in items.EnumerateArray().Take(300))
            {
                if (e.ValueKind != JsonValueKind.Object) continue;
                int Get(string k) => e.TryGetProperty(k, out var x) && x.ValueKind == JsonValueKind.Number && x.TryGetInt32(out var n) ? n : -1;
                var color = e.TryGetProperty("c", out var c) && c.ValueKind == JsonValueKind.String && c.GetString() is { } cs && System.Text.RegularExpressions.Regex.IsMatch(cs, "^#[0-9A-Fa-f]{6}$") ? cs : null;
                var (v, end, s, x2) = (Get("v"), Get("e"), Get("s"), Get("x"));
                if (color is null || v is < 1 or > 200 || s < 0 || x2 < 0 || x2 > 20_000) continue;
                var last = end > v && end <= 200 ? end : v;
                if (last == v && x2 <= s) continue;
                list.Add((v, last, s, x2, color));
            }
        if (list.Count > 0) _peerExactMarks[p.Id] = new ExactMarks(book, chapter, print, list);
        else _peerExactMarks.Remove(p.Id);
    }

    /// <summary>The rectangles (in the layer) that the words from <paramref name="start"/> to <paramref name="end"/> of a verse cell take.</summary>
    IEnumerable<Rect> WordRects(CellRef cell, int start, int end)
    {
        if (_peerLayer is null || cell.Tb.Tag is not CellInfo info || !cell.Tb.IsArrangeValid) yield break;
        start = Math.Clamp(start, 0, info.RawText.Length); end = Math.Clamp(end, 0, info.RawText.Length);
        if (end <= start) yield break;
        var a = TbAt(info, start); var b = TbAt(info, end);
        if (b <= a) yield break;
        IEnumerable<Rect> rects;
        try { rects = cell.Tb.TextLayout.HitTestTextRange(a, b - a).ToList(); } catch (Exception ex) when (ex is not OutOfMemoryException) { yield break; }
        foreach (var r in rects)
            if (r.Width > 0.5 && cell.Tb.TranslatePoint(r.TopLeft, _peerLayer) is { } at) yield return new Rect(at, r.Size);
    }

    /// <summary>The other person's selected words, as the blue highlighting of a selection (with their name at the start). False when the texts differ.</summary>
    bool DrawExactSelection(CollabPerson p, int lane)
    {
        if (!_peerExactSel.TryGetValue(p.Id, out var sel) || sel.Book != _curBook || sel.Chapter != _curChapter) return false;
        if (CellsWithPrint(sel.Print) is not { } cells) return false;
        Rect? first = null;
        foreach (var (verse, start, end) in sel.Parts)
        {
            if (cells.FirstOrDefault(c => c.Data.Number == verse) is not { } cell) continue;
            foreach (var r in WordRects(cell, start, end))
            {
                var box = new Border { Width = r.Width, Height = r.Height, Background = new SolidColorBrush(SelBlue, 0.32), BorderBrush = new SolidColorBrush(p.Ink, 0.9), BorderThickness = new(0, 0, 0, 2) };
                Canvas.SetLeft(box, r.X); Canvas.SetTop(box, r.Y);
                _peerFrames.Add(box);
                first ??= r;
            }
        }
        if (first is not { } f) return false;
        var tag = NameTag(L("{0} wählt aus", p.Name), p.Ink);
        Canvas.SetLeft(tag, f.X + lane * 8); Canvas.SetTop(tag, f.Y - 17);
        _peerFrames.Add(tag);
        return true;
    }

    /// <summary>The highlights of the others on the exact words (when the text is the same), under the selection, and the step of the reading path being led.</summary>
    void AddStudyFrames()
    {
        var open = CollabOpen && ReaderHost.IsVisible;
        if (!open) return;
        foreach (var p in _people.Values)
        {
            if (!_peerExactMarks.TryGetValue(p.Id, out var mk) || mk.Book != _curBook || mk.Chapter != _curChapter) continue;
            if (CellsWithPrint(mk.Print) is not { } cells) continue;
            foreach (var (verse, last, start, stop, color) in mk.Items)
                for (var v = verse; v <= last; v++)
                {
                    if (cells.FirstOrDefault(c => c.Data.Number == v) is not { } cell || cell.Tb.Tag is not CellInfo info) continue;
                    var s = v == verse ? start : 0; var e = v == last ? stop : info.RawText.Length;
                    foreach (var r in WordRects(cell, s, e))
                    {
                        var ink = Color.Parse(color);
                        var box = new Border { Width = r.Width, Height = r.Height, Background = new SolidColorBrush(ink, 0.28), BorderBrush = new SolidColorBrush(ink), BorderThickness = new(0, 0, 0, 2) };
                        ToolTip.SetTip(box, p.Name);
                        Canvas.SetLeft(box, r.X); Canvas.SetTop(box, r.Y);
                        _peerFrames.Add(box);
                    }
                }
        }
        AddTourFrames();
    }

    // ---- the leader's scrolling ------------------------------------------------------------------------------------------------

    /// <summary>Where in the text the middle of the reader is: the verse there and how far into it (0 = its top, 1 = its bottom).</summary>
    (int Verse, double F, bool End)? MiddleOfView()
    {
        if (_independent || _verseBoxes.Count == 0) return null;
        var max = Math.Max(0, ReaderScroll.Extent.Height - ReaderScroll.Viewport.Height);
        var end = max > 0 && ReaderScroll.Offset.Y >= max - 2;
        var mid = ReaderScroll.Offset.Y + ReaderScroll.Viewport.Height / 2;
        (int, double)? best = null;
        foreach (var (n, box) in _verseBoxes.OrderBy(k => k.Key))
        {
            if (box.TranslatePoint(default, ReaderPanel) is not { } at) continue;
            var top = at.Y + ReaderPanel.Margin.Top; var h = Math.Max(1, box.Bounds.Height);
            best = (n, Math.Clamp((mid - top) / h, -3, 4));
            if (mid < top + h) break;
        }
        return best is { } b ? (b.Item1, b.Item2, end) : null;
    }

    void OnLeaderScrolled()
    {
        if (!_iLead || !CollabOpen || _viewSendTimer is not null) return;
        _viewSendTimer = DispatcherTimer.RunOnce(() => { _viewSendTimer = null; SendView(); }, TimeSpan.FromMilliseconds(250));
    }

    void SendView(bool force = false)
    {
        if (!_iLead || !CollabOpen || MiddleOfView() is not { } m) return;
        var f = Math.Round(m.F, 2);
        var key = $"{_curBook} {_curChapter} {m.Verse} {f} {m.End}";
        if (!force && key == _sentView) return;
        _sentView = key;
        CollabSend(new { t = "view", book = _curBook, chapter = _curChapter, v = m.Verse, f, end = m.End });
    }

    /// <summary>The leader scrolled: the same place comes into the middle here (gliding), unless this person scrolls themselves just now.</summary>
    void OnView(CollabPerson p, string book, int chapter, int verse, double f, bool end)
    {
        if (_leaderId != p.Id || !_followLeader || _independent) return;
        _leaderView = (book, chapter);
        if (book != _curBook || chapter != _curChapter)
        {
            _pendingView = (book, chapter, verse, f, end);
            if (!_viewNavigating) _ = GoForViewAsync(book, chapter, verse);
            return;
        }
        if (DateTime.UtcNow < _ownScrollUntil) return;
        GlideTo(verse, f, end);
    }

    async Task GoForViewAsync(string book, int chapter, int verse)
    {
        _viewNavigating = true;
        try { await FollowAsync((book, chapter, verse)); }
        finally { _viewNavigating = false; }
        if (_pendingView is { } pv && pv.Book == _curBook && pv.Chapter == _curChapter)
        {
            _pendingView = null;
            Dispatcher.UIThread.Post(() => GlideTo(pv.Verse, pv.F, pv.End), DispatcherPriority.Background);
        }
    }

    void GlideTo(int verse, double f, bool end)
    {
        var max = Math.Max(0, ReaderScroll.Extent.Height - ReaderScroll.Viewport.Height);
        double target;
        if (end) target = max;
        else
        {
            if (!_verseBoxes.TryGetValue(verse, out var box) || box.TranslatePoint(default, ReaderPanel) is not { } at) return;
            target = at.Y + ReaderPanel.Margin.Top + f * Math.Max(1, box.Bounds.Height) - ReaderScroll.Viewport.Height / 2;
        }
        _viewTarget = Math.Clamp(target, 0, max);
        if (_viewGlide is not null) return;
        _viewGlide = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _viewGlide.Tick += (_, _) =>
        {
            var y = ReaderScroll.Offset.Y;
            if (_viewTarget < 0 || Math.Abs(_viewTarget - y) < 1)
            {
                if (_viewTarget >= 0) ReaderScroll.Offset = new Vector(ReaderScroll.Offset.X, _viewTarget);
                _viewGlide?.Stop(); _viewGlide = null; _viewTarget = -1;
                return;
            }
            ReaderScroll.Offset = new Vector(ReaderScroll.Offset.X, y + (_viewTarget - y) * 0.3);
        };
        _viewGlide.Start();
    }

    // ---- leading through a reading path ----------------------------------------------------------------------------------------

    static string StepText(StudyPassage s, Func<string, string> bookName) =>
        $"{bookName(s.Book)} {s.Chapter}" + (s.From > 0 ? $",{s.From}" + (s.To > s.From ? "–" + s.To : "") : "");

    void StartTour(ReadingPath path)
    {
        if (!_iLead || path.Steps.Count == 0) return;
        _tour = new Tour { Name = path.Name, Color = PathColor(path).ToString(), By = "", Steps = [.. path.Steps.Take(300)], Index = 0 };
        SendTour(full: true);
        AddCollabLog(L("Du führst durch den Lesepfad „{0}“.", path.Name), mine: true, system: true);
        GoTourStep(0);
    }

    void SendTour(bool full)
    {
        if (_tour is not { By: "" } t) return;
        if (full) CollabSend(new { t = "tour", name = t.Name, color = HexOf(t.Color), steps = t.Steps.Select(s => new object[] { s.Book, s.Chapter, s.From, s.To }), i = t.Index });
        else CollabSend(new { t = "tour", i = t.Index });
    }

    static string HexOf(string color) => Color.TryParse(color, out var c) ? $"#{c.R:X2}{c.G:X2}{c.B:X2}" : "#3E63DD";

    void GoTourStep(int index)
    {
        if (_tour is not { By: "" } t || index < 0 || index >= t.Steps.Count) return;
        t.Index = index;
        SendTour(full: false);
        var s = t.Steps[index];
        UpdateTourBar();
        _ = GoToAsync(s.Book, s.Chapter, s.From > 0 ? s.From : null);
    }

    void EndTour(bool tell)
    {
        if (_tour is null) return;
        var mine = _tour.By == "";
        _tour = null;
        if (mine && tell) CollabSend(new { t = "tour", off = true });
        UpdateTourBar(); RenderPeerLayer();
    }

    void ReadTour(CollabPerson p, JsonElement root)
    {
        if (root.TryGetProperty("off", out var off) && off.ValueKind == JsonValueKind.True)
        {
            if (_tour?.By == p.Id) { _tour = null; AddCollabLog(L("{0} hat den Lesepfad beendet.", p.Name), system: true); }
        }
        else if (root.TryGetProperty("steps", out var steps) && steps.ValueKind == JsonValueKind.Array)
        {
            var list = new List<StudyPassage>();
            foreach (var e in steps.EnumerateArray().Take(300))
            {
                if (e.ValueKind != JsonValueKind.Array || e.GetArrayLength() != 4) continue;
                var a = e.EnumerateArray().ToArray();
                var book = a[0].ValueKind == JsonValueKind.String ? a[0].GetString() ?? "" : "";
                int N(JsonElement x) => x.ValueKind == JsonValueKind.Number && x.TryGetInt32(out var n) ? n : -1;
                var (ch, from, to) = (N(a[1]), N(a[2]), N(a[3]));
                if (_books.Any(b => b.Id == book) && ch is >= 1 and <= 200 && from is >= 0 and <= 200 && to is >= 0 and <= 200) list.Add(new StudyPassage(book, ch, from, to));
            }
            if (list.Count == 0) return;
            var name = root.TryGetProperty("name", out var nm) && nm.ValueKind == JsonValueKind.String ? Clip(nm.GetString(), 80).Trim() : "";
            var color = root.TryGetProperty("color", out var cl) && cl.ValueKind == JsonValueKind.String && cl.GetString() is { } cs && System.Text.RegularExpressions.Regex.IsMatch(cs, "^#[0-9A-Fa-f]{6}$") ? cs : "#3E63DD";
            var isNew = _tour is null || _tour.By != p.Id || _tour.Name != name;
            _tour = new Tour { Name = name.Length > 0 ? name : L("Lesepfad"), Color = color, By = p.Id, Steps = list, Index = 0 };
            if (isNew) AddCollabLog(L("{0} führt durch den Lesepfad „{1}“ ({2} Schritte).", p.Name, _tour.Name, list.Count), system: true);
        }
        if (_tour is { } t && t.By == p.Id && root.TryGetProperty("i", out var i) && i.ValueKind == JsonValueKind.Number && i.TryGetInt32(out var idx) && idx >= 0 && idx < t.Steps.Count)
            t.Index = idx;
        UpdateTourBar(); RenderPeerLayer();
    }

    /// <summary>The line of the reading path being led: for the leader with the steps back and on, for the others with the step and "keep the path".</summary>
    void UpdateTourBar()
    {
        // the tour ends with the leading
        if (_tour is { By: "" } && !_iLead) EndTour(true);
        if (_tour is { } other && other.By != "" && (_leaderId != other.By || !_people.ContainsKey(other.By))) _tour = null;

        var panel = new StackPanel { Spacing = 6 };
        if (_tour is not { } t)
        {
            if (!_iLead || !CollabOpen) { _tourBar.IsVisible = false; return; }
            var pick = new Button { Content = L("Durch einen Lesepfad führen ▾"), Classes = { "tool" }, Width = double.NaN, Padding = new(10, 0), HorizontalAlignment = HorizontalAlignment.Left };
            ToolTip.SetTip(pick, L("Du gehst Schritt für Schritt durch einen deiner Lesepfade. Alle sehen dieselbe Stelle und den Schritt."));
            pick.Click += (_, _) =>
            {
                var menu = new MenuFlyout();
                foreach (var path in ReadingPaths().Where(x => x.Steps.Count > 0).Take(60))
                {
                    var rp = path;
                    var item = new MenuItem { Header = $"{rp.Name} ({rp.Steps.Count})" };
                    item.Click += (_, _) => StartTour(rp);
                    menu.Items.Add(item);
                }
                if (menu.Items.Count == 0) menu.Items.Add(new MenuItem { Header = L("Noch kein Lesepfad"), IsEnabled = false });
                menu.ShowAt(pick);
            };
            panel.Children.Add(pick);
            _tourBar.BorderThickness = new(0);
            _tourBar.Background = Brushes.Transparent;
            _tourBar.Padding = new(0);
            _tourBar.Child = panel;
            _tourBar.IsVisible = true;
            return;
        }
        var ink = Color.TryParse(t.Color, out var c) ? c : Color.Parse("#3E63DD");
        var step = t.Steps[Math.Clamp(t.Index, 0, t.Steps.Count - 1)];
        var who = t.By == "" ? L("Du führst") : L("{0} führt", _people.TryGetValue(t.By, out var leader) ? leader.Name : "?");
        panel.Children.Add(new TextBlock { Text = $"{who}: „{t.Name}“ · " + L("Schritt {0} von {1}", t.Index + 1, t.Steps.Count), FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new TextBlock { Text = StepText(step, BookName), TextWrapping = TextWrapping.Wrap });
        var row = new WrapPanel();
        Button Btn(string text, string tip, Action click, bool enabled = true)
        {
            var b = new Button { Content = text, Classes = { "tool" }, Width = double.NaN, Padding = new(10, 0), Margin = new(0, 0, 6, 0), IsEnabled = enabled };
            ToolTip.SetTip(b, tip);
            b.Click += (_, _) => click();
            row.Children.Add(b);
            return b;
        }
        if (t.By == "")
        {
            Btn("‹ " + L("Zurück"), L("Zum Schritt davor"), () => GoTourStep(t.Index - 1), t.Index > 0);
            Btn(L("Weiter") + " ›", L("Zum nächsten Schritt"), () => GoTourStep(t.Index + 1), t.Index + 1 < t.Steps.Count);
            Btn(L("Beenden"), L("Den Lesepfad beenden (du leitest weiter)"), () => EndTour(true));
        }
        else
        {
            if (!_followLeader) Btn(L("Zur Stelle"), L("Dorthin gehen, wo der Lesepfad gerade ist"), () => _ = GoToAsync(step.Book, step.Chapter, step.From > 0 ? step.From : null));
            Button? keep = null;
            keep = Btn(L("Lesepfad behalten"), L("Diesen Lesepfad als neuen Lesepfad in deinem Vault speichern"), () =>
            {
                NewPath(t.Name, t.Steps);
                if (keep is not null) { keep.IsEnabled = false; keep.Content = L("Gespeichert ✓"); }
            });
        }
        panel.Children.Add(row);
        _tourBar.Background = new SolidColorBrush(ink, 0.10);
        _tourBar.BorderBrush = new SolidColorBrush(ink); _tourBar.BorderThickness = new(3, 0, 0, 0);
        _tourBar.Padding = new(10, 6);
        _tourBar.Child = panel;
        _tourBar.IsVisible = true;
    }

    /// <summary>The verses of the current step, framed in the color of the path, with the step at the top.</summary>
    void AddTourFrames()
    {
        if (_tour is not { } t || t.Index >= t.Steps.Count || _peerLayer is null) return;
        var s = t.Steps[t.Index];
        if (s.Book != _curBook || s.Chapter != _curChapter) return;
        var from = s.From > 0 ? s.From : _verseBoxes.Keys.DefaultIfEmpty(0).Min();
        var to = s.To > from ? s.To : s.From > 0 ? from : _verseBoxes.Keys.DefaultIfEmpty(0).Max();
        if (!_verseBoxes.TryGetValue(from, out var a) || a.TranslatePoint(default, _peerLayer) is not { } top) return;
        var lastBox = _verseBoxes.Where(k => k.Key >= from && k.Key <= to).OrderBy(k => k.Key).Select(k => k.Value).LastOrDefault() ?? a;
        if (lastBox.TranslatePoint(default, _peerLayer) is not { } bottom) return;
        var ink = Color.TryParse(t.Color, out var c) ? c : Color.Parse("#3E63DD");
        var band = new Border
        {
            Width = a.Bounds.Width + 16, Height = bottom.Y + lastBox.Bounds.Height - top.Y + 8, CornerRadius = new(8),
            Background = new SolidColorBrush(ink, 0.07), BorderBrush = new SolidColorBrush(ink, 0.8), BorderThickness = new(3, 0, 0, 0),
        };
        Canvas.SetLeft(band, top.X - 10); Canvas.SetTop(band, top.Y - 4);
        var tag = NameTag($"{t.Name} · " + L("Schritt {0} von {1}", t.Index + 1, t.Steps.Count), ink);
        Canvas.SetLeft(tag, top.X + a.Bounds.Width - 200); Canvas.SetTop(tag, top.Y - 18);
        _peerFrames.Add(band); _peerFrames.Add(tag);
    }

    // ---- messages ----------------------------------------------------------------------------------------------------------------

    /// <summary>What a newcomer needs: the path being led and where the leader is.</summary>
    void SendStudyState()
    {
        if (_tour is { By: "" }) SendTour(full: true);
        if (_iLead) Dispatcher.UIThread.Post(() => SendView(force: true), DispatcherPriority.Background);
    }

    bool HandleCollabStudy(string type, JsonElement root)
    {
        var p = _from!;
        switch (type)
        {
            case "view":
            {
                var book = root.TryGetProperty("book", out var b) && b.ValueKind == JsonValueKind.String ? Clip(b.GetString(), 8) : "";
                int Int(string k) => root.TryGetProperty(k, out var e) && e.ValueKind == JsonValueKind.Number && e.TryGetInt32(out var n) ? n : 0;
                var chapter = Int("chapter"); var v = Int("v");
                var f = root.TryGetProperty("f", out var fe) && fe.ValueKind == JsonValueKind.Number && fe.TryGetDouble(out var d) && double.IsFinite(d) ? Math.Clamp(d, -3, 4) : 0;
                var end = root.TryGetProperty("end", out var en) && en.ValueKind == JsonValueKind.True;
                if (_books.All(x => x.Id != book) || chapter is < 1 or > 200 || v is < 1 or > 200) return true;
                OnView(p, book, chapter, v, f, end);
                return true;
            }
            case "tour":
                ReadTour(p, root);
                return true;
        }
        return false;
    }
}
