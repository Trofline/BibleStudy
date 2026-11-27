using System.Text;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using BibleStudy.Core.Storage;

namespace BibleStudy.App;

/// <summary>
/// Studying together: reading paths sent to the others, in the same form as the export file (the steps in their order and, if wished, the marks on
/// those verses). The receiver sees a request and decides: accept (the paths are added as new ones, a name that is taken gets a number, nothing of
/// their own is overwritten), only look at them until the program closes, or decline. Everything received is checked first.
/// </summary>
public partial class MainWindow
{
    const long MaxSentPathBytes = 8L * 1024 * 1024;

    void ShowSendPathsMenu(Control anchor)
    {
        var all = ReadingPaths();
        if (all.Count == 0) { AddCollabLog(L("Du hast noch keinen Lesepfad zum Senden."), system: true); return; }
        var pick = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, MinWidth = 240 };
        pick.Items.Add(new ComboBoxItem { Content = L("Alle Lesepfade ({0})", all.Count) });
        foreach (var p in all) pick.Items.Add(new ComboBoxItem { Content = L("„{0}“ · {1} Schritte", p.Name, p.Steps.Count) });
        pick.SelectedIndex = all.Count == 1 ? 1 : 0;
        var withMarks = new CheckBox { Content = L("Mit den Markierungen auf diesen Versen"), IsChecked = true };
        var help = new TextBlock
        {
            Classes = { "caption" }, TextWrapping = TextWrapping.Wrap, MaxWidth = 300,
            Text = L("Die anderen bekommen eine Anfrage. Nehmen sie an, kommt der Lesepfad als neuer Pfad zu ihnen; ihre eigenen Pfade bleiben, wie sie sind."),
        };
        var send = new Button { Content = L("Senden"), Classes = { "primary" }, HorizontalAlignment = HorizontalAlignment.Left };
        var fly = new Flyout
        {
            Content = new StackPanel { Spacing = 8, MaxWidth = 320, Children = { new TextBlock { Text = L("Lesepfad senden"), FontWeight = FontWeight.SemiBold }, pick, withMarks, help, send } },
        };
        send.Click += async (_, _) =>
        {
            fly.Hide();
            var paths = pick.SelectedIndex is int i && i >= 1 && i <= all.Count ? [all[i - 1]] : all;
            await SendPathsAsync(paths, withMarks.IsChecked == true);
        };
        fly.ShowAt(anchor);
    }

    async Task SendPathsAsync(List<ReadingPath> paths, bool withMarks)
    {
        byte[] bytes;
        try { bytes = Encoding.UTF8.GetBytes(ReadingPathFile.Write(_vault, paths, withMarks)); }
        catch (Exception ex) { AddCollabLog(L("Fehler: ") + ex.Message, system: true); return; }
        if (bytes.LongLength > MaxSentPathBytes) { AddCollabLog(L("Diese Lesepfade sind zu groß zum Senden. Versuch es mit einem einzelnen Pfad oder ohne Markierungen."), system: true); return; }
        var what = paths.Count == 1 ? paths[0].Name : L("{0} Lesepfade", paths.Count);
        await SendBlobAsync("paths", what, bytes, paths.Count == 1 ? L("Du hast den Lesepfad „{0}“ gesendet.", what) : L("Du hast {0} gesendet.", what));
    }

    /// <summary>Paths from someone else arrived: a card asks whether to take them (untrusted, so read and checked like a file from outside).</summary>
    void ShowReceivedPaths(byte[] data)
    {
        if (data.LongLength > MaxSentPathBytes) return;
        List<ReadingPathFile.Imported> found;
        try { found = ReadingPathFile.Checked(ReadingPathFile.Read(Encoding.UTF8.GetString(data)), book => _books.Any(b => b.Id == book)); }
        catch (Exception ex) when (ex is InvalidDataException or ArgumentException or FormatException) { return; }
        if (found.Count == 0) return;

        var who = PeerWho;
        var marks = found.Sum(f => f.Marks.Count);
        var accept = new Button { Content = L("Annehmen"), Classes = { "tool" }, Width = double.NaN, Padding = new(10, 0), Margin = new(0, 0, 6, 6) };
        var look = new Button { Content = L("Nur ansehen"), Classes = { "tool" }, Width = double.NaN, Padding = new(10, 0), Margin = new(0, 0, 6, 6) };
        var decline = new Button { Content = L("Ablehnen"), Classes = { "tool" }, Width = double.NaN, Padding = new(10, 0), Margin = new(0, 0, 6, 6) };
        ToolTip.SetTip(accept, L("Kommt als neuer Lesepfad in deinen Vault. Ist der Name schon vergeben, bekommt er eine Nummer; nichts wird überschrieben."));
        ToolTip.SetTip(look, L("Gilt nur, bis du das Programm schließt; gespeichert wird nichts."));
        var info = new TextBlock { Classes = { "caption" }, TextWrapping = TextWrapping.Wrap };
        async Task Take(int keep)
        {
            accept.IsEnabled = look.IsEnabled = decline.IsEnabled = false;
            try
            {
                await ImportPathsAsync(found, keepChoice: keep);
                info.Text = keep == 0 ? L("Angenommen ✓") : L("Zum Ansehen geöffnet ✓");
            }
            catch (Exception ex) { ErrorLog.Write(ex); info.Text = L("Import fehlgeschlagen: ") + ex.Message; accept.IsEnabled = look.IsEnabled = decline.IsEnabled = true; }
        }
        accept.Click += async (_, _) => await Take(0);
        look.Click += async (_, _) => await Take(1);
        decline.Click += (_, _) => { accept.IsEnabled = look.IsEnabled = decline.IsEnabled = false; info.Text = L("Abgelehnt."); };

        var text = found.Count == 1
            ? L("{0} möchte dir den Lesepfad „{1}“ schicken ({2} Schritte)", who, found[0].Path.Name, found[0].Path.Steps.Count)
            : L("{0} möchte dir {1} Lesepfade schicken", who, found.Count);
        var details = string.Join("\n", found.Take(8).Select(f => "• " + L("„{0}“ · {1} Schritte", f.Path.Name, f.Path.Steps.Count))) + (found.Count > 8 ? "\n…" : "");
        if (marks > 0) details += "\n" + L("Mit {0} Markierungen auf diesen Versen.", marks);
        AddCollabItem(Card(new StackPanel
        {
            Spacing = 6,
            Children =
            {
                new TextBlock { Text = text, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap },
                new TextBlock { Text = details, Classes = { "caption" }, TextWrapping = TextWrapping.Wrap },
                new WrapPanel { Children = { accept, look, decline } },
                info,
            },
        }));
        NotifyChat(text);
    }
}
