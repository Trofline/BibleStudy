using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using BibleStudy.Core.Models;
using BibleStudy.Core.Search;
using BibleStudy.Core.Storage;

namespace BibleStudy.App;

/// <summary>
/// Writing a document the way a word processor does it: a formatting bar (paragraph style, bold, italic, lists, quote, verse, link),
/// shortcuts, lists that continue on Enter, a page-like writing area, and a finished view in which verses and links can be clicked.
/// The text stays plain Markdown, so it is searchable, printable and readable anywhere.
/// </summary>
public partial class MainWindow
{
    readonly ComboBox _paragraphBox = new() { Width = 150 };
    bool _paragraphSync;

    /// <summary>The selection in the text as the user made it. A click on a button of the bar moves the focus away, and the text box then forgets its selection.</summary>
    (int Start, int End) _docSel;

    /// <summary>Back into the text with the selection as the user left it, before a button of the bar works on it.</summary>
    void RestoreDocSelection()
    {
        if (DocReadBtn.IsChecked == true) return;
        var length = (DocBodyBox.Text ?? "").Length;
        var (start, end) = (Math.Clamp(_docSel.Start, 0, length), Math.Clamp(_docSel.End, 0, length));
        DocBodyBox.Focus();
        DocBodyBox.SelectionStart = start; DocBodyBox.SelectionEnd = end;
        _docSel = (start, end);
    }

    FontFamily PageFont => this.TryFindResource("UiSerif", ActualThemeVariant, out var f) && f is FontFamily ff ? ff : ScriptureFont;

    void InitDocEditor()
    {
        BuildRibbon();
        DocReadBtn.IsCheckedChanged += (_, _) => SetReadMode(DocReadBtn.IsChecked == true);
        DocBodyBox.TextChanged += (_, _) => UpdateDocStatus();
        DocBodyBox.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBox.CaretIndexProperty) SyncParagraphBox();
            // only while the box has the focus: when it loses the focus it clears its selection, and that must not count
            if (DocBodyBox.IsFocused && (e.Property == TextBox.SelectionStartProperty || e.Property == TextBox.SelectionEndProperty))
                _docSel = (Math.Min(DocBodyBox.SelectionStart, DocBodyBox.SelectionEnd), Math.Max(DocBodyBox.SelectionStart, DocBodyBox.SelectionEnd));
        };
        DocEditorPanel.AddHandler(KeyDownEvent, OnDocKey, RoutingStrategies.Tunnel);
        InitDocDrag();
        DocAutoSaveBtn.IsChecked = _settings.AutoSaveDocs;
        DocAutoSaveBtn.IsCheckedChanged += (_, _) => { _settings.AutoSaveDocs = DocAutoSaveBtn.IsChecked == true; SaveSettings(); ShowAutoSaveState(); };
        ShowAutoSaveState();
    }

    /// <summary>The switch says in words and with a filled or empty dot whether documents save themselves.</summary>
    void ShowAutoSaveState()
    {
        var on = DocAutoSaveBtn.IsChecked == true;
        var green = new SolidColorBrush(Color.Parse("#30A46C"));
        var dot = new Avalonia.Controls.Shapes.Ellipse { Width = 9, Height = 9, StrokeThickness = 1.5, Stroke = on ? green : Brushes.Gray, Fill = on ? green : null, VerticalAlignment = VerticalAlignment.Center };
        DocAutoSaveBtn.Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7, Children = { dot, new TextBlock { Text = on ? L("Auto-Speichern: an") : L("Auto-Speichern: aus"), VerticalAlignment = VerticalAlignment.Center } } };
    }

    void OnDocKey(object? sender, KeyEventArgs e)
    {
        var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        if (ctrl && e.Key == Key.S) { e.Handled = true; SaveDocument(); return; }
        if (ctrl && e.Key == Key.E) { e.Handled = true; DocReadBtn.IsChecked = DocReadBtn.IsChecked != true; return; }
        if (DocTitleBox.IsFocused && e.Key == Key.Enter) { e.Handled = true; DocReadBtn.IsChecked = false; DocBodyBox.Focus(); DocBodyBox.CaretIndex = 0; return; }
        if (!DocBodyBox.IsFocused) return;
        if (ctrl && e.Key == Key.B) { e.Handled = true; Wrap("**"); return; }
        if (ctrl && e.Key == Key.I) { e.Handled = true; Wrap("*"); return; }
        if (DocEditorKey(e) is { } action) { e.Handled = true; action(); return; }
        if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.None && ContinueList()) e.Handled = true;
    }

    /// <summary>
    /// The formatting keys of the editor (they win over the program's own shortcuts while the text is focused, see OnShortcut):
    /// Ctrl+0 normal text, Ctrl+1/2/3 headings, Ctrl+Shift+8 list, Ctrl+Shift+7 numbered list, Ctrl+Shift+9 quote, Ctrl+Shift+- rule.
    /// </summary>
    Action? DocEditorKey(KeyEventArgs e)
    {
        var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control); var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        if (!ctrl || e.KeyModifiers.HasFlag(KeyModifiers.Alt)) return null;
        if (!shift)
            return e.Key switch
            {
                Key.D0 or Key.NumPad0 => () => SetBlock(""),
                Key.D1 or Key.NumPad1 => () => SetBlock("#"),
                Key.D2 or Key.NumPad2 => () => SetBlock("##"),
                Key.D3 or Key.NumPad3 => () => SetBlock("###"),
                _ => null,
            };
        return e.Key switch
        {
            Key.D8 => () => SetBlock("-"),
            Key.D7 => () => SetBlock("1."),
            Key.D9 => () => SetBlock(">"),
            Key.OemMinus or Key.Subtract => InsertRule,
            _ => null,
        };
    }

    /// <summary>The gestures (as the program writes them) that belong to the document editor while its text is focused.</summary>
    static readonly HashSet<string> DocEditorGestures =
    [
        "Ctrl+B", "Ctrl+I", "Ctrl+S", "Ctrl+E", "Ctrl+D0", "Ctrl+D1", "Ctrl+D2", "Ctrl+D3", "Ctrl+NumPad0", "Ctrl+NumPad1", "Ctrl+NumPad2", "Ctrl+NumPad3",
        "Ctrl+Shift+D7", "Ctrl+Shift+D8", "Ctrl+Shift+D9", "Ctrl+Shift+Minus",
    ];

    // ---- the bar --------------------------------------------------------------------------------------------------------

    static readonly (string Name, string Kind)[] Paragraphs =
    [
        (L("Text"), ""), (L("Überschrift 1"), "#"), (L("Überschrift 2"), "##"), (L("Überschrift 3"), "###"), (L("Zitat"), ">"), (L("Aufzählung"), "-"), (L("Nummerierung"), "1."),
    ];

    Button RibbonButton(object content, string tip, Action click, double width = 32)
    {
        var b = new Button { Classes = { "tool" }, Width = width, Height = 30, Padding = new(0), Content = content, Margin = new(0, 1, 2, 1) };
        ToolTip.SetTip(b, tip);
        b.Click += (_, _) => { RestoreDocSelection(); click(); if (DocReadBtn.IsChecked != true) DocBodyBox.Focus(); };
        return b;
    }

    static Control RibbonSeparator() => new Border { Width = 1, Height = 18, Margin = new(6, 0), VerticalAlignment = VerticalAlignment.Center, Opacity = 0.35, Background = Brushes.Gray };

    void BuildRibbon()
    {
        _paragraphBox.ItemsSource = Paragraphs.Select(p => p.Name).ToList();
        _paragraphBox.SelectedIndex = 0;
        _paragraphBox.SelectionChanged += (_, _) =>
        {
            if (_paragraphSync || _paragraphBox.SelectedIndex < 0) return;
            var kind = Paragraphs[_paragraphBox.SelectedIndex].Kind;   // read first: going back into the text updates this box
            RestoreDocSelection();
            SetBlock(kind);
            DocBodyBox.Focus();
        };
        ToolTip.SetTip(_paragraphBox, L("Absatzformat der Zeile, in der die Schreibmarke steht (Strg+0 Text, Strg+1/2/3 Überschrift)"));
        DocRibbon.Children.Add(new Border { Classes = { "pill" }, Child = _paragraphBox, Margin = new(0, 1, 6, 1) });

        DocRibbon.Children.Add(RibbonButton(new TextBlock { Text = "B", FontWeight = FontWeight.Bold }, L("Fett (Strg+B)"), () => Wrap("**")));
        DocRibbon.Children.Add(RibbonButton(new TextBlock { Text = "I", FontStyle = FontStyle.Italic, FontFamily = PageFont, FontSize = 16 }, L("Kursiv (Strg+I)"), () => Wrap("*")));
        DocRibbon.Children.Add(RibbonSeparator());
        DocRibbon.Children.Add(RibbonButton("•≡", L("Aufzählung (Strg+Umschalt+8)"), () => SetBlock("-")));
        DocRibbon.Children.Add(RibbonButton("1.", L("Nummerierte Liste (Strg+Umschalt+7)"), () => SetBlock("1.")));
        DocRibbon.Children.Add(RibbonButton("❝", L("Zitat (Strg+Umschalt+9)"), () => SetBlock(">")));
        DocRibbon.Children.Add(RibbonButton("—", L("Trennlinie (Strg+Umschalt+−)"), InsertRule));
        DocRibbon.Children.Add(RibbonSeparator());

        var verse = RibbonButton(L("＋ Vers"), L("Bibelstelle als Zitat oder Link einfügen"), () => { }, 74);
        verse.Flyout = BuildVerseFlyout();
        DocRibbon.Children.Add(verse);
        var link = RibbonButton("[[ ]]", L("Auf ein anderes Dokument verlinken"), () => { }, 46);
        link.Click += (_, _) => ShowLinkMenu(link);
        DocRibbon.Children.Add(link);
        DocRibbon.Children.Add(RibbonSeparator());
        DocRibbon.Children.Add(RibbonButton("↶", L("Rückgängig (Strg+Z)"), () => DocBodyBox.Undo()));
        DocRibbon.Children.Add(RibbonButton("↷", L("Wiederholen (Strg+Y)"), () => DocBodyBox.Redo()));
    }

    /// <summary>
    /// Link to another document: pick it from the list; the text of the link is what is selected in the document (or what is typed in the
    /// box), so a link can read "klicke hier" and still lead to the document. Written as [[Title|text]].
    /// </summary>
    void ShowLinkMenu(Control anchor)
    {
        var all = DocBodyBox.Text ?? "";
        var from = Math.Clamp(_docSel.Start, 0, all.Length); var to = Math.Clamp(_docSel.End, from, all.Length);
        // the cursor stands inside a link: this changes that link (its text, or where it leads, also for a link whose document is gone)
        var existing = DocumentLinks.Links(all).FirstOrDefault(l => from >= l.Index && to <= l.Index + l.Length);
        if (existing is not null) { from = existing.Index; to = existing.Index + existing.Length; }
        var documents = _vault.GetDocuments();
        var current = existing is null ? null : VaultStore.Resolve(documents, existing);

        var text = new TextBox
        {
            Text = existing is not null ? existing.Text : all[from..to].ReplaceLineEndings(" ").Trim(),
            PlaceholderText = L("z. B. klicke hier"),
        };
        var list = new StackPanel { Spacing = 2 };
        var content = new StackPanel { Spacing = 6, Width = 340 };
        var fly = new Flyout { Content = content };

        void Write(Document target)
        {
            fly.Hide();
            var link = DocumentLinks.LinkText(target.Id, target.Title, text.Text ?? "");
            if (DocReadBtn.IsChecked == true) DocReadBtn.IsChecked = false;
            DocBodyBox.Focus();
            Splice(from, to, link);
            Avalonia.Threading.Dispatcher.UIThread.Post(() => DocBodyBox.Focus());   // closing the menu hands the focus around once more
        }

        content.Children.Add(new TextBlock { Text = existing is not null ? L("Link ändern") : L("Auf ein anderes Dokument verlinken"), FontWeight = FontWeight.SemiBold });
        content.Children.Add(new TextBlock { Text = L("Angezeigter Text (leer: der Titel des Dokuments)"), Classes = { "caption" }, TextWrapping = TextWrapping.Wrap });
        content.Children.Add(text);
        if (current is not null)
        {
            var keep = new Button { Content = L("Text übernehmen"), Classes = { "primary" }, HorizontalAlignment = HorizontalAlignment.Left };
            keep.Click += (_, _) => Write(current);
            text.KeyDown += (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; Write(current); } };
            content.Children.Add(new TextBlock { Text = L("Führt zu: {0}", current.Title), Classes = { "caption" }, TextWrapping = TextWrapping.Wrap });
            content.Children.Add(keep);
        }
        content.Children.Add(new TextBlock { Text = current is null ? L("Dokument wählen:") : L("Oder zu einem anderen Dokument führen:"), Classes = { "caption" }, TextWrapping = TextWrapping.Wrap, Margin = new(0, 4, 0, 0) });
        content.Children.Add(new ScrollViewer { MaxHeight = 280, Content = list, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled });

        foreach (var d in documents.Where(x => x.Id != _doc?.Id).OrderBy(x => x.Title, StringComparer.CurrentCultureIgnoreCase))
        {
            var doc = d;
            var b = PickRow(new TextBlock { Text = d.Title, TextWrapping = TextWrapping.Wrap });
            b.Click += (_, _) => Write(doc);
            list.Children.Add(b);
        }
        if (list.Children.Count == 0) list.Children.Add(new TextBlock { Text = L("Noch keine anderen Dokumente"), Classes = { "caption" } });
        fly.ShowAt(anchor);
        Avalonia.Threading.Dispatcher.UIThread.Post(() => text.Focus());
    }

    /// <summary>A line in a list to pick from: as wide as the list, the content on the left.</summary>
    Button PickRow(Control content) => new()
    {
        Content = content, Classes = { "ghost" }, Foreground = Res("BsText"),
        HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new(8, 5),
    };

    // ---- verses ---------------------------------------------------------------------------------------------------------

    Flyout BuildVerseFlyout()
    {
        var reference = new TextBox { PlaceholderText = L("z. B. Joh 3,16-18"), Width = 300 };
        var info = new TextBlock { Classes = { "caption" }, TextWrapping = TextWrapping.Wrap, MaxWidth = 250 };
        var quote = new Button { Content = L("Als Zitat einfügen"), Classes = { "primary" }, Margin = new(0, 0, 6, 0) };
        var link = new Button { Content = L("Nur als Link") };
        var clicked = new Button { HorizontalAlignment = HorizontalAlignment.Left };
        var content = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                new TextBlock { Text = L("Bibelstelle einfügen"), FontWeight = FontWeight.SemiBold }, reference,
                new WrapPanel { Children = { quote, link } }, clicked, info,
            },
        };
        var fly = new Flyout { Content = content };
        content.Children.Add(BuildSavedVersesPicker(fly, 300));
        fly.Opening += (_, _) =>
        {
            info.Text = "";
            clicked.IsVisible = _selected is not null;
            if (_selected is int v) clicked.Content = L("Angeklickten Vers einfügen: {0}", Label(new VerseRef(_curBook, _curChapter, v)));
            Avalonia.Threading.Dispatcher.UIThread.Post(() => reference.Focus());
        };
        async Task Insert(bool asQuote)
        {
            if (PassageParser.Parse(reference.Text ?? "", out _) is not { Count: > 0 } passages) { info.Text = L("Das ist keine Bibelstelle. Beispiel: Joh 3,16 oder Ps 23."); return; }
            var text = asQuote ? await QuotePassagesAsync(passages) : string.Join("; ", passages.Select(PassageLabel)) + " ";
            if (text is null) { info.Text = L("Der Text konnte nicht geladen werden."); return; }
            fly.Hide(); reference.Text = "";
            InsertIntoDoc(text, null);
        }
        quote.Click += async (_, _) => await Insert(true);
        link.Click += async (_, _) => await Insert(false);
        reference.KeyDown += async (_, e) => { if (e.Key == Key.Enter) await Insert(true); };
        clicked.Click += (_, _) =>
        {
            if (_selected is not int v) return;
            fly.Hide();
            InsertIntoDoc(L("> {0}\n> — {1}\n\n", VerseTextOf(v).Trim(), Label(new VerseRef(_curBook, _curChapter, v))), null);
        };
        return fly;
    }

    string PassageLabel(PassageRef p)
    {
        var s = $"{BookName(p.Book)} {p.Chapter}";
        if (p.FromVerse is int f) s += "," + f + (p.ToVerse is int t && t != f ? "–" + t : "");
        return s;
    }

    /// <summary>The verses of the passages from the translation in use, as a Markdown quote with its source (at most 40 verses).</summary>
    async Task<string?> QuotePassagesAsync(IReadOnlyList<PassageRef> passages)
    {
        if (TranslationBox.SelectedItem is not TranslationInfo t) return null;
        var sb = new System.Text.StringBuilder();
        try
        {
            foreach (var p in passages)
            {
                var lines = new List<string>(); var count = 0;
                for (var c = p.Chapter; c <= (p.ToChapter ?? p.Chapter) && count < 40; c++)
                {
                    var ch = await _provider.GetChapterAsync(t.Id, p.Book, c);
                    foreach (var v in ch.Verses)
                    {
                        if (p.FromVerse is int from && p.ToChapter is null && (v.Number < from || v.Number > (p.ToVerse ?? from))) continue;
                        if (p.FromVerse is int f2 && p.ToChapter is int tc && ((c == p.Chapter && v.Number < f2) || (c == tc && v.Number > (p.ToVerse ?? int.MaxValue)))) continue;
                        lines.Add(v.Text.Trim()); count++;
                        if (count >= 40) break;
                    }
                }
                if (lines.Count == 0) continue;
                // several verses run on as one quote; a single verse needs no numbers
                sb.Append("> ").Append(string.Join(" ", lines)).Append('\n').Append("> — ").Append(PassageLabel(p)).Append(" (").Append(t.ShortName).Append(")\n\n");
            }
        }
        catch { return null; }
        return sb.Length == 0 ? null : sb.ToString();
    }

    // ---- editing helpers (all go through the text box's own selection, so undo keeps working) -----------------------------

    void Splice(int start, int end, string text, int? selectFrom = null, int? selectTo = null)
    {
        DocBodyBox.SelectionStart = start; DocBodyBox.SelectionEnd = end;
        DocBodyBox.SelectedText = text;
        var a = selectFrom ?? start + text.Length; var b = selectTo ?? a;
        DocBodyBox.SelectionStart = a; DocBodyBox.SelectionEnd = b;
        if (a == b) DocBodyBox.CaretIndex = a;
        _docSel = (Math.Min(a, b), Math.Max(a, b));
    }

    /// <summary>Start and end (without the line break) of the lines that the selection touches.</summary>
    (int Start, int End) LineRange(int from, int to)
    {
        var t = DocBodyBox.Text ?? "";
        from = Math.Clamp(from, 0, t.Length); to = Math.Clamp(to, from, t.Length);
        var s = from == 0 ? 0 : t.LastIndexOf('\n', from - 1) + 1;
        var e = to >= t.Length ? t.Length : t.IndexOf('\n', to);
        if (e < 0) e = t.Length;
        if (e > s && t[e - 1] == '\r') e--;
        return (s, e);
    }

    [GeneratedRegex(@"^(?<ind>\s*)(?:(?<h>#{1,3})\s+|(?<q>>)\s?|(?<b>[-*•])\s+|(?<n>\d+)[.)]\s+)")]
    private static partial Regex BlockStart();

    static (string Kind, string Text) SplitBlock(string line)
    {
        var m = BlockStart().Match(line);
        if (!m.Success) return ("", line);
        var kind = m.Groups["h"].Success ? m.Groups["h"].Value : m.Groups["q"].Success ? ">" : m.Groups["b"].Success ? "-" : "1.";
        return (kind, line[m.Length..]);
    }

    /// <summary>Gives the lines at the caret a paragraph style; the same style again takes it back off.</summary>
    void SetBlock(string kind)
    {
        var from = Math.Min(DocBodyBox.SelectionStart, DocBodyBox.SelectionEnd); var to = Math.Max(DocBodyBox.SelectionStart, DocBodyBox.SelectionEnd);
        var (s, e) = LineRange(from, to);
        var t = DocBodyBox.Text ?? "";
        var lines = t[s..e].Split('\n').Select(l => l.TrimEnd('\r')).ToArray();
        var nonEmpty = lines.Where(l => l.Trim().Length > 0).ToList();
        if (nonEmpty.Count > 0 && nonEmpty.All(l => SplitBlock(l).Kind == kind)) kind = "";
        var n = 1;
        var result = lines.Select(l =>
        {
            var (_, text) = SplitBlock(l);
            if (kind.Length == 0) return text;
            if (l.Trim().Length == 0 && lines.Length > 1) return l;
            return (kind == "1." ? $"{n++}. " : kind + " ") + text;
        }).ToArray();
        var joined = string.Join("\n", result);
        Splice(s, e, joined, s, s + joined.Length);
        if (joined.Length > 0 && lines.Length == 1) { DocBodyBox.SelectionStart = DocBodyBox.SelectionEnd = s + joined.Length; DocBodyBox.CaretIndex = s + joined.Length; }
        SyncParagraphBox();
    }

    void Wrap(string mark)
    {
        var from = Math.Min(DocBodyBox.SelectionStart, DocBodyBox.SelectionEnd); var to = Math.Max(DocBodyBox.SelectionStart, DocBodyBox.SelectionEnd);
        var t = DocBodyBox.Text ?? "";
        // spaces at the edges stay outside the stars ("**Wort **" is no bold in other Markdown programs)
        if (to > from && t[from..to].Trim().Length > 0)
        {
            while (char.IsWhiteSpace(t[from])) from++;
            while (char.IsWhiteSpace(t[to - 1])) to--;
        }
        var sel = t[from..to];
        if (sel.Length == 0) { Splice(from, to, mark + mark, from + mark.Length, from + mark.Length); return; }
        // the marks stand right around the selection (the text without them was selected): take them away there
        static int StarsBefore(string s, int at) { var n = 0; while (at - 1 - n >= 0 && s[at - 1 - n] == '*') n++; return n; }
        static int StarsAfter(string s, int at) { var n = 0; while (at + n < s.Length && s[at + n] == '*') n++; return n; }
        var around = mark == "**" ? StarsBefore(t, from) >= 2 && StarsAfter(t, to) >= 2
            : StarsBefore(t, from) % 2 == 1 && StarsAfter(t, to) % 2 == 1;   // a single * is italic; ** or *** around it is not
        if (around)
        {
            Splice(from - mark.Length, to + mark.Length, sel, from - mark.Length, from - mark.Length + sel.Length);
            return;
        }
        var wrapped = sel.Length > 2 * mark.Length && sel.StartsWith(mark, StringComparison.Ordinal) && sel.EndsWith(mark, StringComparison.Ordinal) && !(mark == "*" && sel.StartsWith("**", StringComparison.Ordinal));
        if (wrapped) { var inner = sel[mark.Length..^mark.Length]; Splice(from, to, inner, from, from + inner.Length); }
        else Splice(from, to, mark + sel + mark, from, from + sel.Length + 2 * mark.Length);
    }

    void InsertRule()
    {
        var at = Math.Min(DocBodyBox.SelectionStart, DocBodyBox.SelectionEnd);
        var (s, e) = LineRange(at, at);
        var t = DocBodyBox.Text ?? "";
        var empty = t[s..e].Trim().Length == 0;
        Splice(empty ? s : e, empty ? e : e, (empty ? "" : "\n") + "---\n\n");
    }

    /// <summary>Enter at the end of a list or quote line starts the next item; Enter on an empty item ends the list.</summary>
    bool ContinueList()
    {
        if (DocBodyBox.SelectionStart != DocBodyBox.SelectionEnd) return false;
        var caret = DocBodyBox.CaretIndex;
        var (s, e) = LineRange(caret, caret);
        var t = DocBodyBox.Text ?? "";
        var line = t[s..e];
        var m = BlockStart().Match(line);
        if (!m.Success || m.Groups["h"].Success || caret < s + m.Length) return false;   // headings do not continue; inside the marker Enter works as usual
        if (line[m.Length..].Trim().Length == 0) { Splice(s, e, "", s, s); return true; }
        var next = m.Groups["n"].Success ? $"{(long.TryParse(m.Groups["n"].Value, out var number) ? number + 1 : 1)}. ": m.Groups["q"].Success ? "> " : m.Groups["b"].Value + " ";
        Splice(caret, caret, "\n" + m.Groups["ind"].Value + next);
        return true;
    }

    void SyncParagraphBox()
    {
        if (!DocBodyBox.IsVisible) return;
        var t = DocBodyBox.Text ?? "";
        var caret = Math.Clamp(DocBodyBox.CaretIndex, 0, t.Length);
        var (s, e) = LineRange(caret, caret);
        var kind = SplitBlock(t[s..e]).Kind;
        var index = Math.Max(0, Array.FindIndex(Paragraphs, p => p.Kind == kind));
        if (_paragraphBox.SelectedIndex == index) return;
        _paragraphSync = true; _paragraphBox.SelectedIndex = index; _paragraphSync = false;
    }

    void UpdateDocStatus()
    {
        var text = DocBodyBox.Text ?? "";
        var words = text.Length == 0 ? 0 : Regex.Matches(text, @"\S+").Count;
        DocStatus.Text = L("{0} Wörter · {1} Zeichen · Strg+S speichert, Strg+E wechselt zur Ansicht", words, text.Length);
    }

    // ---- finished view ---------------------------------------------------------------------------------------------------

    void SetReadMode(bool on)
    {
        DocBodyBox.IsVisible = !on; DocPreview.IsVisible = on;
        // the finished view is for reading: nothing of the editing around it
        DocRibbonBar.IsVisible = DocMetaRow.IsVisible = DocSortExpander.IsVisible = DocSaveBtn.IsVisible = DocAutoSaveBtn.IsVisible = !on;
        DocTitleBox.IsReadOnly = on;
        DocReadBtn.Content = on ? L("Schreiben") : L("Lesen");
        if (on) { RenderPreview(); AnimateIn(DocPreview, 0, 8); }
        else SyncParagraphBox();
    }

    static readonly double[] HeadingSizes = [0, 30, 24, 20];

    List<Document> _previewDocs = [];

    void RenderPreview()
    {
        DocPreview.Children.Clear();
        _previewDocs = _vault.GetDocuments();
        var md = Regex.Replace(DocumentLinks.WithoutTitleHeading(DocBodyBox.Text ?? "", DocTitleBox.Text ?? ""), "<!--.*?-->", "", RegexOptions.Singleline).ReplaceLineEndings("\n");
        var para = new List<string>();
        void Flush() { if (para.Count > 0) { DocPreview.Children.Add(Inline(string.Join("\n", para), 18)); para.Clear(); } }
        foreach (var raw in md.Split('\n'))
        {
            var line = raw.TrimEnd();
            if (line.Length == 0) { Flush(); continue; }
            if (Regex.IsMatch(line, @"^\s*([-*_])\1{2,}\s*$")) { Flush(); DocPreview.Children.Add(new Border { Height = 1, Margin = new(0, 8), Opacity = 0.5, Background = Res("BsBorder") }); continue; }
            var m = BlockStart().Match(line);
            if (m.Success && m.Groups["h"].Success)
            {
                Flush();
                var level = m.Groups["h"].Length;
                var h = Inline(line[m.Length..], HeadingSizes[level], bold: true);
                h.Margin = new(0, level == 1 ? 14 : 10, 0, 2);
                DocPreview.Children.Add(h);
            }
            else if (m.Success && m.Groups["q"].Success)
            {
                Flush();
                var q = Inline(line[m.Length..], 18, italic: true);
                var bar = new Border { BorderThickness = new(3, 0, 0, 0), BorderBrush = Res("BsAccent"), Padding = new(14, 4, 4, 4), Margin = new(8, 2, 0, 2), Child = q };
                // the source line of a quote ("— Joh 3,16") is quieter
                if (line[m.Length..].TrimStart().StartsWith('—')) q.Opacity = 0.75;
                DocPreview.Children.Add(bar);
            }
            else if (m.Success)
            {
                Flush();
                var marker = m.Groups["n"].Success ? m.Groups["n"].Value + "." : "•";
                var grid = new Grid { ColumnDefinitions = new("28,*"), Margin = new(m.Groups["ind"].Length * 8 + 6, 0, 0, 0) };
                grid.Children.Add(new TextBlock { Text = marker, FontSize = 18, FontFamily = PageFont, Opacity = 0.7 });
                var body = Inline(line[m.Length..], 18);
                Grid.SetColumn(body, 1);
                grid.Children.Add(body);
                DocPreview.Children.Add(grid);
            }
            else para.Add(line);
        }
        Flush();
        if (DocPreview.Children.Count == 0) DocPreview.Children.Add(new TextBlock { Text = L("Noch nichts geschrieben."), Classes = { "caption" } });
    }

    [GeneratedRegex(@"\*\*(?<b>.+?)\*\*|(?<![\*\w])\*(?<i>[^\*\s][^\*]*?)\*(?![\*\w])|\[\[(?:#(?<id>\d+):)?(?<l>[^\]|#]+)(?:[|#](?<a>[^\]]*))?\]\]")]
    private static partial Regex InlineMarks();

    sealed class InlineBuilder(InlineCollection into)
    {
        public InlineCollection Into { get; } = into;
        public int Offset;
        public List<(int Start, int End, Action Click)> Links { get; } = [];
    }

    /// <summary>A paragraph as selectable text; verse references and [[links]] are colored runs that react to a click (found by character position).</summary>
    SelectableTextBlock Inline(string text, double size, bool bold = false, bool italic = false)
    {
        var block = new SelectableTextBlock { FontSize = size, FontFamily = PageFont, TextWrapping = TextWrapping.Wrap, LineHeight = size * 1.55, FontWeight = bold ? FontWeight.SemiBold : FontWeight.Normal };
        var builder = new InlineBuilder(block.Inlines!);
        AddInlines(builder, text, bold, italic);
        if (builder.Links.Count == 0) return block;
        Action? LinkAt(PointerEventArgs e)
        {
            var pt = e.GetPosition(block);
            if (pt.X < 0 || pt.Y < 0 || pt.Y > block.TextLayout.Height) return null;   // IsInside is unreliable with a line height set, so the text area is checked here
            var hit = block.TextLayout.HitTestPoint(pt);
            return builder.Links.FirstOrDefault(l => hit.TextPosition >= l.Start && hit.TextPosition < l.End).Click;
        }
        block.PointerMoved += (_, e) => block.Cursor = LinkAt(e) is null ? null : new Cursor(StandardCursorType.Hand);
        block.AddHandler(PointerReleasedEvent, (object? _, PointerReleasedEventArgs e) =>
        {
            if (e.InitialPressMouseButton != MouseButton.Left || !string.IsNullOrEmpty(block.SelectedText)) return;   // dragging to select text is not a click
            LinkAt(e)?.Invoke();
        }, Avalonia.Interactivity.RoutingStrategies.Bubble, handledEventsToo: true);
        return block;
    }

    void AddInlines(InlineBuilder b, string text, bool bold, bool italic)
    {
        var pos = 0;
        foreach (Match m in InlineMarks().Matches(text))
        {
            if (m.Index > pos) AddPlain(b, text[pos..m.Index], bold, italic);
            if (m.Groups["b"].Success) AddInlines(b, m.Groups["b"].Value, true, italic);
            else if (m.Groups["i"].Success) AddInlines(b, m.Groups["i"].Value, bold, true);
            else
            {
                // leads by the number of the document; shows the link's own text, or the title the document has now
                var link = new DocumentLinks.DocLink(long.TryParse(m.Groups["id"].Value, out var number) ? number : null, m.Groups["l"].Value.Trim(), m.Groups["a"].Value.Trim(), 0, 0);
                AddLink(b, link.Shown(VaultStore.Resolve(_previewDocs, link)?.Title), bold, italic, () =>
                {
                    if (VaultStore.Resolve(_vault.GetDocuments(), link) is { } target) OpenDocument(target);
                    else OpenDocument(new Document(0, link.Title, "", null, false, DateTime.Now, []));
                });
            }
            pos = m.Index + m.Length;
        }
        if (pos < text.Length) AddPlain(b, text[pos..], bold, italic);
    }

    void AddPlain(InlineBuilder b, string text, bool bold, bool italic)
    {
        var pos = 0;
        foreach (var (index, length, r) in DocumentLinks.VerseSpans(text))
        {
            if (index > pos) AddRun(b, text[pos..index], bold, italic);
            AddLink(b, text.Substring(index, length), bold, italic, () => _ = GoToAsync(r.Book, r.Chapter, r.Verse));
            pos = index + length;
        }
        if (pos < text.Length) AddRun(b, text[pos..], bold, italic);
    }

    static void AddRun(InlineBuilder b, string text, bool bold, bool italic)
    {
        b.Into.Add(new Run(text) { FontWeight = bold ? FontWeight.SemiBold : FontWeight.Normal, FontStyle = italic ? FontStyle.Italic : FontStyle.Normal });
        b.Offset += text.Length;
    }

    void AddLink(InlineBuilder b, string text, bool bold, bool italic, Action click)
    {
        b.Into.Add(new Run(text)
        {
            Foreground = Res("BsAccent"), TextDecorations = TextDecorations.Underline,
            FontWeight = bold ? FontWeight.SemiBold : FontWeight.Normal, FontStyle = italic ? FontStyle.Italic : FontStyle.Normal,
        });
        b.Links.Add((b.Offset, b.Offset + text.Length, click));
        b.Offset += text.Length;
    }
}
