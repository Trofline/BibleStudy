using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using BibleStudy.Core;

namespace BibleStudy.App;

/// <summary>
/// The handbook: the chapters in docs/handbuch are built into the program and read here, with a table of contents,
/// a search over all chapters, and a button that writes the whole handbook as one .md file. The same files are on GitHub.
/// </summary>
public partial class MainWindow
{
    const int HandbookTab = 26;
    // German is the original; every other language reads the English handbook.
    static bool HandbookGerman => Loc.Code == "de";
    static string HandbookUrl => "https://github.com/Trofline/BibleStudy/tree/main/docs/" + (HandbookGerman ? "handbuch" : "handbook");

    sealed record HbChapter(string File, string Title, string Text);

    List<HbChapter>? _hbChapters;
    int _hbOpen = -1;
    List<MarkdownView.Block> _hbBlocks = [];
    StackPanel? _hbContent;
    ScrollViewer? _hbScroll;
    ListBox? _hbToc;
    TextBox? _hbSearch;
    bool _hbBuilt;

    List<HbChapter> HandbookChapters()
    {
        if (_hbChapters is not null) return _hbChapters;
        var asm = Assembly.GetExecutingAssembly();
        var prefix = HandbookGerman ? "Handbuch/" : "Handbook/";
        _hbChapters = asm.GetManifestResourceNames()
            .Where(n => n.StartsWith(prefix, StringComparison.Ordinal) && Regex.IsMatch(n, @"/\d\d-.*\.md$")).Order()
            .Select(n =>
            {
                using var s = asm.GetManifestResourceStream(n)!;
                using var r = new StreamReader(s, Encoding.UTF8);
                var text = r.ReadToEnd();
                var title = Regex.Match(text, @"^#\s+(.+)$", RegexOptions.Multiline) is { Success: true } m ? m.Groups[1].Value.Trim() : n;
                return new HbChapter(n[prefix.Length..], title, text);
            }).ToList();
        return _hbChapters;
    }

    void BuildHandbookUi()
    {
        if (_hbBuilt) return;
        _hbBuilt = true;
        var chapters = HandbookChapters();
        if (chapters.Count == 0)
        {
            HandbookHost.Children.Add(new TextBlock { Text = L("Das Handbuch ist in diesem Paket nicht enthalten. Du findest es auf GitHub."), TextWrapping = TextWrapping.Wrap, Margin = new(16) });
            return;
        }

        _hbSearch = new TextBox { PlaceholderText = L("Im Handbuch suchen …"), Margin = new(0, 0, 0, 6) };
        _hbToc = new ListBox { Background = Brushes.Transparent };
        _hbSearch.TextChanged += (_, _) => FillHandbookToc();
        _hbToc.SelectionChanged += (_, _) =>
        {
            if (_hbToc.SelectedItem is ListBoxItem { Tag: ValueTuple<int, int, string> t }) ShowHandbookChapter(t.Item1, t.Item2, t.Item3);
        };
        var tocPanel = new DockPanel { Margin = new(14, 0, 8, 0) };
        DockPanel.SetDock(_hbSearch, Dock.Top);
        tocPanel.Children.Add(_hbSearch); tocPanel.Children.Add(_hbToc);

        var file = new Button { Content = L("Als .md-Datei öffnen"), Classes = { "tool" }, Width = double.NaN, Padding = new(10, 0), Height = 28 };
        ToolTip.SetTip(file, L("Schreibt das ganze Handbuch als eine Markdown-Datei und öffnet sie"));
        file.Click += (_, _) => OpenHandbookFile();
        var web = new Button { Content = L("Auf GitHub ansehen"), Classes = { "tool" }, Width = double.NaN, Padding = new(10, 0), Height = 28 };
        web.Click += (_, _) => OpenUrl(HandbookUrl);
        var toggle = new ToggleButton { Content = L("Inhalt"), IsChecked = true, Classes = { "tool" }, Width = double.NaN, Padding = new(10, 0), Height = 28 };
        var bar = new WrapPanel { Margin = new(14, 0, 14, 6) };
        bar.Children.Add(toggle); bar.Children.Add(file); bar.Children.Add(web);

        _hbContent = new StackPanel { Spacing = 0, Margin = new(8, 0, 20, 30), MaxWidth = 860, HorizontalAlignment = HorizontalAlignment.Left };
        _hbScroll = new ScrollViewer { Content = _hbContent };

        var grid = new Grid { ColumnDefinitions = new("250,*") };
        grid.Children.Add(tocPanel);
        Grid.SetColumn(_hbScroll, 1); grid.Children.Add(_hbScroll);
        toggle.IsCheckedChanged += (_, _) =>
        {
            tocPanel.IsVisible = toggle.IsChecked == true;
            grid.ColumnDefinitions[0].Width = tocPanel.IsVisible ? new GridLength(250) : new GridLength(0);
        };

        var root = new DockPanel();
        DockPanel.SetDock(bar, Dock.Top);
        root.Children.Add(bar); root.Children.Add(grid);
        HandbookHost.Children.Add(root);

        FillHandbookToc();
        ShowHandbookChapter(0, -1, "");
    }

    /// <summary>The table of contents (chapters and their headings), or the hits of the search.</summary>
    void FillHandbookToc()
    {
        if (_hbToc is null) return;
        var query = (_hbSearch?.Text ?? "").Trim();
        var items = new List<ListBoxItem>();
        var chapters = HandbookChapters();
        if (query.Length >= 2)
        {
            foreach (var (ch, ci) in chapters.Select((c, i) => (c, i)))
                foreach (var para in Regex.Split(ch.Text, @"\n\s*\n"))
                {
                    var plain = Regex.Replace(para, @"[#*`>|]|\]\([^)]*\)|\[", " ").Replace('\n', ' ').Trim();
                    var at = plain.IndexOf(query, StringComparison.CurrentCultureIgnoreCase);
                    if (at < 0 || items.Count >= 80) continue;
                    var from = Math.Max(0, at - 30);
                    var snippet = (from > 0 ? "…" : "") + plain.Substring(from, Math.Min(plain.Length - from, 90)) + "…";
                    items.Add(HbItem($"{ch.Title}\n{snippet}", (ci, 0, query), 0, false));
                }
            if (items.Count == 0) items.Add(new ListBoxItem { Content = new TextBlock { Text = L("Nichts gefunden."), Opacity = 0.7 }, IsEnabled = false });
        }
        else
        {
            foreach (var (ch, ci) in chapters.Select((c, i) => (c, i)))
            {
                items.Add(HbItem(ch.Title, (ci, -1, ""), 0, true));
                foreach (var m in Regex.Matches(ch.Text, @"^##\s+(.+)$", RegexOptions.Multiline).Cast<Match>())
                    items.Add(HbItem(m.Groups[1].Value.Trim(), (ci, -2, m.Groups[1].Value.Trim()), 14, false));
            }
        }
        _hbToc.ItemsSource = items;
    }

    static ListBoxItem HbItem(string text, (int, int, string) tag, double indent, bool bold) => new()
    {
        Tag = tag, Padding = new(8 + indent, 4, 6, 4),
        Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontWeight = bold ? FontWeight.SemiBold : FontWeight.Normal, FontSize = bold ? 13.5 : 12.5 },
    };

    /// <summary>block: -1 top of the chapter, -2 jump to the heading named in query, 0 the first paragraph with the search words.</summary>
    void ShowHandbookChapter(int chapter, int block, string query)
    {
        var chapters = HandbookChapters();
        if (_hbContent is null || chapter < 0 || chapter >= chapters.Count) return;
        if (_hbOpen != chapter)
        {
            _hbOpen = chapter;
            var md = new MarkdownView(OpenHandbookLink, key => Res(key));
            _hbBlocks = md.Render(chapters[chapter].Text);
            _hbContent.Children.Clear();
            foreach (var b in _hbBlocks) _hbContent.Children.Add(b.Control);
        }
        Control? target = null;
        if (block == -2) target = _hbBlocks.FirstOrDefault(b => b.Heading >= 2 && b.Text == query)?.Control;
        else if (block >= 0 && query.Length > 0) target = _hbBlocks.FirstOrDefault(b => b.Text.Contains(query, StringComparison.CurrentCultureIgnoreCase))?.Control;
        Dispatcher.UIThread.Post(() =>
        {
            if (target is not null) target.BringIntoView();
            else _hbScroll?.ScrollToHome();
        }, DispatcherPriority.Background);
    }

    void OpenHandbookLink(string target)
    {
        if (target.StartsWith("http", StringComparison.OrdinalIgnoreCase)) { OpenUrl(target); return; }
        var name = target.Split('#')[0];
        var idx = HandbookChapters().FindIndex(c => c.File == name);
        if (idx >= 0) { ShowHandbookChapter(idx, -1, ""); return; }
        if (name.StartsWith("../../")) OpenUrl("https://github.com/Trofline/BibleStudy/blob/main/" + name["../../".Length..]);
    }

    /// <summary>The whole handbook as one file in the data folder (links between chapters become "(Kapitel N)").</summary>
    void OpenHandbookFile()
    {
        try
        {
            var sb = new StringBuilder(HandbookGerman ? "# BibleStudy – Handbuch\n\n" : "# BibleStudy – Handbook\n\n");
            foreach (var c in HandbookChapters()) sb.Append("- ").Append(c.Title).Append('\n');
            sb.Append('\n');
            foreach (var c in HandbookChapters())
            {
                var text = Regex.Replace(c.Text, @"\[([^\]]+)\]\((\d\d)-[^)]*\.md[^)]*\)", m => $"{m.Groups[1].Value} ({(HandbookGerman ? "Kapitel" : "chapter")} {int.Parse(m.Groups[2].Value)})");
                text = Regex.Replace(text, @"\[([^\]]+)\]\(\.\./\.\./([^)]+)\)", "$1 (https://github.com/Trofline/BibleStudy/blob/main/$2)");
                sb.Append(text.TrimEnd()).Append("\n\n---\n\n");
            }
            var path = Path.Combine(AppPaths.Root, HandbookGerman ? "Handbuch.md" : "Handbook.md");
            Directory.CreateDirectory(AppPaths.Root);
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
            Shell.Open(path);
        }
        catch (Exception ex) { StatusText.Text = L("Handbuch konnte nicht geöffnet werden: {0}", ex.Message); }
    }
}
