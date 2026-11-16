using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using BibleStudy.Core;
using BibleStudy.Core.Models;
using BibleStudy.Core.Providers;

namespace BibleStudy.App;

/// <summary>
/// The download manager (in the settings): everything that is otherwise loaded the first time it is needed – texts, dictionaries, grammar data,
/// cross references, voices. A tick means "is on this computer": ticking loads it, unticking removes it again. Whoever does it once at the start
/// does not wait while working later.
/// </summary>
public partial class MainWindow
{
    /// <summary>One part of the manager. <paramref name="Installed"/> says whether it is on the computer, <paramref name="Remove"/> takes it away again (null: not possible from here).</summary>
    /// <paramref name="SizeMb"/> is about what it takes on the device (measured); <paramref name="OnlyOnRequest"/>: "Select all" leaves it out on a phone (big, for few).
    sealed record DownloadItem(string Label, string Note, Func<IProgress<string>, CancellationToken, Task> Run, string Group, Func<bool> Installed, Action? Remove, int SizeMb = 0, bool OnlyOnRequest = false);

    /// <summary>"12 MB" or "1.3 GB".</summary>
    static string MbText(double mb) => SizeText((long)(mb * 1048576));

    /// <summary>The free space where the program keeps its data, in MB; null when the system does not say.</summary>
    double? FreeMb()
    {
        try { return new DriveInfo(Path.GetPathRoot(Path.GetFullPath(_root))!).AvailableFreeSpace / 1048576.0; }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException) { return null; }
    }

    string CacheDir(params string[] parts) => Path.Combine([_root, "cache", .. parts]);

    static bool HasFiles(string dir) => Directory.Exists(dir) && Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Any();

    static int CountFiles(string dir, Func<string, bool> match) => Directory.Exists(dir) ? Directory.EnumerateFiles(dir).Count(f => match(Path.GetFileName(f))) : 0;

    static void DeleteDir(string dir) { try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }

    static void DeleteFiles(string dir, Func<string, bool> match)
    {
        try { if (Directory.Exists(dir)) foreach (var f in Directory.EnumerateFiles(dir).Where(f => match(Path.GetFileName(f)))) File.Delete(f); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    const string OfflineMarker = ".offline-complete";

    List<DownloadItem> DownloadItems()
    {
        var items = new List<DownloadItem>();
        var group = L("Texte");

        // a translation counts as loaded when all its chapters were fetched (a marker file says so) or nearly all of them are in the cache
        bool Loaded(TranslationInfo t) => File.Exists(Path.Combine(CacheDir("api", t.Id), OfflineMarker)) || CachedChapters(t.Id) >= 1100;
        DownloadItem Translation(TranslationInfo t, string label, string note) => new(label, note, (p, ct) => Chapters(t, p, ct), group, () => Loaded(t), () => DeleteDir(CacheDir("api", t.Id)), 12);

        if (TranslationBox.SelectedItem is TranslationInfo cur && !PluginOwns(cur))
            items.Add(Translation(cur, L("Gewählte Übersetzung: {0}", cur.Name), L("Alle Kapitel, danach lesbar ohne Netz.")));
        var wanted = LanguageNames.GetValueOrDefault(Loc.Code, "English");
        foreach (var t in _all.Where(x => x.LanguageName == wanted && x.Id != (TranslationBox.SelectedItem as TranslationInfo)?.Id && !PluginOwns(x)))
            items.Add(Translation(t, t.Name, L("Dauert länger und braucht mehr Platz.")));
        var xrefDir = CacheDir("api", "d", "open-cross-ref");
        items.Add(new(L("Querverweise"), L("Für Graphen und die Vers-Übersicht."), async (p, ct) =>
        {
            p.Report(L("wird geladen …"));
            await BuildGraphAsync();
        }, group, CrossRefDataCached, () => DeleteDir(xrefDir), 27));

        group = L("Wörterbücher und Grundtext");
        var strongs = CacheDir("strongs"); var step = CacheDir("step"); var bdb = CacheDir("bdb"); var lsj = CacheDir("lsj"); var tagnt = CacheDir("tagnt"); var macula = CacheDir("macula");
        items.Add(new(L("Strong-Wörterbuch"), "", async (p, ct) => { await _strongs.LookupAsync("G1", ct); await _strongs.LookupAsync("H1", ct); }, group, () => HasFiles(strongs), () => DeleteDir(strongs), 3));
        items.Add(new(L("Kurzlexikon (STEPBible)"), "", async (p, ct) => { await _step.LookupAsync("G1", ct); await _step.LookupAsync("H1", ct); }, group, () => HasFiles(step), () => DeleteDir(step), 8));
        items.Add(new(L("BDB-Lexikon (Hebräisch)"), "", async (p, ct) => { _bdb ??= new BdbLexicon(_http, CacheDir()); await _bdb.GetAsync("H1", ct); }, group, () => HasFiles(bdb), () => DeleteDir(bdb), 5));
        items.Add(new(L("LSJ-Lexikon (Griechisch)"), "", async (p, ct) => { _lsj ??= new GreekLsj(_http, CacheDir()); await _lsj.GetAsync("G1", ct); }, group, () => HasFiles(lsj), () => DeleteDir(lsj), 24));
        items.Add(new(L("Textkritik (Neues Testament)"), "", async (p, ct) =>
        {
            _variants ??= new TextVariants(_http, CacheDir());
            await _variants.GetAsync("MAT", 1, 1, ct); await _variants.GetAsync("ACT", 1, 1, ct);
        }, group, () => File.Exists(Path.Combine(tagnt, "mat-jhn.txt")) && File.Exists(Path.Combine(tagnt, "act-rev.txt")), () => DeleteDir(tagnt), 29));
        items.Add(new(L("Syntax und Morphologie (Griechisch)"), L("Für Syntaxbaum, Satzdiagramm, Grammatiksuche."), async (p, ct) =>
        {
            await _syntax.LoadGreekWordsAsync(new Progress<(int Done, int Total)>(x => p.Report($"{x.Done}/{x.Total}")), ct);
        }, group, () => CountFiles(macula, n => n.EndsWith(".xml") && !n.Contains("lowfat")) >= 27, () => DeleteFiles(macula, n => n.EndsWith(".xml") && !n.Contains("lowfat")), 98));
        items.Add(new(L("Syntax und Morphologie (Hebräisch)"), _phone ? L("Für Syntaxbaum, Satzdiagramm, Grammatiksuche im Alten Testament. Sehr groß: nur laden, wenn du es brauchst.") : L("Für Syntaxbaum, Satzdiagramm, Grammatiksuche."), async (p, ct) =>
        {
            await _syntax.LoadHebrewWordsAsync(new Progress<(int Done, int Total)>(x => p.Report($"{x.Done}/{x.Total}")), ct);
        }, group, () => CountFiles(macula, n => n.Contains("lowfat")) >= 900, () => DeleteFiles(macula, n => n.Contains("lowfat")), 406, OnlyOnRequest: true));

        group = L("Vorlesen");
        if (NaturalVoice.IsAvailable)
            foreach (var v in NaturalVoice.Voices)
            {
                var voice = v;
                items.Add(new($"{LanguageNames.GetValueOrDefault(voice.Language, voice.Language)}: {voice.Name}", voice.License,
                    (p, ct) => NaturalVoice.EnsureAsync(_http, _root, voice, p, ct), group, () => NaturalVoice.IsReady(_root, voice), () => NaturalVoice.Remove(_root, voice), voice.SizeMb));
            }

#if !MOBILE   // Ollama runs on a computer only
        group = L("Übersetzen ohne Internet");
        foreach (var (model, note) in new[] { (OllamaClient.SmallModel, L("Schnell und klein, läuft auch auf schwächeren Rechnern, übersetzt einfacher.")), (OllamaClient.LargeModel, L("Beste Übersetzung, braucht rund 10 GB freien Arbeits- oder Grafikspeicher.")) })
        {
            var name = model;
            items.Add(new(L("Lokales KI-Modell „{0}“ (Ollama muss installiert sein)", name), note + " " + L("Entfernen geht nur in Ollama selbst."), async (p, ct) =>
            {
                _ollama ??= new OllamaClient(_http);
                try { await _ollama.PullAsync(name, p, ct); }
                catch (HttpRequestException) { throw new InvalidOperationException(L("Ollama konnte nicht gestartet werden. Ist es von ollama.com installiert?")); }
            }, group, () => false, null, name == OllamaClient.SmallModel ? 2000 : 8100));
        }
#endif
        return items;
    }

    int CachedChapters(string id)
    {
        var dir = CacheDir("api", id);
        return Directory.Exists(dir) ? Directory.EnumerateFiles(dir, "*.json", SearchOption.AllDirectories).Count(f => !f.EndsWith(".words.json") && Path.GetFileName(f) != "books.json") : 0;
    }

    /// <summary>Translations that are not downloaded: from a plugin or imported by the user (they are on this computer anyway and are not the manager's to remove).</summary>
    static bool PluginOwns(TranslationInfo t) => BibleStudy.Core.Plugins.PluginBibleProvider.Owns(t.Id) || BibleStudy.Core.Import.LocalBibleStore.Owns(t.Id);

    async Task Chapters(TranslationInfo t, IProgress<string> p, CancellationToken ct)
    {
        await OfflineDownloader.DownloadAsync(_provider, t.Id, new Progress<(int Done, int Total)>(x => p.Report(L("{0}/{1} Kapitel", x.Done, x.Total))), ct);
        try { Directory.CreateDirectory(CacheDir("api", t.Id)); File.WriteAllText(Path.Combine(CacheDir("api", t.Id), OfflineMarker), ""); } catch (IOException) { }
    }

    void ShowDownloadAll()
    {
        var items = DownloadItems();
        var state = items.Select(i => i.Installed()).ToArray();   // what is on the computer (updated after each round)
        var boxes = new List<(DownloadItem Item, CheckBox Box, TextBlock Status)>();
        var list = new StackPanel { Spacing = 4 };
        string? lastGroup = null;
        string Note(int i)
        {
            var size = items[i].SizeMb > 0 ? L("ca. {0}", MbText(items[i].SizeMb)) : "";
            var text = state[i] ? L("Geladen ✓") : items[i].Note;
            return size.Length == 0 ? text : text.Length == 0 ? size : text + " · " + size;
        }
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            if (item.Group != lastGroup) { list.Children.Add(new TextBlock { Text = item.Group.ToUpperInvariant(), Classes = { "eyebrow" }, Margin = new(0, 12, 0, 2) }); lastGroup = item.Group; }
            var box = new CheckBox { Content = item.Label, IsChecked = state[i] };
            var status = new TextBlock { Text = Note(i), Classes = { "caption" }, TextWrapping = TextWrapping.Wrap, Margin = new(28, 0, 0, 4) };
            list.Children.Add(box); list.Children.Add(status);
            boxes.Add((item, box, status));
        }

        var info = new TextBlock { Text = L("Ein Haken heißt: ist auf diesem Computer. Setze einen Haken und klicke auf „Anwenden“, um etwas zu laden; nimm ihn weg, um es wieder zu entfernen. Alles wird nacheinander erledigt; du kannst weiterarbeiten. Nichts davon ist im Programm eingebaut, es kommt von den Quellen unter „Quellen und Lizenzen“."), Classes = { "caption" }, TextWrapping = TextWrapping.Wrap };
        var all = new Button { Content = L("Alles auswählen"), Margin = new(0, 0, 6, 0) };
        var none = new Button { Content = L("Nichts auswählen"), Margin = new(0, 0, 6, 0) };
        var go = new Button { Content = L("Anwenden"), Classes = { "primary" } };
        var cancel = new Button { Content = L("Abbrechen"), IsEnabled = false, Margin = new(0, 0, 6, 0) };
        var summary = new TextBlock { Classes = { "caption" }, TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 0) };
        // what the ticks add up to and what is free: before loading everything the phone shows whether it fits
        var sizes = new TextBlock { Classes = { "caption" }, TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 0) };
        void ShowSizes()
        {
            double ticked = 0, toLoad = 0;
            for (var i = 0; i < boxes.Count; i++)
                if (boxes[i].Box.IsChecked == true) { ticked += items[i].SizeMb; if (!state[i]) toLoad += items[i].SizeMb; }
            var text = ticked > 0 ? L("Angehakt zusammen: ca. {0}", MbText(ticked)) : L("Nichts angehakt");
            if (toLoad > 0) text += " · " + L("davon noch zu laden: {0}", MbText(toLoad));
            if (FreeMb() is { } free)
            {
                text += " · " + L("frei: {0}", MbText(free));
                if (toLoad > free - 200) text += " · " + L("Zu wenig Platz!");
            }
            sizes.Text = text;
        }
        foreach (var b in boxes) b.Box.IsCheckedChanged += (_, _) => ShowSizes();
        ShowSizes();
        // on a phone "all" leaves out what is very big and needed by few (the Hebrew syntax): it can still be ticked by hand
        all.Click += (_, _) => { foreach (var b in boxes) if (!(_phone && b.Item.OnlyOnRequest)) b.Box.IsChecked = true; };
        none.Click += (_, _) => { foreach (var b in boxes) b.Box.IsChecked = false; };

        // a narrow screen (a phone): two rows of two equal buttons in a grid (a wrap panel laid them on each other on the iPhone)
        Control buttons;
        if (_phone)
        {
            var grid = new Grid { Margin = new(0, 6, 0, 0), ColumnDefinitions = new("*,*"), RowDefinitions = new("Auto,Auto"), ColumnSpacing = 8, RowSpacing = 8 };
            var cells = new (Button B, int Row, int Col)[] { (all, 0, 0), (none, 0, 1), (cancel, 1, 0), (go, 1, 1) };
            foreach (var (b, row, col) in cells)
            {
                b.Margin = default;
                b.HorizontalAlignment = HorizontalAlignment.Stretch;
                b.HorizontalContentAlignment = HorizontalAlignment.Center;
                Grid.SetRow(b, row); Grid.SetColumn(b, col);
                grid.Children.Add(b);
            }
            buttons = grid;
        }
        else
        {
            var dock = new DockPanel { Margin = new(0, 6, 0, 0) };
            var right = new StackPanel { Orientation = Orientation.Horizontal, Children = { cancel, go } };
            DockPanel.SetDock(right, Dock.Right);
            dock.Children.Add(right);
            dock.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Children = { all, none } });
            buttons = dock;
        }

        var root = new DockPanel { Margin = new(20) };
        DockPanel.SetDock(info, Dock.Top); info.Margin = new(0, 0, 0, 8);
        // the progress line has a row of its own above the buttons: next to them it ran into them
        var bottom = new StackPanel { Children = { sizes, summary, buttons } };
        DockPanel.SetDock(bottom, Dock.Bottom);
        root.Children.Add(info); root.Children.Add(bottom);
        root.Children.Add(new ScrollViewer { Content = list });

        var win = new Window { Title = L("Download-Manager"), Width = 600, Height = 720, MinWidth = 420, MinHeight = 400, Background = Res("BsSurface"), Icon = Icon, WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = root };
        CancellationTokenSource? cts = null;
        var running = false;
        cancel.Click += (_, _) => cts?.Cancel();
        win.Closing += (_, _) => cts?.Cancel();

        go.Click += async (_, _) =>
        {
            if (running) return;
            var todo = new List<(int Index, bool Load)>();
            for (var i = 0; i < boxes.Count; i++)
            {
                var want = boxes[i].Box.IsChecked == true;
                if (want && !state[i]) todo.Add((i, true));
                else if (!want && state[i] && items[i].Remove is not null) todo.Add((i, false));
                else if (!want && state[i]) boxes[i].Box.IsChecked = true;   // cannot be removed from here: the tick stays
            }
            if (todo.Count == 0) { summary.Text = L("Nichts zu tun."); return; }
            running = true; go.IsEnabled = all.IsEnabled = none.IsEnabled = false; cancel.IsEnabled = true;
            foreach (var b in boxes) b.Box.IsEnabled = false;
            cts = new CancellationTokenSource();
            int loaded = 0, removed = 0, failed = 0;
            foreach (var (i, load) in todo)
            {
                if (cts.IsCancellationRequested) break;
                var (item, box, status) = boxes[i];
                summary.Text = L("{0} von {1} …", loaded + removed + failed + 1, todo.Count);
                try
                {
                    if (load)
                    {
                        status.Text = L("läuft …");
                        await item.Run(new Progress<string>(m => status.Text = m), cts.Token);
                        loaded++;
                    }
                    else { await Task.Run(item.Remove!); removed++; }
                    state[i] = item.Installed();
                    status.Text = Note(i);
                    box.IsChecked = state[i];
                }
                catch (OperationCanceledException) { status.Text = L("Abgebrochen."); box.IsChecked = state[i]; break; }
                catch (Exception ex) { status.Text = L("Fehler: ") + ex.Message; box.IsChecked = state[i]; failed++; }
            }
            summary.Text = (failed == 0 ? "" : L("{0} mit Fehler. ", failed)) + L("{0} geladen, {1} entfernt.", loaded, removed);
            running = false; cancel.IsEnabled = false; go.IsEnabled = all.IsEnabled = none.IsEnabled = true;
            foreach (var b in boxes) b.Box.IsEnabled = true;
            UpdateOfflineInfo();
            ShowSizes();
        };
        win.Show(this);
    }
}
