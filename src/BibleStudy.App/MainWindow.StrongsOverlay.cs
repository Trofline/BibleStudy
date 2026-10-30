using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using BibleStudy.Core.Import;
using BibleStudy.Core.Models;
using BibleStudy.Core.Providers;

namespace BibleStudy.App;

/// <summary>
/// Strong's numbers that a plugin added afterwards to a translation (an estimate), the choice of a translation for the word study,
/// and the few dialog tools a plugin may use to talk to the user.
/// </summary>
public partial class MainWindow
{
    StrongsOverlay _overlay = null!;

    bool TranslationHasStrongs(TranslationInfo t) =>
        StrongsSupport.Known.Contains(t.Id) || _overlay.Has(t.Id) || (LocalBibleStore.Owns(t.Id) && (_curChapterData?.Verses.Any(v => v.Segments.Any(s => s.Strongs is { Count: > 0 })) ?? false));

    // ---- the word study without numbers: which translation to take ------------------------------------------------------

    /// <summary>Lists the translations that have Strong's numbers; those with numbers added afterwards are marked as a warning.</summary>
    async Task<TranslationInfo?> PickStrongsTranslationAsync(TranslationInfo current)
    {
        var built = _overlay.List().ToDictionary(x => x.Id, x => x.Reference, StringComparer.OrdinalIgnoreCase);
        var options = _all.Where(t => t.Id != current.Id && (StrongsSupport.Known.Contains(t.Id) || built.ContainsKey(t.Id)))
            .OrderBy(t => t.Language == current.Language ? 0 : t.Id == "eng_kjv" ? 1 : t.Language == "eng" ? 2 : 3)
            .ThenBy(t => built.ContainsKey(t.Id) ? 0 : 1).ThenBy(t => t.LanguageName).ThenBy(t => t.Name).ToList();
        if (options.Count == 0) return null;

        TranslationInfo? chosen = null;
        var amber = Color.Parse("#E0A526");
        var list = new ListBox { MaxHeight = 300, SelectionMode = SelectionMode.Single };
        foreach (var t in options)
        {
            var estimated = built.ContainsKey(t.Id);
            var text = new StackPanel { Spacing = 2, Children = { new TextBlock { Text = $"{t.Name}  ({t.ShortName})  ·  {t.LanguageName}", TextWrapping = TextWrapping.Wrap, FontWeight = FontWeight.SemiBold } } };
            if (estimated) text.Children.Add(new TextBlock { Text = L("⚠ Strong-Nummern nachträglich ergänzt (Schätzung, nicht fehlerfrei) – bitte prüfen"), FontSize = 12, TextWrapping = TextWrapping.Wrap });
            list.Items.Add(new ListBoxItem { Tag = t, Content = text, Background = estimated ? new SolidColorBrush(amber, 0.22) : null });
        }
        list.SelectedIndex = 0;

        var win = new Window { Title = L("Wortstudie: Übersetzung mit Strong-Nummern wählen"), Width = 560, SizeToContent = SizeToContent.Height, Background = Res("BsSurface"), Icon = Icon, WindowStartupLocation = WindowStartupLocation.CenterOwner, CanResize = false };
        var ok = new Button { Content = L("Als Spalte öffnen"), Classes = { "primary" }, Margin = new(8, 0, 0, 0) };
        var cancel = new Button { Content = L("Abbrechen") };
        ok.Click += (_, _) => { chosen = (list.SelectedItem as ListBoxItem)?.Tag as TranslationInfo; win.Close(); };
        cancel.Click += (_, _) => win.Close();
        list.DoubleTapped += (_, _) => { if (list.SelectedItem is ListBoxItem it) { chosen = it.Tag as TranslationInfo; win.Close(); } };
        win.Content = new StackPanel
        {
            Margin = new(22), Spacing = 10,
            Children =
            {
                new TextBlock { Text = L("Wortstudie braucht Strong-Nummern"), FontSize = 18, FontWeight = FontWeight.SemiBold },
                new TextBlock { Text = L("„{0}“ hat keine Strong-Nummern. Wähle eine Übersetzung, die sie hat; sie wird als zusätzliche Spalte neben dem Text geöffnet, und dort kannst du die Wörter anklicken. Gelb hinterlegt sind Übersetzungen, bei denen die Nummern nachträglich ergänzt wurden: Sie sind nur geschätzt.", current.Name), TextWrapping = TextWrapping.Wrap },
                list,
                new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Children = { cancel, ok } },
            },
        };
        await win.ShowDialog(this);
        return chosen;
    }

    // ---- tools for plugins ------------------------------------------------------------------------------------------------

    /// <summary>The words of a chapter with their Strong's numbers (where the translation has them) and the plain verse text.</summary>
    async Task<object> GetChapterStrongsAsync(JsonElement a)
    {
        var (t, book, err) = await ResolveAsync(a);
        if (err is not null || t is null || book is null) return err ?? Err("book is required");
        var ch = await _provider.GetChapterAsync(t.Id, book.Id, ArgInt(a, "chapter") ?? 1);
        return new
        {
            translation = t.Id, book = book.Id, chapter = ch.Number, hasStrongs = ch.Verses.Any(v => v.Segments.Any(s => s.Strongs is { Count: > 0 })),
            verses = ch.Verses.Select(v => new { number = v.Number, text = v.Text, words = StrongsOverlay.TokensOf(v).Select(w => new { w = w.Word, s = w.Strongs }) }),
        };
    }

    /// <summary>Stores estimated numbers for the verses of one chapter: {"translation","reference","book","chapter","verses":[{"number","tags":{"3":["G2316"]}}]}.</summary>
    async Task<object> SetStrongsOverlayAsync(JsonElement a)
    {
        var (t, book, err) = await ResolveAsync(a);
        if (err is not null || t is null || book is null) return err ?? Err("book is required");
        if (t.Id.StartsWith("plugin:", StringComparison.Ordinal)) return Err("Plugin translations are not kept on disk; numbers cannot be added to them.");
        if (Prop(a, "verses") is not { ValueKind: JsonValueKind.Array } verses) return Err("verses is required");
        var reference = ArgStr(a, "reference") ?? "";
        var chapter = ArgInt(a, "chapter") ?? 1;
        // the text is taken from the program itself: the numbers are tied to exactly this text
        var ch = await _provider.GetChapterAsync(t.Id, book.Id, chapter);
        var texts = ch.Verses.ToDictionary(v => v.Number, v => v);
        var count = 0;
        foreach (var item in verses.EnumerateArray())
        {
            if (ArgInt(item, "number") is not int number || !texts.TryGetValue(number, out var verse)) continue;
            var tags = new Dictionary<int, string[]>();
            if (Prop(item, "tags") is { ValueKind: JsonValueKind.Object } tg)
                foreach (var p in tg.EnumerateObject())
                    if (int.TryParse(p.Name, out var i) && p.Value.ValueKind == JsonValueKind.Array)
                        tags[i] = [.. p.Value.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).Where(s => s.Length is > 1 and < 12)];
            tags = tags.Where(x => x.Value.Length > 0).ToDictionary(x => x.Key, x => x.Value);
            _overlay.Set(t.Id, reference, book.Id, chapter, number, verse.Text, tags);
            count++;
        }
        return new { ok = true, verses = count };
    }

    // ---- dialogs for plugins --------------------------------------------------------------------------------------------------

    /// <summary>Shows a message to the user and waits until it is closed; returns which button was pressed.</summary>
    async Task<object> PluginMessageAsync(JsonElement a)
    {
        var title = ArgStr(a, "title") ?? "";
        var text = ArgStr(a, "text") ?? "";
        var buttons = ArgStrings(a, "buttons") is { Length: > 0 } b ? b : [L("OK")];
        return new { button = await AskAsync(title, text, buttons) };
    }

    /// <summary>Lets the user pick one of several options: {"title","text","options":[{"id","label"}],"ok","cancel"}. Answer: {"id": the chosen id or null}.</summary>
    async Task<object> PluginChooseAsync(JsonElement a)
    {
        var options = Prop(a, "options") is { ValueKind: JsonValueKind.Array } arr
            ? arr.EnumerateArray().Select(o => (Id: ArgStr(o, "id") ?? "", Label: ArgStr(o, "label") ?? ArgStr(o, "id") ?? "")).Where(o => o.Id.Length > 0).ToList() : [];
        if (options.Count == 0) return Err("options are required");
        string? chosen = null;
        var list = new ListBox { MaxHeight = 320, SelectionMode = SelectionMode.Single };
        foreach (var o in options) list.Items.Add(new ListBoxItem { Tag = o.Id, Content = new TextBlock { Text = o.Label, TextWrapping = TextWrapping.Wrap } });
        list.SelectedIndex = 0;
        var win = new Window { Title = ArgStr(a, "title") ?? "", Width = 560, SizeToContent = SizeToContent.Height, Background = Res("BsSurface"), Icon = Icon, WindowStartupLocation = WindowStartupLocation.CenterOwner, CanResize = false };
        var ok = new Button { Content = ArgStr(a, "ok") ?? L("OK"), Classes = { "primary" }, Margin = new(8, 0, 0, 0) };
        var cancel = new Button { Content = ArgStr(a, "cancel") ?? L("Abbrechen") };
        ok.Click += (_, _) => { chosen = (list.SelectedItem as ListBoxItem)?.Tag as string; win.Close(); };
        cancel.Click += (_, _) => win.Close();
        win.Content = new StackPanel
        {
            Margin = new(22), Spacing = 10,
            Children =
            {
                new TextBlock { Text = ArgStr(a, "title") ?? "", FontSize = 18, FontWeight = FontWeight.SemiBold },
                new TextBlock { Text = ArgStr(a, "text") ?? "", TextWrapping = TextWrapping.Wrap },
                list,
                new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Children = { cancel, ok } },
            },
        };
        await win.ShowDialog(this);
        return new { id = chosen };
    }
}
