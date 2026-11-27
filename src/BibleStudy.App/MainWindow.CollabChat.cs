using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using BibleStudy.Core.Collab;
using BibleStudy.Core.Models;

namespace BibleStudy.App;

/// <summary>
/// The chat of studying together as its own tile: a fixed height with its own scrolling, the newest line at the bottom and always in view
/// (unless the person scrolled up to read, then a button brings them down). The page around it does not grow with the chat.
/// The chat is kept on this device per person (see <see cref="CollabChatHistory"/>) and comes back when the same person is in the room again.
/// </summary>
public partial class MainWindow
{
    const int ChatShown = 150;          // lines of this meeting in the tile; older ones drop out of view (they stay saved)
    const int ChatRestoredShown = 200;  // lines of the saved history shown above them

    ScrollViewer? _chatScroll;
    Border? _chatTile;
    TextBox? _chatInput;
    Button? _chatNewBelow;
    readonly StackPanel _chatRestored = new() { Spacing = 6 };
    readonly HashSet<string> _chatRestoredFor = [];
    readonly HashSet<string> _chatSessionMids = [];
    CollabChatHistory? _chatHistoryStore;
    Action? _chatSettingsShow;   // brings the numbers in the settings up to date

    CollabChatHistory ChatHistory => _chatHistoryStore ??= CollabChatHistory.Load(Path.Combine(_root, "collab-chats.json"));

    /// <summary>The chat tile (made once, so a half written line survives when the page is built again).</summary>
    Control ChatTile()
    {
        if (_chatTile is not null) { Detach(_chatTile); return _chatTile; }

        _chatScroll = new ScrollViewer { Content = _collabLog, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled, Padding = new(0, 0, 8, 0) };
        _chatNewBelow = new Button { Content = L("Neue Nachrichten ↓"), Classes = { "primary" }, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new(0, 0, 0, 6), IsVisible = false };
        _chatNewBelow.Click += (_, _) => ScrollChatToEnd();
        _chatScroll.ScrollChanged += (_, _) => { if (ChatAtEnd()) _chatNewBelow.IsVisible = false; };

        _chatInput = new TextBox { PlaceholderText = L("Nachricht schreiben, Enter sendet"), MaxLength = 2000, AcceptsReturn = false };
        var send = new Button { Content = "➤", Margin = new(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Stretch };
        ToolTip.SetTip(send, L("Senden"));
        void Send()
        {
            if (string.IsNullOrWhiteSpace(_chatInput.Text)) return;
            var text = _chatInput.Text.Trim(); _chatInput.Text = "";
            var mid = CollabSend(new { t = "chat", text });
            AddChat(_settings.CollabName, text, mid, mine: true, null);
        }
        _chatInput.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Send(); e.Handled = true; } };
        _chatInput.TextChanged += (_, _) => { if (!string.IsNullOrWhiteSpace(_chatInput.Text)) CollabTyping(); };
        send.Click += (_, _) => Send();
        var line = new DockPanel();
        DockPanel.SetDock(send, Dock.Right);
        line.Children.Add(send); line.Children.Add(_chatInput);

        Detach(_peerTyping);
        var grid = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto,Auto"), RowSpacing = 6 };
        var head = new TextBlock { Text = L("Chat"), FontWeight = FontWeight.SemiBold };
        var log = new Panel { Children = { _chatScroll, _chatNewBelow } };
        Grid.SetRow(head, 0); Grid.SetRow(log, 1); Grid.SetRow(_peerTyping, 2); Grid.SetRow(line, 3);
        grid.Children.Add(head); grid.Children.Add(log); grid.Children.Add(_peerTyping); grid.Children.Add(line);

        // a fixed height: the messages scroll inside, the buttons around stay where they are
        _chatTile = new Border { Child = grid, Padding = new(12, 10), CornerRadius = new(12), BorderThickness = new(1), Height = AppPlatform.IsMobile ? 340 : 400, Margin = new(0, 4, 0, 4) };
        _chatTile.Bind(Border.BorderBrushProperty, this.GetResourceObservable("BsBorder"));
        _chatTile.Bind(Border.BackgroundProperty, this.GetResourceObservable("BsSurface"));
        if (!_collabLog.Children.Contains(_chatRestored)) _collabLog.Children.Insert(0, _chatRestored);
        Dispatcher.UIThread.Post(ScrollChatToEnd, DispatcherPriority.Background);
        return _chatTile;
    }

    bool ChatAtEnd() => _chatScroll is not { } s || s.Offset.Y >= s.Extent.Height - s.Viewport.Height - 48;

    void ScrollChatToEnd()
    {
        if (_chatNewBelow is not null) _chatNewBelow.IsVisible = false;
        // after the layout, so the new line is measured already
        Dispatcher.UIThread.Post(() => _chatScroll?.ScrollToEnd(), DispatcherPriority.Background);
    }

    /// <summary>A line or card in the chat, at the bottom. It comes into view when the chat was at the bottom (or it is one's own).</summary>
    void AddCollabItem(Control item, bool toEnd = false)
    {
        var atEnd = ChatAtEnd();
        if (!_collabLog.Children.Contains(_chatRestored)) _collabLog.Children.Insert(0, _chatRestored);
        _collabLog.Children.Add(item);
        while (_collabLog.Children.Count > ChatShown + 1) _collabLog.Children.RemoveAt(1);   // 0 is the saved history
        AnimateIn(item, 0, 8);
        if (atEnd || toEnd) ScrollChatToEnd();
        else if (_chatNewBelow is not null) _chatNewBelow.IsVisible = true;
    }

    // ---- the saved chat -------------------------------------------------------------------------------------------------

    /// <summary>Keeps a line with everybody in the room now (only on this device; nothing is sent).</summary>
    void KeepChat(CollabChatLine line)
    {
        if (line.Mid.Length > 0) _chatSessionMids.Add(line.Mid);
        if (!_settings.CollabKeepChats || _people.Count == 0) return;
        line.When = DateTime.Now;
        ChatHistory.Add(_people.Keys, line);
        _chatSettingsShow?.Invoke();
    }

    /// <summary>
    /// A person came into the room: if there is a saved chat with exactly this person (by their lasting id, not by the name), it comes back
    /// above the new lines. Someone else simply starts with an empty chat.
    /// </summary>
    void RestoreChat(CollabPerson p)
    {
        if (!_settings.CollabKeepChats || !ChatHistory.Has(p.Id) || !_chatRestoredFor.Add(p.Id)) return;
        var lines = ChatHistory.For(_chatRestoredFor).Where(l => l.Mid.Length == 0 || !_chatSessionMids.Contains(l.Mid)).TakeLast(ChatRestoredShown).ToList();
        _chatRestored.Children.Clear();
        if (lines.Count == 0) return;
        var names = string.Join(", ", _people.Values.Where(x => _chatRestoredFor.Contains(x.Id)).Select(x => x.Name.Length > 0 ? x.Name : "?"));
        _chatRestored.Children.Add(new TextBlock { Text = L("Gespeicherter Chat mit {0} (nur auf diesem Gerät)", names), Classes = { "caption" }, HorizontalAlignment = HorizontalAlignment.Center, TextWrapping = TextWrapping.Wrap });
        var day = DateTime.MinValue;
        foreach (var l in lines)
        {
            if (l.When.Date != day)
            {
                day = l.When.Date;
                _chatRestored.Children.Add(new TextBlock { Text = day.ToString("d"), Classes = { "caption" }, HorizontalAlignment = HorizontalAlignment.Center, Margin = new(0, 4, 0, 0) });
            }
            var ink = l.Mine ? (Color?)null : _people.Values.FirstOrDefault(x => x.Name == l.Who)?.Ink;
            _chatRestored.Children.Add(l.Kind == "verse" ? SavedVerseCard(l) : ChatBubble(l.Who, l.Text, l.Mid, l.Mine, ink, l.When));
        }
        var now = new Border { Height = 1, Margin = new(0, 6, 0, 2) };
        now.Bind(Border.BackgroundProperty, this.GetResourceObservable("BsBorder"));
        _chatRestored.Children.Add(now);
        ScrollChatToEnd();
    }

    Control SavedVerseCard(CollabChatLine l)
    {
        var stack = new StackPanel { Spacing = 4 };
        var label = l.Book.Length > 0 && l.Chapter > 0 && l.Verse > 0 ? Label(new VerseRef(l.Book, l.Chapter, l.Verse)) : "";
        stack.Children.Add(new TextBlock { Text = $"{l.Who} · {label}{(l.Translation.Length > 0 ? " (" + l.Translation + ")" : "")} · {l.When:t}", FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap });
        if (l.Text.Length > 0) stack.Children.Add(new TextBlock { Text = l.Text, TextWrapping = TextWrapping.Wrap, FontFamily = ScriptureFont });
        if (_books.Any(b => b.Id == l.Book) && l.Chapter > 0 && l.Verse > 0)
        {
            var go = new Button { Content = L("Hingehen"), Classes = { "tool" }, Width = double.NaN, Padding = new(10, 0), HorizontalAlignment = HorizontalAlignment.Left };
            go.Click += async (_, _) => await GoToAsync(l.Book, l.Chapter, l.Verse);
            stack.Children.Add(go);
        }
        var card = Card(stack);
        card.HorizontalAlignment = l.Mine ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        card.Opacity = 0.85;
        return card;
    }

    /// <summary>Settings → My data: keep chats or not, and empty them.</summary>
    void InitChatSettings()
    {
        if (VaultInfo.Parent is not StackPanel card) return;
        var keep = new CheckBox { Content = L("Chats von „Zusammen studieren“ auf diesem Gerät speichern"), IsChecked = _settings.CollabKeepChats };
        ToolTip.SetTip(keep, L("Bist du wieder mit derselben Person in einem Raum, siehst du euren bisherigen Chat. Gespeichert wird nur bei dir; jede Person speichert für sich."));
        keep.IsCheckedChanged += (_, _) => { _settings.CollabKeepChats = keep.IsChecked == true; SaveSettings(); };
        var info = new TextBlock { Classes = { "caption" }, TextWrapping = TextWrapping.Wrap };
        var clear = new Button { Content = L("Gespeicherte Chats leeren …"), HorizontalAlignment = HorizontalAlignment.Left };
        void Show()
        {
            var h = ChatHistory;
            info.Text = h.Lines == 0 ? L("Keine gespeicherten Chats.") : L("{0} Nachrichten mit {1} Personen gespeichert.", h.Lines, h.People);
            clear.IsEnabled = h.Lines > 0;
        }
        clear.Click += async (_, _) =>
        {
            if (await AskAsync(L("Chats leeren?"), L("Alle gespeicherten Chats von „Zusammen studieren“ werden auf diesem Gerät gelöscht. Bei den anderen Personen bleiben ihre eigenen Kopien."), L("Leeren"), L("Abbrechen")) != 0) return;
            ChatHistory.Clear();
            _chatRestored.Children.Clear(); _chatRestoredFor.Clear();
            Show();
        };
        keep.IsCheckedChanged += (_, _) => Show();
        _chatSettingsShow = Show;
        Show();
        card.Children.Add(new TextBlock { Text = L("Chats"), FontWeight = FontWeight.SemiBold, Margin = new(0, 8, 0, 0) });
        card.Children.Add(keep);
        card.Children.Add(clear);
        card.Children.Add(info);
    }
}
