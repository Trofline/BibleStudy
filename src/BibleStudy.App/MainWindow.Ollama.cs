using Avalonia.Controls;
using Avalonia.Threading;
using BibleStudy.Core;
using BibleStudy.Core.Models;

namespace BibleStudy.App;

/// <summary>The AI tab with a local model (Ollama) instead of Claude Code: private, free, works offline; without the Bible tools.</summary>
public partial class MainWindow
{
    OllamaClient? _ollama;

    void InitOllama()
    {
        AiEngineBox.SelectedIndex = _settings.AiEngine == "ollama" ? 1 : 0;
        OllamaModelBox.Text = _settings.OllamaModel;
        OllamaModelBox.IsVisible = OllamaCheckBtn.IsVisible = OllamaInfo.IsVisible = _settings.AiEngine == "ollama";
        SyncMeaningEngine();
        AiEngineBox.SelectionChanged += (_, _) =>
        {
            _settings.AiEngine = AiEngineBox.SelectedIndex == 1 ? "ollama" : "claude"; SaveSettings(); SyncMeaningEngine();
            OllamaModelBox.IsVisible = OllamaCheckBtn.IsVisible = OllamaInfo.IsVisible = _settings.AiEngine == "ollama";
            if (_settings.AiEngine == "ollama") _ = CheckOllamaAsync();
        };
        OllamaModelBox.LostFocus += (_, _) => { _settings.OllamaModel = OllamaModelBox.Text?.Trim() ?? ""; SaveSettings(); SyncMeaningEngine(); };
        OllamaCheckBtn.Click += async (_, _) => await CheckOllamaAsync();
    }

    /// <summary>With the local AI chosen, the word meanings are translated by it too (offline); otherwise by Claude Code.</summary>
    void SyncMeaningEngine()
    {
        var local = _settings.AiEngine == "ollama" && !string.IsNullOrWhiteSpace(_settings.OllamaModel);
        _ollama ??= new OllamaClient(_http);
        _meanings.Ollama = local ? _ollama : null;
        _meanings.OllamaModel = local ? _settings.OllamaModel : null;
    }

    /// <summary>Looks for Ollama on this computer; if no model is chosen yet, the first installed one is taken.</summary>
    async Task CheckOllamaAsync()
    {
        _ollama ??= new OllamaClient(_http);
        OllamaInfo.Text = L("Suche Ollama …");
        var models = await _ollama.ModelsAsync();
        if (models.Count == 0) { OllamaInfo.Text = L("Ollama antwortet nicht oder hat noch kein Modell. Installiere es von ollama.com, starte es und lade ein Modell, z. B. mit „ollama pull llama3.1“."); return; }
        if (string.IsNullOrWhiteSpace(_settings.OllamaModel) || !models.Contains(_settings.OllamaModel)) { _settings.OllamaModel = models[0]; OllamaModelBox.Text = models[0]; SaveSettings(); SyncMeaningEngine(); }
        OllamaInfo.Text = L("Ollama läuft. Installierte Modelle: {0}", string.Join(", ", models));
    }

    string OllamaSystemPrompt()
    {
        var prompt = L("Du bist der Assistent in der Bibelstudien-Software BibleStudy und läufst lokal auf dem Computer des Nutzers. Du hast hier keine Werkzeuge: zitiere keine Bibelverse aus dem Gedächtnis als wörtlichen Text, sondern nutze den mitgegebenen Vers und weise darauf hin, wenn du etwas nicht sicher weißt. Antworte klar und knapp in der Sprache des Nutzers. ") + L("Hintergrund des Nutzers: ") + _settings.AiContext;
        if (_selected is int v && VerseTextOf(v).Trim() is { Length: > 0 } verse)
            prompt += "\n\n" + L("Angeklickter Vers: {0} ({1}): {2}", Label(new VerseRef(_curBook, _curChapter, v)), (TranslationBox.SelectedItem as TranslationInfo)?.ShortName ?? "", verse);
        return prompt;
    }

    async Task AiSendOllamaAsync(AiChat chat, string text)
    {
        _ollama ??= new OllamaClient(_http);
        if (string.IsNullOrWhiteSpace(_settings.OllamaModel)) { await CheckOllamaAsync(); if (string.IsNullOrWhiteSpace(_settings.OllamaModel)) { AiStatus.Text = OllamaInfo.Text ?? ""; return; } }
        if (chat.HasHint) { chat.Messages.Children.Clear(); chat.HasHint = false; }
        if (chat.Messages.Children.Count == 0) chat.Title = text.Length > 22 ? text[..22] + "…" : text;
        AiInput.Text = ""; chat.Draft = "";
        AddUserMessage(chat, text);
        chat.History.Add(new OllamaMessage("user", text));
        chat.Busy = true; chat.Streamed = false; chat.Current = null;
        SetChatStatus(chat, L("Das lokale Modell antwortet …"));
        AiSendBtn.Content = L("Stopp");
        RebuildChatBar();
        chat.Cts = new CancellationTokenSource();
        try
        {
            var messages = new[] { new OllamaMessage("system", OllamaSystemPrompt()) }.Concat(chat.History).ToList();
            var answer = await _ollama.ChatAsync(_settings.OllamaModel, messages, piece => Dispatcher.UIThread.Post(() =>
            {
                chat.Current ??= AddAssistantText(chat);
                chat.Current.Text += piece; chat.Streamed = true;
                if (chat == _chat) AiScroll.ScrollToEnd();
            }), chat.Cts.Token);
            if (answer.Length > 0) chat.History.Add(new OllamaMessage("assistant", answer));
            SetChatStatus(chat, "");
        }
        catch (OperationCanceledException) { SetChatStatus(chat, L("Abgebrochen.")); }
        catch (Exception ex) { SetChatStatus(chat, L("Fehler: ") + ex.Message); }
        finally
        {
            chat.Busy = false; chat.Current = null;
            if (chat == _chat) AiSendBtn.Content = L("Senden");
            RebuildChatBar();
        }
    }
}
