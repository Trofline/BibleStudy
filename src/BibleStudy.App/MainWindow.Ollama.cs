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
        OllamaClient.AutoStart = _settings.OllamaAutoStart;
        OllamaAutoStartCheck.IsChecked = _settings.OllamaAutoStart;
        OllamaTranslateCheck.IsChecked = _settings.OllamaTranslate;
        OllamaSizeBox.SelectedIndex = _settings.OllamaTranslateSize == "small" ? 0 : 1;
        OllamaSizeBox.IsEnabled = _settings.OllamaTranslate;
        SyncMeaningEngine();
        OllamaAutoStartCheck.IsCheckedChanged += (_, _) =>
        {
            _settings.OllamaAutoStart = OllamaAutoStartCheck.IsChecked == true; OllamaClient.AutoStart = _settings.OllamaAutoStart; SaveSettings();
            if (!_settings.OllamaAutoStart) OllamaClient.StopStarted();
        };
        OllamaTranslateCheck.IsCheckedChanged += (_, _) =>
        {
            _settings.OllamaTranslate = OllamaTranslateCheck.IsChecked == true; OllamaSizeBox.IsEnabled = _settings.OllamaTranslate; SaveSettings(); SyncMeaningEngine();
            if (_settings.OllamaTranslate) _ = CheckTranslateModelAsync();
        };
        OllamaSizeBox.SelectionChanged += (_, _) =>
        {
            _settings.OllamaTranslateSize = OllamaSizeBox.SelectedIndex == 0 ? "small" : "large"; SaveSettings(); SyncMeaningEngine();
            if (_settings.OllamaTranslate) _ = CheckTranslateModelAsync();
        };
        AiEngineBox.SelectionChanged += (_, _) =>
        {
            _settings.AiEngine = AiEngineBox.SelectedIndex == 1 ? "ollama" : "claude"; SaveSettings(); SyncMeaningEngine();
            OllamaModelBox.IsVisible = OllamaCheckBtn.IsVisible = OllamaInfo.IsVisible = _settings.AiEngine == "ollama";
            if (_settings.AiEngine == "ollama") _ = CheckOllamaAsync();
        };
        OllamaModelBox.LostFocus += (_, _) => { _settings.OllamaModel = OllamaModelBox.Text?.Trim() ?? ""; SaveSettings(); SyncMeaningEngine(); };
        OllamaCheckBtn.Click += async (_, _) => await CheckOllamaAsync();
    }

    string TranslateModel => _settings.OllamaTranslateSize == "small" ? OllamaClient.SmallModel : OllamaClient.LargeModel;

    /// <summary>When the user chose it, the word meanings are translated by Ollama (offline); otherwise by Claude Code.</summary>
    void SyncMeaningEngine()
    {
        _ollama ??= new OllamaClient(_http);
        _meanings.Ollama = _settings.OllamaTranslate ? _ollama : null;
        _meanings.OllamaModel = _settings.OllamaTranslate ? TranslateModel : null;
    }

    /// <summary>Tells the user when the model for translating is not loaded yet.</summary>
    async Task CheckTranslateModelAsync()
    {
        _ollama ??= new OllamaClient(_http);
        OllamaTranslateInfo.Text = L("Suche Ollama …");
        var models = await _ollama.ModelsAsync();
        OllamaTranslateInfo.Text = models.Contains(TranslateModel) ? L("Das Modell „{0}“ ist bereit.", TranslateModel)
            : L("Das Modell „{0}“ ist noch nicht geladen. Lade es im Download-Manager unter „Übersetzen ohne Internet“.", TranslateModel);
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
