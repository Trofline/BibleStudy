using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace BibleStudy.App;

/// <summary>Chat panel that talks to the user's local Claude Code (no API key) with the Bible tools of this app.</summary>
public partial class MainWindow
{
    /// <summary>One conversation. Several can be open and even answer at the same time; the panel shows the active one.</summary>
    sealed class AiChat
    {
        public string Title = "";
        public string? Session;
        public CancellationTokenSource? Cts;
        public bool Busy, Streamed, HasHint;
        public AiAnswer? Current;
        public string Status = "";
        public string Draft = "";
        public readonly StackPanel Messages = new() { Spacing = 10, Margin = new(0, 0, 8, 0) };
        public readonly List<BibleStudy.Core.OllamaMessage> History = [];
    }

    readonly List<AiChat> _chats = [];
    AiChat _chat = new();
    int _chatCounter;

    static string McpExe => Path.Combine(AppContext.BaseDirectory, "mcp", OperatingSystem.IsWindows() ? "BibleStudy.Mcp.exe" : "BibleStudy.Mcp");

    string AiSystemPrompt() =>
        L("Du bist der Assistent in der Bibelstudien-Software BibleStudy. Du hast Werkzeuge (mcp__biblestudy__*), um Bibeltexte zu lesen und zu durchsuchen, ") +
        L("Querverweise und Strong-Nummern nachzuschlagen und die App zu steuern (anzeigen, markieren, Notizen, Warteschlange, To-dos). ") +
        L("Nutze die Werkzeuge, statt Verse aus dem Gedächtnis zu zitieren, und zitiere nur, was die Werkzeuge liefern. Prüfe Aussagen am Text. ") +
        L("Wenn der Nutzer sich auf \"diesen Vers\" oder \"hier\" bezieht, frage get_app_state ab. ") +
        L("Für Zahlen und Aufbau hast du Wortstatistik, Satzdiagramm, Perikopen-Analyse und den Lese-Verlauf; mit show_tab, open_workspace und show_word_chart zeigst du dem Nutzer die passende Ansicht in der App. ") +
        L("Wenn du in der App etwas änderst (markieren, Notiz, Warteschlange), sage kurz, was du getan hast. Antworte auf Deutsch, klar und knapp. ") +
        L("Hintergrund des Nutzers: ") + _settings.AiContext;

    void InitAi()
    {
        AiSendBtn.Click += async (_, _) => await AiSendOrStopAsync();
        // Tunnel: the multi-line TextBox would otherwise consume Enter as a line break before we see it.
        AiInput.AddHandler(KeyDownEvent, async (object? _, KeyEventArgs e) =>
        {
            if (e.Key != Key.Enter || e.KeyModifiers.HasFlag(KeyModifiers.Shift)) return;
            e.Handled = true;
            await AiSendOrStopAsync();
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        AiNewBtn.Click += (_, _) => NewChat();
        InitAiSave();

        AiContextBox.Text = _settings.AiContext;
        AiContextBox.LostFocus += (_, _) => { _settings.AiContext = AiContextBox.Text ?? ""; SaveSettings(); };
        AiContextResetBtn.Click += (_, _) => { AiContextBox.Text = _settings.AiContext = BibleStudy.Core.Storage.AppSettings.DefaultAiContext; SaveSettings(); };
        AiContextNeutralBtn.Click += (_, _) => { AiContextBox.Text = _settings.AiContext = BibleStudy.Core.Storage.AppSettings.NeutralAiContext; SaveSettings(); };
        AiModelBox.Text = _settings.AiModel;
        AiModelBox.LostFocus += (_, _) => { _settings.AiModel = AiModelBox.Text?.Trim() ?? ""; SaveSettings(); };

        var exe = ClaudeCli.FindExecutable();
        AiCliInfo.Text = exe is null ? L("Claude Code wurde nicht gefunden. Installiere es (claude.com/claude-code) und melde dich einmal an.") : L("Claude Code gefunden: {0}", exe);
        McpCommandBox.Text = L("claude mcp add biblestudy -- \"{0}\"", McpExe);
        McpCopyBtn.Click += async (_, _) =>
        {
            if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard) await clipboard.SetTextAsync(McpCommandBox.Text);
        };
        NewChat();
    }

    void NewChat()
    {
        var chat = new AiChat { Title = L("Gespräch {0}", ++_chatCounter) };
        _chats.Add(chat);
        ShowAiHint(chat);
        SwitchChat(chat);
    }

    void SwitchChat(AiChat chat)
    {
        _chat.Draft = AiInput.Text ?? "";
        _chat = chat;
        AiScroll.Content = chat.Messages;
        AiInput.Text = chat.Draft;
        AiStatus.Text = chat.Status;
        AiSendBtn.Content = chat.Busy ? L("Stopp") : L("Senden");
        RebuildChatBar();
        AiScroll.ScrollToEnd();
    }

    void CloseChat(AiChat chat)
    {
        chat.Cts?.Cancel();
        _chats.Remove(chat);
        // closing the last conversation leaves a fresh empty one; numbering starts over, otherwise every ✕ on it counted one up
        if (_chats.Count == 0) { _chatCounter = 0; NewChat(); return; }
        if (chat == _chat) SwitchChat(_chats[^1]); else RebuildChatBar();
    }

    void RebuildChatBar()
    {
        AiChatBar.Children.Clear();
        foreach (var chat in _chats)
        {
            var c = chat;
            var title = new Button { Classes = { "seg" }, Content = (c.Busy ? "● " : "") + c.Title, Padding = new(10, 4), FontSize = 12 };
            title.Classes.Set("active", c == _chat);
            title.Click += (_, _) => SwitchChat(c);
            var close = new Button { Content = "✕", Classes = { "tool" }, Width = 22, Height = 22, FontSize = 10 };
            ToolTip.SetTip(close, L("Gespräch schließen"));
            close.Click += (_, _) => CloseChat(c);
            AiChatBar.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Margin = new(0, 0, 6, 4), Children = { title, close } });
        }
    }

    void ShowAiHint(AiChat chat)
    {
        chat.HasHint = true;
        // the examples are one text ("Beispiele:" and one line per example); each example becomes a card that asks it at once
        var lines = L("Beispiele:\n• Was sagt Johannes 1:1 im Grundtext, und wie wird λόγος sonst übersetzt?\n• Markiere in Johannes 3:16 das Wort „Liebe“ gelb und lege einen Ordner „Heil“ an.\n• Suche alle Verse zum Thema Sabbat und setze sie in die Warteschlange.")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var head = new TextBlock { Text = lines[0].TrimEnd(':').ToUpperInvariant(), Classes = { "eyebrow" }, Margin = new(0, 4, 0, 2) };
        chat.Messages.Children.Add(head);
        foreach (var example in lines.Skip(1).Select(l => l.TrimStart('•', ' ')))
        {
            var card = new Button
            {
                Classes = { "aiexample" }, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left,
                Content = new TextBlock { Text = example, TextWrapping = TextWrapping.Wrap, FontSize = 13 },
            };
            ToolTip.SetTip(card, L("Senden"));
            card.Click += async (_, _) => { if (_chat.Busy) return; AiInput.Text = example; await AiSendOrStopAsync(); };
            chat.Messages.Children.Add(card);
        }
    }

    /// <summary>The answer that was streaming is complete: show it formatted, and the next text starts a new block.</summary>
    void FinishAnswer(AiChat chat)
    {
        chat.Current?.Render();
        chat.Current = null;
    }

    AiAnswer AddAssistantText(AiChat chat)
    {
        var tb = new AiAnswer { TextWrapping = TextWrapping.Wrap, LineHeight = 21 };
        tb.ContextMenu = ChatMenu(chat, tb);
        chat.Messages.Children.Add(tb);
        return tb;
    }

    void AddUserMessage(AiChat chat, string text)
    {
        var userText = new SelectableTextBlock { Text = text, TextWrapping = TextWrapping.Wrap };
        userText.ContextMenu = ChatMenu(chat, userText);
        var bubble = new Border
        {
            Padding = new(12, 8), CornerRadius = new(12), Margin = new(28, 0, 0, 0), HorizontalAlignment = HorizontalAlignment.Right,
            Child = userText,
        };
        bubble.Bind(Border.BackgroundProperty, this.GetResourceObservable("BsAccentSoft"));
        chat.Messages.Children.Add(bubble);
    }

    void AddToolNote(AiChat chat, string tool)
    {
        var text = new TextBlock { Text = "⚙  " + tool.Replace("mcp__biblestudy__", "").Replace('_', ' '), FontSize = 11.5 };
        text.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("BsMuted"));
        var chip = new Border { Child = text, Padding = new(8, 2), CornerRadius = new(10), HorizontalAlignment = HorizontalAlignment.Left, BorderThickness = new(1) };
        chip.Bind(Border.BorderBrushProperty, this.GetResourceObservable("BsBorder"));
        chat.Messages.Children.Add(chip);
    }

    async Task AiSendOrStopAsync()
    {
        var chat = _chat;
        if (chat.Busy) { chat.Cts?.Cancel(); return; }
        var text = AiInput.Text?.Trim();
        if (_settings.AiEngine == "ollama" && !string.IsNullOrEmpty(text)) { await AiSendOllamaAsync(chat, text); return; }
        if (string.IsNullOrEmpty(text)) return;
        var exe = ClaudeCli.FindExecutable();
        if (exe is null) { AiStatus.Text = L("Claude Code wurde nicht gefunden (siehe Einstellungen)."); return; }
        if (!File.Exists(McpExe)) { AiStatus.Text = L("Der MCP-Server fehlt: ") + McpExe; return; }
        if (_control is null || _control.Port == 0) { AiStatus.Text = L("Die lokale Steuer-Schnittstelle läuft nicht."); return; }

        if (chat.HasHint) { chat.Messages.Children.Clear(); chat.HasHint = false; }   // remove the hint
        if (chat.Messages.Children.Count == 0) chat.Title = text.Length > 22 ? text[..22] + "…" : text;
        AiInput.Text = ""; chat.Draft = "";
        AddUserMessage(chat, text);
        chat.Busy = true; chat.Streamed = false; chat.Current = null;
        SetChatStatus(chat, L("Claude arbeitet …"));
        AiSendBtn.Content = L("Stopp");
        RebuildChatBar();
        chat.Cts = new CancellationTokenSource();
        // every conversation gets its own working folder, so two answers at the same time never share files
        var workDir = Path.Combine(_root, "ai", chat.GetHashCode().ToString("x"));
        try
        {
            await ClaudeCli.RunAsync(exe, workDir, text, chat.Session, AiSystemPrompt(), ClaudeCli.WriteMcpConfig(workDir, McpExe),
                string.IsNullOrWhiteSpace(_settings.AiModel) ? null : _settings.AiModel,
                ev => Dispatcher.UIThread.Post(() => OnClaudeEvent(chat, ev)), chat.Cts.Token);
        }
        catch (OperationCanceledException) { SetChatStatus(chat, L("Abgebrochen.")); }
        catch (Exception ex) { SetChatStatus(chat, L("Fehler: ") + ex.Message); }
        finally
        {
            chat.Busy = false;
            FinishAnswer(chat);
            if (chat == _chat) AiSendBtn.Content = L("Senden");
            RebuildChatBar();
        }
    }

    void SetChatStatus(AiChat chat, string text)
    {
        chat.Status = text;
        if (chat == _chat) AiStatus.Text = text;
    }

    void OnClaudeEvent(AiChat chat, ClaudeEvent ev)
    {
        switch (ev)
        {
            case ClaudeTextDelta d:
                chat.Current ??= AddAssistantText(chat);
                chat.Current.Append(d.Text);
                chat.Streamed = true;
                break;
            case ClaudeToolUse t:
                FinishAnswer(chat);
                AddToolNote(chat, t.Name);
                SetChatStatus(chat, L("Claude nutzt: ") + t.Name.Replace("mcp__biblestudy__", ""));
                break;
            case ClaudeResult r:
                chat.Session = r.SessionId ?? chat.Session;
                // an error is shown in the status line only, not a second time as an answer
                if (!chat.Streamed && r.Text.Length > 0 && !r.IsError) { chat.Current = AddAssistantText(chat); chat.Current.SetText(r.Text); }
                SetChatStatus(chat, r.IsError ? L("Fehler: ") + r.Text : "");
                FinishAnswer(chat);
                break;
        }
        if (chat == _chat) AiScroll.ScrollToEnd();
    }}
