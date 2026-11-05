using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using BibleStudy.Core;
using BibleStudy.Core.Models;
using BibleStudy.Core.Providers;
using BibleStudy.Core.Storage;

namespace BibleStudy.Android;

/// <summary>
/// The whole app in one view: read a translation, tap verses to mark them in a color, mark chapters as read.
/// The data is the same vault file as in the desktop program (vaults/Standard/vault.db), so a sync program such as Syncthing keeps both in step.
/// </summary>
public sealed class ReaderView : UserControl
{
    static readonly string[] Colors = ["#FFD54F", "#FFA95C", "#F26D6D", "#E983B8", "#B28DF0", "#6FA8F2", "#55C2C2", "#7ACB7A"];

    readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(30) };
    HelloAoProvider _provider = null!;
    VaultStore _vault = null!;
    List<TranslationInfo> _translations = [];
    List<BookInfo> _books = [];
    string _tr = "", _book = "JHN";
    int _chapter = 1;
    Chapter? _loaded;
    List<Mark> _marks = [];
    readonly HashSet<int> _sel = [];

    readonly Button _trBtn = Btn(""), _placeBtn = Btn(""), _readBtn = Btn("");
    readonly StackPanel _verses = new() { Margin = new(14, 8, 14, 24), Spacing = 2 };
    readonly ScrollViewer _scroll = new();
    readonly WrapPanel _colorBar = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new(0, 6), IsVisible = false };
    readonly TextBlock _status = new() { Margin = new(14, 4), TextWrapping = TextWrapping.Wrap, IsVisible = false };
    readonly Grid _overlay = new() { IsVisible = false };

    public ReaderView()
    {
        Loc.Use(System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName);
        OpenStores();
        var settings = Btn("⚙"); settings.Click += (_, _) => ShowSettings();
        _trBtn.Click += async (_, _) => await PickTranslationAsync();
        _placeBtn.Click += (_, _) => PickBook();
        _readBtn.Click += (_, _) => ToggleRead();
        var prev = Btn("◀"); prev.Click += async (_, _) => await StepAsync(-1);
        var next = Btn("▶"); next.Click += async (_, _) => await StepAsync(1);

        var top = new Grid { ColumnDefinitions = new("Auto,*,Auto"), Margin = new(8, 8, 8, 0) };
        top.Children.Add(_trBtn);
        Grid.SetColumn(_placeBtn, 1); _placeBtn.HorizontalAlignment = HorizontalAlignment.Stretch; _placeBtn.Margin = new(6, 0); top.Children.Add(_placeBtn);
        Grid.SetColumn(settings, 2); top.Children.Add(settings);

        _scroll.Content = _verses;
        var bottom = new Grid { ColumnDefinitions = new("Auto,*,Auto"), Margin = new(8, 0, 8, 8) };
        bottom.Children.Add(prev);
        Grid.SetColumn(_readBtn, 1); _readBtn.HorizontalAlignment = HorizontalAlignment.Stretch; _readBtn.Margin = new(6, 0); bottom.Children.Add(_readBtn);
        Grid.SetColumn(next, 2); bottom.Children.Add(next);

        var main = new Grid { RowDefinitions = new("Auto,Auto,*,Auto,Auto") };
        main.Children.Add(top);
        Grid.SetRow(_status, 1); main.Children.Add(_status);
        Grid.SetRow(_scroll, 2); main.Children.Add(_scroll);
        Grid.SetRow(_colorBar, 3); main.Children.Add(_colorBar);
        Grid.SetRow(bottom, 4); main.Children.Add(bottom);
        BuildColorBar();

        var root = new Grid();
        root.Children.Add(main);
        root.Children.Add(_overlay);
        Content = root;
        Loaded += async (_, _) => await StartAsync();
    }

    static Button Btn(string text) => new() { Content = text, Padding = new(14, 10), MinHeight = 44, VerticalContentAlignment = VerticalAlignment.Center };

    // ---- data -----------------------------------------------------------------------------------------------------

    void OpenStores()
    {
        var root = Store.Root;
        Directory.CreateDirectory(Path.Combine(root, "vaults", "Standard"));
        _provider = new HelloAoProvider(_http, Path.Combine(root, "cache"));
        _vault = new VaultStore(Path.Combine(root, "vaults", "Standard", "vault.db"), "Standard");
        var pos = _vault.GetLastPosition();
        if (pos is { } p) { _book = p.Item1; _chapter = p.Item2; }
        _tr = Store.Translation;
    }

    async Task StartAsync()
    {
        try
        {
            _translations = (await _provider.GetTranslationsAsync()).ToList();
            if (_tr == "" || _translations.All(t => t.Id != _tr))
            {
                var lang = Loc.Code;
                _tr = (_translations.FirstOrDefault(t => t.Language == lang) ?? _translations.FirstOrDefault())?.Id ?? "";
            }
            await LoadBooksAsync();
            await ShowChapterAsync();
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or IOException)
        {
            Say(L("Keine Verbindung. Schon gelesene Kapitel sind auch offline da."));
        }
    }

    async Task LoadBooksAsync()
    {
        if (_tr == "") return;
        _books = (await _provider.GetBooksAsync(_tr)).OrderBy(b => b.Order).ToList();
        if (_books.All(b => b.Id != _book)) { _book = _books.FirstOrDefault()?.Id ?? "GEN"; _chapter = 1; }
    }

    async Task ShowChapterAsync()
    {
        if (_tr == "") return;
        _sel.Clear(); _colorBar.IsVisible = false;
        _status.IsVisible = false;
        try { _loaded = await _provider.GetChapterAsync(_tr, _book, _chapter); }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or IOException)
        {
            _loaded = null; _verses.Children.Clear();
            Say(L("Keine Verbindung. Schon gelesene Kapitel sind auch offline da."));
            return;
        }
        _vault.SetLastPosition(_book, _chapter);
        Store.Translation = _tr;
        _marks = _vault.GetMarks(_book, _chapter);
        Render();
        _scroll.Offset = default;
    }

    void Say(string text) { _status.Text = text; _status.IsVisible = true; }

    // ---- reading view ---------------------------------------------------------------------------------------------

    void Render()
    {
        var info = _translations.FirstOrDefault(t => t.Id == _tr);
        _trBtn.Content = info?.ShortName is { Length: > 0 } s ? s : _tr;
        var book = _books.FirstOrDefault(b => b.Id == _book);
        _placeBtn.Content = (book?.CommonName ?? _book) + " " + _chapter;
        var read = _vault.GetReadChapters().Contains((_book, _chapter));
        _readBtn.Content = read ? "✓ " + L("Gelesen") : L("Als gelesen markieren");
        if (read) { _readBtn.Background = new SolidColorBrush(Color.Parse("#2E7D32")); _readBtn.Foreground = Brushes.White; }
        else { _readBtn.ClearValue(Avalonia.Controls.Primitives.TemplatedControl.BackgroundProperty); _readBtn.ClearValue(Avalonia.Controls.Primitives.TemplatedControl.ForegroundProperty); }

        _verses.Children.Clear();
        if (_loaded is null) return;
        var accent = new SolidColorBrush(Color.Parse("#6FA8F2")) { Opacity = 0.28 };
        foreach (var v in _loaded.Verses)
        {
            var n = v.Number;
            var text = v.Text;
            var block = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 18, LineHeight = 28 };
            block.Inlines!.Add(new Run(n + "  ") { FontWeight = FontWeight.Bold, FontSize = 13, Foreground = new SolidColorBrush(Color.Parse("#6FA8F2")) });
            foreach (var (part, color) in Paint(n, text)) block.Inlines.Add(color is null ? new Run(part) : new Run(part) { Background = new SolidColorBrush(Color.Parse(color)), Foreground = Brushes.Black });
            var box = new Border { Child = block, Padding = new(8, 6), CornerRadius = new(8), Background = _sel.Contains(n) ? accent : Brushes.Transparent };
            box.Tapped += (_, _) => { if (!_sel.Remove(n)) _sel.Add(n); Render2(); };
            _verses.Children.Add(box);
        }
    }

    void Render2()
    {
        var off = _scroll.Offset;
        Render();
        _colorBar.IsVisible = _sel.Count > 0;
        _scroll.Offset = off;
    }

    /// <summary>The text of a verse cut into pieces, each with the color of the mark on it (or none).</summary>
    IEnumerable<(string Text, string? Color)> Paint(int verse, string text)
    {
        var colors = new string?[text.Length];
        foreach (var m in _marks)
        {
            if (verse < m.Ref.Verse || verse > m.LastVerse) continue;
            int from, to;
            if (m.WholeVerse) { from = 0; to = text.Length; }
            else if (m.TranslationId == _tr || m.TranslationId == Mark.AnyTranslation)
            {
                from = verse == m.Ref.Verse ? m.Start : 0;
                to = verse == m.LastVerse ? m.End : text.Length;
            }
            else continue;
            from = Math.Clamp(from, 0, text.Length); to = Math.Clamp(to, from, text.Length);
            for (var i = from; i < to; i++) colors[i] = m.Color;
        }
        var start = 0;
        for (var i = 1; i <= text.Length; i++)
            if (i == text.Length || colors[i] != colors[start]) { yield return (text[start..i], colors[start]); start = i; }
    }

    void BuildColorBar()
    {
        foreach (var c in Colors)
        {
            var b = new Button { Width = 40, Height = 40, Margin = new(4), CornerRadius = new(20), Background = new SolidColorBrush(Color.Parse(c)), Padding = default };
            b.Click += (_, _) => ApplyColor(c);
            _colorBar.Children.Add(b);
        }
        var clear = new Button { Content = "✕", Width = 40, Height = 40, Margin = new(4), CornerRadius = new(20), Padding = default, HorizontalContentAlignment = HorizontalAlignment.Center };
        ToolTip.SetTip(clear, L("Markierung entfernen"));
        clear.Click += (_, _) => ApplyColor(null);
        _colorBar.Children.Add(clear);
    }

    /// <summary>Colors the chosen verses as whole verses; null takes every mark off them.</summary>
    void ApplyColor(string? color)
    {
        if (_loaded is null) return;
        foreach (var n in _sel.OrderBy(x => x))
        {
            var text = _loaded.Verses.FirstOrDefault(v => v.Number == n)?.Text ?? "";
            var touching = _marks.Where(m => n >= m.Ref.Verse && n <= m.LastVerse && (m.WholeVerse || m.TranslationId == _tr)).ToList();
            if (color is null) { foreach (var m in touching) _vault.DeleteMark(m.Id); continue; }
            var whole = touching.FirstOrDefault(m => m.WholeVerse);
            if (whole is not null) _vault.UpdateMark(whole.Id, color, whole.Note, whole.FolderId, whole.Tags);
            else _vault.AddMark(_tr, new VerseRef(_book, _chapter, n), -1, -1, text, color, "", null, []);
        }
        _sel.Clear(); _colorBar.IsVisible = false;
        _marks = _vault.GetMarks(_book, _chapter);
        Render();
    }

    void ToggleRead()
    {
        var read = _vault.GetReadChapters().Contains((_book, _chapter));
        _vault.SetRead(_book, [_chapter], !read);
        Render2();
    }

    async Task StepAsync(int dir)
    {
        var i = _books.FindIndex(b => b.Id == _book);
        if (i < 0) return;
        var c = _chapter + dir;
        if (c < 1) { if (i == 0) return; i--; c = _books[i].NumberOfChapters; }
        else if (c > _books[i].NumberOfChapters) { if (i == _books.Count - 1) return; i++; c = 1; }
        _book = _books[i].Id; _chapter = c;
        await ShowChapterAsync();
    }

    // ---- pickers --------------------------------------------------------------------------------------------------

    /// <summary>A list with a filter field over the whole screen.</summary>
    void Pick<T>(string title, IEnumerable<T> items, Func<T, string> label, Func<T, Task> chosen)
    {
        var all = items.ToList();
        var filter = new TextBox { PlaceholderText = L("Suchen …"), Margin = new(0, 6) };
        var list = new ListBox { MinHeight = 200 };
        void Fill() => list.ItemsSource = all.Where(x => label(x).Contains(filter.Text ?? "", StringComparison.CurrentCultureIgnoreCase)).Select(x => new ListBoxItem { Content = label(x), Tag = x, Padding = new(12, 12) }).ToList();
        filter.TextChanged += (_, _) => Fill();
        list.SelectionChanged += async (_, _) =>
        {
            if (list.SelectedItem is not ListBoxItem { Tag: T pick }) return;
            CloseOverlay();
            await chosen(pick);
        };
        Fill();
        var panel = new DockPanel { Margin = new(12) };
        var close = Btn("✕"); close.Click += (_, _) => CloseOverlay();
        var head = new DockPanel();
        DockPanel.SetDock(close, Dock.Right); head.Children.Add(close);
        head.Children.Add(new TextBlock { Text = title, FontWeight = FontWeight.SemiBold, FontSize = 18, VerticalAlignment = VerticalAlignment.Center });
        DockPanel.SetDock(head, Dock.Top); panel.Children.Add(head);
        DockPanel.SetDock(filter, Dock.Top); panel.Children.Add(filter);
        panel.Children.Add(list);
        ShowOverlay(panel);
    }

    async Task PickTranslationAsync()
    {
        if (_translations.Count == 0) { await StartAsync(); if (_translations.Count == 0) return; }
        Pick(L("Übersetzung wählen"), _translations.OrderBy(t => t.Language == Loc.Code ? 0 : 1).ThenBy(t => t.LanguageName).ThenBy(t => t.ShortName),
            t => $"{t.ShortName} – {t.Name} ({t.LanguageName})", async t =>
            {
                _tr = t.Id;
                try { await LoadBooksAsync(); await ShowChapterAsync(); }
                catch (Exception e) when (e is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or IOException) { Say(L("Keine Verbindung. Schon gelesene Kapitel sind auch offline da.")); }
            });
    }

    void PickBook() =>
        Pick(L("Buch wählen"), _books, b => b.CommonName, b =>
        {
            Pick(b.CommonName, Enumerable.Range(1, b.NumberOfChapters), c => L("Kapitel {0}", c), async c => { _book = b.Id; _chapter = c; await ShowChapterAsync(); });
            return Task.CompletedTask;
        });

    void ShowOverlay(Control content)
    {
        _overlay.Children.Clear();
        _overlay.Children.Add(new Border { Background = (IBrush?)(this.FindResource("SystemRegionBrush") ?? Brushes.Black), Child = content });
        _overlay.IsVisible = true;
    }

    void CloseOverlay() { _overlay.IsVisible = false; _overlay.Children.Clear(); }

    // ---- settings -------------------------------------------------------------------------------------------------

    void ShowSettings()
    {
        var path = new TextBox { Text = Store.Root, TextWrapping = TextWrapping.Wrap };
        var info = new TextBlock { TextWrapping = TextWrapping.Wrap, Opacity = 0.75 };
        var apply = Btn(L("Übernehmen"));
        apply.Click += (_, _) =>
        {
            try { Store.Root = (path.Text ?? "").Trim(); OpenStores(); info.Text = L("Fertig."); _ = RestartViewAsync(); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { info.Text = e.Message; }
        };
        var files = Btn(L("Zugriff auf alle Dateien erlauben"));
        files.Click += (_, _) => Store.AskForAllFilesAccess();
        var offline = Btn(L("Ganze Übersetzung offline laden"));
        offline.Click += async (_, _) =>
        {
            offline.IsEnabled = false;
            var progress = new Progress<(int Done, int Total)>(p => info.Text = $"{p.Done} / {p.Total}");
            try { await OfflineDownloader.DownloadAsync(_provider, _tr, progress); info.Text = L("Fertig."); }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException) { info.Text = e.Message; }
            offline.IsEnabled = true;
        };
        var close = Btn("✕"); close.Click += (_, _) => CloseOverlay();
        var panel = new StackPanel { Margin = new(14), Spacing = 10 };
        panel.Children.Add(new DockPanel { Children = { new TextBlock { Text = L("Einstellungen"), FontSize = 18, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center }, close } });
        DockPanel.SetDock(close, Dock.Right);
        panel.Children.Add(new TextBlock { Text = L("Datenordner"), FontWeight = FontWeight.SemiBold });
        panel.Children.Add(new TextBlock { Text = L("Hier liegen deine Markierungen und gelesenen Kapitel (Unterordner vaults). Zeige hier den Ordner, den Syncthing mit dem Computer abgleicht; im Programm auf dem Computer ist es der Ordner BibleStudy in LocalAppData."), TextWrapping = TextWrapping.Wrap, Opacity = 0.75 });
        panel.Children.Add(path); panel.Children.Add(apply); panel.Children.Add(files); panel.Children.Add(offline); panel.Children.Add(info);
        ShowOverlay(new ScrollViewer { Content = panel });
    }

    async Task RestartViewAsync()
    {
        _translations = []; _books = [];
        await StartAsync();
    }
}
