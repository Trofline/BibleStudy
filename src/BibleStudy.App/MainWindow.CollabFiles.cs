using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;

namespace BibleStudy.App;

/// <summary>
/// Studying together: a file from the own computer (a Word document, a PDF, a picture …) sent to the others, at most 50 MB. It goes sealed end to
/// end like everything else. The receiver sees a card and decides: save it where they want, or decline. Nothing is opened or saved by itself.
/// </summary>
public partial class MainWindow
{
    const long MaxSentFile = 50L * 1024 * 1024;

    async Task PickAndSendFileAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = L("Datei senden"), AllowMultiple = false });
        if (files.Count == 0) return;
        var file = files[0];
        try
        {
            var props = await file.GetBasicPropertiesAsync();
            if (props.Size is ulong size && size > (ulong)MaxSentFile)
            { AddCollabLog(L("„{0}“ ist zu groß ({1}). Senden lassen sich Dateien bis {2}.", file.Name, SizeText((long)size), SizeText(MaxSentFile)), system: true); return; }
            byte[] data;
            await using (var stream = await file.OpenReadAsync())
            {
                using var buffer = new MemoryStream();
                var chunk = new byte[81920];
                int n;
                while ((n = await stream.ReadAsync(chunk)) > 0)
                {
                    buffer.Write(chunk, 0, n);
                    if (buffer.Length > MaxSentFile) { AddCollabLog(L("„{0}“ ist zu groß. Senden lassen sich Dateien bis {1}.", file.Name, SizeText(MaxSentFile)), system: true); return; }
                }
                data = buffer.ToArray();
            }
            if (data.Length == 0) { AddCollabLog(L("„{0}“ ist leer.", file.Name), system: true); return; }
            await SendBlobAsync("file", SafeIncomingFileName(file.Name), data, L("Du hast die Datei „{0}“ gesendet ({1}).", file.Name, SizeText(data.LongLength)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { AddCollabLog(L("Die Datei ließ sich nicht lesen: ") + ex.Message, system: true); }
    }

    /// <summary>A name that is only a name: no folders, no characters a file system refuses, not empty.</summary>
    static string SafeIncomingFileName(string? name)
    {
        var n = System.IO.Path.GetFileName((name ?? "").Replace('\\', '/').Split('/').LastOrDefault() ?? "").Trim().Trim('.');
        foreach (var c in System.IO.Path.GetInvalidFileNameChars().Concat(['<', '>', ':', '"', '|', '?', '*'])) n = n.Replace(c, '_');
        if (n.Length > 100) n = n[..100];
        return n.Length == 0 ? "Datei" : n;
    }

    /// <summary>A file from someone else arrived: a card offers to save it (where the receiver chooses) or to decline it.</summary>
    void ShowReceivedFile(string name, byte[] data)
    {
        name = SafeIncomingFileName(name);
        var who = PeerWho;
        var save = new Button { Content = L("Annehmen und speichern …"), Classes = { "tool" }, Width = double.NaN, Padding = new(10, 0), Margin = new(0, 0, 6, 6) };
        var decline = new Button { Content = L("Ablehnen"), Classes = { "tool" }, Width = double.NaN, Padding = new(10, 0), Margin = new(0, 0, 6, 6) };
        ToolTip.SetTip(save, L("Du wählst, wo die Datei hinkommt. Geöffnet wird sie nicht von selbst; öffne nur Dateien von Menschen, denen du vertraust."));
        var info = new TextBlock { Classes = { "caption" }, TextWrapping = TextWrapping.Wrap };
        var held = data;   // dropped when declined or saved, so it does not stay in memory
        save.Click += async (_, _) =>
        {
            if (held is null) return;
            var ext = System.IO.Path.GetExtension(name);
            var target = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = L("Datei speichern"), SuggestedFileName = name,
                FileTypeChoices = ext.Length > 1 ? [new FilePickerFileType(ext[1..].ToUpperInvariant()) { Patterns = ["*" + ext] }, FilePickerFileTypes.All] : [FilePickerFileTypes.All],
            });
            if (target is null) return;
            try
            {
                await using (var stream = await target.OpenWriteAsync()) await stream.WriteAsync(held);
                held = null;
                save.IsEnabled = decline.IsEnabled = false;
                info.Text = L("Gespeichert ✓ {0}", target.Name);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { info.Text = L("Speichern fehlgeschlagen: ") + ex.Message; }
        };
        decline.Click += (_, _) => { held = null; save.IsEnabled = decline.IsEnabled = false; info.Text = L("Abgelehnt."); };

        var text = L("{0} möchte dir die Datei „{1}“ schicken ({2})", who, name, SizeText(data.LongLength));
        AddCollabItem(Card(new StackPanel
        {
            Spacing = 6,
            Children =
            {
                new TextBlock { Text = text, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap },
                new WrapPanel { Children = { save, decline } },
                info,
            },
        }));
        NotifyChat(text);
    }
}
