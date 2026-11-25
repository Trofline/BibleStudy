using BibleStudy.Core;
using BibleStudy.Core.Models;
using BibleStudy.Core.Search;

namespace BibleStudy.App;

/// <summary>
/// The "Today" widget on the home screen of the phone (Android): the verse of the day and what the reading plan has for today.
/// The program writes what the widget shows into a small file whenever it goes to the background; the widget reads it.
/// The verses of the day are written once per translation, with their text, so the widget changes its verse at midnight on its own.
/// </summary>
public partial class MainWindow
{
    bool _todayCardBusy;

    /// <summary>Brings the file of the widget up to date; true when something changed.</summary>
    internal async Task<bool> UpdateTodayCardAsync()
    {
        if (_todayCardBusy || _books.Count == 0 || TranslationBox.SelectedItem is not TranslationInfo t) return false;
        _todayCardBusy = true;
        try
        {
            var card = TodayCard.Load(AppPaths.Root) ?? new TodayCard();
            if (card.Translation != t.Id || card.Verses.Count != DailyVerses.All.Length || card.Verses.Any(v => v.Text.Length == 0))
            {
                var verses = new List<TodayCard.Verse>();
                foreach (var r in DailyVerses.All)
                {
                    var text = "";
                    try { text = (await _provider.GetChapterAsync(t.Id, r.Book, r.Chapter)).Verses.FirstOrDefault(v => v.Number == r.Verse)?.Text ?? ""; }
                    catch { /* offline and not downloaded: the reference alone, the text comes next time */ }
                    verses.Add(new(BibleLinks.AppLink([BibleLinks.Passage(r.Book, r.Chapter, r.Verse)]), Label(r), text));
                }
                card.Verses = verses;
                card.Translation = t.Id;
            }
            card.TranslationName = t.ShortName;
            card.Reading = null;
            if (PlanTodayCard() is { } plan)
            {
                // where a tap on the plan leads: the next chapter to read (nothing when today is done: the widget then just opens the app)
                var link = NextPlanChapter() is { } next ? BibleLinks.AppLink([new PassageRef(next.Book, next.Chapter, null, null)]) : "";
                card.Reading = new(DateTime.Today.ToString("yyyy-MM-dd"), plan.Label, plan.Hint, link);
            }
            card.Words = new()
            {
                ["verse"] = L("Vers des Tages"),
                ["plan"] = L("Heute dran"),
                ["empty"] = L("Kein Leseplan. In BibleStudy unter Planen einen starten."),
            };
            return card.Save(AppPaths.Root);
        }
        catch (Exception ex) { ErrorLog.Write(ex); return false; }
        finally { _todayCardBusy = false; }
    }

#if ANDROID
    async Task RefreshTodayWidgetAsync()
    {
        if (await UpdateTodayCardAsync()) BibleStudy.Droid.TodayWidget.Refresh();
    }
#endif

    /// <summary>The chapter the running reading plan wants next (up to today), or null.</summary>
    (string Book, int Chapter)? NextPlanChapter()
    {
        var plan = Plans.FirstOrDefault(p => p.Id == _settings.PlanId);
        if (plan is null || !DateTime.TryParse(_settings.PlanStart, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var start)) return null;
        var today = ReadingPlans.DayOf(start, DateTime.Today, plan.Days);
        var next = Enumerable.Range(1, today).SelectMany(d => PlanDay(plan, d)).FirstOrDefault(c => !_read.Contains(c));
        return next == default ? null : (next.Book, next.Chapter);
    }
}
