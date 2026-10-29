using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using BibleStudy.Core.Models;
using BibleStudy.Core.Storage;

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
        Item(L("{0} – Vers-Übersicht", Label(r)), () => { Open(GuideTab); _ = ShowGuideAsync(r); });
        menu.Items.Add(new Separator());
        Item(L("Vers mit Stelle kopieren"), () => _ = CopyVerseAsync());
        Item(L("Als Bild exportieren"), ExportVerseImage);
        Item(L("Zum Auswendiglernen"), () => { Memory.Add(MemoryKey(r), DateTime.Now); StatusText.Text = L("{0} zum Auswendiglernen hinzugefügt.", Label(r)); });
        Item(L("Markieren (Farbe wählen) …"), ShowColorFlyout);
        Item(L("Merken / nicht mehr merken"), ToggleBookmark);
        Item(L("Notiz schreiben"), () => { Open(2); NoteEditor.Focus(); });
        Item(L("Neues Dokument aus diesem Vers"), () => { Open(KnowledgeTab); NewDocumentFromVerse(); });
        Item(_doc is null ? L("In offenes Dokument einfügen (kein Dokument offen)") : L("In „{0}“ einfügen", Shorten(_doc.Title, 30)),
             () => { Open(KnowledgeTab); InsertIntoDoc(L("> {0}\n> — {1}\n\n", VerseTextOf(verse).Trim(), Label(r)), null); }, _doc is not null);
        Item(L("Zur Leseliste"), () => AddToQueue([r]));
        Item(L("Als Aufgabe merken"), () => { _vault.AddTodo(L("{0} studieren", Label(r)), r); RefreshTodos(); StatusText.Text = L("Aufgabe zu {0} angelegt.", Label(r)); });
        menu.Items.Add(new Separator());
        Item(L("Querverweise"), () => Open(4));
        Item(L("Eigenen Querverweis setzen …"), () => ShowMyXrefEditor(r, anchor));
        Item(L("Grundtext Wort für Wort"), () => { Open(GrundtextTab); _ = ShowGrundtextAsync(r); });
        Item(L("Vorlesen"), ToggleSpeak);
        menu.ShowAt(anchor, showAtPointer: true);
    }

    // ---- verse overview -----------------------------------------------------------------------------------------------

    TextBlock Eyebrow(string text) => new() { Text = text.ToUpperInvariant(), Classes = { "eyebrow" }, Margin = new(0, 6, 0, 0) };

    TextBlock Muted(string text) => new() { Text = text, Classes = { "caption" }, TextWrapping = TextWrapping.Wrap };

    async Task ShowGuideAsync(VerseRef r)
    {
        var version = ++_guideVersion;
        GuidePanel.Children.Clear();
        if (r.Book == "") { GuidePanel.Children.Add(Muted(L("Klicke im Bibeltext einen Vers an. Hier erscheint dann alles, was du und die Werkzeuge zu diesem Vers haben."))); return; }
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
        Action(L("Notiz"), () => { RevealSideTab(2); NoteEditor.Focus(); }, true);
        Action(L("Dokument"), () => { SideTabs.SelectedIndex = KnowledgeTab; NewDocumentFromVerse(); });
        Action(L("Kopieren"), () => _ = CopyVerseAsync());
        Action(L("Leseliste"), () => AddToQueue([r]));
        GuidePanel.Children.Add(actions);

        // what you already have
        GuidePanel.Children.Add(Eyebrow(L("Deine Notizen und Markierungen")));
        var mine = new StackPanel { Spacing = 6 };
        if (_vault.GetNotes().FirstOrDefault(n => n.Ref == r) is { } note)
            mine.Children.Add(new TextBlock { Text = "✎ " + note.Text, TextWrapping = TextWrapping.Wrap });
        foreach (var m in _vault.GetMarks(r.Book, r.Chapter).Where(m => m.Ref.Verse == r.Verse))
        {
            var dot = new Border { Width = 10, Height = 10, CornerRadius = new(5), Background = new SolidColorBrush(Color.Parse(m.Color)), VerticalAlignment = VerticalAlignment.Top, Margin = new(0, 5, 8, 0) };
            var body = new TextBlock { TextWrapping = TextWrapping.Wrap, Text = (m.WholeVerse ? L("Ganzer Vers") : $"„{Shorten(m.Text, 60)}“") + (m.Note.Length > 0 ? " – " + Shorten(m.Note.ReplaceLineEndings(" "), 160) : "") + (m.Tags.Count > 0 ? "  " + string.Join(" ", m.Tags.Select(t => "#" + t)) : "") };
            var row = new DockPanel { Children = { dot, body } };
            DockPanel.SetDock(dot, Dock.Left);
            mine.Children.Add(row);
        }
        if (mine.Children.Count == 0) mine.Children.Add(Muted(L("Noch nichts. Rechtsklick auf einen Vers bietet Markieren, Notiz und Dokument an.")));
        GuidePanel.Children.Add(mine);

        // everything else you wrote that is about this verse (documents, notes on other verses, prayers, own cross references; ranges count)
        var about = OwnSearch.About(_vault, r).Where(h => !(h.Kind == OwnKind.Note && h.Ref == r) && !(h.Kind == OwnKind.Mark && h.Ref == r)).ToList();
        GuidePanel.Children.Add(Eyebrow(L("Wo du darüber geschrieben hast ({0})", about.Count)));
        if (about.Count == 0) GuidePanel.Children.Add(Muted(L("Nichts anderes von dir erwähnt diesen Vers.")));
        else foreach (var h in about.Take(25)) GuidePanel.Children.Add(OwnRow(h));
        if (about.Count > 25) GuidePanel.Children.Add(Muted(L("… und {0} weitere. Das Suchfeld oben (Strg+K) findet alles Eigene.", about.Count - 25)));

        // other translations
        GuidePanel.Children.Add(Eyebrow(L("Übersetzungen im Vergleich")));
        var compare = new StackPanel { Spacing = 8 };
        GuidePanel.Children.Add(compare);
        compare.Children.Add(Muted(L("Lade …")));
        // cross references
        GuidePanel.Children.Add(Eyebrow(L("Querverweise")));
        var xrefs = new StackPanel { Spacing = 6 };
        GuidePanel.Children.Add(xrefs);
        xrefs.Children.Add(Muted(L("Lade …")));
        // original text
        GuidePanel.Children.Add(Eyebrow(L("Grundtext")));
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
        if (marked && current is not null) compare.Children.Add(Muted(L("Farbig und unterstrichen: Wörter, die in {0} anders lauten.", current.ShortName)));
        if (rows.Count == 0) compare.Children.Add(Muted(L("Keine weitere Übersetzung erreichbar (offline?).")));
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
        if (xr.Count == 0) xrefs.Children.Add(Muted(L("Keine Querverweise gefunden.")));
    }

    /// <summary>Same verse in other translations of the same language (local ones first) plus the KJV.</summary>
    async Task<List<(string Name, string Text)>> CompareTranslationsAsync(VerseRef r, TranslationInfo? current)
    {
        var picks = _all.Where(t => t.Id != current?.Id && (t.LanguageName == current?.LanguageName || t.Id == "eng_kjv"))
            .OrderBy(t => (t.Id.StartsWith("local:") || t.Id.StartsWith("plugin:")) ? 0 : t.Id == "eng_kjv" ? 2 : 1).Take(6).ToList();
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
