using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using BibleStudy.Core.Collab;
using BibleStudy.Core.Models;
using BibleStudy.Core.Storage;
using Avalonia.Input.Platform;

namespace BibleStudy.App;

/// <summary>
/// Studying together: a direct encrypted connection to one other person, set up by swapping two codes (no server, no account, no cost).
/// Only what a person sends on purpose arrives: their place in the Bible (if they switch that on), chat lines, single verses, documents as copies.
/// Everything received is treated as untrusted text: nothing is run, saved or opened without a click (following the place is opt-in).
/// </summary>
public partial class MainWindow
{
    const int CollabTab = 23;
    const int MaxSharedBody = 200_000;

    CollabPeer? _peer;
    string _peerName = "";
    bool _collabSuppress;       // true while the app itself jumps to the partner's place, so that it does not echo back
    bool _follow;
    (string Book, int Chapter, int? Verse)? _peerPlace;
    readonly StackPanel _collabLog = new() { Spacing = 6 };
    readonly TextBlock _collabPlace = new() { Classes = { "caption" }, TextWrapping = TextWrapping.Wrap };

    static void OnUi(Action a) => Dispatcher.UIThread.Post(a);

    void BuildCollabUi()
    {
        CollabPanel.Children.Clear();
        CollabPanel.Children.Add(new TextBlock { Text = L("GEMEINSAM STUDIEREN"), Classes = { "eyebrow" } });
        if (!CollabConsented) { BuildCollabConsent(); return; }
        if (_peer?.IsOpen == true) BuildCollabConnected(); else BuildCollabStart();
        var revoke = new Button { Content = L("Zustimmung zurücknehmen"), Classes = { "tool" }, Padding = new(10, 0), HorizontalAlignment = HorizontalAlignment.Left, Margin = new(0, 12, 0, 0) };
        revoke.Click += (_, _) => RevokeCollabConsent();
        CollabPanel.Children.Add(revoke);
    }

    // ---- before the connection: two codes ------------------------------------------------------------------------------

    void BuildCollabStart()
    {
        CollabPanel.Children.Add(new TextBlock
        {
            Text = L("Studiere die Bibel mit einer anderen Person, direkt von Programm zu Programm: verschlüsselt, ohne Server, ohne Konto und kostenlos. Ihr tauscht dafür zwei kurze Codes aus, zum Beispiel per Messenger."),
            Classes = { "caption" }, TextWrapping = TextWrapping.Wrap,
        });
        CollabPanel.Children.Add(Card(new StackPanel
        {
            Spacing = 4,
            Children =
            {
                new TextBlock { Text = L("Was geteilt wird"), FontWeight = FontWeight.SemiBold },
                new TextBlock { Text = L("Nur, was du hier ausdrücklich sendest: Chat, einzelne Verse und Kopien von Dokumenten, auf Wunsch deine aktuelle Bibelstelle. Dein Vault bleibt bei dir. Die andere Person sieht deine Internetadresse (wie bei jeder Direktverbindung), deshalb verbinde dich nur mit Menschen, denen du vertraust."), Classes = { "caption" }, TextWrapping = TextWrapping.Wrap },
            },
        }));

        var name = new TextBox { Watermark = L("Dein Name (wird der anderen Person gezeigt)"), Text = _settings.CollabName, MaxLength = 40 };
        name.LostFocus += (_, _) => { _settings.CollabName = (name.Text ?? "").Trim(); SaveSettings(); };
        CollabPanel.Children.Add(name);

        var area = new StackPanel { Spacing = 8 };
        var host = new Button { Content = L("Einladung erstellen"), Classes = { "primary" }, Margin = new(0, 0, 8, 0) };
        var join = new Button { Content = L("Einladung annehmen") };
        host.Click += async (_, _) => { if (CollabNameOk(name)) await HostFlowAsync(area); };
        join.Click += (_, _) => { if (CollabNameOk(name)) JoinFlow(area); };
        CollabPanel.Children.Add(new WrapPanel { Children = { host, join } });
        CollabPanel.Children.Add(area);
    }

    bool CollabNameOk(TextBox box)
    {
        _settings.CollabName = (box.Text ?? "").Trim(); SaveSettings();
        if (_settings.CollabName.Length > 0) return true;
        box.Focus();
        StatusText.Text = L("Bitte gib zuerst deinen Namen ein.");
        return false;
    }

    CollabPeer NewPeer()
    {
        _peer?.Dispose();
        var peer = new CollabPeer();
        peer.Connected += () => OnUi(() => { if (_peer != peer) return; CollabSend(new { t = "hello", name = _settings.CollabName }); BuildCollabUi(); });
        peer.Received += m => OnUi(() => { if (_peer == peer) HandleCollab(m); });
        peer.Closed += _ => OnUi(() => { if (_peer != peer) return; _peer = null; _follow = false; AddCollabLog(L("Die Verbindung wurde beendet."), system: true); if (CollabPanel.Children.Count > 0) BuildCollabUi(); });
        _peer = peer;
        return peer;
    }

    Control CodeBox(string code)
    {
        var box = new TextBox { Text = code, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, MaxHeight = 110, FontSize = 11 };
        var copy = new Button { Content = L("Code kopieren"), HorizontalAlignment = HorizontalAlignment.Left };
        copy.Click += async (_, _) =>
        {
            if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard) { await clipboard.SetTextAsync(code); copy.Content = L("Kopiert ✓"); }
        };
        return new StackPanel { Spacing = 6, Children = { box, copy } };
    }

    async Task HostFlowAsync(StackPanel area)
    {
        area.Children.Clear();
        var status = new TextBlock { Text = L("Einladung wird erstellt … (kann einige Sekunden dauern)"), Classes = { "caption" }, TextWrapping = TextWrapping.Wrap };
        area.Children.Add(status);
        var peer = NewPeer();
        try
        {
            var code = await peer.CreateInvitationAsync();
            area.Children.Clear();
            area.Children.Add(new TextBlock { Text = L("1. Schicke diesen Code der anderen Person:"), FontWeight = FontWeight.SemiBold });
            area.Children.Add(CodeBox(code));
            area.Children.Add(new TextBlock { Text = L("2. Füge hier ihren Antwortcode ein:"), FontWeight = FontWeight.SemiBold, Margin = new(0, 6, 0, 0) });
            var answer = new TextBox { Watermark = "BS1-…", TextWrapping = TextWrapping.Wrap, MaxHeight = 110, FontSize = 11 };
            var connect = new Button { Content = L("Verbinden"), Classes = { "primary" }, HorizontalAlignment = HorizontalAlignment.Left };
            var info = new TextBlock { Classes = { "caption" }, TextWrapping = TextWrapping.Wrap };
            connect.Click += async (_, _) =>
            {
                try
                {
                    peer.CompleteWithAnswer(answer.Text ?? "");
                    connect.IsEnabled = false; info.Text = L("Verbinde … ");
                    await WaitConnectedAsync(peer, info);
                    connect.IsEnabled = true;
                }
                catch { info.Text = L("Dieser Code passt nicht. Es muss der Antwortcode sein, der mit „BS1-“ beginnt."); connect.IsEnabled = true; }
            };
            area.Children.Add(answer); area.Children.Add(connect); area.Children.Add(info);
        }
        catch (Exception ex) { status.Text = L("Fehler: ") + ex.Message; }
    }

    void JoinFlow(StackPanel area)
    {
        area.Children.Clear();
        area.Children.Add(new TextBlock { Text = L("1. Füge hier den Einladungscode ein, den du bekommen hast:"), FontWeight = FontWeight.SemiBold });
        var invitation = new TextBox { Watermark = "BS1-…", TextWrapping = TextWrapping.Wrap, MaxHeight = 110, FontSize = 11 };
        var next = new Button { Content = L("Weiter"), Classes = { "primary" }, HorizontalAlignment = HorizontalAlignment.Left };
        var result = new StackPanel { Spacing = 8 };
        next.Click += async (_, _) =>
        {
            result.Children.Clear();
            var info = new TextBlock { Text = L("Antwort wird erstellt … (kann einige Sekunden dauern)"), Classes = { "caption" }, TextWrapping = TextWrapping.Wrap };
            result.Children.Add(info);
            var peer = NewPeer();
            try
            {
                var code = await peer.AcceptInvitationAsync(invitation.Text ?? "");
                result.Children.Clear();
                result.Children.Add(new TextBlock { Text = L("2. Schicke diesen Antwortcode zurück:"), FontWeight = FontWeight.SemiBold });
                result.Children.Add(CodeBox(code));
                var waiting = new TextBlock { Text = L("Sobald die andere Person ihn eingefügt hat, seid ihr verbunden."), Classes = { "caption" }, TextWrapping = TextWrapping.Wrap };
                result.Children.Add(waiting);
                _ = WaitConnectedAsync(peer, waiting, 120);
            }
            catch (FormatException) { info.Text = L("Dieser Code passt nicht. Es muss der Einladungscode sein, der mit „BS1-“ beginnt."); }
            catch (Exception ex) { info.Text = L("Fehler: ") + ex.Message; }
        };
        area.Children.Add(invitation); area.Children.Add(next); area.Children.Add(result);
    }

    async Task WaitConnectedAsync(CollabPeer peer, TextBlock info, int seconds = 45)
    {
        try { await peer.WaitOpenAsync(TimeSpan.FromSeconds(seconds)); }
        catch
        {
            if (_peer == peer && !peer.IsOpen)
                info.Text = L("Die Verbindung kam nicht zustande. Häufige Gründe: ein VPN, eine strenge Firewall oder ein Firmennetz. Versucht es im Heimnetz oder ohne VPN noch einmal.");
        }
    }

    // ---- connected ------------------------------------------------------------------------------------------------------

    void BuildCollabConnected()
    {
        var head = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        head.Children.Add(new Border { Width = 10, Height = 10, CornerRadius = new(5), Background = new SolidColorBrush(Color.Parse("#30A46C")), VerticalAlignment = VerticalAlignment.Center });
        head.Children.Add(new TextBlock { Text = _peerName.Length > 0 ? L("Verbunden mit {0}", _peerName) : L("Verbunden"), FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        var leave = new Button { Content = L("Trennen"), Classes = { "tool" }, Padding = new(10, 0) };
        leave.Click += (_, _) => { _peer?.Dispose(); _peer = null; _follow = false; BuildCollabUi(); };
        var top = new DockPanel();
        DockPanel.SetDock(leave, Dock.Right);
        top.Children.Add(leave); top.Children.Add(head);
        CollabPanel.Children.Add(top);

        var send = new CheckBox { Content = L("Meine Bibelstelle mitsenden"), IsChecked = _settings.CollabSendLocation };
        ToolTip.SetTip(send, L("Die andere Person sieht, welches Kapitel du gerade liest."));
        send.IsCheckedChanged += (_, _) => { _settings.CollabSendLocation = send.IsChecked == true; SaveSettings(); if (_settings.CollabSendLocation) CollabSendLocation(_selected); };
        var follow = new CheckBox { Content = L("Ihrer/seiner Stelle folgen"), IsChecked = _follow };
        ToolTip.SetTip(follow, L("Dein Lesefenster springt mit, wenn die andere Person die Stelle wechselt."));
        follow.IsCheckedChanged += async (_, _) => { _follow = follow.IsChecked == true; if (_follow && _peerPlace is { } p) await FollowAsync(p); };
        CollabPanel.Children.Add(send); CollabPanel.Children.Add(follow);
        CollabPanel.Children.Add(_collabPlace);
        ShowPeerPlace();

        var actions = new WrapPanel();
        var verse = new Button { Content = L("Diesen Vers teilen"), Margin = new(0, 0, 8, 6) };
        verse.Click += (_, _) => ShareSelectedVerse();
        var doc = new Button { Content = L("Dokument teilen ▾"), Margin = new(0, 0, 8, 6) };
        doc.Click += (_, _) => ShowShareDocMenu(doc);
        actions.Children.Add(verse); actions.Children.Add(doc);
        CollabPanel.Children.Add(actions);

        var input = new TextBox { Watermark = L("Nachricht schreiben, Enter sendet"), MaxLength = 2000, AcceptsReturn = false };
        input.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter || string.IsNullOrWhiteSpace(input.Text)) return;
            var text = input.Text.Trim(); input.Text = "";
            CollabSend(new { t = "chat", text });
            AddCollabLog(text, mine: true);
        };
        CollabPanel.Children.Add(input);
        CollabPanel.Children.Add(_collabLog);
    }

    void AddCollabLog(string text, bool mine = false, bool system = false)
    {
        var bubble = new Border { Padding = new(10, 6), CornerRadius = new(10), HorizontalAlignment = mine ? HorizontalAlignment.Right : HorizontalAlignment.Left, MaxWidth = 340 };
        bubble.Bind(Border.BackgroundProperty, this.GetResourceObservable(mine ? "BsAccentSoft" : "BsSurface2"));
        bubble.Child = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Classes = { system ? "caption" : "" } };
        AddCollabItem(bubble);
    }

    void AddCollabItem(Control item)
    {
        _collabLog.Children.Insert(0, item);   // newest on top, so it is visible without scrolling
        while (_collabLog.Children.Count > 80) _collabLog.Children.RemoveAt(_collabLog.Children.Count - 1);
        AnimateIn(item, 0, -8);
    }

    // ---- sending ----------------------------------------------------------------------------------------------------------

    void CollabSend(object message)
    {
        if (_peer?.IsOpen != true) return;
        try { _peer.Send(JsonSerializer.Serialize(message)); } catch { /* the connection reports its end itself */ }
    }

    /// <summary>Called whenever the reader shows a chapter.</summary>
    void CollabSendLocation(int? verse)
    {
        if (_peer?.IsOpen != true || _collabSuppress || !_settings.CollabSendLocation) return;
        CollabSend(new { t = "nav", book = _curBook, chapter = _curChapter, verse });
    }

    void ShareSelectedVerse()
    {
        if (_selected is not int v) { StatusText.Text = L("Erst einen Vers anklicken."); return; }
        var r = new VerseRef(_curBook, _curChapter, v);
        var translation = (TranslationBox.SelectedItem as TranslationInfo)?.ShortName ?? "";
        // text of a privately imported translation (private use only) is never sent: only the place, the other person reads it in their own Bible
        var isPrivate = TranslationBox.SelectedItem is TranslationInfo { Id: var tid } && BibleStudy.Core.Import.LocalBibleStore.Owns(tid);
        CollabSend(new { t = "verse", book = r.Book, chapter = r.Chapter, verse = r.Verse, text = isPrivate ? "" : VerseTextOf(v).Trim(), translation = isPrivate ? "" : translation });
        AddCollabLog(L("Du hast {0} geteilt.", Label(r)), mine: true, system: true);
    }

    void ShowShareDocMenu(Control anchor)
    {
        var menu = new MenuFlyout();
        foreach (var d in _vault.GetDocuments().Where(x => x.Path is null).Take(40))
        {
            var doc = d; var item = new MenuItem { Header = doc.Title };
            item.Click += (_, _) =>
            {
                CollabSend(new { t = "doc", title = doc.Title, body = doc.Body.Length > MaxSharedBody ? doc.Body[..MaxSharedBody] : doc.Body });
                AddCollabLog(L("Du hast das Dokument „{0}“ als Kopie gesendet.", doc.Title), mine: true, system: true);
            };
            menu.Items.Add(item);
        }
        if (menu.Items.Count == 0) menu.Items.Add(new MenuItem { Header = L("Noch keine Dokumente"), IsEnabled = false });
        menu.ShowAt(anchor);
    }

    // ---- receiving (all of it untrusted) ---------------------------------------------------------------------------------

    static string Clip(string? s, int max) => s is null ? "" : s.Length > max ? s[..max] : s;

    void HandleCollab(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            string Str(string key, int max) => root.TryGetProperty(key, out var e) && e.ValueKind == JsonValueKind.String ? Clip(e.GetString(), max) : "";
            int Int(string key) => root.TryGetProperty(key, out var e) && e.ValueKind == JsonValueKind.Number && e.TryGetInt32(out var n) ? n : 0;
            switch (Str("t", 12))
            {
                case "hello":
                    _peerName = Str("name", 40).Trim();
                    AddCollabLog(L("{0} ist verbunden.", _peerName.Length > 0 ? _peerName : L("Die andere Person")), system: true);
                    if (CollabPanel.Children.Count > 0) BuildCollabUi();
                    CollabSendLocation(_selected);
                    break;
                case "chat":
                    if (Str("text", 2000) is { Length: > 0 } text) AddCollabLog(text);
                    break;
                case "nav":
                {
                    var book = Str("book", 8); var chapter = Int("chapter");
                    if (_books.All(b => b.Id != book) || chapter < 1 || chapter > 200) break;
                    int? verse = Int("verse") is > 0 and var vv ? vv : null;
                    _peerPlace = (book, chapter, verse);
                    ShowPeerPlace();
                    if (_follow) _ = FollowAsync(_peerPlace.Value);
                    break;
                }
                case "verse":
                    ShowSharedVerse(Str("book", 8), Int("chapter"), Int("verse"), Str("text", 4000), Str("translation", 20));
                    break;
                case "doc":
                    ShowSharedDocument(Str("title", 200), Str("body", MaxSharedBody));
                    break;
            }
        }
        catch { /* a malformed message is ignored */ }
    }

    void ShowPeerPlace()
    {
        if (_peerPlace is not { } p) { _collabPlace.Text = ""; return; }
        _collabPlace.Text = L("{0} liest: {1}", _peerName.Length > 0 ? _peerName : L("Die andere Person"), $"{BookName(p.Book)} {p.Chapter}" + (p.Verse is int pv ? "," + pv : ""));
    }

    async Task FollowAsync((string Book, int Chapter, int? Verse) p)
    {
        _collabSuppress = true;
        try { await GoToAsync(p.Book, p.Chapter, p.Verse); }
        finally { _collabSuppress = false; }
    }

    void ShowSharedVerse(string book, int chapter, int verse, string text, string translation)
    {
        if (_books.All(b => b.Id != book) || chapter < 1 || verse < 1) return;
        var r = new VerseRef(book, chapter, verse);
        var go = new Button { Content = L("Hingehen"), Classes = { "tool" }, Padding = new(10, 0) };
        go.Click += async (_, _) => await GoToAsync(book, chapter, verse);
        var keep = new Button { Content = L("Merken"), Classes = { "tool" }, Padding = new(10, 0) };
        keep.Click += (_, _) =>
        {
            _vault.AddMark(Mark.AnyTranslation, r, -1, -1, text, _settings.DefaultColor, L("Geteilt von {0}", _peerName.Length > 0 ? _peerName : L("einer anderen Person")), null, AutoTags(r));
            keep.IsEnabled = false; keep.Content = L("Gemerkt ✓");
            LoadChapterMarks(); Paint(); RefreshCollection();
        };
        var card = Card(new StackPanel
        {
            Spacing = 6,
            Children =
            {
                new TextBlock { Text = $"{(_peerName.Length > 0 ? _peerName : L("Die andere Person"))} · {Label(r)}{(translation.Length > 0 ? " (" + translation + ")" : "")}", FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap },
                new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontFamily = ScriptureFont, IsVisible = text.Length > 0 },
                new WrapPanel { Children = { go, keep } },
            },
        });
        AddCollabItem(card);
    }

    void ShowSharedDocument(string title, string body)
    {
        if (title.Trim().Length == 0) return;
        var save = new Button { Content = L("Als Kopie speichern"), Classes = { "tool" }, Padding = new(10, 0), HorizontalAlignment = HorizontalAlignment.Left };
        save.Click += (_, _) =>
        {
            // always a new document, tagged with its origin; nothing of the existing documents is touched
            _vault.AddDocument(title.Trim(), body, null, false, [L("geteilt")]);
            save.IsEnabled = false; save.Content = L("Gespeichert ✓");
            RefreshDocs();
        };
        var card = Card(new StackPanel
        {
            Spacing = 6,
            Children =
            {
                new TextBlock { Text = L("{0} sendet ein Dokument: „{1}“", _peerName.Length > 0 ? _peerName : L("Die andere Person"), title), FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap },
                new TextBlock { Text = Shorten(body.ReplaceLineEndings(" ").Trim(), 240), Classes = { "caption" }, TextWrapping = TextWrapping.Wrap },
                save,
            },
        });
        AddCollabItem(card);
    }
}
