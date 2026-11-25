using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using BibleStudy.Core;
using BibleStudy.Core.Models;

namespace BibleStudy.App;

/// <summary>
/// The widgets on the home screen (Android) and on the home and lock screen (iPhone) show the verse of the day and what is next in the reading plan.
/// They cannot read the vault or the Bibles themselves: the app writes what they show into a small file, for today and the next two weeks, so a
/// widget still turns to the next verse at midnight when the app has not been opened for a while. The widget only picks the entry of its day.
/// </summary>
public partial class MainWindow
{
    /// <summary>How many days ahead the file reaches (from today).</summary>
    const int WidgetDays = 14;

    string? _widgetKey;
    bool _widgetBusy;

    /// <summary>
    /// Writes the file for the widgets when something they show has changed (the day, the translation, the language, the plan or what was read).
    /// Quiet and cheap when nothing changed; never throws. Phones only: a desktop has no widgets.
    /// </summary>
    async Task RefreshWidgetsAsync()
    {
        if (!WidgetFile.Supported || _widgetBusy || _books.Count == 0 || TranslationBox.SelectedItem is not TranslationInfo t) return;
        var plan = Plans.FirstOrDefault(p => p.Id == _settings.PlanId);
        DateTime start = default;
        if (plan is not null && !DateTime.TryParse(_settings.PlanStart, CultureInfo.InvariantCulture, DateTimeStyles.None, out start)) plan = null;
        var key = string.Join("|", DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), t.Id, _settings.Language, plan?.Id, plan?.Days, _settings.PlanStart, _read.Count, _read.GetHashCode());
        if (key == _widgetKey) return;
        _widgetBusy = true;
        try
        {
            var snapshot = await BuildWidgetSnapshotAsync(t, plan, start);
            WidgetFile.Write(JsonSerializer.Serialize(snapshot, WidgetJson));
            _widgetKey = key;
        }
        catch (Exception ex) { ErrorLog.Write(ex); }
        finally { _widgetBusy = false; }
    }

    static readonly JsonSerializerOptions WidgetJson = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    async Task<WidgetSnapshot> BuildWidgetSnapshotAsync(TranslationInfo t, PlanDef? plan, DateTime start)
    {
        // the plan: all its chapters day by day, once (the days ahead are taken from it)
        List<List<(string Book, int Chapter)>>? planDays = null;
        WidgetPlan? planInfo = null;
        if (plan is not null)
        {
            planDays = Enumerable.Range(1, plan.Days).Select(d => PlanDay(plan, d)).ToList();
            var total = planDays.Sum(d => d.Count);
            planInfo = new WidgetPlan(plan.Name, planDays.Sum(d => d.Count(c => _read.Contains(c))), total);
        }

        var cache = new Dictionary<(string, int), Chapter?>();
        var days = new List<WidgetDay>();
        for (var k = 0; k < WidgetDays; k++)
        {
            var date = DateTime.Today.AddDays(k);
            var r = VerseOfTheDay(date);
            if (!cache.TryGetValue((r.Book, r.Chapter), out var ch))
            {
                try { ch = await _provider.GetChapterAsync(t.Id, r.Book, r.Chapter); } catch { ch = null; }   // not there in this translation: the reference alone
                cache[(r.Book, r.Chapter)] = ch;
            }
            var text = string.Concat(ch?.Verses.FirstOrDefault(x => x.Number == r.Verse)?.Segments.Where(s => s.NoteId is null).Select(s => s.Text) ?? []).Trim();

            string? planLabel = null, planHint = null, planBook = null; int? planChapter = null;
            if (plan is not null && planDays is not null)
            {
                // on a later day the plan has moved on; what was read stays read: the first chapter not read up to that day
                var day = ReadingPlans.DayOf(start, date, plan.Days);
                var next = planDays.Take(day).SelectMany(d => d).FirstOrDefault(c => !_read.Contains(c));
                planHint = L("{0} · Tag {1} von {2}", plan.Name, day, plan.Days);
                if (next == default) planLabel = L("Heute erledigt ✓");
                else { planLabel = L("Weiterlesen: {0} {1}", BookName(next.Book), next.Chapter); planBook = next.Book; planChapter = next.Chapter; }
            }
            days.Add(new WidgetDay(date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), Label(r), text, t.ShortName, r.Book, r.Chapter, r.Verse, planLabel, planHint, planBook, planChapter));
        }
        return new WidgetSnapshot(1, DateTime.Now.ToString("o", CultureInfo.InvariantCulture), L("Vers des Tages"), L("Heute dran"), L("BibleStudy öffnen"), L("Kein Leseplan – tippen, um einen zu wählen"), planInfo, days);
    }

    /// <summary>A tap on a widget: "verse" opens the verse of the day, "plan" what is next in the reading plan (or the plans, when none runs).</summary>
    internal void OpenFromWidget(string target, int tries = 0)
    {
        // the books come a moment after the start: waited for (a few seconds at most; without them the app just stays where it opened)
        if (_books.Count == 0) { if (tries < 25) Avalonia.Threading.DispatcherTimer.RunOnce(() => OpenFromWidget(target, tries + 1), TimeSpan.FromMilliseconds(400)); return; }
        switch (target)
        {
            case "verse": _ = GoToDailyVerseAsync(VerseOfTheDay()); break;
            case "plan":
                if (PlanTodayCard() is { } plan) plan.Go();
                else OpenWork(NavGroups.First(g => g.Name == "Planen"), PlanTab);
                break;
        }
    }

    sealed record WidgetSnapshot(int Version, string Written, string VerseTitle, string PlanTitle, string OpenHint, string NoPlan, WidgetPlan? Plan, List<WidgetDay> Days);
    sealed record WidgetPlan(string Name, int Read, int Total);
    sealed record WidgetDay(string Date, string Ref, string Text, string Translation, string Book, int Chapter, int Verse, string? Plan, string? PlanHint, string? PlanBook, int? PlanChapter);
}

/// <summary>Where the file for the widgets goes on each phone, and how the widgets are told that it changed.</summary>
static class WidgetFile
{
#if ANDROID || IOS
    public static bool Supported => true;
#else
    public static bool Supported => false;
#endif

    public static void Write(string json)
    {
#if ANDROID
        var ctx = Android.App.Application.Context;
        var path = Path.Combine(ctx.FilesDir!.AbsolutePath, BibleStudy.Droid.TodayWidget.FileName);
        WriteAtomic(path, json);
        BibleStudy.Droid.TodayWidget.UpdateAll(ctx);
#elif IOS
        // the shared folder of the app group: the widget extension reads it there. SideStore renames the group when it signs the app (with the team
        // of the Apple ID), so its real name is taken from the signature of the installed app, not from here.
        if (IosAppGroup.Container() is not { } dir) return;
        WriteAtomic(Path.Combine(dir, "widget.json"), json);
#endif
    }

    static void WriteAtomic(string path, string json)
    {
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, json);
        File.Move(tmp, path, true);   // a widget reading at that moment sees the old file or the new one, never half of one
    }
}
