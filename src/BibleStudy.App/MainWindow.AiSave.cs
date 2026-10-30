using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;

namespace BibleStudy.App;

/// <summary>Saving a conversation with the AI, a single answer or a selected piece as a document in the vault – as proof of what the AI said.</summary>
public partial class MainWindow
{
    void InitAiSave() => AiSaveBtn.Click += (_, _) => SaveChatNote(_chat, 0, null, L("KI-Gespräch: {0}", _chat.Title));

    /// <summary>The conversation as (who, text) pairs; tool notes and hints are left out.</summary>
    static List<(bool User, string Text)> Transcript(AiChat chat, int fromIndex)
    {
        var parts = new List<(bool, string)>();
        for (var i = Math.Max(0, fromIndex); i < chat.Messages.Children.Count; i++)
        {
            switch (chat.Messages.Children[i])
            {
                case Border { Child: SelectableTextBlock u } when !string.IsNullOrWhiteSpace(u.Text): parts.Add((true, u.Text!.Trim())); break;
                case SelectableTextBlock a when !string.IsNullOrWhiteSpace(a.Text):
                    if (parts.Count > 0 && !parts[^1].Item1) parts[^1] = (false, parts[^1].Item2 + "\n\n" + a.Text!.Trim()); else parts.Add((false, a.Text!.Trim()));
                    break;
            }
        }
        return parts;
    }

    /// <summary>
    /// Saves a part of the conversation: from message <paramref name="fromIndex"/> on, or only <paramref name="excerpt"/> if given.
    /// The document starts with a short proof block (date, source, place) and the warning that an AI can be wrong.
    /// </summary>
    void SaveChatNote(AiChat chat, int fromIndex, string? excerpt, string title)
    {
        var parts = Transcript(chat, fromIndex);
        if (excerpt is null && parts.Count == 0) { StatusText.Text = L("Es gibt noch nichts zu speichern."); return; }
        var model = string.IsNullOrWhiteSpace(_settings.AiModel) ? L("Standardmodell") : _settings.AiModel;
        var place = _selected is int v ? Label(new BibleStudy.Core.Models.VerseRef(_curBook, _curChapter, v)) : _curBook == "" ? "" : $"{BookName(_curBook)} {_curChapter}";
        var body = new StringBuilder();
        body.AppendLine(L("> **Beleg: von einer KI.** Gespeichert am {0} · Quelle: Claude (KI), {1}{2}", DateTime.Now.ToString("g"), model, place.Length > 0 ? " · " + L("Stelle im Reader: {0}", place) : ""));
        body.AppendLine("> " + L("⚠ Eine KI kann Fehler machen. Prüfe Aussagen am Bibeltext."));
        body.AppendLine();
        if (excerpt is not null)
        {
            body.AppendLine("**" + L("Auszug aus der Antwort der KI:") + "**");
            body.AppendLine();
            foreach (var line in excerpt.Trim().ReplaceLineEndings("\n").Split('\n')) body.AppendLine("> " + line);
        }
        else
            foreach (var (user, text) in parts)
            {
                body.AppendLine("**" + (user ? L("Du:") : L("KI:")) + "**");
                body.AppendLine();
                body.AppendLine(text);
                body.AppendLine();
            }
        title = title.Trim();
        if (_vault.FindDocument(title) is not null) title += " · " + DateTime.Now.ToString("g");
        _vault.AddDocument(title, body.ToString().TrimEnd(), null, false, [L("KI-Gespräch")]);
        RefreshCollection(); RefreshDocs();
        StatusText.Text = L("Gespeichert als Dokument „{0}“ (unter Dokumente).", title);
    }

    /// <summary>Right-click menu of a message: selected text, this answer, the whole conversation.</summary>
    ContextMenu ChatMenu(AiChat chat, SelectableTextBlock block)
    {
        var menu = new ContextMenu();
        var sel = new MenuItem { Header = L("Markierten Text als Notiz speichern") };
        sel.Click += (_, _) =>
        {
            var text = block.SelectedText;
            if (string.IsNullOrWhiteSpace(text)) { StatusText.Text = L("Markiere zuerst Text in der Nachricht."); return; }
            SaveChatNote(chat, 0, text, L("KI-Auszug: {0}", Shorten(text.ReplaceLineEndings(" "), 40)));
        };
        var answer = new MenuItem { Header = L("Diese Antwort (mit der Frage) als Notiz speichern") };
        answer.Click += (_, _) =>
        {
            var children = chat.Messages.Children;
            var at = children.IndexOf(block);
            // back to the question that belongs to this answer
            var start = at;
            while (start > 0 && !(children[start] is Border)) start--;
            var end = at + 1;
            while (end < children.Count && !(children[end] is Border)) end++;
            var slice = new AiChat();
            for (var i = start; i < end && i < children.Count; i++) slice.Messages.Children.Add(CloneForTranscript(children[i]));
            SaveChatNote(slice, 0, null, L("KI-Antwort: {0}", Shorten(Transcript(slice, 0).FirstOrDefault(p => p.User).Text ?? block.Text ?? "", 40).ReplaceLineEndings(" ")));
        };
        var whole = new MenuItem { Header = L("Ganzes Gespräch als Notiz speichern") };
        whole.Click += (_, _) => SaveChatNote(chat, 0, null, L("KI-Gespräch: {0}", chat.Title));
        var copy = new MenuItem { Header = L("Kopieren") };
        copy.Click += async (_, _) => { if (TopLevel.GetTopLevel(this)?.Clipboard is { } c && !string.IsNullOrEmpty(block.SelectedText)) await c.SetTextAsync(block.SelectedText); };
        menu.Items.Add(sel); menu.Items.Add(answer); menu.Items.Add(whole); menu.Items.Add(new Separator()); menu.Items.Add(copy);
        return menu;
    }

    /// <summary>A text-only copy of a message, so a part of the conversation can be put together without touching the visible one.</summary>
    static Control CloneForTranscript(Control c) => c switch
    {
        Border { Child: SelectableTextBlock u } => new Border { Child = new SelectableTextBlock { Text = u.Text } },
        SelectableTextBlock a => new SelectableTextBlock { Text = a.Text },
        _ => new TextBlock(),
    };
}
