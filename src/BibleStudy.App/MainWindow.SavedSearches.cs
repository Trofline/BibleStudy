using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using BibleStudy.Core.Models;
using BibleStudy.Core.Storage;

namespace BibleStudy.App;

/// <summary>Searches kept under a name (with their options), and what to do with the hits: save them as a document or export them as a table.</summary>
public partial class MainWindow
{
    readonly Button _saveSearchBtn = new() { Content = "☆", Classes = { "seg" }, Margin = new(0, 0, 8, 4) };
    readonly Button _savedListBtn = new() { Content = "", Classes = { "seg" }, Margin = new(0, 0, 8, 4) };
    readonly Button _hitsDocBtn = new() { Classes = { "seg" }, IsVisible = false, Margin = new(0, 0, 8, 4) };
    readonly Button _hitsCsvBtn = new() { Classes = { "seg" }, IsVisible = false, Margin = new(0, 0, 8, 4) };
    readonly Button _hitsMapBtn = new() { Classes = { "seg" }, IsVisible = false, Margin = new(0, 0, 8, 4) };

    void InitSavedSearches()
    {
        ToolTip.SetTip(_saveSearchBtn, L("Diese Suche mit ihren Optionen unter einem Namen merken"));
        ToolTip.SetTip(_savedListBtn, L("Gemerkte Suchen"));
        _savedListBtn.Content = L("Gemerkte Suchen ▾");
        _hitsDocBtn.Content = L("Treffer als Dokument");
        _hitsCsvBtn.Content = L("Treffer als Tabelle (CSV)");
        _hitsMapBtn.Content = L("Orte auf der Karte");
        ToolTip.SetTip(_hitsMapBtn, L("Zeigt auf der Karte, wo die Orte der Treffer liegen; je mehr Verse, desto größer der Punkt"));
        ToolTip.SetTip(_hitsDocBtn, L("Legt ein Dokument mit allen Treffern und Versen an"));
        ToolTip.SetTip(_hitsCsvBtn, L("Speichert die Treffer als CSV-Datei, die sich in Excel öffnen lässt"));
        if (SearchScopeBox.Parent?.Parent is Panel options)
        {
            options.Children.Add(_saveSearchBtn);
            options.Children.Add(_savedListBtn);
        }
        if (SearchQueueBtn.Parent is Panel actions)
        {
            var at = actions.Children.IndexOf(SearchQueueBtn) + 1;
            actions.Children.Insert(at, new WrapPanel { Children = { _hitsDocBtn, _hitsCsvBtn, _hitsMapBtn } });
        }
        _saveSearchBtn.Flyout = BuildSaveFlyout();
        _savedListBtn.Click += (_, _) => ShowSavedMenu();
        _hitsDocBtn.Click += (_, _) => SaveHitsAsDocument();
        _hitsCsvBtn.Click += (_, _) => ExportHitsCsv();
        _hitsMapBtn.Click += async (_, _) => await ShowHitsOnMapAsync();
    }

    void UpdateHitActions(bool visible) { _hitsDocBtn.IsVisible = visible; _hitsCsvBtn.IsVisible = visible; _hitsMapBtn.IsVisible = visible; }

    async Task ShowHitsOnMapAsync()
    {
        var hits = CurrentHits().Select(h => h.Ref).ToList();
        if (hits.Count == 0) return;
        ShowReader();
        Home.IsVisible = false; ReaderHost.IsVisible = false; GraphPane.IsVisible = false; KnowledgePane.IsVisible = false; TimelinePane.IsVisible = false; FamilyPane.IsVisible = false; MapPane.IsVisible = true;
        await MapPane.OpenHitsAsync(L("Treffer: {0}", Shorten((SearchBox.Text ?? "").Trim(), 40)), hits);
    }

    Flyout BuildSaveFlyout()
    {
        var name = new TextBox { Watermark = L("Name der Suche"), Width = 240 };
        var save = new Button { Content = L("Merken"), Classes = { "primary" }, HorizontalAlignment = HorizontalAlignment.Left };
        var info = new TextBlock { Classes = { "caption" }, TextWrapping = TextWrapping.Wrap, MaxWidth = 240 };
        var fly = new Flyout { Content = new StackPanel { Spacing = 8, Children = { new TextBlock { Text = L("Suche merken"), FontWeight = FontWeight.SemiBold }, name, save, info } } };
        fly.Opening += (_, _) => { name.Text = Shorten((SearchBox.Text ?? "").Trim(), 40); info.Text = ""; };
        void Do()
        {
            var query = (SearchBox.Text ?? "").Trim();
            var title = (name.Text ?? "").Trim();
            if (query.Length == 0) { info.Text = L("Erst eine Suche eingeben."); return; }
            if (title.Length == 0) title = query;
            _settings.SavedSearches.RemoveAll(s => string.Equals(s.Name, title, StringComparison.OrdinalIgnoreCase));
            _settings.SavedSearches.Add(new SavedSearch
            {
                Name = title, Query = query, Scope = Math.Max(0, SearchScopeBox.SelectedIndex), Match = Math.Max(0, SearchMatchBox.SelectedIndex), Range = Math.Max(0, SearchRangeBox.SelectedIndex),
                Original = SearchOriginalCheck.IsChecked == true, CaseSensitive = SearchCaseCheck.IsChecked == true,
            });
            SaveSettings();
            fly.Hide();
            StatusText.Text = L("Suche „{0}“ gemerkt.", title);
        }
        save.Click += (_, _) => Do();
        name.KeyDown += (_, e) => { if (e.Key == Key.Enter) Do(); };
        return fly;
    }

    void ShowSavedMenu()
    {
        var panel = new StackPanel { Spacing = 4, MinWidth = 280, MaxWidth = 380 };
        var fly = new Flyout { Content = new ScrollViewer { MaxHeight = 360, Content = panel } };
        void Fill()
        {
            panel.Children.Clear();
            panel.Children.Add(new TextBlock { Text = L("Gemerkte Suchen"), FontWeight = FontWeight.SemiBold, Margin = new(0, 0, 0, 4) });
            if (_settings.SavedSearches.Count == 0) panel.Children.Add(new TextBlock { Text = L("Noch keine. Mit ☆ merkst du die aktuelle Suche."), Classes = { "caption" }, TextWrapping = TextWrapping.Wrap });
            foreach (var s in _settings.SavedSearches.OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).ToList())
            {
                var saved = s;
                var run = new Button
                {
                    Content = new StackPanel { Children = { new TextBlock { Text = saved.Name, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis }, new TextBlock { Text = saved.Query, Classes = { "caption" }, TextTrimming = TextTrimming.CharacterEllipsis } } },
                    HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left,
                };
                run.Click += async (_, _) => { fly.Hide(); await RunSavedSearchAsync(saved); };
                var del = new Button { Content = "✕", Classes = { "tool" }, Width = 28, Height = 28 };
                ToolTip.SetTip(del, L("Entfernen"));
                del.Click += (_, _) => { _settings.SavedSearches.Remove(saved); SaveSettings(); Fill(); };
                var row = new DockPanel();
                DockPanel.SetDock(del, Dock.Right);
                row.Children.Add(del); row.Children.Add(run);
                panel.Children.Add(row);
            }
        }
        Fill();
        fly.ShowAt(_savedListBtn);
    }

    async Task RunSavedSearchAsync(SavedSearch s)
    {
        SearchScopeBox.SelectedIndex = Math.Clamp(s.Scope, 0, Scopes.Length - 1);
        SearchMatchBox.SelectedIndex = Math.Clamp(s.Match, 0, MatchModes.Length - 1);
        SearchRangeBox.SelectedIndex = Math.Clamp(s.Range, 0, Ranges.Length - 1);
        SearchOriginalCheck.IsChecked = s.Original; SearchCaseCheck.IsChecked = s.CaseSensitive;
        SearchBox.Text = s.Query;
        await SearchAsync();
    }

    // ---- what to do with the hits ----------------------------------------------------------------------------------------

    void SaveHitsAsDocument()
    {
        var hits = CurrentHits().ToList();
        if (hits.Count == 0 || TranslationBox.SelectedItem is not TranslationInfo t) return;
        var query = (SearchBox.Text ?? "").Trim();
        var sb = new StringBuilder();
        sb.Append("# ").Append(L("Suche: {0}", query)).Append("\n\n");
        sb.Append(L("{0} Verse in {1} · {2:dd.MM.yyyy}", hits.Count, t.ShortName, DateTime.Now)).Append("\n\n");
        foreach (var h in hits.Take(1000))
        {
            sb.Append("- **").Append(Label(h.Ref)).Append("** ").Append(h.Text.Trim());
            if (_hitNotes.TryGetValue(h.Ref, out var note)) sb.Append("  ").Append(note);
            sb.Append('\n');
        }
        if (hits.Count > 1000) sb.Append('\n').Append(L("(erste {0} Verse)", 1000)).Append('\n');
        var title = L("Suche: {0}", Shorten(query, 60));
        var id = _vault.AddDocument(title, sb.ToString(), null, false, [L("Suche")]);
        RefreshDocs();
        OpenWork(NavGroups.First(g => g.Name == "Dokumente"), KnowledgeTab);
        OpenDocument(_vault.GetDocuments().First(d => d.Id == id));
    }

    void ExportHitsCsv()
    {
        var hits = CurrentHits().ToList();
        if (hits.Count == 0 || TranslationBox.SelectedItem is not TranslationInfo t) return;
        static string Q(string s) => "\"" + s.Replace("\"", "\"\"").ReplaceLineEndings(" ") + "\"";
        var sb = new StringBuilder();
        sb.Append(L("Stelle")).Append(';').Append(L("Buch")).Append(';').Append(L("Kapitel")).Append(';').Append(L("Vers")).Append(';').Append(L("Text")).Append(';').Append(L("Hinweis")).Append('\n');
        foreach (var h in hits)
            sb.Append(Q(Label(h.Ref))).Append(';').Append(Q(BookName(h.Ref.Book))).Append(';').Append(h.Ref.Chapter).Append(';').Append(h.Ref.Verse).Append(';')
              .Append(Q(h.Text.Trim())).Append(';').Append(Q(_hitNotes.GetValueOrDefault(h.Ref, ""))).Append('\n');
        var path = Path.Combine(ExportFolder, SafeName(L("Suche") + "-" + (SearchBox.Text ?? ""), "suche") + "-" + t.ShortName + ".csv");
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));   // with byte order mark: Excel then reads the umlauts correctly
        StatusText.Text = L("Gespeichert: {0}", path);
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true }); } catch { /* optional */ }
    }
}
