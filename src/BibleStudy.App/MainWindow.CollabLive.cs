using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace BibleStudy.App;

/// <summary>
/// Studying together, live: the verses the other person has chosen get a frame in their color, and their mouse pointer moves over your
/// text with their name, when you both read the same chapter. Positions are sent per verse (which verse, where in it), so they fit
/// although the windows, fonts and translations differ. Also: "is typing", "look here" and a notice for chat lines while the page is closed.
/// </summary>
public partial class MainWindow
{
    static readonly Color PeerInk = Color.Parse("#8E4EC6");
    Canvas? _peerLayer;
    readonly List<Control> _peerFrames = [];
    Control? _peerPointer;
    TextBlock? _peerPointerName;
    (string Book, int Chapter, int[] Verses)? _peerSel;
    (string Book, int Chapter, int Verse, double X, double Y)? _peerPtr;
    string _sentSel = "";
    DateTime _ptrSentAt, _typingSentAt;
    bool _ptrInside;
    IDisposable? _typingTimer;
    readonly TextBlock _peerTyping = new() { Classes = { "caption" }, IsVisible = false };

    string PeerWho => _peerName.Length > 0 ? _peerName : L("Die andere Person");

    /// <summary>Wires the reader once: pointer moves are sent, the layer is redrawn on scrolling and resizing.</summary>
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
    }

    void OnReaderPointer(PointerEventArgs e)
    {
        if (_peer?.IsOpen != true || !_settings.CollabSendPointer || !ReaderHost.IsVisible) return;
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

    /// <summary>Called after every repaint of the selection: sends the chosen verses when they changed.</summary>
    void CollabSendSelection()
    {
        if (_peer?.IsOpen != true || _collabSuppress || !_settings.CollabSendLocation) return;
        var verses = (_selVerses.Count > 0 ? _selVerses.OrderBy(v => v).ToArray() : _selected is int s ? [s] : []).Take(200).ToArray();
        var key = $"{_curBook} {_curChapter} {string.Join(",", verses)}";
        if (key == _sentSel) return;
        _sentSel = key;
        CollabSend(new { t = "sel", book = _curBook, chapter = _curChapter, verses });
    }

    void ResetCollabLive()
    {
        _peerSel = null; _peerPtr = null; _sentSel = "";
        _peerTyping.IsVisible = false;
        RenderPeerLayer();
    }

    /// <summary>Messages of the live part; true when the message was one of them.</summary>
    bool HandleCollabLive(string type, JsonElement root)
    {
        string Str(string key, int max) => root.TryGetProperty(key, out var e) && e.ValueKind == JsonValueKind.String ? Clip(e.GetString(), max) : "";
        int Int(string key) => root.TryGetProperty(key, out var e) && e.ValueKind == JsonValueKind.Number && e.TryGetInt32(out var n) ? n : 0;
        double Num(string key) => root.TryGetProperty(key, out var e) && e.ValueKind == JsonValueKind.Number && e.TryGetDouble(out var d) && double.IsFinite(d) ? Math.Clamp(d, 0, 1) : 0;
        switch (type)
        {
            case "sel":
            {
                var book = Str("book", 8); var chapter = Int("chapter");
                if (_books.All(b => b.Id != book) || chapter is < 1 or > 200) return true;
                var verses = root.TryGetProperty("verses", out var arr) && arr.ValueKind == JsonValueKind.Array
                    ? arr.EnumerateArray().Take(200).Where(x => x.ValueKind == JsonValueKind.Number && x.TryGetInt32(out _)).Select(x => x.GetInt32()).Where(v => v is > 0 and <= 200).ToArray() : [];
                _peerSel = (book, chapter, verses);
                ShowPeerPlace();
                RenderPeerLayer();
                return true;
            }
            case "ptr":
            {
                var book = Str("book", 8); var chapter = Int("chapter"); var v = Int("v");
                _peerPtr = v is > 0 and <= 200 && _books.Any(b => b.Id == book) ? (book, chapter, v, Num("x"), Num("y")) : null;
                RenderPeerLayer();
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
                var book = Str("book", 8); var chapter = Int("chapter");
                if (_books.All(b => b.Id != book) || chapter is < 1 or > 200) return true;
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

    /// <summary>Draws the frames around the other person's verses and their pointer, if they are in the chapter shown here.</summary>
    void RenderPeerLayer()
    {
        if (_peerLayer is null) return;
        foreach (var f in _peerFrames) _peerLayer.Children.Remove(f);
        _peerFrames.Clear();
        var open = _peer?.IsOpen == true && ReaderHost.IsVisible;
        if (open && _peerSel is { } sel && sel.Book == _curBook && sel.Chapter == _curChapter)
        {
            var first = true;
            foreach (var v in sel.Verses)
            {
                if (!_verseBoxes.TryGetValue(v, out var box) || box.TranslatePoint(default, _peerLayer) is not { } at) continue;
                var frame = new Border
                {
                    Width = box.Bounds.Width + 8, Height = box.Bounds.Height + 4, CornerRadius = new(6), BorderThickness = new(2),
                    BorderBrush = new SolidColorBrush(PeerInk, 0.85), Background = new SolidColorBrush(PeerInk, 0.07),
                };
                Canvas.SetLeft(frame, at.X - 4); Canvas.SetTop(frame, at.Y - 2);
                _peerFrames.Add(frame);
                if (first)
                {
                    var tag = new Border
                    {
                        Background = new SolidColorBrush(PeerInk), CornerRadius = new(4), Padding = new(5, 1),
                        Child = new TextBlock { Text = PeerWho, Foreground = Brushes.White, FontSize = 11, FontWeight = FontWeight.SemiBold },
                    };
                    Canvas.SetLeft(tag, at.X + box.Bounds.Width - 60); Canvas.SetTop(tag, at.Y - 16);
                    _peerFrames.Add(tag);
                    first = false;
                }
            }
            foreach (var f in _peerFrames) _peerLayer.Children.Add(f);
        }

        if (_peerPointer is null)
        {
            _peerPointerName = new TextBlock { Foreground = Brushes.White, FontSize = 11, FontWeight = FontWeight.SemiBold };
            _peerPointer = new StackPanel
            {
                Children =
                {
                    new Avalonia.Controls.Shapes.Path { Data = Geometry.Parse("M0,0 L0,16 L4.5,12 L7.5,19 L10,18 L7,11 L12.5,11 Z"), Fill = new SolidColorBrush(PeerInk), Stroke = Brushes.White, StrokeThickness = 1.2 },
                    new Border { Background = new SolidColorBrush(PeerInk), CornerRadius = new(4), Padding = new(5, 1), Margin = new(12, -4, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, Child = _peerPointerName },
                },
            };
            _peerLayer.Children.Add(_peerPointer);
        }
        if (open && _peerPtr is { } p && p.Book == _curBook && p.Chapter == _curChapter && _verseBoxes.TryGetValue(p.Verse, out var vbox)
            && vbox.TranslatePoint(new Point(p.X * vbox.Bounds.Width, p.Y * vbox.Bounds.Height), _peerLayer) is { } pos)
        {
            _peerPointerName!.Text = PeerWho;
            Canvas.SetLeft(_peerPointer, pos.X); Canvas.SetTop(_peerPointer, pos.Y);
            _peerPointer.IsVisible = true;
        }
        else _peerPointer.IsVisible = false;
    }

    /// <summary>Sends "is typing" at most every two seconds while a chat line is written.</summary>
    void CollabTyping()
    {
        if ((DateTime.UtcNow - _typingSentAt).TotalSeconds < 2) return;
        _typingSentAt = DateTime.UtcNow;
        CollabSend(new { t = "typing" });
    }

    /// <summary>"Look here": the other person gets a notice with a button to the open place.</summary>
    void CollabCallOver()
    {
        CollabSend(new { t = "come", book = _curBook, chapter = _curChapter, verse = _selected });
        AddCollabLog(L("Du hast {0} {1} gezeigt.", PeerWho, $"{BookName(_curBook)} {_curChapter}" + (_selected is int v ? "," + v : "")), mine: true, system: true);
    }

    /// <summary>A chat line while this page is not open: a small notice, so it is not missed.</summary>
    void NotifyChat(string text)
    {
        if (SideTabs.SelectedIndex == CollabTab && SidePanel.IsVisible) return;
        Notify(PeerWho, Shorten(text, 140), true, L("Antworten"), () => { SetPanelOpen(true); SideTabs.SelectedIndex = CollabTab; }, TimeSpan.FromSeconds(8));
    }
}
