using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using BibleStudy.Core;
using BibleStudy.Core.Models;
using BibleStudy.Core.Storage;

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
        new("year", L("Bibel in einem Jahr"), L("Alle 1189 Kapitel, etwa 3 am Tag"), 365, _ => true),
        new("nt90", L("Neues Testament in 90 Tagen"), L("Alle 260 Kapitel, etwa 3 am Tag"), 90, b => BookIndex(b) >= 39),
        new("gospels30", L("Die vier Evangelien in 30 Tagen"), L("89 Kapitel, etwa 3 am Tag"), 30, b => Gospels.Contains(b.Id)),
        new("psalms30", L("Psalmen in 30 Tagen"), L("150 Psalmen, 5 am Tag"), 30, b => b.Id == "PSA"),
        new("proverbs31", L("Sprüche in 31 Tagen"), L("Ein Kapitel pro Tag"), 31, b => b.Id == "PRO"),
        new("ot365", L("Altes Testament in einem Jahr"), L("929 Kapitel, etwa 2 bis 3 am Tag"), 365, b => BookIndex(b) < 39),
        new("bible730", L("Bibel in zwei Jahren"), L("Alle 1189 Kapitel, etwa 1 bis 2 am Tag – gemütlich"), 730, _ => true),
        new("paul30", L("Die Briefe des Paulus in 30 Tagen"), L("87 Kapitel von Römer bis Philemon, etwa 3 am Tag"), 30, b => PaulBooks.Contains(b.Id)),
        new("wisdom60", L("Weisheit und Dichtung in 60 Tagen"), L("Hiob, Psalmen, Sprüche, Prediger, Hoheslied: 243 Kapitel"), 60, b => WisdomBooks.Contains(b.Id)),
        new("prophets90", L("Die Propheten in 90 Tagen"), L("Jesaja bis Maleachi: 261 Kapitel, etwa 3 am Tag"), 90, b => BookIndex(b) is >= 22 and < 39),
        ..CustomPlans(),
    ];

    /// <summary>The plan the user made, if any: a stretch of books (in the order of the books) over a number of days.</summary>
    IEnumerable<PlanDef> CustomPlans()
    {
        if (_settings.CustomPlan is not { } cp || _books.Count == 0) return [];
        int from = _books.ToList().FindIndex(b => b.Id == cp.FromBook), to = _books.ToList().FindIndex(b => b.Id == cp.ToBook);
        if (from < 0 || to < from) return [];
        var chapters = _books.Skip(from).Take(to - from + 1).Sum(b => b.NumberOfChapters);
        var about = L("{0} Kapitel, etwa {1} am Tag", chapters, Math.Max(1, (int)Math.Round((double)chapters / Math.Max(1, cp.Days))));
        return [new("custom", cp.Name.Length > 0 ? cp.Name : $"{BookName(cp.FromBook)} – {BookName(cp.ToBook)}", about, Math.Max(1, cp.Days), b => BookIndex(b) >= from && BookIndex(b) <= to)];
    }


    static readonly string[] PaulBooks = ["ROM", "1CO", "2CO", "GAL", "EPH", "PHP", "COL", "1TH", "2TH", "1TI", "2TI", "TIT", "PHM"];
    static readonly string[] WisdomBooks = ["JOB", "PSA", "PRO", "ECC", "SNG"];

    int BookIndex(BookInfo b) => _books.ToList().IndexOf(b);

    /// <summary>The chapters of one day (1-based) of a plan.</summary>
    List<(string Book, int Chapter)> PlanDay(PlanDef plan, int day)
    {
        var chapters = _books.Where(plan.Books).SelectMany(b => Enumerable.Range(1, b.NumberOfChapters).Select(c => (b.Id, c))).ToList();
        return ReadingPlans.Day(chapters, plan.Days, day);
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
            PlanPanel.Children.Add(CustomPlanForm());
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
                 + (today > plan.Days ? L("Plan abgeschlossen oder überfällig") : behind == 0 ? L("im Plan") : L("{0} Kapitel im Rückstand", behind)) + L(" · Serie: {0} Tage", PlanStreak()),
            Classes = { "caption" }, TextWrapping = TextWrapping.Wrap,
        });

        var nav = new DockPanel();
        var prev = new Button { Content = "‹", Classes = { "tool" }, Width = 32 }; var next = new Button { Content = "›", Classes = { "tool" }, Width = 32 };
        prev.Click += (_, _) => { _planDayShown = Math.Max(1, day - 1); BuildPlanUi(); };
        next.Click += (_, _) => { _planDayShown = Math.Min(plan.Days, day + 1); BuildPlanUi(); };
        DockPanel.SetDock(prev, Dock.Left); DockPanel.SetDock(next, Dock.Right);
        nav.Children.Add(prev); nav.Children.Add(next);
        nav.Children.Add(new TextBlock { Text = L("Tag {0}", day) + (day == Math.Min(today, plan.Days) ? L(" (heute)") : "") + $" · {start.AddDays(day - 1):dd.MM.yyyy}", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeight.SemiBold });
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

        // one click to the next thing to read: the first unread chapter of the days up to today (or of this day)
        var upTo = Math.Min(today, plan.Days);
        var next1 = Enumerable.Range(1, Math.Max(upTo, day)).SelectMany(d => PlanDay(plan, d)).FirstOrDefault(c => !_read.Contains(c));
        if (next1 != default)
        {
            var go = new Button { Content = L("Weiterlesen: {0} {1}", BookName(next1.Book), next1.Chapter), Classes = { "primary" }, HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new(0, 6, 0, 0) };
            go.Click += async (_, _) => await GoToAsync(next1.Book, next1.Chapter);
            PlanPanel.Children.Add(go);
        }

        var allDone = chapters.All(c => _read.Contains(c));
        var finish = new Button { Content = allDone ? L("Tag erledigt ✓") : L("Alle Kapitel dieses Tages als gelesen markieren"), Classes = { "primary" }, HorizontalAlignment = HorizontalAlignment.Stretch };
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
        var print = new Button { Content = L("Plan drucken / PDF"), HorizontalAlignment = HorizontalAlignment.Left };
        print.Click += (_, _) => PrintPlan();
        PlanPanel.Children.Add(new WrapPanel { Children = { print } });
        PlanPanel.Children.Add(reset);
    }

    /// <summary>Consecutive days (ending today or yesterday) on which a plan day was finished.</summary>
    int PlanStreak() =>
        ReadingPlans.Streak(_settings.PlanDoneDays.Select(d => DateTime.TryParse(d, out var t) ? t : (DateTime?)null).OfType<DateTime>(), DateTime.Today);

    /// <summary>The card on the start page: what is next in the reading plan today (null without a running plan).</summary>
    (string Label, string Hint, Action Go)? PlanTodayCard()
    {
        var plan = Plans.FirstOrDefault(p => p.Id == _settings.PlanId);
        if (plan is null || _books.Count == 0 || !DateTime.TryParse(_settings.PlanStart, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var start)) return null;
        var today = ReadingPlans.DayOf(start, DateTime.Today, plan.Days);
        var next = Enumerable.Range(1, today).SelectMany(d => PlanDay(plan, d)).FirstOrDefault(c => !_read.Contains(c));
        var hint = L("{0} · Tag {1} von {2}", plan.Name, today, plan.Days);
        if (next == default) return (L("Heute erledigt ✓"), hint, () => OpenWork(NavGroups.First(g => g.Name == "Planen"), PlanTab));
        return (L("Weiterlesen: {0} {1}", BookName(next.Book), next.Chapter), hint, () => _ = GoToAsync(next.Book, next.Chapter));
    }

    /// <summary>Own plan: a stretch of books over a number of days.</summary>
    Control CustomPlanForm()
    {
        var names = _books.Select(b => b.CommonName).ToList();
        var from = new ComboBox { ItemsSource = names, SelectedIndex = Math.Max(0, _books.ToList().FindIndex(b => b.Id == "MAT")), MinWidth = 140 };
        var to = new ComboBox { ItemsSource = names, SelectedIndex = Math.Max(0, _books.ToList().FindIndex(b => b.Id == "JHN")), MinWidth = 140 };
        var days = new NumericUpDown { Minimum = 1, Maximum = 1500, Value = 30, Increment = 1, FormatString = "0", Width = 130 };
        var name = new TextBox { Watermark = L("Name (freiwillig)") };
        var info = new TextBlock { Classes = { "caption" }, TextWrapping = TextWrapping.Wrap };
        void Show()
        {
            int a = from.SelectedIndex, b = to.SelectedIndex;
            if (a < 0 || b < a) { info.Text = L("Das erste Buch muss vor dem letzten stehen."); return; }
            var count = _books.Skip(a).Take(b - a + 1).Sum(x => x.NumberOfChapters);
            var perDay = count / Math.Max(1, (double)(days.Value ?? 1));
            info.Text = L("{0} Kapitel, etwa {1} am Tag", count, perDay.ToString("0.#"));
        }
        from.SelectionChanged += (_, _) => Show(); to.SelectionChanged += (_, _) => Show(); days.ValueChanged += (_, _) => Show();
        var start = new Button { Content = L("Eigenen Plan starten"), Classes = { "primary" }, HorizontalAlignment = HorizontalAlignment.Left };
        start.Click += (_, _) =>
        {
            int a = from.SelectedIndex, b = to.SelectedIndex;
            if (a < 0 || b < a) return;
            _settings.CustomPlan = new CustomPlan { Name = name.Text?.Trim() ?? "", FromBook = _books[a].Id, ToBook = _books[b].Id, Days = (int)(days.Value ?? 30) };
            _settings.PlanId = "custom"; _settings.PlanStart = DateTime.Today.ToString("yyyy-MM-dd"); _settings.PlanDoneDays = []; _planDayShown = 0;
            SaveSettings(); BuildPlanUi();
        };
        Show();
        return new Expander
        {
            Header = L("Eigener Plan: von Buch zu Buch in so vielen Tagen"), Margin = new(0, 6, 0, 0),
            Content = new StackPanel { Spacing = 6, Margin = new(0, 8, 0, 0), Children = { new WrapPanel { Children = { new TextBlock { Text = L("Von"), VerticalAlignment = VerticalAlignment.Center, Margin = new(0, 0, 6, 0) }, from, new TextBlock { Text = L("bis"), VerticalAlignment = VerticalAlignment.Center, Margin = new(8, 0, 6, 0) }, to } },
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { new TextBlock { Text = L("in"), VerticalAlignment = VerticalAlignment.Center }, days, new TextBlock { Text = L("Tagen"), VerticalAlignment = VerticalAlignment.Center } } }, name, info, start } },
        };
    }
}
