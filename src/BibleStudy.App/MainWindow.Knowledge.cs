using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using BibleStudy.Core.Models;
using BibleStudy.Core.Storage;

namespace BibleStudy.App;

/// <summary>Knowledge base: own notes, linked files, templates, links between documents and to verses, and the knowledge graph.</summary>
public partial class MainWindow
{
    Document? _doc;
    long? _newDocFolder;   // folder preset for a document created from the folder tree
    string? _docTag;
    bool _docsLoading;

    void InitKnowledge()
    {
        DocSearchBox.TextChanged += (_, _) => RefreshDocs();
        DocFilterBox.SelectionChanged += (_, _) => { if (!_docsLoading) RefreshDocs(); };
        DocList.SelectionChanged += (_, _) =>
        {
            if (DocList.SelectedItem is Entry { Id: long id } && _vault.GetDocuments().FirstOrDefault(d => d.Id == id) is { } d) OpenDocument(d);
        };
        DocNewBtn.Click += (_, _) => OpenDocument(new Document(0, "", "", null, false, DateTime.Now, []));
        DocFromVerseBtn.Click += (_, _) => NewDocumentFromVerse();
        DocFileBtn.Click += async (_, _) => await LinkFileAsync();
        DocGraphBtn.Click += (_, _) => ShowKnowledgeGraph();
        DocBackBtn.Click += (_, _) => CloseDocument();
        DocSaveBtn.Click += (_, _) => SaveDocument();
        DocDeleteBtn.Click += (_, _) => { if (_doc is { Id: > 0 } d) { _vault.DeleteDocument(d.Id); CloseDocument(); } };
        DocOpenFileBtn.Click += (_, _) => OpenLinkedFile();
        DocInsertVerseBtn.Click += (_, _) => InsertIntoDoc(_selected is int v ? $"> {VerseTextOf(v).Trim()}\n> — {Label(new VerseRef(_curBook, _curChapter, v))}\n\n" : null, "Erst im Bibeltext einen Vers anklicken.");
        DocInsertLinkBtn.Click += (_, _) =>
        {
            var menu = new MenuFlyout();
            foreach (var d in _vault.GetDocuments().Where(x => x.Id != _doc?.Id).Take(60))
            {
                var title = d.Title; var item = new MenuItem { Header = title };
                item.Click += (_, _) => InsertIntoDoc($"[[{title}]]", null);
                menu.Items.Add(item);
            }
            if (menu.Items.Count == 0) menu.Items.Add(new MenuItem { Header = "Noch keine anderen Dokumente", IsEnabled = false });
            menu.ShowAt(DocInsertLinkBtn);
        };
        DocFromTemplateBtn.Click += (_, _) =>
        {
            if (_doc is { } d) OpenDocument(new Document(0, "Neu: " + d.Title, d.Body, null, false, DateTime.Now, d.Tags));
        };
        KnowledgePane.CloseRequested += ShowReader;
        KnowledgePane.NodeOpened += async node =>
        {
            if (node.Kind == "doc" && long.TryParse(node.Key, out var id) && _vault.GetDocuments().FirstOrDefault(d => d.Id == id) is { } d)
            { ShowReader(); SideTabs.SelectedIndex = KnowledgeTab; OpenDocument(d); }
            else if (node.Kind == "verse" && node.Key.Split('|') is [var book, var chapter, var verse]) await GoToAsync(book, int.Parse(chapter), int.Parse(verse));
        };
        RefreshDocs();
    }

    // ---- list ---------------------------------------------------------------------------------------------------------

    void RefreshDocs()
    {
        if (_vault is null) return;
        var query = DocSearchBox.Text?.Trim();
        var docs = _vault.GetDocuments(string.IsNullOrEmpty(query) ? null : query, tag: _docTag, templates: DocFilterBox.SelectedIndex == 1 ? true : null);
        BuildDocTagChips();
        if (DocFilterBox.SelectedIndex == 2)
        {
            if (_selected is int v) { var r = new VerseRef(_curBook, _curChapter, v); docs = docs.Where(d => DocumentLinks.VerseRefs(d.Body).Contains(r)).ToList(); DocInfo.Text = $"Dokumente, die {Label(r)} erwähnen: {docs.Count}"; }
            else { docs = []; DocInfo.Text = "Erst einen Vers anklicken."; }
        }
        else DocInfo.Text = $"{docs.Count} Dokument{(docs.Count == 1 ? "" : "e")}";
        DocList.ItemsSource = docs.Select(d =>
        {
            var preview = d.Path is not null ? "Datei: " + System.IO.Path.GetFileName(d.Path) : Shorten(d.Body.ReplaceLineEndings(" ").Replace("#", "").Replace("**", "").Replace("[[", "").Replace("]]", "").Trim(), 90);
            var tags = d.Tags.Count > 0 ? "  " + string.Join(" ", d.Tags.Select(t => "#" + t)) : "";
            return new Entry(default, $"{(d.IsTemplate ? "◇ " : "")}{d.Title}\n{preview}{tags}".TrimEnd('\n'), d.Id);
        }).ToList();
    }

    void BuildDocTagChips()
    {
        DocTagChips.Children.Clear();
        foreach (var g in _vault.GetDocuments().SelectMany(d => d.Tags).GroupBy(t => t, StringComparer.OrdinalIgnoreCase).OrderByDescending(g => g.Count()).Take(14))
        {
            var tag = g.Key; var active = string.Equals(_docTag, tag, StringComparison.OrdinalIgnoreCase);
            DocTagChips.Children.Add(FilterChip($"#{tag} {g.Count()}", active, null, () => { _docTag = active ? null : tag; RefreshDocs(); }));
        }
    }

    void InsertIntoDoc(string? text, string? hint)
    {
        if (text is null) { DocPathText.Text = hint ?? ""; return; }
        var body = DocBodyBox.Text ?? ""; var at = Math.Clamp(DocBodyBox.CaretIndex, 0, body.Length);
        DocBodyBox.Text = body.Insert(at, text); DocBodyBox.CaretIndex = at + text.Length; DocBodyBox.Focus();
    }

    // ---- editor -------------------------------------------------------------------------------------------------------

    void OpenDocument(Document d)
    {
        _doc = d;
        DocTitleBox.Text = d.Title; DocBodyBox.Text = d.Body; DocTagsBox.Text = string.Join(", ", d.Tags);
        DocTemplateCheck.IsChecked = d.IsTemplate;
        var folders = _vault.GetFolders().OrderBy(f => f.Name, Comparer<string>.Create(NaturalCompare)).ToList();
        DocFolderBox.ItemsSource = new List<object> { "(kein Ordner)" }.Concat(folders).ToList();
        var fid = d.FolderId ?? _newDocFolder;
        DocFolderBox.SelectedIndex = fid is long f2 && folders.FindIndex(x => x.Id == f2) is var fi and >= 0 ? fi + 1 : 0;
        DocPathText.Text = d.Path is null ? "" : "Verknüpfte Datei: " + d.Path;
        DocOpenFileBtn.IsVisible = d.Path is not null;
        DocFromTemplateBtn.IsVisible = d.IsTemplate && d.Id > 0;
        DocDeleteBtn.IsVisible = d.Id > 0;
        DocListPanel.IsVisible = false; DocEditorPanel.IsVisible = true;
        RefreshDocChips(d);
    }

    void CloseDocument()
    {
        _doc = null; _newDocFolder = null;
        DocEditorPanel.IsVisible = false; DocListPanel.IsVisible = true;
        DocList.SelectedItem = null;
        RefreshDocs();
    }

    void SaveDocument()
    {
        if (_doc is null) return;
        var title = DocTitleBox.Text?.Trim() ?? "";
        if (title.Length == 0) { DocInfo.Text = ""; DocPathText.Text = "Bitte einen Titel eingeben."; return; }
        var tags = (DocTagsBox.Text ?? "").Split(',', ';');
        var body = DocBodyBox.Text ?? "";
        var template = DocTemplateCheck.IsChecked == true;
        var folderId = DocFolderBox.SelectedItem is Folder fo ? fo.Id : (long?)null;
        if (_doc.Id == 0) { var id = _vault.AddDocument(title, body, _doc.Path, template, tags, folderId); _doc = _vault.GetDocuments().First(d => d.Id == id); }
        else { _vault.UpdateDocument(_doc.Id, title, body, _doc.Path, template, tags, folderId); _doc = _vault.GetDocuments().First(d => d.Id == _doc.Id); }
        OpenDocument(_doc);
        RefreshCollection();
        DocPathText.Text = (_doc.Path is null ? "" : "Verknüpfte Datei: " + _doc.Path + "\n") + "Gespeichert.";
    }

    /// <summary>Verse references, [[links]] and backlinks of the document, as clickable chips.</summary>
    void RefreshDocChips(Document d)
    {
        DocVerseChips.Children.Clear(); DocLinkChips.Children.Clear(); DocBackChips.Children.Clear();
        var trId = (TranslationBox.SelectedItem as TranslationInfo)?.Id ?? "";
        foreach (var r in DocumentLinks.VerseRefs(d.Body))
            DocVerseChips.Children.Add(Chip(Label(r), () => _ = GoToAsync(r.Book, r.Chapter, r.Verse), trId, null));
        if (DocVerseChips.Children.Count == 0) DocVerseChips.Children.Add(new TextBlock { Text = "–", Opacity = 0.6 });

        var all = _vault.GetDocuments();
        foreach (var title in DocumentLinks.WikiLinks(d.Body))
        {
            var target = all.FirstOrDefault(x => string.Equals(x.Title, title, StringComparison.OrdinalIgnoreCase));
            var text = target is null ? $"„{title}“ (neu anlegen)" : title;
            DocLinkChips.Children.Add(Chip(text, () =>
            {
                if (target is not null) OpenDocument(target);
                else OpenDocument(new Document(0, title, "", null, false, DateTime.Now, []));
            }, trId, null));
        }
        if (DocLinkChips.Children.Count == 0) DocLinkChips.Children.Add(new TextBlock { Text = "–", Opacity = 0.6 });

        foreach (var other in all.Where(x => x.Id != d.Id && DocumentLinks.WikiLinks(x.Body).Any(t => string.Equals(t, d.Title, StringComparison.OrdinalIgnoreCase))))
            DocBackChips.Children.Add(Chip(other.Title, () => OpenDocument(other), trId, null));
        if (DocBackChips.Children.Count == 0) DocBackChips.Children.Add(new TextBlock { Text = "–", Opacity = 0.6 });
    }

    // ---- creating documents -------------------------------------------------------------------------------------------

    void NewDocumentFromVerse()
    {
        if (_selected is not int v || _curChapterData is null) { DocInfo.Text = "Erst einen Vers anklicken."; return; }
        var r = new VerseRef(_curBook, _curChapter, v);
        var translation = (TranslationBox.SelectedItem as TranslationInfo)?.ShortName ?? "";
        var body = $"## {Label(r)}\n\n> {VerseTextOf(v)}\n> ({translation})\n\nGedanken:\n\n";
        OpenDocument(new Document(0, Label(r), body, null, false, DateTime.Now, AutoTags(r).ToList()));
    }

    async Task LinkFileAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Datei verknüpfen", AllowMultiple = true });
        foreach (var file in files)
        {
            if (file.TryGetLocalPath() is not { } path) continue;
            var body = "";
            var ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
            if (ext is ".txt" or ".md" or ".markdown" && new FileInfo(path).Length < 2_000_000) body = await File.ReadAllTextAsync(path); // text files become searchable
            var id = _vault.AddDocument(System.IO.Path.GetFileName(path), body, path, false, []);
            if (files.Count == 1) OpenDocument(_vault.GetDocuments().First(d => d.Id == id));
        }
        RefreshDocs();
        DocInfo.Text = $"{files.Count} Datei(en) verknüpft. Die Dateien bleiben, wo sie sind; nur der Verweis wird gespeichert.";
    }

    void OpenLinkedFile()
    {
        if (_doc?.Path is not { } path) return;
        if (!File.Exists(path)) { DocPathText.Text = "Die Datei wurde nicht gefunden: " + path; return; }
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception ex) { DocPathText.Text = "Öffnen nicht möglich: " + ex.Message; }
    }

    // ---- knowledge graph ----------------------------------------------------------------------------------------------

    void ShowKnowledgeGraph()
    {
        var docs = _vault.GetDocuments();
        var nodes = new List<KnowledgeNode>(); var edges = new List<(int, int)>();
        var index = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        int Node(string key, Func<KnowledgeNode> make) { if (!index.TryGetValue(key, out var i)) { i = nodes.Count; nodes.Add(make()); index[key] = i; } return i; }
        var gold = Color.Parse("#E3B65F");
        foreach (var d in docs) Node("doc:" + d.Id, () => new KnowledgeNode("doc", d.Title, d.Id.ToString(), gold));
        foreach (var d in docs)
        {
            var from = index["doc:" + d.Id];
            foreach (var title in DocumentLinks.WikiLinks(d.Body))
                if (docs.FirstOrDefault(x => string.Equals(x.Title, title, StringComparison.OrdinalIgnoreCase)) is { } t && t.Id != d.Id) edges.Add((from, index["doc:" + t.Id]));
            foreach (var r in DocumentLinks.VerseRefs(d.Body))
                edges.Add((from, Node($"v:{r.Book}|{r.Chapter}|{r.Verse}", () => new KnowledgeNode("verse", Label(r), $"{r.Book}|{r.Chapter}|{r.Verse}", HomeView.ColorOf(r.Book)))));
            foreach (var tag in d.Tags)
                edges.Add((from, Node("t:" + tag, () => new KnowledgeNode("tag", "#" + tag, tag, Color.Parse("#9a9890")))));
        }
        Home.IsVisible = false; ReaderHost.IsVisible = false; GraphPane.IsVisible = false; MapPane.IsVisible = false; KnowledgePane.IsVisible = true;
        KnowledgePane.SetData(nodes, edges);
    }
}
