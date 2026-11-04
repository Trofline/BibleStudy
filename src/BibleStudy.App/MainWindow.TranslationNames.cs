using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using BibleStudy.Core;
using BibleStudy.Core.Import;
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

    // ---- names of the books ---------------------------------------------------------------------------------------------

    /// <summary>The books with the names the user chose in the settings: as the translation has them, always German or always English.</summary>
    /// <summary>The language of the short book names (tiles, charts): the one chosen for the book names, else the language of the program.</summary>
    internal static string BookNameLanguage { get; private set; } = Loc.Code;

    IReadOnlyList<BookInfo> ApplyBookNames(IReadOnlyList<BookInfo> books)
    {
        var mode = _settings.BookNames;
        BookNameLanguage = mode is "de" or "en" ? mode : Loc.Code;
        if (mode is not ("de" or "en")) return books;
        return [.. books.Select(b => BookData.UsfmIds.Contains(b.Id) ? b with { Name = BookData.NameOf(b.Id, mode), CommonName = BookData.NameOf(b.Id, mode) } : b)];
    }

    void InitBookNames()
    {
        BookNamesBox.SelectedIndex = _settings.BookNames switch { "de" => 1, "en" => 2, _ => 0 };
        BookNamesBox.SelectionChanged += async (_, _) =>
        {
            var mode = BookNamesBox.SelectedIndex switch { 1 => "de", 2 => "en", _ => "Auto" };
            if (mode == _settings.BookNames) return;
            _settings.BookNames = mode; SaveSettings();
            await LoadBooksAsync();   // the book list, the start page and the picker take the names over
            StatusText.Text = L("Namen der Bücher geändert.");
        };
        ShareMarksCheck.IsChecked = _settings.ShareMarksAcrossTranslations;
        ShareMarksCheck.IsCheckedChanged += async (_, _) => { _settings.ShareMarksAcrossTranslations = ShareMarksCheck.IsChecked == true; SaveSettings(); await RedrawChapterAsync(); };
        SelectionPopupCheck.IsChecked = _settings.SelectionPopup;
        SelectionPopupCheck.IsCheckedChanged += (_, _) => { _settings.SelectionPopup = SelectionPopupCheck.IsChecked == true; SaveSettings(); };
        InitAppearance();
    }

    readonly HashSet<string> _aliasRemoved = [];

    /// <summary>A second running copy (installed and development, say) must not wipe the names this one never saw: names found in the file are kept unless this copy removed them.</summary>
    void KeepAliasesOfOtherInstances()
    {
        try
        {
            if (!File.Exists(_settingsPath)) return;
            var disk = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_settingsPath));
            if (disk is null) return;
            foreach (var (id, a) in disk.TranslationAliases)
                if (!_settings.TranslationAliases.ContainsKey(id) && !_aliasRemoved.Contains(id)) _settings.TranslationAliases[id] = a;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException) { }
    }

    string? TranslationId() => (TranslationBox.SelectedItem as TranslationInfo)?.Id;

    void ShowRenameTranslation(TranslationInfo t)
    {
        var original = _allOriginal.FirstOrDefault(x => x.Id == t.Id) ?? t;
        _settings.TranslationAliases.TryGetValue(t.Id, out var alias);
        var name = new TextBox { Text = alias?.Name ?? "", PlaceholderText = original.Name, MaxLength = 80 };
        var shortName = new TextBox { Text = alias?.Short ?? "", PlaceholderText = original.ShortName, MaxLength = 12, Width = 160, HorizontalAlignment = HorizontalAlignment.Left };
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
            if (n.Length == 0 && s.Length == 0) { _settings.TranslationAliases.Remove(t.Id); _aliasRemoved.Add(t.Id); }
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
