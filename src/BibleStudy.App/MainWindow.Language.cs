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
        try
        {
            var exe = Environment.ProcessPath;
            if (exe is null) return;
            _exiting = true;
            // a moment's wait lets this instance release its files first
            Process.Start(new ProcessStartInfo("cmd.exe", $"/c ping 127.0.0.1 -n 2 >nul & start \"\" \"{exe}\"") { CreateNoWindow = true, UseShellExecute = false });
            Close();
        }
        catch { /* the language applies at the next manual start */ }
    }
}
