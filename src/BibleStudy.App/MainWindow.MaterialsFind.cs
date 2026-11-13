using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using BibleStudy.Core.Materials;
using BibleStudy.Core.Models;
using BibleStudy.Core.Storage;

namespace BibleStudy.App;

/// <summary>
/// Finding things in the materials: a search over all files of the folder (beside the tree), Ctrl+F in the open file, the places of a verse
/// for the verse overview, and the place where each file was read last (it opens there again).
/// </summary>
public partial class MainWindow
{
    TextBox? _matSearchBox;
    StackPanel _matResults = null!;
    ScrollViewer _matResultsScroll = null!;
    int _matSearchVersion;
    DispatcherTimer? _matSearchTimer;
    readonly Dictionary<string, (DateTime Stamp, MaterialDoc Doc)> _matTextCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>A place to show when the file is open: from a search hit or the verse overview.</summary>
    (string Path, MaterialHit Hit, string? Word)? _matPending;
    string? _matSpotReady;   // the file whose reading place may be kept (not while it is being loaded)
    DispatcherTimer? _matSpotTimer;

    // ---- the text of all files ------------------------------------------------------------------------------------------

    /// <summary>
    /// The text of every file in the folder that can be shown (pictures aside), read once and kept while the file is unchanged.
    /// <paramref name="progress"/> hears how far it is and can stop it by answering false (a newer search came).
    /// </summary>
    async Task<List<(string Path, MaterialDoc Doc)>?> MaterialTextsAsync(Func<int, int, bool>? progress = null)
    {
        var root = _settings.MaterialsFolder;
        if (root.Length == 0 || !Directory.Exists(root)) return [];
        var sub = _settings.MaterialsSubfolders;
        var files = (await Task.Run(() => MaterialReader.Scan(root, sub))).Where(f => MaterialReader.CanShow(f) && !MaterialReader.Pictures.Contains(Path.GetExtension(f))).ToList();
        var result = new List<(string, MaterialDoc)>();
        for (var i = 0; i < files.Count; i++)
        {
            var f = files[i];
            DateTime stamp;
            try { stamp = File.GetLastWriteTimeUtc(f); } catch { continue; }
            if (!_matTextCache.TryGetValue(f, out var c) || c.Stamp != stamp)
            {
                try { c = (stamp, await Task.Run(() => MaterialReader.Read(f))); }
                catch { c = (stamp, new MaterialDoc(f, [])); }
                _matTextCache[f] = c;
            }
            result.Add((f, c.Doc));
            if (progress is not null && !progress(i + 1, files.Count)) return null;
        }
        return result;
    }

    // ---- search over all files --------------------------------------------------------------------------------------------

    Control BuildMaterialSearch()
    {
        _matSearchBox = new TextBox { PlaceholderText = L("In allen Materialien suchen …"), Classes = { "search" } };
        ToolTip.SetTip(_matSearchBox, L("Sucht in allen Dateien des Ordners (Groß- und Kleinschreibung egal). Esc leert das Feld."));
        _matSearchBox.TextChanged += (_, _) =>
        {
            _matSearchTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
            _matSearchTimer.Stop();
            _matSearchTimer.Tick -= MaterialSearchTick; _matSearchTimer.Tick += MaterialSearchTick;
            _matSearchTimer.Start();
        };
        _matSearchBox.KeyDown += (_, e) => { if (e.Key == Key.Escape && (_matSearchBox.Text ?? "").Length > 0) { e.Handled = true; _matSearchBox.Text = ""; } };
        _matResults = new StackPanel { Spacing = 2 };
        _matResultsScroll = new ScrollViewer { Content = _matResults, IsVisible = false, Margin = new(0, 6, 0, 0) };
        return _matSearchBox;
    }

    void MaterialSearchTick(object? sender, EventArgs e)
    {
        _matSearchTimer?.Stop();
        _ = RunMaterialSearchAsync((_matSearchBox?.Text ?? "").Trim());
    }

    async Task RunMaterialSearchAsync(string query)
    {
        var version = ++_matSearchVersion;
        var searching = query.Length >= 2;
        _matResultsScroll.IsVisible = searching;
        _matTree.IsVisible = !searching;
        if (!searching) return;
        _matResults.Children.Clear();
        var status = new TextBlock { Classes = { "caption" }, TextWrapping = TextWrapping.Wrap, Text = L("Wird gesucht …"), Margin = new(4, 0, 0, 6) };
        _matResults.Children.Add(status);
        var docs = await MaterialTextsAsync((i, n) => { status.Text = L("Durchsuche {0} von {1} Dateien …", i, n); return version == _matSearchVersion; });
        if (docs is null || version != _matSearchVersion) return;
        int total = 0, files = 0;
        foreach (var (path, doc) in docs)
        {
            var hits = MaterialSearch.Find(path, doc.Blocks, query, 100);
            if (hits.Count == 0) continue;
            total += hits.Count; files++;
            var head = new Button { Classes = { "ghost" }, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new(4, 6, 4, 2) };
            head.Content = new TextBlock { Text = $"{FileGlyph(path)}  {Path.GetFileName(path)}  ·  {(hits.Count >= 100 ? "100+" : hits.Count.ToString())}", FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
            ToolTip.SetTip(head, path);
            var first = hits[0];
            head.Click += (_, _) => OpenMaterialAt(path, first, query);
            _matResults.Children.Add(head);
            foreach (var h in hits.Take(4))
            {
                var hit = h;
                var snippet = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12.5 };
                HighlightInto(snippet.Inlines!, h.Snippet, query);
                var row = new Button { Classes = { "ghost" }, Content = snippet, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new(14, 3, 4, 3) };
                row.Click += (_, _) => OpenMaterialAt(path, hit, query);
                _matResults.Children.Add(row);
            }
            if (hits.Count > 4)
            {
                var more = new Button { Classes = { "ghost" }, Content = L("… {0} weitere Stellen in dieser Datei (Strg+F springt weiter)", hits.Count - 4), Padding = new(14, 2, 4, 6), FontSize = 12, Opacity = 0.8 };
                more.Click += (_, _) => OpenMaterialAt(path, first, query);
                _matResults.Children.Add(more);
            }
        }
        status.Text = total == 0 ? L("„{0}“ steht in keiner Datei.", query) : Places(total, files);
    }

    string Places(int places, int files) => places == 1 ? L("1 Stelle in 1 Datei") : L("{0} Stellen in {1} Dateien", places, files);

    static readonly CompareInfo LooseCompare = CultureInfo.InvariantCulture.CompareInfo;

    /// <summary>Text with the searched words in bold and the accent color.</summary>
    void HighlightInto(InlineCollection into, string text, string word)
    {
        var at = 0;
        while (word.Length > 0 && at < text.Length)
        {
            var i = LooseCompare.IndexOf(text, word, at, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace);
            if (i < 0) break;
            if (i > at) into.Add(new Run(text[at..i]));
            var hit = new Run(text.Substring(i, Math.Min(word.Length, text.Length - i))) { FontWeight = FontWeight.Bold };
            hit.Bind(TextElement.ForegroundProperty, this.GetResourceObservable("BsAccent"));
            into.Add(hit);
            at = i + Math.Max(1, word.Length);
        }
        if (at < text.Length) into.Add(new Run(text[at..]));
    }

    /// <summary>Opens a file at a place (a search hit, a verse); the word, if any, goes into Ctrl+F, so Enter goes on to its next place.</summary>
    void OpenMaterialAt(string path, MaterialHit hit, string? word)
    {
        if (!_matBuilt || !MaterialsHost.IsEffectivelyVisible) OpenWork(NavGroups.First(g => g.Name == "Material"), MaterialsTab);
        _matPending = (path, hit, word);
        if (string.Equals(_matCurrent, path, StringComparison.OrdinalIgnoreCase) && _matSpotReady is not null) ApplyPendingPlace();
        else OpenMaterial(path);
    }

    /// <summary>Called when a file is shown: the place asked for, else where it was read last.</summary>
    void MaterialArrived(string path, int version)
    {
        if (version != _matShowVersion) return;
        if (_matPending is { } p && string.Equals(p.Path, path, StringComparison.OrdinalIgnoreCase)) { ApplyPendingPlace(); return; }
        _matSpotReady = null;
        if (_settings.MaterialsSpots.TryGetValue(path, out var spot))
        {
            if (spot.Page > 1 && IsPdfPages(path)) _ = JumpToAsync(new Block(BlockKind.Page, [], Page: spot.Page), path, version).ContinueWith(_ => Dispatcher.UIThread.Post(() => _matSpotReady = path));
            else { RestoreOffset(spot.Offset, path, version, 12); return; }
            return;
        }
        _matSpotReady = path;
    }

    bool IsPdfPages(string path) => string.Equals(Path.GetExtension(path), ".pdf", StringComparison.OrdinalIgnoreCase) && !_matPdfText;

    /// <summary>The scroll place of last time; a long file needs a few rounds of layout before it is as tall as then.</summary>
    void RestoreOffset(double offset, string path, int version, int tries)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (version != _matShowVersion) return;
            var max = Math.Max(0, _matScroll.Extent.Height - _matScroll.Viewport.Height);
            if (max + 1 < offset && tries > 0) { RestoreOffset(offset, path, version, tries - 1); return; }
            _matScroll.Offset = new Vector(0, Math.Min(offset, max));
            _matSpotReady = path;
        }, DispatcherPriority.Background);
    }

    void ApplyPendingPlace()
    {
        if (_matPending is not { } p) return;
        _matPending = null;
        var path = p.Path; var hit = p.Hit;
        if (IsPdfPages(path))
        {
            if (hit.Page > 0) _ = JumpToAsync(new Block(BlockKind.Page, [], Page: hit.Page), path, _matShowVersion);
            if (p.Word is { Length: > 0 } w) StatusText.Text = L("Die Stelle steht auf Seite {0}. Mit „Nur Text zeigen“ ist das Wort markiert.", hit.Page);
            _matSpotReady = path;
            return;
        }
        ShowMaterialPlace(hit.TextBlock, hit.Start, hit.Length);
        if (p.Word is { Length: > 0 } word) OpenFindBar(word, focus: false, keepPlace: hit);
        _matSpotReady = path;
    }

    /// <summary>Scrolls to a paragraph and chooses the words there, so they stand out.</summary>
    void ShowMaterialPlace(int textNo, int start, int length)
    {
        var entry = _matBlocks.FirstOrDefault(x => x.TextNo == textNo);
        if (entry.Control is null) return;
        ScrollToControl(entry.Control);
        if (_matSelected is { } old && old != entry.Text) old.SelectionEnd = old.SelectionStart;
        if (entry.Text is { } tb)
        {
            var len = (tb.Text ?? string.Concat(tb.Inlines?.OfType<Run>().Select(r => r.Text) ?? [])).Length;
            tb.SelectionStart = Math.Clamp(start, 0, len);
            tb.SelectionEnd = Math.Clamp(start + length, 0, len);
            _matSelected = tb;
        }
    }

    SelectableTextBlock? _matSelected;

    /// <summary>Keeps where the file is read (a little after scrolling stops).</summary>
    void MaterialScrolled()
    {
        if (_matCurrent is not { } path || !string.Equals(_matSpotReady, path, StringComparison.OrdinalIgnoreCase)) return;
        _matSpotTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
        _matSpotTimer.Stop();
        _matSpotTimer.Tick -= KeepMaterialSpot; _matSpotTimer.Tick += KeepMaterialSpot;
        _matSpotTimer.Start();
    }

    void KeepMaterialSpot(object? sender, EventArgs e)
    {
        _matSpotTimer?.Stop();
        if (_matCurrent is not { } path || !string.Equals(_matSpotReady, path, StringComparison.OrdinalIgnoreCase)) return;
        var y = _matScroll.Offset.Y;
        var page = 0;
        foreach (var (n, mark) in _pdfPageMarks)
            if (mark.TranslatePoint(new Point(0, 0), _matView) is { } pt && pt.Y <= y + 40 && n > page) page = n;
        _settings.MaterialsSpots[path] = new MaterialSpot { Offset = y, Page = page };
        // only the newest hundred files are kept
        if (_settings.MaterialsSpots.Count > 100) _settings.MaterialsSpots.Remove(_settings.MaterialsSpots.Keys.First(k => !string.Equals(k, path, StringComparison.OrdinalIgnoreCase)));
        SaveSettings();
    }

    // ---- Ctrl+F in the open file ----------------------------------------------------------------------------------------

    Border _matFindBar = null!;
    TextBox _matFindBox = null!;
    TextBlock _matFindCount = null!;
    List<MaterialHit> _matFindHits = [];
    int _matFindAt;

    Control BuildFindBar()
    {
        _matFindBox = new TextBox { PlaceholderText = L("In dieser Datei suchen …"), Width = 260, VerticalAlignment = VerticalAlignment.Center };
        _matFindCount = new TextBlock { Classes = { "caption" }, VerticalAlignment = VerticalAlignment.Center, MinWidth = 90 };
        Button Tool(string glyph, string tip, Action act)
        {
            var b = new Button { Classes = { "tool" }, Width = 30, Height = 30, Padding = new(0), Content = glyph };
            ToolTip.SetTip(b, tip);
            b.Click += (_, _) => act();
            return b;
        }
        var prev = Tool("↑", L("Vorige Stelle (Umschalt+Enter)"), () => StepFind(-1));
        var next = Tool("↓", L("Nächste Stelle (Enter)"), () => StepFind(1));
        var close = Tool("✕", L("Schließen (Esc)"), CloseFindBar);
        _matFindBox.TextChanged += (_, _) => FindInMaterial(null);
        _matFindBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { e.Handled = true; StepFind(e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? -1 : 1); }
            else if (e.Key == Key.Escape) { e.Handled = true; CloseFindBar(); }
        };
        _matFindBar = new Border
        {
            IsVisible = false, CornerRadius = new(8), Padding = new(8, 4), Margin = new(0, 0, 0, 6), BorderThickness = new(1),
            Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { _matFindBox, _matFindCount, prev, next, close } },
        };
        _matFindBar.Bind(Border.BackgroundProperty, this.GetResourceObservable("BsSurface2"));
        _matFindBar.Bind(Border.BorderBrushProperty, this.GetResourceObservable("BsBorder"));
        return _matFindBar;
    }

    /// <summary>Ctrl+F: the bar above the file; a PDF shown as pages switches to its text, where words can be found and marked.</summary>
    void OpenFindBar(string? word = null, bool focus = true, MaterialHit? keepPlace = null)
    {
        if (_matCurrent is not { } path) return;
        if (IsPdfPages(path))
        {
            _matPdfText = true;
            _matPending = null;
            _ = ShowMaterialAsync(path).ContinueWith(_ => Dispatcher.UIThread.Post(() => OpenFindBar(word, focus)));
            return;
        }
        _matFindBar.IsVisible = true;
        _matFindBox.Tag = keepPlace;   // the place shown already: the count starts there, nothing moves
        if (word is not null && _matFindBox.Text != word) _matFindBox.Text = word;
        else FindInMaterial(keepPlace);
        _matFindBox.Tag = null;
        if (focus) { _matFindBox.Focus(); _matFindBox.SelectAll(); }
    }

    void CloseFindBar()
    {
        _matFindBar.IsVisible = false;
        if (_matSelected is { } s) s.SelectionEnd = s.SelectionStart;
        _matViewer.Focus();
    }

    void FindInMaterial(MaterialHit? keep)
    {
        keep ??= _matFindBox.Tag as MaterialHit;
        var word = (_matFindBox.Text ?? "").Trim();
        _matFindHits = word.Length == 0 || _matCurrent is not { } path || !_matCache.TryGetValue(path, out var c) ? [] : MaterialSearch.Find(path, c.Doc.Blocks, word, 2000);
        if (_matFindHits.Count == 0) { _matFindCount.Text = word.Length == 0 ? "" : L("Nicht gefunden"); return; }
        _matFindAt = keep is { } k ? Math.Max(0, _matFindHits.FindIndex(h => h.TextBlock == k.TextBlock && h.Start == k.Start)) : 0;
        if (keep is null) ShowFindHit();
        else UpdateFindCount();
    }

    void StepFind(int dir)
    {
        if (_matFindHits.Count == 0) return;
        _matFindAt = ((_matFindAt + dir) % _matFindHits.Count + _matFindHits.Count) % _matFindHits.Count;
        ShowFindHit();
    }

    void ShowFindHit()
    {
        var h = _matFindHits[_matFindAt];
        ShowMaterialPlace(h.TextBlock, h.Start, h.Length);
        UpdateFindCount();
    }

    void UpdateFindCount() => _matFindCount.Text = L("{0} von {1}", _matFindAt + 1, _matFindHits.Count >= 2000 ? "2000+" : _matFindHits.Count.ToString());

    // ---- the verse overview: where the verse stands in the materials ------------------------------------------------------

    async Task FillGuideMaterialsAsync(VerseRef r, int version, StackPanel into)
    {
        try
        {
            var docs = await MaterialTextsAsync((_, _) => version == _guideVersion);
            if (docs is null || version != _guideVersion) return;
            var hits = docs.SelectMany(d => MaterialSearch.VerseHits(d.Path, d.Doc.Blocks, r, 20)).ToList();
            into.Children.Clear();
            if (hits.Count == 0) { into.Children.Add(Muted(L("{0} kommt in deinen Materialien nicht vor.", Label(r)))); return; }
            var files = hits.Select(h => h.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count();
            into.Children.Add(Muted(Places(hits.Count, files)));
            void Add(MaterialHit h)
            {
                var text = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12.5 };
                text.Inlines!.Add(new Run(Path.GetFileNameWithoutExtension(h.Path) + (h.Page > 0 ? " · " + L("Seite {0}", h.Page) : "") + "\n") { FontWeight = FontWeight.SemiBold });
                text.Inlines.Add(new Run(h.Snippet));
                var row = new Button { Classes = { "ghost" }, Content = text, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new(6, 4) };
                ToolTip.SetTip(row, L("In den Materialien öffnen"));
                row.Click += (_, _) => OpenMaterialAt(h.Path, h, null);
                into.Children.Add(row);
            }
            foreach (var h in hits.Take(5)) Add(h);
            if (hits.Count > 5)
            {
                var all = new Button { Content = L("Alle {0} Stellen zeigen", hits.Count), HorizontalAlignment = HorizontalAlignment.Left, Margin = new(0, 4, 0, 0) };
                all.Click += (_, _) => { into.Children.Remove(all); foreach (var h in hits.Skip(5)) Add(h); };
                into.Children.Add(all);
            }
        }
        catch (Exception ex)
        {
            if (version != _guideVersion) return;
            into.Children.Clear();
            into.Children.Add(Muted(ex.Message));
        }
    }
}
