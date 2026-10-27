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
    string? _aiSession;
    CancellationTokenSource? _aiCts;
    bool _aiBusy, _aiStreamed;
    SelectableTextBlock? _aiCurrent;

    static string McpExe => Path.Combine(AppContext.BaseDirectory, "mcp", OperatingSystem.IsWindows() ? "BibleStudy.Mcp.exe" : "BibleStudy.Mcp");

    string AiSystemPrompt() =>
        "Du bist der Assistent in der Bibelstudien-Software BibleStudy. Du hast Werkzeuge (mcp__biblestudy__*), um Bibeltexte zu lesen und zu durchsuchen, " +
        "Querverweise und Strong-Nummern nachzuschlagen und die App zu steuern (anzeigen, markieren, Notizen, Warteschlange, To-Dos). " +
        "Nutze die Werkzeuge, statt Verse aus dem Gedächtnis zu zitieren, und zitiere nur, was die Werkzeuge liefern. Prüfe Aussagen am Text. " +
        "Wenn der Nutzer sich auf \"diesen Vers\" oder \"hier\" bezieht, frage get_app_state ab. " +
        "Wenn du in der App etwas änderst (markieren, Notiz, Warteschlange), sage kurz, was du getan hast. Antworte auf Deutsch, klar und knapp. " +
        "Hintergrund des Nutzers: " + _settings.AiContext;

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
        AiNewBtn.Click += (_, _) => { _aiCts?.Cancel(); _aiSession = null; AiMessages.Children.Clear(); AiStatus.Text = ""; ShowAiHint(); };

        AiContextBox.Text = _settings.AiContext;
        AiContextBox.LostFocus += (_, _) => { _settings.AiContext = AiContextBox.Text ?? ""; SaveSettings(); };
        AiModelBox.Text = _settings.AiModel;
        AiModelBox.LostFocus += (_, _) => { _settings.AiModel = AiModelBox.Text?.Trim() ?? ""; SaveSettings(); };

        var exe = ClaudeCli.FindExecutable();
        AiCliInfo.Text = exe is null ? "Claude Code wurde nicht gefunden. Installiere es (claude.com/claude-code) und melde dich einmal an." : $"Claude Code gefunden: {exe}";
        McpCommandBox.Text = $"claude mcp add biblestudy -- \"{McpExe}\"";
        McpCopyBtn.Click += async (_, _) =>
        {
            if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard) await clipboard.SetTextAsync(McpCommandBox.Text);
        };
        ShowAiHint();
    }

    void ShowAiHint()
    {
        var hint = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap, FontSize = 13,
            Text = "Beispiele:\n• Was sagt Johannes 1:1 im Grundtext, und wie wird λόγος sonst übersetzt?\n• Markiere in Johannes 3:16 das Wort „Liebe“ gelb und lege einen Ordner „Heil“ an.\n• Suche alle Verse zum Thema Sabbat und setze sie in die Warteschlange.",
        };
        hint.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("BsMuted"));
        AiMessages.Children.Add(hint);
    }

    SelectableTextBlock AddAssistantText()
    {
        var tb = new SelectableTextBlock { TextWrapping = TextWrapping.Wrap };
        AiMessages.Children.Add(tb);
        return tb;
    }

    void AddUserMessage(string text)
    {
        var bubble = new Border
        {
            Padding = new(12, 8), CornerRadius = new(12), Margin = new(28, 0, 0, 0), HorizontalAlignment = HorizontalAlignment.Right,
            Child = new SelectableTextBlock { Text = text, TextWrapping = TextWrapping.Wrap },
        };
        bubble.Bind(Border.BackgroundProperty, this.GetResourceObservable("BsAccentSoft"));
        AiMessages.Children.Add(bubble);
    }

    void AddToolNote(string tool)
    {
        var note = new TextBlock { Text = "› " + tool.Replace("mcp__biblestudy__", ""), FontSize = 12 };
        note.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("BsMuted"));
        AiMessages.Children.Add(note);
    }

    async Task AiSendOrStopAsync()
    {
        if (_aiBusy) { _aiCts?.Cancel(); return; }
        var text = AiInput.Text?.Trim();
        if (string.IsNullOrEmpty(text)) return;
        var exe = ClaudeCli.FindExecutable();
        if (exe is null) { AiStatus.Text = "Claude Code wurde nicht gefunden (siehe Einstellungen)."; return; }
        if (!File.Exists(McpExe)) { AiStatus.Text = "Der MCP-Server fehlt: " + McpExe; return; }
        if (_control is null || _control.Port == 0) { AiStatus.Text = "Die lokale Steuer-Schnittstelle läuft nicht."; return; }

        if (AiMessages.Children.Count == 1 && AiMessages.Children[0] is TextBlock) AiMessages.Children.Clear(); // remove the hint
        AiInput.Text = "";
        AddUserMessage(text);
        _aiBusy = true; _aiStreamed = false; _aiCurrent = null;
        AiSendBtn.Content = "Stopp";
        AiStatus.Text = "Claude arbeitet …";
        _aiCts = new CancellationTokenSource();
        var workDir = Path.Combine(_root, "ai");
        try
        {
            await ClaudeCli.RunAsync(exe, workDir, text, _aiSession, AiSystemPrompt(), ClaudeCli.WriteMcpConfig(workDir, McpExe),
                string.IsNullOrWhiteSpace(_settings.AiModel) ? null : _settings.AiModel,
                ev => Dispatcher.UIThread.Post(() => OnClaudeEvent(ev)), _aiCts.Token);
        }
        catch (OperationCanceledException) { AiStatus.Text = "Abgebrochen."; }
        catch (Exception ex) { AiStatus.Text = "Fehler: " + ex.Message; }
        finally { _aiBusy = false; AiSendBtn.Content = "Senden"; }
    }

    void OnClaudeEvent(ClaudeEvent ev)
    {
        switch (ev)
        {
            case ClaudeTextDelta d:
                _aiCurrent ??= AddAssistantText();
                _aiCurrent.Text += d.Text;
                _aiStreamed = true;
                break;
            case ClaudeToolUse t:
                _aiCurrent = null;
                AddToolNote(t.Name);
                AiStatus.Text = "Claude nutzt: " + t.Name.Replace("mcp__biblestudy__", "");
                break;
            case ClaudeResult r:
                _aiSession = r.SessionId ?? _aiSession;
                if (!_aiStreamed && r.Text.Length > 0) AddAssistantText().Text = r.Text;
                AiStatus.Text = r.IsError ? "Fehler: " + r.Text : "";
                _aiCurrent = null;
                break;
        }
        AiScroll.ScrollToEnd();
    }
}
