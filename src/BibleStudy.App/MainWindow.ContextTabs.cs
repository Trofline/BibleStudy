using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using BibleStudy.Core.Models;
using BibleStudy.Core.Storage;

namespace BibleStudy.App;

/// <summary>
/// A cross reference in its context: the whole chapter in a tab of its own, with the verses of the reference standing out. The reader stays where it is,
/// and the tab can be dragged out like any other, so both passages can be read side by side.
/// </summary>
public partial class MainWindow
{
    const string ContextPrefix = "ctx:";
    const int MaxContextTabs = 6;   // the tab strip does not scroll: more would push tabs out of reach
    const string ContextIcon = "M4 6h16 M4 12h16 M4 18h10";

    sealed class ContextTab
    {
        public required Detached Work { get; init; }
        public required VerseRef Target { get; init; }
        public required int End { get; init; }
        public StackPanel Body { get; set; } = null!;
        public ScrollViewer Scroll { get; set; } = null!;
        public TextBlock Head { get; set; } = null!;
        public Button Prev { get; set; } = null!;
        public Button Next { get; set; } = null!;
        public Button Pop { get; set; } = null!;
        public Vector? KeepOffset { get; set; }
        public string Book { get; set; } = "";
        public int Chapter { get; set; }
        public bool ScrollToTarget { get; set; } = true;
        public string Drawn { get; set; } = "";
        public int Generation { get; set; }
    }

    readonly Dictionary<string, ContextTab> _contextTabs = [];

    static bool IsContextKey(string? key) => key?.StartsWith(ContextPrefix, StringComparison.Ordinal) == true;

    static string ContextKey(VerseRef target, int end) => $"{ContextPrefix}{target.Book}.{target.Chapter}.{target.Verse}.{end}";

    static (VerseRef Target, int End)? ParseContextKey(string key)
    {
        var parts = key[ContextPrefix.Length..].Split('.');
        return parts.Length == 4 && int.TryParse(parts[1], out var chapter) && int.TryParse(parts[2], out var verse) && int.TryParse(parts[3], out var end)
            ? (new VerseRef(parts[0], chapter, verse), end) : null;
    }

    /// <summary>What a click on a cross reference does: go there, or (as the user chose in the settings) open it in a tab.</summary>
    void FollowXref(VerseRef target, int end)
    {
        if (_settings.XrefClickOpensTab) OpenContextTab(target, end);
        else _ = GoToRangeAsync(target, end);
    }

    /// <summary>The right-click choices of a cross reference: always the tab, and going there when a click opens tabs.</summary>
    IEnumerable<MenuItem> XrefOpenItems(VerseRef target, int end)
    {
        var tab = new MenuItem { Header = L("Im Zusammenhang in neuem Reiter öffnen") };
        tab.Click += (_, _) => OpenContextTab(target, end);
        yield return tab;
        if (!_settings.XrefClickOpensTab) yield break;
        var go = new MenuItem { Header = L("Im Lesefenster dorthin gehen") };
        go.Click += (_, _) => _ = GoToRangeAsync(target, end);
        yield return go;
    }

    void InitContextTabs()
    {
        XrefTabCheck.IsChecked = _settings.XrefClickOpensTab;
        XrefTabCheck.IsCheckedChanged += (_, _) => { _settings.XrefClickOpensTab = XrefTabCheck.IsChecked == true; SaveSettings(); };
    }

    /// <summary>Opens the chapter of a cross reference in a tab; the same reference again brings its tab back to the verses.</summary>
    void OpenContextTab(VerseRef target, int end, bool show = true)
    {
        end = Math.Max(end, target.Verse);
        var key = ContextKey(target, end);
        if (_contextTabs.TryGetValue(key, out var open))
        {
            open.Book = target.Book; open.Chapter = target.Chapter; open.ScrollToTarget = true;
            FocusContextTab(open);
            return;
        }
        var oldest = _contextTabs.Values.FirstOrDefault(t => t.Work.Win is null && t.Work.Key != _activeWork && !InPane(t.Work.Key));
        if (_contextTabs.Values.Count(t => t.Work.Win is null) >= MaxContextTabs && oldest is not null) Restore(oldest.Work);

        var work = new Detached { Items = [], Key = key, Title = ContextLabel(target, end), Panel = new DockPanel(), Host = new Grid(), Indices = [], Side = false, Current = -1, Icon = ContextIcon };
        var tab = new ContextTab { Work = work, Target = target, End = end, Book = target.Book, Chapter = target.Chapter };
        work.Rebuild = () =>
        {
            tab.KeepOffset = tab.Scroll?.Offset;
            BuildContextPanel(tab);
            Dispatcher.UIThread.Post(() => { if (_contextTabs.ContainsKey(key)) _ = DrawContextTabAsync(tab); }, DispatcherPriority.Background);
        };
        BuildContextPanel(tab);
        _windows[key] = work;
        _contextTabs[key] = tab;
        DockWork(work);
        if (show) ShowWork(key); else RebuildTabStrip();
    }

    /// <summary>
    /// The controls of a tab. They are made anew each time it changes between the tab strip and a window of its own: moving them to the other window
    /// crashes the layout when a pass of the old window is still pending for them ("wrong LayoutManager"), and that cannot be ruled out here.
    /// </summary>
    void BuildContextPanel(ContextTab tab)
    {
        var work = tab.Work; var key = work.Key; var target = tab.Target;
        var head = new TextBlock { FontWeight = FontWeight.SemiBold, FontSize = 16, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new(10, 0, 10, 0), Text = work.Title };
        Button Arrow(string data, string tip)
        {
            var b = new Button { Classes = { "tool" }, Content = new Icon { Data = Geometry.Parse(data), IconSize = 14 } };
            ToolTip.SetTip(b, tip);
            return b;
        }
        var prev = Arrow("M15 18l-6-6 6-6", L("Vorheriges Kapitel"));
        var next = Arrow("M9 6l6 6-6 6", L("Nächstes Kapitel"));
        var read = new Button { Classes = { "ghost" }, Content = L("Im Lesefenster öffnen"), VerticalAlignment = VerticalAlignment.Center };
        var pop = new Button { Classes = { "ghost" }, VerticalAlignment = VerticalAlignment.Center, Margin = new(6, 0, 0, 0) };
        var right = new StackPanel { Orientation = Orientation.Horizontal, Children = { read, pop } };
        DockPanel.SetDock(right, Dock.Right); DockPanel.SetDock(prev, Dock.Left); DockPanel.SetDock(next, Dock.Left);
        var bar = new DockPanel { Margin = new(14, 10, 14, 6), Children = { right, prev, next, head } };
        DockPanel.SetDock(bar, Dock.Top);
        var body = new StackPanel { Spacing = 2, Margin = new(16, 6, 16, 60) };
        var scroll = new ScrollViewer { Content = body, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };

        tab.Body = body; tab.Scroll = scroll; tab.Head = head; tab.Prev = prev; tab.Next = next; tab.Pop = pop; tab.Drawn = "";
        work.Panel = new DockPanel { Children = { bar, scroll } };

        prev.Click += (_, _) => { tab.Chapter--; tab.ScrollToTarget = true; _ = DrawContextTabAsync(tab); };
        next.Click += (_, _) => { tab.Chapter++; tab.ScrollToTarget = true; _ = DrawContextTabAsync(tab); };
        read.Click += (_, _) =>
        {
            _ = GoToAsync(tab.Book, tab.Chapter, tab.Book == target.Book && tab.Chapter == target.Chapter ? target.Verse : null);
            if (work.Win is not null) Activate();
        };
        // not in the click itself: the button is replaced by it
        pop.Click += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            if (!_contextTabs.ContainsKey(key)) return;
            if (work.Win is { } win) { work.Redock = true; win.Close(); }   // back into the tab strip
            else TearOutWork(key, null);
            UpdatePopButton(tab);
        }, DispatcherPriority.Background);
        UpdatePopButton(tab);
    }

    void UpdatePopButton(ContextTab tab) => tab.Pop.Content = tab.Work.Win is null ? L("In eigenem Fenster") : L("Zurück in die Reiter");

    void FocusContextTab(ContextTab tab)
    {
        if (tab.Work.Win is { } win) { win.Activate(); _ = DrawContextTabAsync(tab); }
        else ShowWork(tab.Work.Key);
    }

    string ContextLabel(VerseRef target, int end) => Label(target) + (end > target.Verse ? "–" + end : "");

    /// <summary>Draws the chapter of a tab; what is on screen stays as it is (and where it is) when nothing changed since.</summary>
    async Task DrawContextTabAsync(ContextTab tab)
    {
        var generation = ++tab.Generation;
        var t = TranslationBox.SelectedItem as TranslationInfo;
        var translation = t?.Id ?? _settings.TranslationId;
        var atTarget = tab.Book == tab.Target.Book && tab.Chapter == tab.Target.Chapter;
        tab.Head.Text = atTarget ? ContextLabel(tab.Target, tab.End) : $"{BookName(tab.Book)} {tab.Chapter}";
        tab.Prev.IsEnabled = tab.Chapter > 1;
        tab.Next.IsEnabled = _books.FirstOrDefault(b => b.Id == tab.Book) is not { } book || tab.Chapter < book.NumberOfChapters;
        if (tab.Work.Title != tab.Head.Text)
        {
            tab.Work.Title = tab.Head.Text;
            if (tab.Work.Win is { } win) win.Title = L("BibleStudy – {0}", tab.Head.Text);
            RebuildTabStrip();
        }
        if (string.IsNullOrEmpty(translation)) return;   // right at the start, before the translations are there: drawn when one is chosen
        Chapter chapter;
        try { chapter = await _provider.GetChapterAsync(translation, tab.Book, tab.Chapter); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException or System.Text.Json.JsonException or IOException or KeyNotFoundException)
        {
            if (generation != tab.Generation) return;
            tab.Body.Children.Clear(); tab.Drawn = "";
            tab.Body.Children.Add(new TextBlock { Text = L("Kapitel nicht verfügbar: ") + ex.Message, TextWrapping = TextWrapping.Wrap });
            return;
        }
        if (generation != tab.Generation) return;

        var marks = _vault.GetMarks(tab.Book, tab.Chapter);
        var drawn = string.Join("|", translation, tab.Book, tab.Chapter, _fontSize, _settings.LineSpacing, _settings.VerseNumbers, ActualThemeVariant, string.Join(",", marks.Select(m => $"{m.Id}{m.Color}{m.Start}{m.End}")));
        if (drawn != tab.Drawn)
        {
            tab.Drawn = drawn;
            var offset = tab.KeepOffset ?? tab.Scroll.Offset; tab.KeepOffset = null;
            tab.Body.Children.Clear();
            foreach (var verse in chapter.Verses) tab.Body.Children.Add(ContextVerse(verse, t, marks, atTarget && verse.Number >= tab.Target.Verse && verse.Number <= tab.End));
            if (!tab.ScrollToTarget) Dispatcher.UIThread.Post(() => tab.Scroll.Offset = offset, DispatcherPriority.Background);
        }
        if (tab.ScrollToTarget)
        {
            tab.ScrollToTarget = false;
            var first = atTarget ? tab.Target.Verse : 0;
            Dispatcher.UIThread.Post(() =>
            {
                var box = tab.Body.Children.FirstOrDefault(c => Equals(c.Tag, first));
                tab.Scroll.Offset = new Vector(0, box?.TranslatePoint(new Point(0, 0), tab.Body) is { } p ? Math.Max(0, p.Y - 60) : 0);
            }, DispatcherPriority.Background);
        }
    }

    /// <summary>One verse of a tab: the words as in the reader (colors of Jesus' words, your marks) but read-only; the verses of the reference stand out.</summary>
    Border ContextVerse(Verse verse, TranslationInfo? t, List<Mark> marks, bool target)
    {
        var size = _fontSize;
        var tb = new SelectableTextBlock
        {
            TextWrapping = TextWrapping.Wrap, FontSize = size, LineHeight = size * _settings.LineSpacing, FontFamily = ScriptureFont,
            LetterSpacing = _settings.ReadableText ? size * 0.05 : 0, TextAlignment = _settings.Justify ? TextAlignment.Justify : TextAlignment.Left,
            FlowDirection = t?.TextDirection == "rtl" ? FlowDirection.RightToLeft : FlowDirection.LeftToRight,
        };
        var number = new Run(_settings.VerseNumbers == "Hidden" ? "" : verse.Number + " ") { FontSize = _settings.VerseNumbers == "Normal" ? size * 0.8 : 14, FontWeight = FontWeight.Bold, BaselineAlignment = _settings.VerseNumbers == "Normal" ? BaselineAlignment.Baseline : BaselineAlignment.Top };
        number.Bind(TextElement.ForegroundProperty, this.GetResourceObservable("BsVerseNumber"));
        tb.Inlines!.Add(number);

        var length = MarkLogic.RawText(verse.Segments).Length;
        var parts = marks.Where(m => !m.WholeVerse && m.Covers(verse.Number) && m.TranslationId == t?.Id)
            .Select(m => (Mark: m, Part: MarkLogic.PartIn(m, verse.Number, length))).Where(x => x.Part is not null).Select(x => (x.Mark, x.Part!.Value.Start, x.Part!.Value.End)).ToList();
        var raw = 0;
        Run? last = null; Mark? lastMark = null; var lastJesus = false;
        foreach (var s in verse.Segments)
        {
            if (s.NoteId is not null) { tb.Inlines.Add(new Run(" ")); last = null; continue; }
            var cuts = new SortedSet<int> { 0, s.Text.Length };
            foreach (var p in parts) { cuts.Add(Math.Clamp(p.Start - raw, 0, s.Text.Length)); cuts.Add(Math.Clamp(p.End - raw, 0, s.Text.Length)); }
            var points = cuts.ToList();
            for (var i = 0; i + 1 < points.Count; i++)
            {
                var piece = s.Text[points[i]..points[i + 1]];
                var from = raw + points[i];
                var mark = parts.Where(p => p.Start <= from && p.End >= from + piece.Length).Select(p => p.Mark).LastOrDefault();
                // neighbours with the same background are one run: two backgrounds that touch leave a thin dark seam
                if (mark is not null && last is not null && ReferenceEquals(mark, lastMark) && lastJesus == s.WordsOfJesus) { last.Text += piece; continue; }
                var run = new Run(piece);
                if (s.WordsOfJesus && _settings.JesusRed && Color.TryParse(_settings.JesusColor, out var jesus)) run.Foreground = new SolidColorBrush(jesus);
                else if (s.WordsOfJesus && _settings.JesusRed) run.Bind(TextElement.ForegroundProperty, this.GetResourceObservable("BsJesus"));
                if (mark is not null) run.Background = HighlightBrush(mark.Color);
                tb.Inlines.Add(run);
                last = run; lastMark = mark; lastJesus = s.WordsOfJesus;
            }
            raw += s.Text.Length;
        }

        var box = new Border { Child = tb, Padding = new(10, 3), CornerRadius = new(8), BorderThickness = new(3, 0, 0, 0), BorderBrush = Brushes.Transparent, Tag = verse.Number };
        var whole = marks.FirstOrDefault(m => m.WholeVerse && m.Covers(verse.Number));
        if (target && Res("BsAccent") is ISolidColorBrush { Color: var accent })
        {
            box.Background = new SolidColorBrush(accent, 0.14);
            box.BorderBrush = new SolidColorBrush(accent);
        }
        else if (whole is not null) box.Background = new SolidColorBrush(MarkColor(whole.Color), 0.14);
        return box;
    }
}
