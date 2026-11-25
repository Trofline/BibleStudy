using Android.App;
using Android.Appwidget;
using Android.Content;
using Android.Widget;
using BibleStudy.Core;
using Resource = global::BibleStudy.App.Resource;   // the ids of the layout (generated in the root namespace of the app)

namespace BibleStudy.Droid;

/// <summary>
/// The "Today" widget on the home screen: the verse of the day and what the reading plan has for today. It draws only what the app wrote into
/// widget.json (see MainWindow.TodayWidget) and needs nothing else of the app. A tap on the verse opens it in the app, a tap on the plan opens
/// the next chapter. Android redraws it once an hour (so the verse changes after midnight) and the app asks for it when it goes to the background.
/// </summary>
[BroadcastReceiver(Label = "BibleStudy", Exported = true)]
[IntentFilter(new[] { AppWidgetManager.ActionAppwidgetUpdate })]
[MetaData(AppWidgetManager.MetaDataAppwidgetProvider, Resource = "@xml/widget_today")]
public class TodayWidget : AppWidgetProvider
{
    public override void OnUpdate(Context? context, AppWidgetManager? manager, int[]? ids)
    {
        if (context is null || manager is null || ids is null) return;
        var views = Build(context);
        foreach (var id in ids) manager.UpdateAppWidget(id, views);
    }

    /// <summary>Redraws every "Today" widget on the home screen (after the app wrote new content).</summary>
    public static void Refresh()
    {
        try
        {
            var context = global::Android.App.Application.Context;
            var manager = AppWidgetManager.GetInstance(context);
            var ids = manager?.GetAppWidgetIds(new ComponentName(context, Java.Lang.Class.FromType(typeof(TodayWidget))));
            if (manager is null || ids is null || ids.Length == 0) return;
            var views = Build(context);
            foreach (var id in ids) manager.UpdateAppWidget(id, views);
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }   // a widget that is not redrawn is no reason to stop the app
    }

    static RemoteViews Build(Context context)
    {
        var views = new RemoteViews(context.PackageName, Resource.Layout.widget_today);
        var card = TodayCard.Load(AppPaths.Root);
        var german = Java.Util.Locale.Default.Language == "de";
        var today = DateTime.Today;
        var openApp = Open(context, null, 0);

        if (card?.VerseOf(today) is { } verse)
        {
            views.SetTextViewText(Resource.Id.widget_verse_title, card.Word("verse", german ? "Vers des Tages" : "Verse of the day") + (card.TranslationName.Length > 0 ? " · " + card.TranslationName : ""));
            views.SetTextViewText(Resource.Id.widget_verse_text, verse.Text.Length > 0 ? verse.Text : verse.Label);
            views.SetTextViewText(Resource.Id.widget_verse_ref, verse.Text.Length > 0 ? verse.Label : "");
            views.SetOnClickPendingIntent(Resource.Id.widget_verse_box, Open(context, verse.Link, 1));
        }
        else
        {
            views.SetTextViewText(Resource.Id.widget_verse_title, "BibleStudy");
            views.SetTextViewText(Resource.Id.widget_verse_text, german ? "Öffne BibleStudy einmal, dann erscheint hier der Vers des Tages." : "Open BibleStudy once and the verse of the day appears here.");
            views.SetTextViewText(Resource.Id.widget_verse_ref, "");
            views.SetOnClickPendingIntent(Resource.Id.widget_verse_box, openApp);
        }

        if (card?.PlanOf(today) is { } plan)
        {
            views.SetTextViewText(Resource.Id.widget_plan_title, card.Word("plan", german ? "Heute dran" : "Today") + ": " + plan.Title);
            views.SetTextViewText(Resource.Id.widget_plan_hint, plan.Hint);
            views.SetOnClickPendingIntent(Resource.Id.widget_plan_box, plan.Link.Length > 0 ? Open(context, plan.Link, 2) : openApp);
        }
        else
        {
            // no plan, or the app was not opened today: what is next is only known to the app
            var hasPlan = card?.Reading is not null;
            views.SetTextViewText(Resource.Id.widget_plan_title, hasPlan ? card!.Word("plan", german ? "Heute dran" : "Today") : "");
            views.SetTextViewText(Resource.Id.widget_plan_hint, hasPlan ? (german ? "Tippen, um weiterzulesen" : "Tap to read on") : card?.Word("empty", "") ?? "");
            views.SetOnClickPendingIntent(Resource.Id.widget_plan_box, openApp);
        }
        views.SetOnClickPendingIntent(Resource.Id.widget_root, openApp);
        return views;
    }

    /// <summary>Opens the app, at a passage when <paramref name="link"/> is given (biblestudy://open/…). Each target needs its own request code.</summary>
    static PendingIntent Open(Context context, string? link, int code)
    {
        var intent = new Intent(context, typeof(MainActivity));
        intent.SetAction(link is null ? Intent.ActionMain : Intent.ActionView);
        if (link is not null) intent.SetData(global::Android.Net.Uri.Parse(link));
        intent.AddFlags(ActivityFlags.NewTask | ActivityFlags.ClearTop);
        return PendingIntent.GetActivity(context, code, intent, PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable)!;
    }
}
