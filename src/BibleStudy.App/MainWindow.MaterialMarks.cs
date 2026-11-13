using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.LogicalTree;
using BibleStudy.Core.Materials;
using BibleStudy.Core.Models;
using BibleStudy.Core.Storage;

namespace BibleStudy.App;

/// <summary>
/// The text of a file in the materials: Bible references are links (a click opens the passage, resting on one shows the verse), and words can
/// be marked in a color and get a note. The file itself is never changed; marks and notes are kept in the vault with the marked words, so
/// they are found again when the file changes. A right click offers marking, notes and the projector.
/// </summary>
public partial class MainWindow
{
    /// <summary>The marks of the open file, each where it was found in the text now (TextNo &lt; 0: its words are no longer there).</summary>
    readonly List<(MaterialMark Mark, int TextNo, int Start)> _matMarks = [];

    void LoadMaterialMarks(string path, MaterialDoc doc)
    {
        _matMarks.Clear();
        List<MaterialMark> marks;
        try { marks = _vault.GetMaterialMarks(path); } catch (Exception ex) { ErrorLog.Write(ex); return; }
        foreach (var m in marks)
        {
            var at = MaterialSearch.Locate(doc.Blocks, m.Block, m.Start, m.Text);
            if (at is { } a && (a.TextBlock != m.Block || a.Start != m.Start || !string.Equals(m.Path, path, StringComparison.OrdinalIgnoreCase)))
                try { _vault.MoveMaterialMark(m.Id, path, a.TextBlock, a.Start); } catch (Exception ex) { ErrorLog.Write(ex); }
            _matMarks.Add((m, at?.TextBlock ?? -1, at?.Start ?? 0));
        }
    }

    /// <summary>The runs of a paragraph: its bold and italic parts, links on Bible references, marks with their color; and what the pointer does on it.</summary>
    void FillMaterialText(SelectableTextBlock tb, Block b, int textNo, string path)
    {
        // found and chosen words keep their letters readable: a light tint of the accent, not a full bar
        if (Res("BsAccent") is ISolidColorBrush accent) { tb.SelectionBrush = new SolidColorBrush(accent.Color, 0.3); tb.SelectionForegroundBrush = Res("BsText"); }
        var whole = string.Concat(b.Spans.Select(s => s.Text));
        var links = DocumentLinks.VerseSpans(whole).Select(v =>
        {
            var end = DocumentLinks.VerseRanges(whole.Substring(v.Index, v.Length)).FirstOrDefault().EndVerse;
            return (Start: v.Index, End: v.Index + v.Length, v.Ref, To: end);
        }).ToList();
        var marks = _matMarks.Where(m => m.TextNo == textNo && m.Start + m.Mark.Text.Length <= whole.Length).Select(m => (Start: m.Start, End: m.Start + m.Mark.Text.Length, m.Mark)).ToList();

        // the text is cut wherever a span, a link or a mark begins or ends; each piece gets its looks
        var cuts = new SortedSet<int> { 0, whole.Length };
        var at = 0;
        foreach (var s in b.Spans) { at += s.Text.Length; cuts.Add(at); }
        foreach (var l in links) { cuts.Add(l.Start); cuts.Add(l.End); }
        foreach (var m in marks) { cuts.Add(m.Start); cuts.Add(m.End); }
        var edges = cuts.Where(c => c >= 0 && c <= whole.Length).ToList();
        for (var i = 0; i + 1 < edges.Count; i++)
        {
            var (from, to) = (edges[i], edges[i + 1]);
            if (to <= from) continue;
            var spanStart = 0; BibleStudy.Core.Materials.Span span = b.Spans.Count > 0 ? b.Spans[0] : new BibleStudy.Core.Materials.Span("");
            foreach (var s in b.Spans) { if (from < spanStart + s.Text.Length) { span = s; break; } spanStart += s.Text.Length; }
            var run = new Run(whole[from..to]) { FontWeight = span.Bold ? FontWeight.SemiBold : FontWeight.Normal, FontStyle = span.Italic ? FontStyle.Italic : FontStyle.Normal };
            if (links.Any(l => from >= l.Start && from < l.End)) { run.Foreground = Res("BsAccent"); run.TextDecorations = TextDecorations.Underline; }
            if (marks.LastOrDefault(m => from >= m.Start && from < m.End) is { Mark: { } mark }) run.Background = new SolidColorBrush(MarkColor(mark.Color), 0.45);
            tb.Inlines!.Add(run);
        }

        int? PosAt(PointerEventArgs e)
        {
            var pt = e.GetPosition(tb);
            if (pt.X < 0 || pt.Y < 0 || pt.Y > tb.TextLayout.Height || pt.X > tb.Bounds.Width) return null;
            return tb.TextLayout.HitTestPoint(pt).TextPosition;
        }
        (int Start, int End, VerseRef Ref, int To)? LinkAt(int? pos) { if (pos is int p) foreach (var l in links) if (p >= l.Start && p < l.End) return l; return null; }
        MaterialMark? MarkAt(int? pos) => pos is int p ? marks.LastOrDefault(m => p >= m.Start && p < m.End).Mark : null;

        // resting on a Bible reference shows the verse; on a mark with a note, the note
        object? hoverKey = null;
        void Hover(object? key, Func<Control>? make)
        {
            if (Equals(key, hoverKey)) return;
            hoverKey = key;
            ToolTip.SetIsOpen(tb, false);
            ToolTip.SetTip(tb, null);
            if (key is null || make is null) return;
            ToolTip.SetTip(tb, make());
            ToolTip.SetIsOpen(tb, true);
        }
        tb.PointerMoved += (_, e) =>
        {
            var pos = PosAt(e);
            var link = LinkAt(pos);
            tb.Cursor = link is null ? null : new Cursor(StandardCursorType.Hand);
            if (link is { } l) Hover(("link", l.Start), () => VersePreview(l.Ref, l.To));
            else if (MarkAt(pos) is { Note.Length: > 0 } m) Hover(("mark", m.Id), () => new TextBlock { Text = m.Note, TextWrapping = TextWrapping.Wrap, MaxWidth = 380 });
            else Hover(null, null);
        };
        tb.PointerExited += (_, _) => Hover(null, null);
        if (links.Count > 0)
            tb.AddHandler(PointerReleasedEvent, (object? _, PointerReleasedEventArgs e) =>
            {
                if (e.InitialPressMouseButton != MouseButton.Left || !string.IsNullOrEmpty(tb.SelectedText)) return;   // dragging to select text is not a click
                if (LinkAt(PosAt(e)) is { } l) { Hover(null, null); _ = OpenMaterialRefAsync(l.Ref, l.To); }
            }, Avalonia.Interactivity.RoutingStrategies.Bubble, handledEventsToo: true);

        // right click: mark, note, projector
        tb.ContextFlyout = null;
        tb.ContextRequested += (_, e) =>
        {
            e.Handled = true;
            Hover(null, null);
            int? pos = e.TryGetPosition(tb, out var pt) && pt.Y >= 0 && pt.Y <= tb.TextLayout.Height ? tb.TextLayout.HitTestPoint(pt).TextPosition : null;
            var (s0, s1) = (Math.Min(tb.SelectionStart, tb.SelectionEnd), Math.Max(tb.SelectionStart, tb.SelectionEnd));
            s0 = Math.Clamp(s0, 0, whole.Length); s1 = Math.Clamp(s1, 0, whole.Length);
            while (s0 < s1 && char.IsWhiteSpace(whole[s0])) s0++;
            while (s1 > s0 && char.IsWhiteSpace(whole[s1 - 1])) s1--;
            var chosen = whole[s0..s1];
            var menu = new MenuFlyout();
            MenuItem Item(string text, Action act) { var i = new MenuItem { Header = text }; i.Click += (_, _) => act(); return i; }
            MenuItem Colors(string title, Action<string> pick)
            {
                var sub = new MenuItem { Header = title };
                foreach (var hex in PresetColors.Concat(_settings.CustomColors).Distinct(StringComparer.OrdinalIgnoreCase).Take(14))
                {
                    var h = hex;
                    var name = ColorName(h);
                    sub.Items.Add(new MenuItem
                    {
                        Header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { new Border { Width = 14, Height = 14, CornerRadius = new(7), Background = new SolidColorBrush(MarkColor(h)) }, new TextBlock { Text = name.Length > 0 ? name : h } } },
                        Command = new RelayAction(() => pick(h)),
                    });
                }
                return sub;
            }
            var title = Path.GetFileNameWithoutExtension(path);
            if (chosen.Length > 0)
            {
                menu.Items.Add(Colors(L("Markieren"), hex => AddMaterialMark(path, textNo, s0, chosen, hex, null, tb)));
                menu.Items.Add(Item(L("Markieren mit Notiz …"), () => AddMaterialMark(path, textNo, s0, chosen, _settings.DefaultColor, "", tb)));
                menu.Items.Add(Item(L("Auf dem Beamer zeigen"), () => ProjectText(title, chosen)));
                menu.Items.Add(Item(L("Kopieren"), async () => { if (TopLevel.GetTopLevel(this)?.Clipboard is { } c) await c.SetTextAsync(chosen); }));
            }
            if (MarkAt(pos) is { } mark)
            {
                if (menu.Items.Count > 0) menu.Items.Add(new Separator());
                menu.Items.Add(Item(mark.Note.Length > 0 ? L("Notiz bearbeiten …") : L("Notiz schreiben …"), () => EditMaterialNote(tb, mark)));
                menu.Items.Add(Colors(L("Farbe ändern"), hex => { _vault.UpdateMaterialMark(mark.Id, hex, mark.Note); ReloadMaterialMarks(); }));
                menu.Items.Add(Item(L("Markierung entfernen"), () => RemoveMaterialMark(mark)));
            }
            if (LinkAt(pos) is { } link)
            {
                if (menu.Items.Count > 0) menu.Items.Add(new Separator());
                menu.Items.Add(Item(L("Bibelstelle öffnen"), () => _ = OpenMaterialRefAsync(link.Ref, link.To)));
                menu.Items.Add(Item(L("Bibelstelle auf dem Beamer zeigen"), () => _ = ProjectVersesAsync(link.Ref, link.To)));
            }
            if (chosen.Length == 0)
            {
                if (menu.Items.Count > 0) menu.Items.Add(new Separator());
                menu.Items.Add(Item(L("Absatz auf dem Beamer zeigen"), () => ProjectText(title, whole)));
                menu.Items.Add(new MenuItem { Header = L("Zum Markieren erst Text auswählen."), IsEnabled = false });
            }
            menu.ShowAt(tb, true);
        };
    }

    /// <summary>The verse of a reference in the translation read now, for resting the pointer on it.</summary>
    Control VersePreview(VerseRef r, int end)
    {
        var head = new TextBlock { Text = Label(r) + (end > r.Verse ? "–" + end : ""), FontWeight = FontWeight.SemiBold };
        var text = new TextBlock { Text = "…", TextWrapping = TextWrapping.Wrap, MaxWidth = 420, FontFamily = ScriptureFont, FontSize = 15, LineHeight = 22 };
        var translation = CurrentTranslationId;
        _ = Task.Run(async () =>
        {
            try
            {
                var ch = await _provider.GetChapterAsync(translation, r.Book, r.Chapter);
                var verses = ch.Verses.Where(v => v.Number >= r.Verse && v.Number <= Math.Max(r.Verse, end)).ToList();
                var s = verses.Count == 1 ? verses[0].Text : string.Join(" ", verses.Select(v => v.Number + " " + v.Text));
                if (s.Length > 700) s = s[..700].TrimEnd() + " …";
                Dispatcher.UIThread.Post(() => text.Text = s.Length > 0 ? s : L("Diesen Vers gibt es in der gewählten Übersetzung nicht."));
            }
            catch (Exception ex) { Dispatcher.UIThread.Post(() => text.Text = ex.Message); }
        });
        return new StackPanel { Spacing = 4, MaxWidth = 440, Children = { head, text, new TextBlock { Text = L("Klicken öffnet die Stelle."), Classes = { "caption" } } } };
    }

    void AddMaterialMark(string path, int textNo, int start, string text, string color, string? note, Control anchor)
    {
        long id;
        try { id = _vault.AddMaterialMark(path, textNo, start, text, color, note ?? ""); }
        catch (Exception ex) { StatusText.Text = ex.Message; return; }
        ReloadMaterialMarks();
        StatusText.Text = L("Markiert. Die Datei selbst bleibt unverändert.");
        if (note is not null && _matMarks.FirstOrDefault(m => m.Mark.Id == id).Mark is { } made)
            EditMaterialNote(_matBlocks.FirstOrDefault(x => x.TextNo == textNo).Text ?? anchor, made);
    }

    void RemoveMaterialMark(MaterialMark mark)
    {
        _vault.DeleteMaterialMark(mark.Id);
        ReloadMaterialMarks();
        ShowUndoToast(L("Markierung entfernt."), () =>
        {
            _vault.AddMaterialMark(mark.Path, mark.Block, mark.Start, mark.Text, mark.Color, mark.Note);
            ReloadMaterialMarks();
            return Task.CompletedTask;
        });
    }

    /// <summary>The marks changed: the paragraphs they touch are drawn again (the rest of the file and its scroll place stay).</summary>
    void ReloadMaterialMarks()
    {
        if (_matCurrent is not { } path || !_matCache.TryGetValue(path, out var c)) return;
        var before = _matMarks.Select(m => m.TextNo).ToHashSet();
        LoadMaterialMarks(path, c.Doc);
        foreach (var n in before.Concat(_matMarks.Select(m => m.TextNo)).Where(n => n >= 0).Distinct().ToList()) RedrawMaterialBlock(n, path);
        UpdateMarksButton(path);
    }

    void RedrawMaterialBlock(int textNo, string path)
    {
        var i = _matBlocks.FindIndex(x => x.TextNo == textNo);
        if (i < 0) return;
        var old = _matBlocks[i];
        var made = BlockControl(old.Block, path, _matShowVersion, textNo);
        var at = _matView.Children.IndexOf(old.Control);
        if (at < 0) return;
        _matView.Children[at] = made;
        _matBlocks[i] = (old.Block, made, textNo, TextOf(made));
    }

    static SelectableTextBlock? TextOf(Control c) => c as SelectableTextBlock ?? c.GetLogicalDescendants().OfType<SelectableTextBlock>().FirstOrDefault();

    /// <summary>The notes of a paragraph, below it: what was marked, and the note; a click writes on it.</summary>
    Control WithMaterialNotes(Control paragraph, int textNo)
    {
        var notes = _matMarks.Where(m => m.TextNo == textNo && m.Mark.Note.Length > 0).ToList();
        if (notes.Count == 0) return paragraph;
        var panel = new StackPanel { Spacing = 4, Children = { paragraph } };
        foreach (var (mark, _, _) in notes)
        {
            var m = mark;
            var text = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 13.5 };
            text.Inlines!.Add(new Run("✎  ") { FontWeight = FontWeight.SemiBold });
            text.Inlines.Add(new Run(m.Note));
            var box = new Button
            {
                Classes = { "ghost" }, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new(10, 6), Margin = new(12, 0, 0, 4),
                BorderThickness = new(3, 0, 0, 0), BorderBrush = new SolidColorBrush(MarkColor(m.Color)), Content = text,
            };
            ToolTip.SetTip(box, L("Zu „{0}“ · klicken zum Bearbeiten", Shorten(m.Text, 60)));
            box.Click += (_, _) => EditMaterialNote(box, m);
            panel.Children.Add(box);
        }
        return panel;
    }

    void EditMaterialNote(Control anchor, MaterialMark mark)
    {
        var flyout = new Flyout { Placement = PlacementMode.BottomEdgeAlignedLeft };
        var box = new TextBox { Text = mark.Note, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 90, Width = 380, PlaceholderText = L("Deine Notiz …") };
        var save = new Button { Content = L("Speichern"), Classes = { "primary" } };
        var cancel = new Button { Content = L("Abbrechen") };
        void Save()
        {
            flyout.Hide();
            var note = (box.Text ?? "").Trim();
            if (note == mark.Note) return;
            _vault.UpdateMaterialMark(mark.Id, mark.Color, note);
            ReloadMaterialMarks();
        }
        save.Click += (_, _) => Save();
        cancel.Click += (_, _) => flyout.Hide();
        box.KeyDown += (_, e) => { if (e.Key == Key.Enter && e.KeyModifiers.HasFlag(KeyModifiers.Control)) { e.Handled = true; Save(); } };
        flyout.Content = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                new TextBlock { Text = L("NOTIZ"), Classes = { "eyebrow" } },
                new TextBlock { Text = "„" + Shorten(mark.Text, 120) + "“", TextWrapping = TextWrapping.Wrap, Classes = { "caption" }, MaxWidth = 380 },
                box,
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { save, cancel, new TextBlock { Text = L("Strg+Enter speichert"), Classes = { "caption" }, VerticalAlignment = VerticalAlignment.Center } } },
            },
        };
        flyout.ShowAt(anchor);
        box.Focus();
    }

    Button? _matMarksBtn;

    /// <summary>The button "My marks (n)" above the file: all marks and notes of the file, to jump to; those whose words are gone can be deleted.</summary>
    void UpdateMarksButton(string path)
    {
        if (_matMarksBtn is not null) _matActions.Children.Remove(_matMarksBtn);
        _matMarksBtn = null;
        if (_matMarks.Count == 0) return;
        var btn = new Button { Content = L("Meine Markierungen ({0})", _matMarks.Count), Classes = { "ghost" }, Margin = new(0, 0, 6, 4) };
        btn.Click += (_, _) =>
        {
            var flyout = new Flyout { Placement = PlacementMode.BottomEdgeAlignedLeft };
            var list = new StackPanel { Spacing = 2, Width = 420 };
            list.Children.Add(new TextBlock { Text = L("MEINE MARKIERUNGEN"), Classes = { "eyebrow" }, Margin = new(0, 0, 0, 6) });
            foreach (var (mark, textNo, start) in _matMarks.OrderBy(m => m.TextNo < 0).ThenBy(m => m.TextNo).ThenBy(m => m.Start))
            {
                var m = mark; var n = textNo; var s = start;
                var text = new TextBlock { TextWrapping = TextWrapping.Wrap };
                text.Inlines!.Add(new Run("„" + Shorten(m.Text, 90) + "“"));
                if (m.Note.Length > 0) text.Inlines.Add(new Run("\n✎ " + Shorten(m.Note, 140)) { FontSize = 12.5 });
                if (n < 0) text.Inlines.Add(new Run("\n" + L("Diese Worte stehen nicht mehr in der Datei.")) { FontSize = 12, FontStyle = FontStyle.Italic });
                var row = new DockPanel();
                row.Children.Add(new Border { Width = 4, CornerRadius = new(2), Background = new SolidColorBrush(MarkColor(m.Color)), Margin = new(0, 2, 8, 2), [DockPanel.DockProperty] = Dock.Left });
                if (n < 0)
                {
                    var del = new Button { Classes = { "tool" }, Width = 28, Height = 28, Padding = new(0), Content = "✕", [DockPanel.DockProperty] = Dock.Right };
                    ToolTip.SetTip(del, L("Markierung entfernen"));
                    del.Click += (_, _) => { flyout.Hide(); RemoveMaterialMark(m); };
                    row.Children.Add(del);
                }
                row.Children.Add(text);
                var item = new Button { Classes = { "ghost" }, Content = row, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new(6, 4), IsEnabled = true };
                if (n >= 0) item.Click += (_, _) => { flyout.Hide(); ShowMaterialPlace(n, s, m.Text.Length); };
                list.Children.Add(item);
            }
            flyout.Content = new ScrollViewer { MaxHeight = 520, Content = list };
            flyout.ShowAt(btn);
        };
        _matMarksBtn = btn;
        _matActions.Children.Insert(Math.Min(_matActions.Children.Count, 2), btn);
    }
}

/// <summary>A command that runs an action (for menu items whose header is not a plain text).</summary>
sealed class RelayAction(Action act) : System.Windows.Input.ICommand
{
    public event EventHandler? CanExecuteChanged { add { } remove { } }
    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) => act();
}
