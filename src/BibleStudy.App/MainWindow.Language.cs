using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Layout;
using BibleStudy.Core.Storage;

namespace BibleStudy.App;

/// <summary>Choosing the program's language. It applies at the next start (every text is looked up when it is created).</summary>
public partial class MainWindow
{
    void InitLanguage()
    {
        var items = Loc.Languages.ToList();
        UiLanguageBox.ItemsSource = items.Select(l => l.Name).ToList();
        UiLanguageBox.SelectedIndex = Math.Max(0, items.FindIndex(l => l.Code == Loc.Code));
        UiLanguageBox.SelectionChanged += async (_, _) =>
        {
            if (UiLanguageBox.SelectedIndex < 0 || UiLanguageBox.SelectedIndex >= items.Count) return;
            var code = items[UiLanguageBox.SelectedIndex].Code;
            if (code == Loc.Code) { LanguageNote.Text = ""; return; }
            _settings.Language = code; SaveSettings();
            LanguageNote.Text = LText(code, "Die neue Sprache gilt nach einem Neustart des Programms.");
            var choice = await AskAsync(LText(code, "Sprache ändern"), LText(code, "Die neue Sprache gilt nach einem Neustart des Programms. Jetzt neu starten?"), LText(code, "Jetzt neu starten"), LText(code, "Später"));
            if (choice == 0) Restart();
        };
    }

    /// <summary>A text in another language than the current one (so the question appears in the language that was just chosen).</summary>
    static string LText(string code, string de)
    {
        if (code == "de") return de;
        return Loc.Texts(code).TryGetValue(de, out var t) && t.Length > 0 ? t : de;
    }

    void Restart()
    {
#if ANDROID
        // a phone has no program file to start again: the app is opened anew and this process ends (so that a new language really applies)
        try
        {
            SaveForBackground();
            var ctx = Android.App.Application.Context;
            var intent = new Android.Content.Intent(ctx, typeof(BibleStudy.Droid.RestartActivity));
            intent.AddFlags(Android.Content.ActivityFlags.NewTask);
            ctx.StartActivity(intent);
            Avalonia.Threading.DispatcherTimer.RunOnce(() => Android.OS.Process.KillProcess(Android.OS.Process.MyPid()), TimeSpan.FromMilliseconds(300));
        }
        catch (Exception ex) when (ex is Java.Lang.Exception or InvalidOperationException) { ErrorLog.Write(ex); }
        return;
#elif IOS
        // an iPhone does not let an app start itself again: the person closes it and opens it anew
        StatusText.Text = L("Schließe die App und öffne sie neu (am unteren Rand nach oben wischen), damit die Änderung gilt.");
        return;
#endif
        try
        {
            var exe = Environment.ProcessPath;
            if (exe is null) return;
            _exiting = true;
            // a moment's wait lets this instance release its files first
            if (OperatingSystem.IsWindows())
                Process.Start(new ProcessStartInfo("cmd.exe", $"/c ping 127.0.0.1 -n 2 >nul & start \"\" \"{exe}\"") { CreateNoWindow = true, UseShellExecute = false });
            else
                Process.Start(new ProcessStartInfo("/bin/sh") { ArgumentList = { "-c", "sleep 1; exec \"$0\"", exe }, UseShellExecute = false });
            Close();
        }
        catch { /* the language applies at the next manual start */ }
    }
}
