using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Input.Platform;
using BibleStudy.Core;

namespace BibleStudy.App;

/// <summary>The page "Sources and data" in the settings: every source with its purpose, author, licence and link.</summary>
public partial class MainWindow
{
    void InitSources()
    {
        foreach (var s in DataSources.All)
        {
            var source = s;
            var panel = new StackPanel { Spacing = 2 };
            panel.Children.Add(new TextBlock { Text = source.Name, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap });
            panel.Children.Add(new TextBlock { Text = source.Purpose, Classes = { "caption" }, TextWrapping = TextWrapping.Wrap });
            panel.Children.Add(new TextBlock { Text = $"{L("Von")}: {source.Author}  ·  {L("Lizenz")}: {source.License}", FontSize = 12, TextWrapping = TextWrapping.Wrap });
            if (source.Url.Length > 0)
            {
                var link = new Button { Content = source.Url, Classes = { "ghost" }, Padding = new(0), HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left, FontSize = 12, Foreground = Res("BsAccent") };
                link.Click += (_, _) => OpenUrl(source.Url);
                panel.Children.Add(link);
            }
            SourcesList.Children.Add(panel);
        }
        SourcesNote.Text = L("Alle Daten stammen von Dritten und wurden von Menschen oder Programmen erstellt. Strong, Easton und Abbott-Smith waren Gelehrte des 19. und frühen 20. Jahrhunderts aus protestantischen Kirchen und sind Kinder ihrer Zeit. Keine Quelle ist verbindlich: Prüfe alles an der Bibel selbst, egal, was es ist und von wem es kommt.");
        ShowStepCheck.IsChecked = _settings.ShowStepLexicon;
        ShowStepCheck.IsCheckedChanged += (_, _) => { _settings.ShowStepLexicon = ShowStepCheck.IsChecked == true; SaveSettings(); };
        SourcesNoticeBtn.Click += async (_, _) => await ShowSourcesNoticeAsync();
        // once, after the program has opened: where the data comes from and not to believe everything
        Avalonia.Threading.DispatcherTimer.RunOnce(async () => { if (_settings.SourcesNoticeSeen != SourcesNoticeVersion && IsVisible) { await ShowSourcesNoticeAsync(); _settings.SourcesNoticeSeen = SourcesNoticeVersion; SaveSettings(); } }, TimeSpan.FromSeconds(3));
        SourcesCopyBtn.Click += async (_, _) =>
        {
            if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard) { await clipboard.SetTextAsync(DataSources.AsText()); StatusText.Text = L("Die Liste der Quellen wurde kopiert."); }
        };
        SourcesCacheBtn.Click += (_, _) =>
        {
            var dir = Path.Combine(_root, "cache");
            Directory.CreateDirectory(dir);
            OpenUrl(dir);
        };
    }

    const string SourcesNoticeVersion = "1";

    /// <summary>The one-time note: who delivers the data, that it can be wrong, and that everything has to be checked against Scripture.</summary>
    async Task ShowSourcesNoticeAsync()
    {
        var text = L("Querverweise, Personen, Orte, Zeitleiste, Wörterbücher und Grammatikdaten in BibleStudy stammen nicht von uns, sondern aus freien Quellen Dritter:\n\n• Querverweise: OpenBible.info (von Lesern bewertet)\n• Personen, Orte, Zeitleiste, Lexikon: Theographic Bible Metadata\n• Wörterbücher: Open Scriptures (Strong), STEPBible (Tyndale House)\n• Grammatik und Satzbau: MACULA (Biblica, Clear Bible)\n• Wort für Wort: Berean Interlinear\n• Bibeltexte: Free Use Bible API\n\nDiese Daten wurden von Menschen und Programmen erstellt und können Fehler enthalten. Glaube nichts, nur weil es online oder in einem Programm steht – auch nicht hier. Prüfe alles an der Bibel selbst, egal, was es ist und von wem es kommt (vgl. Apg 17,11).\n\nEinen Querverweis, den du für falsch hältst, kannst du dauerhaft ausblenden (Rechtsklick darauf). Die vollständige Liste steht unter Einstellungen → Quellen und Daten.");
        await AskAsync(L("Woher die Daten kommen – bitte kurz lesen"), text, L("Verstanden"));
    }
}
