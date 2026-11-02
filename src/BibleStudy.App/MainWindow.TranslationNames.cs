using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using BibleStudy.Core;
using BibleStudy.Core.Models;
using BibleStudy.Core.Storage;

namespace BibleStudy.App;

/// <summary>Own names for translations (imported or ready-made), so similar ones are easy to tell apart. The originals stay and can be restored.</summary>
public partial class MainWindow
{
    IReadOnlyList<TranslationInfo> _allOriginal = [];

    /// <summary>The translation list with the user's own names applied.</summary>
    IReadOnlyList<TranslationInfo> Named(IReadOnlyList<TranslationInfo> list)
    {
        _allOriginal = list;
        return list.Select(t => _settings.TranslationAliases.TryGetValue(t.Id, out var a)
            ? t with { Name = a.Name.Length > 0 ? a.Name : t.Name, ShortName = a.Short.Length > 0 ? a.Short : t.ShortName }
            : t).ToList();
    }

    string? TranslationId() => (TranslationBox.SelectedItem as TranslationInfo)?.Id;

    void ShowRenameTranslation(TranslationInfo t)
    {
        var original = _allOriginal.FirstOrDefault(x => x.Id == t.Id) ?? t;
        _settings.TranslationAliases.TryGetValue(t.Id, out var alias);
        var name = new TextBox { Text = alias?.Name ?? "", Watermark = original.Name, MaxLength = 80 };
        var shortName = new TextBox { Text = alias?.Short ?? "", Watermark = original.ShortName, MaxLength = 12, Width = 160, HorizontalAlignment = HorizontalAlignment.Left };
        var save = new Button { Content = L("Speichern"), Classes = { "primary" } };
        var cancel = new Button { Content = L("Abbrechen") };
        var stack = new StackPanel
        {
            Spacing = 6, Margin = new(20),
            Children =
            {
                new TextBlock { Text = L("Übersetzung umbenennen"), Classes = { "h2" } },
                new TextBlock { Text = L("Ursprünglich: {0} ({1})", original.Name, original.ShortName), Classes = { "caption" }, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                new TextBlock { Text = L("Name"), Classes = { "caption" }, Margin = new(0, 8, 0, 0) }, name,
                new TextBlock { Text = L("Kurzname (erscheint oben in der Leiste)"), Classes = { "caption" }, Margin = new(0, 8, 0, 0) }, shortName,
                new TextBlock { Text = L("Leer lassen heißt: der ursprüngliche Name bleibt."), Classes = { "caption" } },
                new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0, 12, 0, 0), Children = { cancel, save } },
            },
        };
        save.Margin = new(8, 0, 0, 0);
        var win = new Window { Title = L("Übersetzung umbenennen"), Width = 460, SizeToContent = SizeToContent.Height, CanResize = false, Background = Res("BsSurface"), Icon = Icon, WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = stack };
        cancel.Click += (_, _) => win.Close();
        save.Click += async (_, _) =>
        {
            var n = (name.Text ?? "").Trim(); var s = (shortName.Text ?? "").Trim();
            if (n.Length == 0 && s.Length == 0) _settings.TranslationAliases.Remove(t.Id);
            else _settings.TranslationAliases[t.Id] = new TranslationAlias { Name = n, Short = s };
            SaveSettings();
            win.Close();
            await ReloadTranslationsAsync(TranslationId());
            StatusText.Text = L("Name gespeichert.");
        };
        name.KeyDown += (_, e) => { if (e.Key == Avalonia.Input.Key.Enter) save.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); };
        win.Show(this);
        name.Focus();
    }
}
