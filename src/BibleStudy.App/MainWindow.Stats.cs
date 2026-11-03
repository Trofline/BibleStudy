using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using BibleStudy.Core;
using BibleStudy.Core.Models;

namespace BibleStudy.App;

/// <summary>
/// The statistics page: how much of the Bible was read (from the chapters marked as "read"), the reading streak, the last weeks,
/// and a count of what the vault holds. Everything is worked out on the computer from the vault; nothing is sent anywhere.
/// </summary>
public partial class MainWindow
{
    const int StatsTab = 27;

    void BuildStatsUi()
    {
        StatsPanel.Children.Clear();
        StatsPanel.Children.Add(new TextBlock { Text = L("STATISTIK"), Classes = { "eyebrow" } });        if (_books.Count == 0) { StatsPanel.Children.Add(new TextBlock { Text = L("Bücher werden noch geladen …"), Classes = { "caption" } }); return; }

        _read = _vault.GetReadChapters();
        var dates = _vault.GetReadDates().Select(x => x.At).ToList();
        int Total(IEnumerable<BookInfo> books) => books.Sum(b => b.NumberOfChapters);
        int Done(IEnumerable<BookInfo> books) => books.Sum(b => _read.Count(r => r.Book == b.Id && r.Chapter >= 1 && r.Chapter <= b.NumberOfChapters));
        var ot = _books.Where(b => BookCategories.IsOldTestament(BookCategories.Of(b.Id))).ToList();
        var nt = _books.Where(b => BookCategories.IsNewTestament(BookCategories.Of(b.Id))).ToList();

        // progress
        StatsPanel.Children.Add(Card(new StackPanel
        {
            Spacing = 6,
            Children =
            {
                new TextBlock { Text = L("Gelesen"), FontWeight = FontWeight.SemiBold },
                Bar(Done(_books), Total(_books), L("{0} von {1} Kapiteln ({2} %)", Done(_books), Total(_books), 100 * Done(_books) / Math.Max(1, Total(_books)))),
                Bar(Done(ot), Total(ot), L("Altes Testament: {0} von {1}", Done(ot), Total(ot))),
                Bar(Done(nt), Total(nt), L("Neues Testament: {0} von {1}", Done(nt), Total(nt))),
            },
        }));

        // streak and the last weeks
        var today = DateTime.Today;
        var week = ReadingStats.PerWeek(dates, today, 12);
        StatsPanel.Children.Add(Card(new StackPanel
        {
            Spacing = 6,
            Children =
            {
                new TextBlock { Text = L("Lesen im Alltag"), FontWeight = FontWeight.SemiBold },
                new TextBlock { Text = L("Serie: {0} Tage in Folge · längste Serie: {1} Tage", ReadingPlans.Streak(dates, today), ReadingStats.LongestStreak(dates)), TextWrapping = TextWrapping.Wrap },
                new TextBlock { Text = L("Letzte 7 Tage: {0} Kapitel · letzte 30 Tage: {1} Kapitel", week[^1], ReadingStats.PerDay(dates, today, 30).Sum()), Classes = { "caption" }, TextWrapping = TextWrapping.Wrap },
                new TextBlock { Text = L("Kapitel pro Woche (die letzten 12 Wochen, rechts die aktuelle)"), Classes = { "caption" }, TextWrapping = TextWrapping.Wrap, Margin = new(0, 6, 0, 0) },
                WeekChart(week),
                new TextBlock { Text = L("Gezählt wird ab dem Tag, an dem ein Kapitel als „Gelesen“ markiert wurde. Ältere Markierungen ohne Datum zählen nur beim Fortschritt."), Classes = { "caption" }, TextWrapping = TextWrapping.Wrap },
            },
        }));

        // what the vault holds
        var prayers = _vault.GetPrayers();
        StatsPanel.Children.Add(Card(new StackPanel
        {
            Spacing = 4,
            Children =
            {
                new TextBlock { Text = L("Dein Vault"), FontWeight = FontWeight.SemiBold },
                new TextBlock { Text = L("{0} Notizen · {1} Markierungen · {2} Dokumente · {3} Lesezeichen", _vault.GetNotes().Count, _vault.GetMarks().Count, _vault.GetDocuments().Count, _vault.GetBookmarks().Count), TextWrapping = TextWrapping.Wrap },
                new TextBlock { Text = L("{0} Gebete, davon {1} erhört · {2} Verse zum Auswendiglernen, sicher gelernt: {3}", prayers.Count, prayers.Count(p => p.Answered is not null), Memory.Ids.Count(), Memory.Known), TextWrapping = TextWrapping.Wrap },
            },
        }));

        // books with progress, the most read first
        var rows = _books.Select(b => (b, n: _read.Count(r => r.Book == b.Id && r.Chapter >= 1 && r.Chapter <= b.NumberOfChapters))).Where(x => x.n > 0)
            .OrderByDescending(x => (double)x.n / x.b.NumberOfChapters).ThenBy(x => x.b.Order).ToList();
        var books = new StackPanel { Spacing = 6 };
        books.Children.Add(new TextBlock { Text = L("Bücher"), FontWeight = FontWeight.SemiBold });
        if (rows.Count == 0) books.Children.Add(new TextBlock { Text = L("Noch kein Kapitel als „Gelesen“ markiert."), Classes = { "caption" } });
        foreach (var (b, n) in rows) books.Children.Add(Bar(n, b.NumberOfChapters, $"{b.CommonName}: {n}/{b.NumberOfChapters}"));
        StatsPanel.Children.Add(Card(books));
    }

    Control Bar(int value, int max, string caption)
    {
        var bar = new ProgressBar { Minimum = 0, Maximum = Math.Max(1, max), Value = value, Height = 8 };
        return new StackPanel { Spacing = 2, Children = { bar, new TextBlock { Text = caption, Classes = { "caption" }, TextWrapping = TextWrapping.Wrap } } };
    }

    /// <summary>Plain bars, one per week; the tallest bar is the maximum of the shown weeks.</summary>
    Control WeekChart(int[] weeks)
    {
        var max = Math.Max(1, weeks.Max());
        var grid = new Grid { Height = 70, ColumnDefinitions = new(string.Join(",", Enumerable.Repeat("*", weeks.Length))) };
        for (var i = 0; i < weeks.Length; i++)
        {
            var bar = new Border { VerticalAlignment = VerticalAlignment.Bottom, Height = Math.Max(2, 60.0 * weeks[i] / max), Margin = new(2, 0), CornerRadius = new(3), Opacity = weeks[i] == 0 ? 0.25 : 1 };
            bar.Bind(Border.BackgroundProperty, this.GetResourceObservable("BsAccent"));
            ToolTip.SetTip(bar, L("{0} Kapitel", weeks[i]));
            Grid.SetColumn(bar, i);
            grid.Children.Add(bar);
        }
        return grid;
    }
}
