using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace BibleStudy.App;

/// <summary>
/// Studying together only starts after the user has read and agreed to what it means (who sees what, own responsibility, no warranty).
/// The agreement is stored with a version; a changed text (new version number) asks again. It can be withdrawn at any time.
/// </summary>
public partial class MainWindow
{
    const string CollabConsentVersion = "1";

    bool CollabConsented => _settings.CollabConsent.StartsWith(CollabConsentVersion + "|", StringComparison.Ordinal);

    static readonly string[] ConsentPoints =
    [
        L("Es gibt keinen Server der Entwickler. Die Entwickler erhalten, speichern und sehen keine Daten aus dieser Verbindung."),
        L("Die andere Person sieht deinen Namen, deine Internetadresse (IP-Adresse) und alles, was du ausdrücklich sendest. Beim Verbindungsaufbau erfährt außerdem ein öffentlicher Hilfsdienst (STUN von Google bzw. Cloudflare) deine IP-Adresse; Inhalte laufen dort nicht durch."),
        L("Du entscheidest selbst, mit wem du dich verbindest und was du teilst. Verbinde dich nur mit Personen, denen du vertraust, und gib die Codes nur an sie weiter."),
        L("Teile nur Inhalte, an denen du die Rechte hast oder deren Weitergabe erlaubt ist. Importierte Bibeltexte (nur private Nutzung) verschickt dieses Programm nicht; Texte, die du in eigenen Dokumenten zitierst, prüfst du selbst."),
        L("Nutze die Funktion nicht für rechtswidrige, beleidigende oder belästigende Inhalte. Für das, was du sendest, bist du verantwortlich; was du empfängst, ist ungeprüft."),
        L("Wenn du minderjährig bist, nutze die Funktion nur mit Einverständnis deiner Erziehungsberechtigten."),
        L("Das Programm wird, wie in der MIT-Lizenz beschrieben, ohne Gewähr bereitgestellt. Die Entwickler haften im gesetzlich zulässigen Umfang nicht für Schäden aus der Nutzung dieser Funktion; gesetzlich zwingende Haftung bleibt unberührt."),
        L("Du kannst die Verbindung jederzeit trennen und diese Zustimmung hier wieder zurücknehmen."),
    ];

    void BuildCollabConsent()
    {
        var stack = new StackPanel { Spacing = 8 };
        stack.Children.Add(new TextBlock { Text = L("Bevor du zusammen studierst"), FontWeight = FontWeight.SemiBold, FontSize = 16 });
        stack.Children.Add(new TextBlock { Text = L("Diese Funktion verbindet dein Programm direkt mit dem Programm einer anderen Person. Bitte lies und bestätige:"), TextWrapping = TextWrapping.Wrap, Classes = { "caption" } });
        for (var i = 0; i < ConsentPoints.Length; i++)
        {
            var row = new Grid { ColumnDefinitions = new("22,*") };
            row.Children.Add(new TextBlock { Text = $"{i + 1}.", Classes = { "caption" } });
            var text = new TextBlock { Text = ConsentPoints[i], TextWrapping = TextWrapping.Wrap };
            Grid.SetColumn(text, 1);
            row.Children.Add(text);
            stack.Children.Add(row);
        }
        var agree = new CheckBox { Content = L("Ich habe das gelesen und stimme zu."), Margin = new(0, 8, 0, 0) };
        var go = new Button { Content = L("Zustimmen und fortfahren"), Classes = { "primary" }, IsEnabled = false, Margin = new(0, 0, 8, 0) };
        var no = new Button { Content = L("Nicht zustimmen") };
        var info = new TextBlock { Classes = { "caption" }, TextWrapping = TextWrapping.Wrap };
        agree.IsCheckedChanged += (_, _) => go.IsEnabled = agree.IsChecked == true;
        go.Click += (_, _) =>
        {
            _settings.CollabConsent = $"{CollabConsentVersion}|{DateTime.Now:yyyy-MM-dd}";
            SaveSettings();
            BuildCollabUi();
        };
        no.Click += (_, _) => info.Text = L("Ohne Zustimmung bleibt die Funktion aus. Alles andere im Programm funktioniert wie gewohnt.");
        stack.Children.Add(agree);
        stack.Children.Add(new WrapPanel { Children = { go, no } });
        stack.Children.Add(info);
        CollabPanel.Children.Add(Card(stack));
    }

    /// <summary>Withdraws the agreement and ends a running connection.</summary>
    void RevokeCollabConsent()
    {
        _peer?.Dispose(); _peer = null; _follow = false;
        _settings.CollabConsent = "";
        SaveSettings();
        BuildCollabUi();
    }
}
