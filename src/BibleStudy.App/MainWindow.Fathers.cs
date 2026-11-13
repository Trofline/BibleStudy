using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using BibleStudy.Core;

namespace BibleStudy.App;

/// <summary>
/// The church fathers on the verse clicked: what Augustine, Chrysostom, Jerome and others wrote on it, the oldest first. The collection
/// (public domain, about 180 MB) is loaded once on request; the texts are in English, as the source has them.
/// </summary>
public partial class MainWindow
{
    const int FathersTab = 35;
    ChurchFathers? _fathers;
    bool _fathersOnlyEarly = true;
    bool _fathersLoading;
    int _fathersGeneration;
    ProgressBar? _fathersProgress;   // the bar and line shown now: the download reports into them, also after the tab was built anew
    TextBlock? _fathersInfo;
    (long Done, long? Total) _fathersLast;

    ChurchFathers Fathers => _fathers ??= new ChurchFathers(_http, Path.Combine(_root, "cache"));

    void ShowFathersProgress((long Done, long? Total) p)
    {
        _fathersLast = p;
        if (_fathersProgress is { } bar && p.Total is > 0) { bar.IsIndeterminate = false; bar.Value = 100.0 * p.Done / p.Total.Value; }
        if (_fathersInfo is { } info) info.Text = L("Lade … {0} MB", p.Done / 1_000_000);
    }

    async Task ShowFathersAsync()
    {
        var generation = ++_fathersGeneration;
        FathersPanel.Children.Clear();
        FathersPanel.Children.Add(new TextBlock { Text = L("KIRCHENVÄTER"), Classes = { "eyebrow" } });
        if (!Fathers.IsLoaded)
        {
            FathersPanel.Children.Add(new TextBlock { Text = L("Was die Kirchenväter und spätere Ausleger zu einem Vers geschrieben haben: Augustinus, Johannes Chrysostomus, Hieronymus, Origenes und viele mehr, Vers für Vers geordnet."), TextWrapping = TextWrapping.Wrap });
            FathersPanel.Children.Add(new TextBlock { Text = L("Die Sammlung „Historical Christian Faith“ ist gemeinfrei und auf Englisch. Sie wird einmal geladen (etwa 180 MB) und liegt danach auf deinem Rechner, auch offline."), Classes = { "caption" }, TextWrapping = TextWrapping.Wrap });
            var progress = new ProgressBar { Minimum = 0, Maximum = 100, IsVisible = _fathersLoading, IsIndeterminate = true };
            var info = new TextBlock { Classes = { "caption" }, TextWrapping = TextWrapping.Wrap };
            _fathersProgress = progress; _fathersInfo = info;
            if (_fathersLoading) ShowFathersProgress(_fathersLast);
            var load = new Button { Content = L("Sammlung laden (etwa 180 MB)"), Classes = { "primary" }, HorizontalAlignment = HorizontalAlignment.Left, IsEnabled = !_fathersLoading };
            load.Click += async (_, _) =>
            {
                if (_fathersLoading) return;
                _fathersLoading = true; load.IsEnabled = false; progress.IsVisible = true; _fathersLast = (0, null);
                try
                {
                    await Fathers.LoadAsync(new Progress<(long Done, long? Total)>(ShowFathersProgress));
                    _fathersLoading = false;
                    await ShowFathersAsync();
                }
                catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or TaskCanceledException or UnauthorizedAccessException)
                {
                    _fathersLoading = false;
                    await ShowFathersAsync();   // the button again, in the panel shown now
                    if (_fathersInfo is { } i) i.Text = L("Laden nicht möglich: {0}", ex.Message);
                }
            };
            FathersPanel.Children.Add(load);
            FathersPanel.Children.Add(progress);
            FathersPanel.Children.Add(info);
            return;
        }

        if (_curBook.Length == 0) { FathersPanel.Children.Add(new TextBlock { Text = L("Öffne eine Bibelstelle und klicke einen Vers an."), Classes = { "caption" } }); return; }
        var verse = _selected ?? 1;
        FathersPanel.Children.Add(new TextBlock { Text = Label(new(_curBook, _curChapter, verse)), FontSize = 18, FontWeight = FontWeight.SemiBold });
        var early = new CheckBox { Content = L("Nur Kirchenväter (bis zum Jahr 750)"), IsChecked = _fathersOnlyEarly };
        early.IsCheckedChanged += (_, _) => { _fathersOnlyEarly = early.IsChecked == true; _ = ShowFathersAsync(); };
        FathersPanel.Children.Add(early);

        List<FatherQuote> quotes;
        try
        {
            var (book, chapter, onlyEarly) = (_curBook, _curChapter, _fathersOnlyEarly);
            quotes = await Task.Run(() => Fathers.For(book, chapter, verse, onlyEarly ? 750 : 9999));
        }
        catch (Exception ex) when (ex is Microsoft.Data.Sqlite.SqliteException or IOException)
        {
            FathersPanel.Children.Add(new TextBlock { Text = L("Die Sammlung lässt sich nicht lesen: {0}", ex.Message), Classes = { "caption" }, TextWrapping = TextWrapping.Wrap });
            return;
        }
        if (generation != _fathersGeneration) return;
        if (quotes.Count == 0)
            FathersPanel.Children.Add(new TextBlock { Text = _fathersOnlyEarly ? L("Zu diesem Vers ist von den Kirchenvätern nichts gesammelt. Ohne den Haken oben kommen spätere Ausleger dazu.") : L("Zu diesem Vers ist nichts gesammelt."), Classes = { "caption" }, TextWrapping = TextWrapping.Wrap });
        else
            FathersPanel.Children.Add(new TextBlock { Text = L("{0} Auszüge von {1} Autoren, die ältesten zuerst", quotes.Count, quotes.Select(q => q.Father).Distinct().Count()), Classes = { "caption" } });

        foreach (var q in quotes.Take(150))
        {
            var meta = (q.Year is > 0 and < 3000 ? L("um {0}", q.Year) : "") + (q.Category.Length > 0 ? "  ·  " + q.Category : "")
                + (q.StartChapter != _curChapter || q.StartVerse != verse || q.EndVerse != verse ? "  ·  " + L("zu {0}", q.Range) : "");
            var full = q.Text;
            var shortText = full.Length > 700 ? full[..700].TrimEnd() + " …" : full;
            var text = new SelectableTextBlock { Text = shortText, TextWrapping = TextWrapping.Wrap };
            var panel = new StackPanel
            {
                Spacing = 4,
                Children =
                {
                    new TextBlock { Text = q.Father, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap },
                    new TextBlock { Text = meta.Trim(' ', '·'), Classes = { "caption" }, TextWrapping = TextWrapping.Wrap },
                    text,
                },
            };
            var actions = new WrapPanel { ItemSpacing = 6 };
            if (full.Length > shortText.Length)
            {
                var more = new Button { Content = L("Ganzer Text"), Classes = { "tool" }, Width = double.NaN, Padding = new(8, 0) };
                more.Click += (_, _) => { text.Text = full; more.IsVisible = false; };
                actions.Children.Add(more);
            }
            if (q.SourceUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                var source = new Button { Content = q.SourceTitle.Length > 0 ? L("Quelle: {0}", q.SourceTitle) : L("Quelle"), Classes = { "tool" }, Width = double.NaN, Padding = new(8, 0) };
                ToolTip.SetTip(source, q.SourceUrl);
                source.Click += (_, _) => OpenUrl(q.SourceUrl);
                actions.Children.Add(source);
            }
            if (actions.Children.Count > 0) panel.Children.Add(actions);
            FathersPanel.Children.Add(new Border { Classes = { "card" }, Child = panel });
        }
        var remove = new Button { Content = L("Sammlung vom Rechner löschen"), Classes = { "tool" }, Width = double.NaN, Padding = new(8, 0), HorizontalAlignment = HorizontalAlignment.Left, Margin = new(0, 10, 0, 0) };
        remove.Click += (_, _) =>
        {
            var yes = new Button { Content = L("Löschen"), Classes = { "primary" } };
            var fly = new Flyout { Content = new StackPanel { Spacing = 6, Children = { new TextBlock { Text = L("Die Sammlung (etwa 180 MB) löschen? Sie lässt sich jederzeit neu laden.") }, yes } } };
            yes.Click += (_, _) => { fly.Hide(); try { Fathers.Delete(); } catch (IOException) { } _ = ShowFathersAsync(); };
            fly.ShowAt(remove);
        };
        FathersPanel.Children.Add(remove);
        FathersPanel.Children.Add(new TextBlock { Text = ChurchFathers.Attribution + " · " + ChurchFathers.Website, Classes = { "caption" }, TextWrapping = TextWrapping.Wrap, Opacity = 0.75 });
    }
}
