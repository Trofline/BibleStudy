using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace BibleStudy.App;

/// <summary>Shown once at the first start (and again when the text changes, see <see cref="Version"/>): terms of use and liability notice.</summary>
sealed class TermsWindow : Window
{
    public const string Version = "1";

    static string[] Points() =>
    [
        L("BibleStudy ist kostenlose Open-Source-Software (MIT-Lizenz). Es wird unentgeltlich und ohne Gewähr „wie besehen“ bereitgestellt."),
        L("Das Programm enthält keine Bibeltexte, sondern lädt sie auf deinen Wunsch aus freien Quellen. Übersetzungen, Kommentare und Daten Dritter stammen nicht von den Entwicklern; für ihre Richtigkeit, Vollständigkeit und Rechtmäßigkeit wird keine Gewähr übernommen. Die jeweilige Lizenz steht beim Text."),
        L("Dateien, die du selbst importierst (z. B. eine Elberfelder-CSV), und eigene Inhalte, die du teilst, exportierst oder veröffentlichst, verantwortest du selbst, einschließlich der Urheberrechte."),
        L("Maschinelle Übersetzungen, Analysen und die KI-Funktion können Fehler enthalten. Prüfe wichtige Aussagen selbst. Das Programm ersetzt keine seelsorgerliche, theologische, rechtliche oder sonstige fachliche Beratung."),
        L("Sichere deine Daten selbst (Sicherungsfunktion). Für Datenverlust, etwa durch Fehler, Updates oder Defekte, wird keine Haftung übernommen, soweit gesetzlich zulässig."),
        L("Haftung: Die Entwickler haften nur für Vorsatz und grobe Fahrlässigkeit sowie nach den gesetzlichen Vorschriften für Schäden an Leben, Körper und Gesundheit und für zwingende gesetzliche Haftung. Im Übrigen ist die Haftung ausgeschlossen, soweit gesetzlich zulässig."),
        L("Optionale Funktionen (Updates, Quellen laden, KI, Zusammen studieren) nutzen das Internet oder Dienste Dritter, deren Bedingungen gelten."),
        L("Wenn du minderjährig bist, nutze das Programm nur mit Einverständnis deiner Erziehungsberechtigten."),
    ];

    public TermsWindow()
    {
        Title = L("Nutzungsbedingungen"); Width = 640; SizeToContent = SizeToContent.Height; MaxHeight = 760; CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var stack = new StackPanel { Spacing = 10 };
        stack.Children.Add(new TextBlock { Text = L("Nutzungsbedingungen und Haftungshinweis"), FontSize = 20, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap });
        stack.Children.Add(new TextBlock { Text = L("Bitte lies das kurz durch. Du brauchst deine Zustimmung nur einmal zu geben."), TextWrapping = TextWrapping.Wrap, Opacity = 0.7 });
        var points = Points();
        for (var i = 0; i < points.Length; i++)
        {
            var row = new Grid { ColumnDefinitions = new("24,*") };
            row.Children.Add(new TextBlock { Text = $"{i + 1}.", Opacity = 0.7 });
            var text = new TextBlock { Text = points[i], TextWrapping = TextWrapping.Wrap };
            Grid.SetColumn(text, 1);
            row.Children.Add(text);
            stack.Children.Add(row);
        }
        var agree = new CheckBox { Content = L("Ich habe das gelesen und stimme zu."), Margin = new(0, 8, 0, 0) };
        var ok = new Button { Content = L("Zustimmen und starten"), IsEnabled = false, Margin = new(0, 0, 8, 0) };
        var no = new Button { Content = L("Ablehnen und beenden") };
        agree.IsCheckedChanged += (_, _) => ok.IsEnabled = agree.IsChecked == true;
        ok.Click += (_, _) => Close(true);
        no.Click += (_, _) => Close(false);
        stack.Children.Add(agree);
        stack.Children.Add(new WrapPanel { Children = { ok, no } });
        Content = new ScrollViewer { Content = new Border { Padding = new(26, 22), Child = stack } };
    }
}
