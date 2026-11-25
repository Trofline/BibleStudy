using System.Globalization;
using System.Text.Json;
using Android.App;
using Android.Appwidget;
using Android.Content;
using Android.Widget;

// the Android project builds into the namespace BibleStudy.App, so its generated Resource class lives there
using Resource = BibleStudy.App.Resource;

namespace BibleStudy.Droid;

/// <summary>
/// The widget on the home screen: the verse of the day and what is next in the reading plan, with the progress of the plan.
/// It reads the small file that the app writes (see MainWindow.Widgets.cs): an entry for today and the next two weeks, so the verse changes at midnight
/// even when the app has not been opened. A tap on the verse opens it in the app, a tap on the plan opens the reading.
/// </summary>
[BroadcastReceiver(Name = "de.trofline.biblestudy.TodayWidget", Label = "@string/widget_today_name", Exported = true)]
[IntentFilter(new[] { AppWidgetManager.ActionAppwidgetUpdate })]
[MetaData(AppWidgetManager.MetaDataAppwidgetProvider, Resource = "@xml/widget_today_info")]
public class TodayWidget : AppWidgetProvider
{
    public const string FileName = "widget.json";
    public const string Extra = "de.trofline.biblestudy.widget";

    public override void OnUpdate(Context? context, AppWidgetManager? appWidgetManager, int[]? appWidgetIds)
    {
        if (context is null || appWidgetManager is null || appWidgetIds is null) return;
        var views = Build(context);
        foreach (var id in appWidgetIds) appWidgetManager.UpdateAppWidget(id, views);
    }

    /// <summary>The app wrote a new file: every widget on the home screen shows it at once.</summary>
    public static void UpdateAll(Context context)
    {
        try
        {
            var manager = AppWidgetManager.GetInstance(context);
            var ids = manager?.GetAppWidgetIds(new ComponentName(context, Java.Lang.Class.FromType(typeof(TodayWidget))));
            if (manager is null || ids is null || ids.Length == 0) return;
            var views = Build(context);
            foreach (var id in ids) manager.UpdateAppWidget(id, views);
        }
        catch (Exception ex) when (ex is Java.Lang.Exception or InvalidOperationException) { /* the widget keeps what it shows */ }
    }

    static RemoteViews Build(Context context)
    {
        var v = new RemoteViews(context.PackageName, Resource.Layout.widget_today);
        var (snap, day) = Read(context);
        v.SetOnClickPendingIntent(Resource.Id.widget_verse, Tap(context, "verse", 1));
        v.SetOnClickPendingIntent(Resource.Id.widget_plan, Tap(context, "plan", 2));
        if (snap is null || day is null)
        {
            // not filled yet (the app was never opened since the widget came) or the two weeks are over: an invitation to open the app
            v.SetTextViewText(Resource.Id.widget_verse_title, context.GetString(Resource.String.widget_today_name));
            v.SetTextViewText(Resource.Id.widget_verse_text, snap?.OpenHint ?? context.GetString(Resource.String.widget_open_app));
            v.SetViewVisibility(Resource.Id.widget_verse_ref, Android.Views.ViewStates.Gone);
            v.SetViewVisibility(Resource.Id.widget_plan, Android.Views.ViewStates.Gone);
            return v;
        }
        v.SetTextViewText(Resource.Id.widget_verse_title, day.Translation.Length > 0 ? $"{snap.VerseTitle} · {day.Translation}" : snap.VerseTitle);
        v.SetTextViewText(Resource.Id.widget_verse_text, day.Text.Length > 0 ? day.Text : day.Ref);
        v.SetTextViewText(Resource.Id.widget_verse_ref, day.Ref);
        v.SetViewVisibility(Resource.Id.widget_verse_ref, day.Text.Length > 0 ? Android.Views.ViewStates.Visible : Android.Views.ViewStates.Gone);

        v.SetViewVisibility(Resource.Id.widget_plan, Android.Views.ViewStates.Visible);
        v.SetTextViewText(Resource.Id.widget_plan_title, snap.PlanTitle);
        if (snap.Plan is { } plan && day.Plan is { } label)
        {
            v.SetTextViewText(Resource.Id.widget_plan_label, label);
            var percent = plan.Total > 0 ? (int)Math.Round(100.0 * plan.Read / plan.Total) : 0;
            v.SetTextViewText(Resource.Id.widget_plan_hint, $"{day.PlanHint} · {percent} %");
            v.SetViewVisibility(Resource.Id.widget_plan_hint, Android.Views.ViewStates.Visible);
            v.SetViewVisibility(Resource.Id.widget_plan_progress, Android.Views.ViewStates.Visible);
            v.SetProgressBar(Resource.Id.widget_plan_progress, Math.Max(1, plan.Total), plan.Read, false);
        }
        else
        {
            v.SetTextViewText(Resource.Id.widget_plan_label, snap.NoPlan);
            v.SetViewVisibility(Resource.Id.widget_plan_hint, Android.Views.ViewStates.Gone);
            v.SetViewVisibility(Resource.Id.widget_plan_progress, Android.Views.ViewStates.Gone);
        }
        return v;
    }

    /// <summary>Opens the app (the one that is running, if any) and tells it what was tapped.</summary>
    static PendingIntent? Tap(Context context, string target, int code)
    {
        var intent = new Intent(context, typeof(MainActivity));
        intent.SetAction(Extra + "." + target);   // each tap needs an intent of its own, or Android hands out the same one for both
        intent.PutExtra(Extra, target);
        intent.AddFlags(ActivityFlags.NewTask | ActivityFlags.SingleTop);
        return PendingIntent.GetActivity(context, code, intent, PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);
    }

    static (Snapshot?, Day?) Read(Context context)
    {
        try
        {
            var path = Path.Combine(context.FilesDir!.AbsolutePath, FileName);
            if (!File.Exists(path)) return (null, null);
            var snap = JsonSerializer.Deserialize<Snapshot>(File.ReadAllText(path), Json);
            var today = DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            return (snap, snap?.Days?.FirstOrDefault(d => d.Date == today));
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or Java.Lang.Exception) { return (null, null); }
    }

    static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    sealed class Snapshot
    {
        public string VerseTitle { get; set; } = "";
        public string PlanTitle { get; set; } = "";
        public string OpenHint { get; set; } = "";
        public string NoPlan { get; set; } = "";
        public PlanInfo? Plan { get; set; }
        public List<Day>? Days { get; set; }
    }

    sealed class PlanInfo
    {
        public string Name { get; set; } = "";
        public int Read { get; set; }
        public int Total { get; set; }
    }

    sealed class Day
    {
        public string Date { get; set; } = "";
        public string Ref { get; set; } = "";
        public string Text { get; set; } = "";
        public string Translation { get; set; } = "";
        public string? Plan { get; set; }
        public string? PlanHint { get; set; }
    }
}
