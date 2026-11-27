using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace BibleStudy.App;

/// <summary>
/// Studying together, live: for every person reading the same chapter, the verses they chose get a frame in their color (with the words they
/// selected), their mouse pointer moves over your text with their name, their highlights show as a bar at the side, and the verse they are
/// reading aloud is marked. Positions are sent per verse (which verse, where in it), so they fit although windows, fonts and translations differ.
/// </summary>
public partial class MainWindow
{
    Canvas? _peerLayer;
    readonly List<Control> _peerFrames = [];
    string _sentSel = "", _sentMarks = "";
    DateTime _ptrSentAt, _typingSentAt;
    bool _ptrInside;
    IDisposable? _typingTimer;
    DispatcherTimer? _readTimer;
    int _sentReading;
    readonly TextBlock _peerTyping = new() { Classes = { "caption" }, IsVisible = false };

    /// <summary>Wires the reader once: pointer moves are sent, the layer is redrawn on scrolling and resizing, reading aloud is watched.</summary>
    void InitCollabLive()
    {
        if (_peerLayer is not null) return;
        _peerLayer = new Canvas { IsHitTestVisible = false, ClipToBounds = true, ZIndex = 50 };
        Grid.SetRow(_peerLayer, 1);
        ReaderHost.Children.Add(_peerLayer);
        ReaderScroll.ScrollChanged += (_, _) => RenderPeerLayer();
        ReaderPanel.SizeChanged += (_, _) => RenderPeerLayer();
        ReaderPanel.AddHandler(PointerMovedEvent, (_, e) => OnReaderPointer(e), handledEventsToo: true);
        ReaderPanel.AddHandler(PointerExitedEvent, (_, _) => { if (_ptrInside) { _ptrInside = false; SendPointer(0, 0, 0); } }, handledEventsToo: true);
        _readTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
        _readTimer.Tick += (_, _) => CollabReadTick();
        _readTimer.Start();
        InitCollabStudy();
    }

    void OnReaderPointer(PointerEventArgs e)
    {
        if (!CollabOpen || !_settings.CollabSendPointer || !ReaderHost.IsVisible) return;
        if ((DateTime.UtcNow - _ptrSentAt).TotalMilliseconds < 50) return;
        foreach (var (n, box) in _verseBoxes)
        {
            var p = e.GetPosition(box);
            if (p.X < 0 || p.Y < 0 || p.X > box.Bounds.Width || p.Y > box.Bounds.Height || box.Bounds.Width < 1 || box.Bounds.Height < 1) continue;
            _ptrInside = true;
            SendPointer(n, p.X / box.Bounds.Width, p.Y / box.Bounds.Height);
            return;
        }
        if (_ptrInside) { _ptrInside = false; SendPointer(0, 0, 0); }
    }

    void SendPointer(int verse, double x, double y)
    {
        _ptrSentAt = DateTime.UtcNow;
        CollabSend(new { t = "ptr", book = _curBook, chapter = _curChapter, v = verse, x = Math.Round(x, 3), y = Math.Round(y, 3) });
    }

    /// <summary>Sends the chosen verses (and selected words) when they changed.</summary>
    void CollabSendSelection()
    {
        if (!CollabOpen || _collabSuppress || !_settings.CollabSendLocation) return;
        var words = _sel is { Parts.Count: > 0 } ? Clip(SelectedText().Trim(), 120) : "";
        var verses = (_sel is { Parts.Count: > 0 } s ? s.Parts.Select(p => p.Verse).Distinct().ToArray()
            : _selVerses.Count > 0 ? _selVerses.OrderBy(v => v).ToArray() : _selected is int one ? [one] : []).Take(200).ToArray();
        // the exact words (offsets in the verse texts) with the fingerprint of the text: drawn as a selection where the other one has the same text
        var (tr, parts) = ExactSelection();
        var key = $"{_curBook} {_curChapter} {string.Join(",", verses)} {words} {tr} {string.Join(";", parts.Select(x => string.Join(",", x)))}";
        if (key == _sentSel) return;
        _sentSel = key;
        CollabSend(new { t = "sel", book = _curBook, chapter = _curChapter, verses, words, tr, parts });
    }

    /// <summary>Sends the own highlights of the open chapter when they changed (the others see them as a bar at the side and can take them over).</summary>
    void CollabSendChapterMarks()
    {
        if (!CollabOpen || !_settings.CollabSendLocation || _curChapter < 1) return;
        // marks on words of the translation in the first column carry their words too (s = start, x = end); the others see them on the words when their text is the same
        var mainId = _cells.FirstOrDefault(c => !c.Data.Foreign && c.Data.Col == 0)?.Data.T.Id;
        var items = _vault.GetMarks(_curBook, _curChapter).Take(300).Select(m =>
        {
            var exact = !m.WholeVerse && mainId is not null && m.TranslationId == mainId;
            return new { v = m.Ref.Verse, e = m.EndVerse, c = m.Color, s = exact ? m.Start : -1, x = exact ? m.End : -1 };
        }).ToList();
        var tr = MainPrint();
        var key = $"{_curBook} {_curChapter} {tr} " + string.Join(";", items.Select(i => $"{i.v}-{i.e}-{i.c}-{i.s}-{i.x}"));
        if (key == _sentMarks) return;
        _sentMarks = key;
        CollabSend(new { t = "mk", book = _curBook, chapter = _curChapter, items, tr });
    }

    /// <summary>While reading aloud: which verse is read now (from the speech progress, or estimated from the time), sent when it changes.</summary>
    void CollabReadTick()
    {
        if (!CollabOpen) return;
        var now = 0;
        if (_speechMode && _speakVerses.Count > 0)
        {
            var pos = _speakAbs > 0 ? _speakAbs : (int)((DateTime.UtcNow - _speakStartedAt).TotalSeconds * 14 * (1 + _settings.SpeakRate * 0.1));
            var sum = 0;
            now = _speakVerses[^1].Verse;
            foreach (var (verse, length) in _speakVerses) { sum += length; if (pos < sum) { now = verse; break; } }
        }
        if (now == _sentReading) return;
        _sentReading = now;
        CollabSend(new { t = "readv", book = _curBook, chapter = _curChapter, v = now });
    }

    void ResetCollabLive()
    {
        foreach (var p in _people.Values) { p.Sel = null; p.Ptr = null; p.Reading = null; p.Marks = null; }
        _sentSel = ""; _sentMarks = ""; _sentReading = 0;
        _peerTyping.IsVisible = false;
        ResetCollabStudy();
        RenderPeerLayer();
    }

    /// <summary>Messages of the live part; true when the message was one of them.</summary>
    bool HandleCollabLive(string type, JsonElement root)
    {
        string Str(string key, int max) => root.TryGetProperty(key, out var e) && e.ValueKind == JsonValueKind.String ? Clip(e.GetString(), max) : "";
        int Int(string key) => root.TryGetProperty(key, out var e) && e.ValueKind == JsonValueKind.Number && e.TryGetInt32(out var n) ? n : 0;
        double Num(string key) => root.TryGetProperty(key, out var e) && e.ValueKind == JsonValueKind.Number && e.TryGetDouble(out var d) && double.IsFinite(d) ? Math.Clamp(d, 0, 1) : 0;
        if (HandleCollabStudy(type, root)) return true;
        var p = _from!;
        var book = Str("book", 8); var chapter = Int("chapter");
        var place = _books.Any(b => b.Id == book) && chapter is >= 1 and <= 200;
        switch (type)
        {
            case "sel":
            {
                if (!place) return true;
                var verses = root.TryGetProperty("verses", out var arr) && arr.ValueKind == JsonValueKind.Array
                    ? arr.EnumerateArray().Take(200).Where(x => x.ValueKind == JsonValueKind.Number && x.TryGetInt32(out _)).Select(x => x.GetInt32()).Where(v => v is > 0 and <= 200).ToArray() : [];
                p.Sel = (book, chapter, verses, Str("words", 120).Trim());
                ReadExactSel(p, root, book, chapter);
                ShowPeople(); RenderPeerLayer();
                // whoever is followed clicks a verse: it comes into view here too (nav is sent only when the chapter changes)
                if (verses.Length > 0 && (_follow || (_leaderId == p.Id && _followLeader)) && !LeaderScrollsHere(p, book, chapter)) _ = FollowAsync((book, chapter, verses.Min()));
                return true;
            }
            case "ptr":
            {
                var v = Int("v");
                p.Ptr = place && v is > 0 and <= 200 ? (book, chapter, v, Num("x"), Num("y")) : null;
                RenderPeerLayer();
                return true;
            }
            case "mk":
            {
                if (!place || !root.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array) return true;
                var list = new List<(int, int, string)>();
                foreach (var e in items.EnumerateArray().Take(300))
                {
                    if (e.ValueKind != JsonValueKind.Object) continue;
                    int Get(string k) => e.TryGetProperty(k, out var x) && x.ValueKind == JsonValueKind.Number && x.TryGetInt32(out var n) ? n : 0;
                    var color = e.TryGetProperty("c", out var c) && c.ValueKind == JsonValueKind.String && c.GetString() is { } cs && System.Text.RegularExpressions.Regex.IsMatch(cs, "^#[0-9A-Fa-f]{6}$") ? cs : null;
                    var (v, end) = (Get("v"), Get("e"));
                    if (color is not null && v is > 0 and <= 200) list.Add((v, end > v && end <= 200 ? end : 0, color));
                }
                p.Marks = (book, chapter, list);
                ReadExactMarks(p, root, book, chapter);
                ShowPeople(); RenderPeerLayer();
                return true;
            }
            case "readv":
            {
                var v = Int("v");
                p.Reading = place && v is > 0 and <= 200 ? (book, chapter, v) : null;
                ShowPeople(); RenderPeerLayer();
                return true;
            }
            case "typing":
                _peerTyping.Text = L("{0} schreibt …", PeerWho);
                _peerTyping.IsVisible = true;
                _typingTimer?.Dispose();
                _typingTimer = DispatcherTimer.RunOnce(() => _peerTyping.IsVisible = false, TimeSpan.FromSeconds(3));
                return true;
            case "come":
            {
                if (!place) return true;
                int? verse = Int("verse") is > 0 and <= 200 and var vv ? vv : null;
                var where = $"{BookName(book)} {chapter}" + (verse is int x ? "," + x : "");
                var text = L("{0} möchte dir {1} zeigen.", PeerWho, where);
                Notify(L("Zusammen studieren"), text, true, L("Hingehen"), () => _ = GoToAsync(book, chapter, verse), TimeSpan.FromSeconds(20));
                var go = new Button { Content = L("Hingehen"), Classes = { "tool" }, Width = double.NaN, Padding = new(10, 0), HorizontalAlignment = HorizontalAlignment.Left };
                go.Click += async (_, _) => await GoToAsync(book, chapter, verse);
                AddCollabItem(Card(new StackPanel { Spacing = 6, Children = { new TextBlock { Text = text, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap }, go } }));
                return true;
            }
        }
        return false;
    }

    static Border NameTag(string text, Color ink) => new()
    {
        Background = new SolidColorBrush(ink), CornerRadius = new(4), Padding = new(5, 1),
        Child = new TextBlock { Text = text, Foreground = Brushes.White, FontSize = 11, FontWeight = FontWeight.SemiBold, MaxWidth = 260, TextTrimming = TextTrimming.CharacterEllipsis },
    };

    /// <summary>Draws, for everyone in the chapter shown here: frames around their verses, their highlights at the side, the verse read aloud, their pointer.</summary>
    void RenderPeerLayer()
    {
        if (_peerLayer is null) return;
        foreach (var f in _peerFrames) _peerLayer.Children.Remove(f);
        _peerFrames.Clear();
        var open = CollabOpen && ReaderHost.IsVisible;
        bool Here(string b, int c) => open && b == _curBook && c == _curChapter;
        Point? At(int verse, out Border? box) => _verseBoxes.TryGetValue(verse, out box) ? box.TranslatePoint(default, _peerLayer) : null;
        var lane = 0;
        foreach (var p in _people.Values)
        {
            if (p.Marks is { } mk && Here(mk.Book, mk.Chapter))
                foreach (var (verse, end, color) in mk.Items)
                {
                    if (At(verse, out var box) is not { } a) continue;
                    var bottom = end > verse && At(end, out var last) is { } b2 ? b2.Y + last!.Bounds.Height : a.Y + box!.Bounds.Height;
                    var bar = new Border { Width = 4, Height = Math.Max(6, bottom - a.Y), CornerRadius = new(2), Background = new SolidColorBrush(Color.Parse(color)), BorderBrush = new SolidColorBrush(p.Ink), BorderThickness = new(0, 0, 0, 0) };
                    ToolTip.SetTip(bar, p.Name);
                    Canvas.SetLeft(bar, a.X - 12 - lane * 6); Canvas.SetTop(bar, a.Y);
                    _peerFrames.Add(bar);
                }
            // the same text on both sides: the selected words themselves, as a selection; else a frame around the verses
            if (p.Sel is { } sel && Here(sel.Book, sel.Chapter) && !DrawExactSelection(p, lane))
            {
                var first = true;
                foreach (var v in sel.Verses)
                {
                    if (At(v, out var box) is not { } a) continue;
                    var frame = new Border
                    {
                        Width = box!.Bounds.Width + 8 + lane * 4, Height = box.Bounds.Height + 4 + lane * 4, CornerRadius = new(6), BorderThickness = new(2),
                        BorderBrush = new SolidColorBrush(p.Ink, 0.85), Background = new SolidColorBrush(p.Ink, 0.06),
                    };
                    Canvas.SetLeft(frame, a.X - 4 - lane * 2); Canvas.SetTop(frame, a.Y - 2 - lane * 2);
                    _peerFrames.Add(frame);
                    if (first)
                    {
                        var tag = NameTag(sel.Words.Length > 0 ? $"{p.Name}: »{Shorten(sel.Words, 40)}«" : p.Name, p.Ink);
                        Canvas.SetLeft(tag, a.X + 8 + lane * 70); Canvas.SetTop(tag, a.Y - 16);
                        _peerFrames.Add(tag);
                        first = false;
                    }
                }
            }
            if (p.Reading is { } rd && Here(rd.Book, rd.Chapter) && At(rd.Verse, out var rbox) is { } ra)
            {
                var under = new Border { Width = rbox!.Bounds.Width, Height = 3, CornerRadius = new(2), Background = new SolidColorBrush(p.Ink) };
                Canvas.SetLeft(under, ra.X); Canvas.SetTop(under, ra.Y + rbox.Bounds.Height);
                var tag = NameTag(L("{0} liest vor", p.Name), p.Ink);
                Canvas.SetLeft(tag, ra.X + rbox.Bounds.Width - 110); Canvas.SetTop(tag, ra.Y + rbox.Bounds.Height - 2);
                _peerFrames.Add(under); _peerFrames.Add(tag);
            }

            if (p.Pointer is null)
            {
                p.PointerName = new TextBlock { Foreground = Brushes.White, FontSize = 11, FontWeight = FontWeight.SemiBold };
                p.Pointer = new StackPanel
                {
                    Children =
                    {
                        new Avalonia.Controls.Shapes.Path { Data = Geometry.Parse("M0,0 L0,16 L4.5,12 L7.5,19 L10,18 L7,11 L12.5,11 Z"), Fill = new SolidColorBrush(p.Ink), Stroke = Brushes.White, StrokeThickness = 1.2 },
                        new Border { Background = new SolidColorBrush(p.Ink), CornerRadius = new(4), Padding = new(5, 1), Margin = new(12, -4, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, Child = p.PointerName },
                    },
                };
                _peerLayer.Children.Add(p.Pointer);
            }
            if (p.Ptr is { } ptr && Here(ptr.Book, ptr.Chapter) && _verseBoxes.TryGetValue(ptr.Verse, out var vbox)
                && vbox.TranslatePoint(new Point(ptr.X * vbox.Bounds.Width, ptr.Y * vbox.Bounds.Height), _peerLayer) is { } pos)
            {
                p.PointerName!.Text = p.Name;
                Canvas.SetLeft(p.Pointer, pos.X); Canvas.SetTop(p.Pointer, pos.Y);
                p.Pointer.IsVisible = true;
            }
            else p.Pointer.IsVisible = false;
            lane++;
        }
        AddStudyFrames();
        foreach (var f in _peerFrames) _peerLayer.Children.Insert(0, f);   // under the pointers
        ShowFloatingReactions();
    }

    /// <summary>Sends "is typing" at most every two seconds while a chat line is written.</summary>
    void CollabTyping()
    {
        if ((DateTime.UtcNow - _typingSentAt).TotalSeconds < 2) return;
        _typingSentAt = DateTime.UtcNow;
        CollabSend(new { t = "typing" });
    }

    /// <summary>"Look here": the others get a notice with a button to the open place.</summary>
    void CollabCallOver()
    {
        CollabSend(new { t = "come", book = _curBook, chapter = _curChapter, verse = _selected });
        AddCollabLog(L("Du hast {0} {1} gezeigt.", PeopleNames, $"{BookName(_curBook)} {_curChapter}" + (_selected is int v ? "," + v : "")), mine: true, system: true);
    }

    /// <summary>A chat line while this page is not open: a small notice, so it is not missed.</summary>
    void NotifyChat(string text)
    {
        if (SideTabs.SelectedIndex == CollabTab && SidePanel.IsVisible) return;
        Notify(PeerWho, Shorten(text, 140), true, L("Antworten"), () => { SetPanelOpen(true); SideTabs.SelectedIndex = CollabTab; }, TimeSpan.FromSeconds(8));
    }

    /// <summary>Takes over the highlights a person has in the open chapter (only those not there yet).</summary>
    void TakeLiveMarks(CollabPerson p)
    {
        if (p.Marks is not { } mk) return;
        var have = _vault.GetMarks(mk.Book, mk.Chapter).Select(m => (m.Ref.Verse, m.Color.ToUpperInvariant())).ToHashSet();
        foreach (var (verse, end, color) in mk.Items)
        {
            if (!have.Add((verse, color.ToUpperInvariant()))) continue;
            var r = new BibleStudy.Core.Models.VerseRef(mk.Book, mk.Chapter, verse);
            _vault.AddMark(BibleStudy.Core.Storage.Mark.AnyTranslation, r, -1, -1, "", color, L("Geteilt von {0}", p.Name), null, AutoTags(r), end);
        }
        LoadChapterMarks(); Paint(); RefreshCollection();
    }
}
