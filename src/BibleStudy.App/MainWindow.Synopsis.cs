using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using BibleStudy.Core;
using BibleStudy.Core.Models;

namespace BibleStudy.App;

/// <summary>
/// The synopsis: one event or saying as Matthew, Mark, Luke and John tell it, side by side in the translation that is read now. Words
/// only one gospel has are coloured, so that it shows at a glance what each one tells differently.
/// </summary>
public partial class MainWindow
{
    const int SynopsisTab = 34;
    int _synopsisSection = -1;
    bool _synopsisDiff = true;

    [GeneratedRegex(@"\p{L}[\p{L}\p{M}'’]*|[^\p{L}]+")]
    private static partial Regex SynopsisTokens();

    string GospelShort(string book) => BookName(book) is { Length: > 0 } n ? n : book;

    /// <summary>Opens the synopsis at the passage that is read now (or at the chosen verse).</summary>
    void OpenSynopsisHere()
    {
        _synopsisSection = -1;
        OpenWork(NavGroups.First(g => g.Name == "Studium"), SynopsisTab);
    }

    async Task ShowSynopsisAsync()
    {
        var all = Synopsis.All;
        // the passage open in the reader decides, as long as no other section was chosen
        if (_synopsisSection < 0 && Synopsis.Gospels.Contains(_curBook))
        {
            var here = Synopsis.At(_curBook, _curChapter, _selected ?? 1);
            if (here.Count == 0) here = all.Where(s => s.Covers(_curBook, _curChapter)).ToList();
            if (here.Count > 0) _synopsisSection = here[0].Number - 1;
        }
        if (_synopsisSection < 0 || _synopsisSection >= all.Count) _synopsisSection = 9;   // the baptism: told by all four

        SynopsisPanel.Children.Clear();
        SynopsisPanel.Children.Add(new TextBlock { Text = L("SYNOPSE DER EVANGELIEN"), Classes = { "eyebrow" } });
        string Line(SynopsisSection s) => $"{s.Number}. {s.Title(Loc.Code)}  ·  " + string.Join(" ", Synopsis.Gospels.Where(s.Places.ContainsKey).Select(GospelShort));
        var pick = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = all.Select(Line).ToList(), SelectedIndex = _synopsisSection, MaxDropDownHeight = 480 };
        pick.SelectionChanged += (_, _) => { if (pick.SelectedIndex >= 0 && pick.SelectedIndex != _synopsisSection) { _synopsisSection = pick.SelectedIndex; _ = ShowSynopsisAsync(); } };
        var prev = new Button { Content = "‹", IsEnabled = _synopsisSection > 0 };
        ToolTip.SetTip(prev, L("Vorheriger Abschnitt"));
        prev.Click += (_, _) => { _synopsisSection--; _ = ShowSynopsisAsync(); };
        var next = new Button { Content = "›", IsEnabled = _synopsisSection < all.Count - 1 };
        ToolTip.SetTip(next, L("Nächster Abschnitt"));
        next.Click += (_, _) => { _synopsisSection++; _ = ShowSynopsisAsync(); };
        var here_ = new Button { Content = L("Zur Stelle im Lesefenster"), IsEnabled = Synopsis.Gospels.Contains(_curBook) };
        here_.Click += (_, _) => { _synopsisSection = -1; _ = ShowSynopsisAsync(); };
        var diff = new CheckBox { Content = L("Eigenes eines Evangeliums farbig"), IsChecked = _synopsisDiff };
        diff.IsCheckedChanged += (_, _) => { _synopsisDiff = diff.IsChecked == true; _ = ShowSynopsisAsync(); };
        var bar = new DockPanel { Children = { new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, [DockPanel.DockProperty] = Dock.Right, Margin = new(6, 0, 0, 0), Children = { prev, next } }, pick } };
        SynopsisPanel.Children.Add(bar);
        SynopsisPanel.Children.Add(new WrapPanel { ItemSpacing = 10, LineSpacing = 6, Children = { here_, diff } });

        var section = all[_synopsisSection];
        var translation = CurrentTranslationId;
        if (translation.Length == 0) { SynopsisPanel.Children.Add(new TextBlock { Text = L("Wähle zuerst eine Übersetzung."), Classes = { "caption" } }); return; }

        // the text of each gospel that tells it, with verse numbers
        var columns = new List<(string Book, IReadOnlyList<GospelRange> Ranges, List<(int Chapter, int Verse, string Text)> Verses)>();
        foreach (var g in Synopsis.Gospels)
        {
            if (!section.Places.TryGetValue(g, out var ranges)) continue;
            var verses = new List<(int, int, string)>();
            try
            {
                foreach (var r in ranges)
                    for (var c = r.Chapter; c <= r.ToChapter; c++)
                    {
                        var ch = await _provider.GetChapterAsync(translation, g, c);
                        verses.AddRange(ch.Verses.Where(v => r.Contains(c, v.Number)).Select(v => (c, v.Number, v.Text)));
                    }
            }
            catch (Exception ex) { verses.Add((0, 0, L("Nicht geladen: {0}", ex.Message))); }
            columns.Add((g, ranges, verses));
        }
        if (_synopsisSection != section.Number - 1) return;   // another section was chosen meanwhile

        var own = _synopsisDiff && columns.Count > 1 ? Synopsis.OwnWords(columns.Select(c => string.Join(" ", c.Verses.Select(v => v.Text))).ToList()) : null;
        var accent = Res("BsAccent");
        var wide = (SynopsisPanel.Bounds.Width > 0 ? SynopsisPanel.Bounds.Width : 900) >= 260 * Math.Max(1, columns.Count);
        var grid = new Grid { ColumnSpacing = 14, RowSpacing = 14 };
        for (var i = 0; i < columns.Count; i++)
        {
            if (wide) grid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
            else grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var (book, ranges, verses) = columns[i];
            var head = new Button { Content = new TextBlock { Text = $"{GospelShort(book)} " + string.Join("; ", ranges.Select(r => r.ToString().Replace(':', ','))), FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap }, Classes = { "tool" }, Width = double.NaN, HorizontalAlignment = HorizontalAlignment.Left, Padding = new(6, 2) };
            ToolTip.SetTip(head, L("Springt zur Stelle"));
            var first = ranges[0];
            head.Click += async (_, _) => await GoToAsync(book, first.Chapter, first.From);
            var text = new SelectableTextBlock { TextWrapping = TextWrapping.Wrap, FontFamily = ScriptureFont, FontSize = Math.Max(14, _fontSize - 2), LineHeight = Math.Max(14, _fontSize - 2) * 1.55 };
            var lastChapter = first.Chapter;
            foreach (var (c, v, t) in verses)
            {
                var number = c != lastChapter ? $"{c},{v}" : v.ToString();
                lastChapter = c;
                text.Inlines!.Add(new Run(" " + number + " ") { FontSize = 11, BaselineAlignment = BaselineAlignment.Superscript, Foreground = Res("BsMuted") });
                foreach (Match m in SynopsisTokens().Matches(t))
                {
                    var run = new Run(m.Value);
                    if (own is not null && own[i].Contains(Synopsis.Fold(m.Value))) { run.Foreground = accent; run.FontWeight = FontWeight.SemiBold; }
                    text.Inlines.Add(run);
                }
            }
            var col = new StackPanel { Spacing = 6, Children = { head, text } };
            var card = new Border { Classes = { "card" }, Child = col };
            if (wide) Grid.SetColumn(card, i); else Grid.SetRow(card, i);
            grid.Children.Add(card);
        }
        SynopsisPanel.Children.Add(grid);
        if (columns.Count == 1)
            SynopsisPanel.Children.Add(new TextBlock { Text = L("Nur {0} erzählt das.", GospelShort(columns[0].Book)), Classes = { "caption" } });
        else if (own is not null)
            SynopsisPanel.Children.Add(new TextBlock { Text = L("Farbig: Wörter, die nur dieses Evangelium hier hat (verglichen in der gewählten Übersetzung, kurze Wörter ausgenommen)."), Classes = { "caption" }, TextWrapping = TextWrapping.Wrap });
        SynopsisPanel.Children.Add(new TextBlock { Text = L("Die Zuordnung der Parallelen ist für dieses Programm zusammengestellt und folgt der üblichen Einteilung in Abschnitte."), Classes = { "caption" }, TextWrapping = TextWrapping.Wrap, Opacity = 0.75 });
    }
}
