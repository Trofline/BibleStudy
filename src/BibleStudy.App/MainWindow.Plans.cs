using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using BibleStudy.Core.Models;

namespace BibleStudy.App;

/// <summary>
/// Reading plans: the chapters of a plan are split evenly over its days. Progress comes from the chapters marked as read, so reading
/// anywhere in the program counts. Plans are generated here (no third-party content).
/// </summary>
public partial class MainWindow
{
    const int PlanTab = 18;

    sealed record PlanDef(string Id, string Name, string About, int Days, Func<BookInfo, bool> Books);

    static readonly string[] Gospels = ["MAT", "MRK", "LUK", "JHN"];

    PlanDef[] Plans =>
    [
        new("year", "Bibel in einem Jahr", "Alle 1189 Kapitel, etwa 3 am Tag", 365, _ => true),
        new("nt90", "Neues Testament in 90 Tagen", "Alle 260 Kapitel, etwa 3 am Tag", 90, b => BookIndex(b) >= 39),
        new("gospels30", "Die vier Evangelien in 30 Tagen", "89 Kapitel, etwa 3 am Tag", 30, b => Gospels.Contains(b.Id)),
        new("psalms30", "Psalmen in 30 Tagen", "150 Psalmen, 5 am Tag", 30, b => b.Id == "PSA"),
        new("proverbs31", "Sprüche in 31 Tagen", "Ein Kapitel pro Tag", 31, b => b.Id == "PRO"),
    ];

    int BookIndex(BookInfo b) => _books.ToList().IndexOf(b);

    /// <summary>The chapters of one day (1-based) of a plan.</summary>
    List<(string Book, int Chapter)> PlanDay(PlanDef plan, int day)
    {
        var chapters = _books.Where(plan.Books).SelectMany(b => Enumerable.Range(1, b.NumberOfChapters).Select(c => (b.Id, c))).ToList();
        int from = (int)((long)(day - 1) * chapters.Count / plan.Days), to = (int)((long)day * chapters.Count / plan.Days);
        return chapters.Skip(from).Take(to - from).ToList();
    }

    int _planDayShown;

    void BuildPlanUi()
    {
        PlanPanel.Children.Clear();
        if (_books.Count == 0) { PlanPanel.Children.Add(new TextBlock { Text = L("Bücher werden noch geladen …"), Classes = { "caption" } }); return; }
        PlanPanel.Children.Add(new TextBlock { Text = L("LESEPLÄNE"), Classes = { "eyebrow" } });
        var plan = Plans.FirstOrDefault(p => p.Id == _settings.PlanId);
        if (plan is null || !DateTime.TryParse(_settings.PlanStart, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var start))
        {
            PlanPanel.Children.Add(new TextBlock { Text = L("Wähle einen Plan. Er verteilt die Kapitel gleichmäßig auf die Tage; gelesen ist ein Kapitel, sobald du es als „Gelesen“ markierst – egal wo du liest."), TextWrapping = TextWrapping.Wrap, Classes = { "caption" } });
            foreach (var p in Plans)
            {
                var pl = p;
                var btn = new Button { HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new(14, 10), Content = new StackPanel { Children = { new TextBlock { Text = p.Name, FontWeight = FontWeight.SemiBold }, new TextBlock { Text = L("{0} · {1} Tage", p.About, p.Days), Classes = { "caption" } } } } };
                btn.Click += (_, _) => { _settings.PlanId = pl.Id; _settings.PlanStart = DateTime.Today.ToString("yyyy-MM-dd"); _settings.PlanDoneDays = []; _planDayShown = 0; SaveSettings(); BuildPlanUi(); };
                PlanPanel.Children.Add(btn);
            }
            return;
        }

        var today = Math.Max(1, (DateTime.Today - start.Date).Days + 1);
        if (_planDayShown == 0) _planDayShown = Math.Min(today, plan.Days);
        var day = Math.Clamp(_planDayShown, 1, plan.Days);
        var all = Enumerable.Range(1, plan.Days).SelectMany(d => PlanDay(plan, d)).ToList();
        var readCount = all.Count(c => _read.Contains(c));
        var shouldBe = Enumerable.Range(1, Math.Min(today, plan.Days)).SelectMany(d => PlanDay(plan, d)).Count();
        var behind = Math.Max(0, shouldBe - readCount);

        PlanPanel.Children.Add(new TextBlock { Text = plan.Name, FontSize = 18, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap });
        PlanPanel.Children.Add(new ProgressBar { Minimum = 0, Maximum = Math.Max(1, all.Count), Value = readCount, Height = 8 });
        PlanPanel.Children.Add(new TextBlock
        {
            Text = L("{0} von {1} Kapiteln gelesen ({2} %) · Tag {3} von {4} · ", readCount, all.Count, 100 * readCount / Math.Max(1, all.Count), Math.Min(today, plan.Days), plan.Days)
                 + (today > plan.Days ? "Plan abgeschlossen oder überfällig" : behind == 0 ? "im Plan" : $"{behind} Kapitel im Rückstand") + $" · Serie: {PlanStreak()} Tage",
            Classes = { "caption" }, TextWrapping = TextWrapping.Wrap,
        });

        var nav = new DockPanel();
        var prev = new Button { Content = "‹", Classes = { "tool" }, Width = 32 }; var next = new Button { Content = "›", Classes = { "tool" }, Width = 32 };
        prev.Click += (_, _) => { _planDayShown = Math.Max(1, day - 1); BuildPlanUi(); };
        next.Click += (_, _) => { _planDayShown = Math.Min(plan.Days, day + 1); BuildPlanUi(); };
        DockPanel.SetDock(prev, Dock.Left); DockPanel.SetDock(next, Dock.Right);
        nav.Children.Add(prev); nav.Children.Add(next);
        nav.Children.Add(new TextBlock { Text = L("Tag {0}", day) + (day == Math.Min(today, plan.Days) ? " (heute)" : "") + $" · {start.AddDays(day - 1):dd.MM.yyyy}", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeight.SemiBold });
        PlanPanel.Children.Add(nav);

        var chapters = PlanDay(plan, day);
        var list = new WrapPanel();
        foreach (var (book, chapter) in chapters)
        {
            var (b, c) = (book, chapter);
            var done = _read.Contains((b, c));
            var chip = new Button { Classes = { "seg" }, Content = (done ? "✓ " : "") + $"{BookName(b)} {c}" };
            chip.Classes.Set("active", done);
            chip.Click += async (_, _) => { await GoToAsync(b, c); };
            list.Children.Add(chip);
        }
        PlanPanel.Children.Add(list);

        var allDone = chapters.All(c => _read.Contains(c));
        var finish = new Button { Content = allDone ? L("Tag erledigt ✓") : "Alle Kapitel dieses Tages als gelesen markieren", Classes = { "primary" }, HorizontalAlignment = HorizontalAlignment.Stretch };
        finish.Click += (_, _) =>
        {
            foreach (var g in chapters.GroupBy(c => c.Book)) SetRead(g.Key, g.Select(c => c.Chapter).ToList(), true);
            var key = DateTime.Today.ToString("yyyy-MM-dd");
            if (!_settings.PlanDoneDays.Contains(key)) _settings.PlanDoneDays.Add(key);
            SaveSettings(); BuildPlanUi();
        };
        PlanPanel.Children.Add(finish);

        var reset = new Button { Content = L("Plan beenden / anderen wählen"), HorizontalAlignment = HorizontalAlignment.Left };
        reset.Click += (_, _) => { _settings.PlanId = null; _settings.PlanStart = null; _planDayShown = 0; SaveSettings(); BuildPlanUi(); };
        PlanPanel.Children.Add(reset);
    }

    /// <summary>Consecutive days (ending today or yesterday) on which a plan day was finished.</summary>
    int PlanStreak()
    {
        var days = _settings.PlanDoneDays.Select(d => DateTime.TryParse(d, out var t) ? t.Date : (DateTime?)null).Where(d => d is not null).Select(d => d!.Value).ToHashSet();
        var cursor = days.Contains(DateTime.Today) ? DateTime.Today : DateTime.Today.AddDays(-1);
        var n = 0;
        while (days.Contains(cursor)) { n++; cursor = cursor.AddDays(-1); }
        return n;
    }
}
