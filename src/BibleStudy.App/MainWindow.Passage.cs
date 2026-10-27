using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using BibleStudy.Core.Models;

namespace BibleStudy.App;

/// <summary>
/// Verse context menu (right click) and the verse overview ("Passage Guide"): everything about one verse in one place –
/// your notes, marks and documents, other translations, cross references, original text.
/// </summary>
public partial class MainWindow
{
    const int GuideTab = 15;
    int _guideVersion;

    // ---- right-click menu on a verse ----------------------------------------------------------------------------------

    void ShowVerseMenu(int verse, Control anchor)
    {
        var r = new VerseRef(_curBook, _curChapter, verse);
        var menu = new MenuFlyout();
        void Item(string text, Action act, bool enabled = true) { var i = new MenuItem { Header = text, IsEnabled = enabled }; i.Click += (_, _) => act(); menu.Items.Add(i); }
        void Open(int tab) { SetPanelOpen(true); SideTabs.SelectedIndex = tab; }
        Item($"{Label(r)} – Vers-Übersicht", () => { Open(GuideTab); _ = ShowGuideAsync(r); });
        menu.Items.Add(new Separator());
        Item("Vers mit Stelle kopieren", () => _ = CopyVerseAsync());
        Item("Markieren (Farbe wählen) …", ShowColorFlyout);
        Item("Merken / nicht mehr merken", ToggleBookmark);
        Item("Notiz schreiben", () => { Open(2); NoteEditor.Focus(); });
        Item("Neues Dokument aus diesem Vers", () => { Open(KnowledgeTab); NewDocumentFromVerse(); });
        Item(_doc is null ? "In offenes Dokument einfügen (kein Dokument offen)" : $"In „{Shorten(_doc.Title, 30)}“ einfügen",
             () => { Open(KnowledgeTab); InsertIntoDoc($"> {VerseTextOf(verse).Trim()}\n> — {Label(r)}\n\n", null); }, _doc is not null);
        Item("Zur Leseliste", () => AddToQueue([r]));
        Item("Als Aufgabe merken", () => { _vault.AddTodo($"{Label(r)} studieren", r); RefreshTodos(); StatusText.Text = L("Aufgabe zu {0} angelegt.", Label(r)); });
        menu.Items.Add(new Separator());
        Item("Querverweise", () => Open(4));
        Item("Grundtext Wort für Wort", () => { Open(GrundtextTab); _ = ShowGrundtextAsync(r); });
        Item("Vorlesen", ToggleSpeak);
        menu.ShowAt(anchor, showAtPointer: true);
    }

    // ---- verse overview -----------------------------------------------------------------------------------------------

    TextBlock Eyebrow(string text) => new() { Text = text.ToUpperInvariant(), Classes = { "eyebrow" }, Margin = new(0, 6, 0, 0) };

    TextBlock Muted(string text) => new() { Text = text, Classes = { "caption" }, TextWrapping = TextWrapping.Wrap };

    async Task ShowGuideAsync(VerseRef r)
    {
        var version = ++_guideVersion;
        GuidePanel.Children.Clear();
        if (r.Book == "") { GuidePanel.Children.Add(Muted("Klicke im Bibeltext einen Vers an. Hier erscheint dann alles, was du und die Werkzeuge zu diesem Vers haben.")); return; }
        var current = TranslationBox.SelectedItem as TranslationInfo;

        GuidePanel.Children.Add(new TextBlock { Text = Label(r), Classes = { "h2" } });
        var text = r.Book == _curBook && r.Chapter == _curChapter ? VerseTextOf(r.Verse) : "";
        if (text.Length > 0) GuidePanel.Children.Add(new TextBlock { Text = text.Trim(), TextWrapping = TextWrapping.Wrap, FontFamily = ScriptureFont, FontSize = 18, LineHeight = 28 });

        var actions = new WrapPanel();
        void Action(string label, Action act, bool primary = false)
        {
            var b = new Button { Content = label, Margin = new(0, 0, 6, 6) };
            if (primary) b.Classes.Add("primary");
            b.Click += (_, _) => act();
            actions.Children.Add(b);
        }
        Action("Notiz", () => { SideTabs.SelectedIndex = 2; NoteEditor.Focus(); }, true);
        Action("Dokument", () => { SideTabs.SelectedIndex = KnowledgeTab; NewDocumentFromVerse(); });
        Action("Kopieren", () => _ = CopyVerseAsync());
        Action("Leseliste", () => AddToQueue([r]));
        GuidePanel.Children.Add(actions);

        // what you already have
        GuidePanel.Children.Add(Eyebrow("Deine Notizen und Markierungen"));
        var mine = new StackPanel { Spacing = 6 };
        if (_vault.GetNotes().FirstOrDefault(n => n.Ref == r) is { } note)
            mine.Children.Add(new TextBlock { Text = "✎ " + note.Text, TextWrapping = TextWrapping.Wrap });
        foreach (var m in _vault.GetMarks(r.Book, r.Chapter).Where(m => m.Ref.Verse == r.Verse))
        {
            var dot = new Border { Width = 10, Height = 10, CornerRadius = new(5), Background = new SolidColorBrush(Color.Parse(m.Color)), VerticalAlignment = VerticalAlignment.Top, Margin = new(0, 5, 8, 0) };
            var body = new TextBlock { TextWrapping = TextWrapping.Wrap, Text = (m.WholeVerse ? "Ganzer Vers" : $"„{Shorten(m.Text, 60)}“") + (m.Note.Length > 0 ? " – " + Shorten(m.Note.ReplaceLineEndings(" "), 160) : "") + (m.Tags.Count > 0 ? "  " + string.Join(" ", m.Tags.Select(t => "#" + t)) : "") };
            var row = new DockPanel { Children = { dot, body } };
            DockPanel.SetDock(dot, Dock.Left);
            mine.Children.Add(row);
        }
        if (mine.Children.Count == 0) mine.Children.Add(Muted("Noch nichts. Rechtsklick auf einen Vers bietet Markieren, Notiz und Dokument an."));
        GuidePanel.Children.Add(mine);

        // documents that mention the verse
        var docs = _vault.GetDocuments().Where(d => DocumentLinks.VerseRefs(d.Body).Contains(r)).ToList();
        GuidePanel.Children.Add(Eyebrow($"In deinen Dokumenten ({docs.Count})"));
        var docChips = new WrapPanel();
        foreach (var d in docs.Take(20))
        {
            var doc = d;
            docChips.Children.Add(Chip(d.Title, () => { SideTabs.SelectedIndex = KnowledgeTab; OpenDocument(doc); }, current?.Id ?? "", null));
        }
        GuidePanel.Children.Add(docs.Count == 0 ? Muted("Kein Dokument erwähnt diesen Vers.") : docChips);

        // other translations
        GuidePanel.Children.Add(Eyebrow("Übersetzungen im Vergleich"));
        var compare = new StackPanel { Spacing = 8 };
        GuidePanel.Children.Add(compare);
        compare.Children.Add(Muted("Lade …"));
        // cross references
        GuidePanel.Children.Add(Eyebrow("Querverweise"));
        var xrefs = new StackPanel { Spacing = 6 };
        GuidePanel.Children.Add(xrefs);
        xrefs.Children.Add(Muted("Lade …"));
        // original text
        GuidePanel.Children.Add(Eyebrow("Grundtext"));
        var orig = new Button { Content = L("Wort für Wort im Grundtext öffnen"), HorizontalAlignment = HorizontalAlignment.Left };
        orig.Click += (_, _) => { SideTabs.SelectedIndex = GrundtextTab; _ = ShowGrundtextAsync(r); };
        GuidePanel.Children.Add(orig);

        var compareTask = CompareTranslationsAsync(r, current);
        var xrefTask = GuideXrefsAsync(r, current?.Id ?? "");
        var rows = await compareTask;
        if (version != _guideVersion) return;
        compare.Children.Clear();
        // words that differ from the open translation are colored (only where the texts are alike enough to compare)
        var baseText = "";
        try { if (current is not null) baseText = (await _provider.GetChapterAsync(current.Id, r.Book, r.Chapter)).Verses.FirstOrDefault(x => x.Number == r.Verse)?.Text ?? ""; } catch { /* no highlight */ }
        static string Norm(string w) => new string(w.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
        var baseWords = baseText.Split([' ', ' '], StringSplitOptions.RemoveEmptyEntries).Select(Norm).Where(w => w.Length > 0).ToHashSet();
        var marked = false;
        foreach (var (name, verseText) in rows)
        {
            var tb = new TextBlock { TextWrapping = TextWrapping.Wrap };
            tb.Inlines!.Add(new Avalonia.Controls.Documents.Run(name + "  ") { FontWeight = FontWeight.SemiBold, Foreground = Res("BsAccent") });
            var tokens = verseText.Split(' ');
            var same = tokens.Count(t => baseWords.Contains(Norm(t)));
            var comparable = baseWords.Count > 0 && same >= Math.Max(2, tokens.Length / 4);
            for (var i = 0; i < tokens.Length; i++)
            {
                var differs = comparable && Norm(tokens[i]).Length > 0 && !baseWords.Contains(Norm(tokens[i]));
                var run = new Avalonia.Controls.Documents.Run(tokens[i] + (i < tokens.Length - 1 ? " " : ""));
                if (differs) { run.Foreground = Res("BsAccent"); run.TextDecorations = TextDecorations.Underline; marked = true; }
                tb.Inlines.Add(run);
            }
            compare.Children.Add(tb);
        }
        if (marked && current is not null) compare.Children.Add(Muted($"Farbig und unterstrichen: Wörter, die in {current.ShortName} anders lauten."));
        if (rows.Count == 0) compare.Children.Add(Muted("Keine weitere Übersetzung erreichbar (offline?)."));
        var xr = await xrefTask;
        if (version != _guideVersion) return;
        xrefs.Children.Clear();
        foreach (var (x, t) in xr)
        {
            var target = x;
            var head = new Button { Classes = { "ghost" }, Padding = new(0), Content = $"{Label(x.Target)}{(x.EndVerse > x.Target.Verse ? "–" + x.EndVerse : "")}", FontWeight = FontWeight.SemiBold, Foreground = Res("BsAccent") };
            head.Click += async (_, _) => await GoToAsync(target.Target.Book, target.Target.Chapter, target.Target.Verse);
            xrefs.Children.Add(new StackPanel { Children = { head, new TextBlock { Text = t, TextWrapping = TextWrapping.Wrap, Opacity = 0.85 } } });
        }
        if (xr.Count == 0) xrefs.Children.Add(Muted("Keine Querverweise gefunden."));
    }

    /// <summary>Same verse in other translations of the same language (local ones first) plus the KJV.</summary>
    async Task<List<(string Name, string Text)>> CompareTranslationsAsync(VerseRef r, TranslationInfo? current)
    {
        var picks = _all.Where(t => t.Id != current?.Id && (t.LanguageName == current?.LanguageName || t.Id == "eng_kjv"))
            .OrderBy(t => t.Id.StartsWith("local:") ? 0 : t.Id == "eng_kjv" ? 2 : 1).Take(6).ToList();
        var tasks = picks.Select(async t =>
        {
            try
            {
                var ch = await _provider.GetChapterAsync(t.Id, r.Book, r.Chapter).WaitAsync(TimeSpan.FromSeconds(12));
                var v = ch.Verses.FirstOrDefault(x => x.Number == r.Verse);
                return v is null ? null : ((string Name, string Text)?)(t.ShortName, v.Text.Trim());
            }
            catch { return null; }
        }).ToList();
        return (await Task.WhenAll(tasks)).OfType<(string, string)>().ToList();
    }

    async Task<List<(CrossRef X, string Text)>> GuideXrefsAsync(VerseRef r, string translationId)
    {
        try
        {
            var refs = (await _hello.GetCrossReferencesAsync(r.Book, r.Chapter, r.Verse)).Take(8).ToList();
            var texts = await Task.WhenAll(refs.Select(x => VerseTextAsync(translationId, x)));
            return refs.Zip(texts).ToList();
        }
        catch { return []; }
    }
}
