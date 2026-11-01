using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using BibleStudy.Core;
using BibleStudy.Core.Models;
using BibleStudy.Core.Providers;

namespace BibleStudy.App;

/// <summary>
/// The setup on the very first start: language, a translation to read, and what to fetch now so that it works offline later.
/// Everything can be skipped and changed in the settings at any time.
/// </summary>
public partial class MainWindow
{
    static readonly Dictionary<string, string> LanguageNames = new() { ["de"] = "German", ["en"] = "English", ["es"] = "Spanish", ["fr"] = "French", ["ru"] = "Russian" };

    /// <summary>Returns when the setup window is closed.</summary>
    async Task RunSetupWizardAsync()
    {
        var done = new TaskCompletionSource();
        var languages = Loc.Languages.ToList();
        var chosenCode = Loc.Code;
        var title = new TextBlock { Text = L("Willkommen bei BibleStudy"), FontSize = 24, FontWeight = FontWeight.SemiBold };
        var intro = new TextBlock { Text = L("Drei kurze Schritte. Alles lässt sich später in den Einstellungen ändern, und du kannst jeden Schritt überspringen."), TextWrapping = TextWrapping.Wrap, Opacity = 0.8 };

        var langBox = new ComboBox { ItemsSource = languages.Select(l => l.Name).ToList(), SelectedIndex = Math.Max(0, languages.FindIndex(l => l.Code == Loc.Code)), HorizontalAlignment = HorizontalAlignment.Stretch };
        var transBox = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        void FillTranslations()
        {
            var wanted = LanguageNames.GetValueOrDefault(chosenCode, "English");
            var list = _all.Where(t => t.LanguageName == wanted).OrderBy(t => t.Name).ToList();
            if (list.Count == 0) list = _all.Where(t => t.LanguageName == "English").OrderBy(t => t.Name).ToList();
            transBox.ItemsSource = list;
            transBox.SelectedItem = list.FirstOrDefault(t => t.License.PublicDomain) ?? list.FirstOrDefault();
        }
        FillTranslations();
        var note = new TextBlock { Classes = { "caption" }, TextWrapping = TextWrapping.Wrap };
        langBox.SelectionChanged += (_, _) =>
        {
            if (langBox.SelectedIndex < 0) return;
            chosenCode = languages[langBox.SelectedIndex].Code; FillTranslations();
            note.Text = chosenCode == Loc.Code ? "" : LText(chosenCode, "Die neue Sprache gilt nach einem Neustart des Programms.");
        };

        var offline = new CheckBox { Content = L("Diese Übersetzung jetzt für die Offline-Nutzung laden (einige Minuten, geht auch später)"), IsChecked = false };
        var refs = new CheckBox { Content = L("Querverweise laden (für Graphen und die Vers-Übersicht)"), IsChecked = false };
        var status = new TextBlock { Classes = { "caption" }, TextWrapping = TextWrapping.Wrap };
        var go = new Button { Content = L("Los geht’s"), Classes = { "primary" }, HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0, 12, 0, 0) };
        var skip = new Button { Content = L("Überspringen"), HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0, 12, 8, 0) };

        var stack = new StackPanel { Spacing = 8, Margin = new(24) };
        stack.Children.Add(title); stack.Children.Add(intro);
        stack.Children.Add(new TextBlock { Text = L("1. Sprache des Programms"), FontWeight = FontWeight.SemiBold, Margin = new(0, 10, 0, 0) });
        stack.Children.Add(langBox); stack.Children.Add(note);
        stack.Children.Add(new TextBlock { Text = L("2. Übersetzung zum Lesen"), FontWeight = FontWeight.SemiBold, Margin = new(0, 10, 0, 0) });
        stack.Children.Add(transBox);
        stack.Children.Add(new TextBlock { Text = L("3. Daten gleich laden"), FontWeight = FontWeight.SemiBold, Margin = new(0, 10, 0, 0) });
        stack.Children.Add(offline); stack.Children.Add(refs); stack.Children.Add(status);
        stack.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Children = { skip, go } });

        var win = new Window { Title = L("Einrichtung"), Width = 560, SizeToContent = SizeToContent.Height, MinWidth = 420, CanResize = false, Background = Res("BsSurface"), Icon = Icon, WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = stack };
        var closing = false;
        win.Closed += (_, _) => done.TrySetResult();
        skip.Click += (_, _) => { closing = true; win.Close(); };
        go.Click += async (_, _) =>
        {
            go.IsEnabled = skip.IsEnabled = langBox.IsEnabled = transBox.IsEnabled = false;
            try
            {
                if (transBox.SelectedItem is TranslationInfo t)
                {
                    LanguageBox.SelectedItem = t.LanguageName;
                    TranslationBox.SelectedItem = _all.FirstOrDefault(x => x.Id == t.Id) ?? t;
                    if (offline.IsChecked == true)
                    {
                        var progress = new Progress<(int Done, int Total)>(p => status.Text = L("{0}: {1}/{2} Kapitel geladen …", t.ShortName, p.Done, p.Total));
                        await OfflineDownloader.DownloadAsync(_provider, t.Id, progress);
                    }
                }
                if (refs.IsChecked == true) { status.Text = L("Querverweise werden geladen …"); await BuildGraphAsync(); }
            }
            catch (Exception ex) { status.Text = L("Fehler: ") + ex.Message; await Task.Delay(2500); }
            _settings.Language = chosenCode; SaveSettings();
            closing = true; win.Close();
            if (chosenCode != Loc.Code) Restart();
        };
        win.Show(this);
        await done.Task;
        _ = closing;
    }
}
