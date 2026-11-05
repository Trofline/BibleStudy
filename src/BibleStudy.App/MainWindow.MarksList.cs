using Avalonia.Controls;
using BibleStudy.Core;
using BibleStudy.Core.Models;
using BibleStudy.Core.Storage;

namespace BibleStudy.App;

/// <summary>
/// The list of marks in the collection: as a tree of folders, with several verses of one mark as one entry, the most important parts of marked
/// verses shown inside the verse (not as extra entries), and on request the marks of all vaults at once.
/// </summary>
public partial class MainWindow
{
    readonly HashSet<long> _openFolders = [];
    bool _foldersTouched;
    bool _marksListLoading;
    readonly Dictionary<string, VaultStore> _otherVaults = [];

    const long DocRowBase = 1_000_000_000_000;
    /// <summary>Marks of another vault (not the open one) get an id from here on: base + (vault number) × span + the id in that vault.</summary>
    const long ForeignRowBase = 10_000_000_000_000;
    const long ForeignSpan = 1_000_000_000;
    /// <summary>A folder is told apart over several vaults: vault number × span + folder id; vault headings count from <see cref="VaultKeyBase"/>.</summary>
    const long FolderKeySpan = 10_000_000;
    const long VaultKeyBase = 9_000_000_000;

    static int NaturalCompare(string a, string b)
    {
        var x = System.Text.RegularExpressions.Regex.Split(a, @"(\d+)"); var y = System.Text.RegularExpressions.Regex.Split(b, @"(\d+)");
        for (var i = 0; i < Math.Min(x.Length, y.Length); i++)
        {
            var c = long.TryParse(x[i], out var nx) && long.TryParse(y[i], out var ny) ? nx.CompareTo(ny) : string.Compare(x[i], y[i], StringComparison.CurrentCultureIgnoreCase);
            if (c != 0) return c;
        }
        return x.Length.CompareTo(y.Length);
    }

    /// <summary>The folders as a tree: every folder followed by its sub-folders (each level sorted by name), with <see cref="Folder.Depth"/> set,
    /// so lists and boxes can show them indented.</summary>
    static List<Folder> FolderTree(IEnumerable<Folder> folders)
    {
        var all = folders.ToList();
        var ids = all.Select(f => f.Id).ToHashSet();
        var result = new List<Folder>(all.Count);
        var seen = new HashSet<long>();
        long? Parent(Folder f) => f.ParentId is long p && p != f.Id && ids.Contains(p) ? p : null;
        void Add(long? parent, int depth)
        {
            foreach (var f in all.Where(f => Parent(f) == parent).OrderBy(f => f.Name, Comparer<string>.Create(NaturalCompare)))
                if (seen.Add(f.Id)) { result.Add(f with { Depth = depth }); Add(f.Id, depth + 1); }
        }
        Add(null, 0);
        foreach (var f in all) if (seen.Add(f.Id)) result.Add(f);   // a broken chain must not hide a folder
        return result;
    }

    VaultStore StoreOf(string name)
    {
        if (name == _vault.Name) return _vault;
        if (!_otherVaults.TryGetValue(name, out var s)) _otherVaults[name] = s = OpenVault(name);
        return s;
    }

    /// <summary>The folders as a collapsible tree; verses that follow each other with the same note and color are shown as one range (19,24–29).</summary>
    void ApplyCollectionFilter()
    {
        var all = MarksAllVaultsBtn.IsChecked == true;
        FolderFilterBox.IsEnabled = !all;
        long? folder = all ? null : (FolderFilterBox.SelectedItem as Folder)?.Id;
        var noFolder = !all && FolderFilterBox.SelectedIndex == 1;
        var tagFilter = TagFilterBox.SelectedIndex > 0 ? TagFilterBox.SelectedItem as string : null;
        var detail = MarksDetailBtn.IsChecked == true;
        var names = all ? VaultNames() : [_vault.Name];
        var rows = new List<Entry>();
        var total = 0;
        // few folders: start expanded; many: collapsed (until the user decides)
        if (!_foldersTouched) _openFolders.Clear();

        for (var vi = 0; vi < names.Count; vi++)
        {
            var store = StoreOf(names[vi]);
            var foreign = !ReferenceEquals(store, _vault);
            var vnum = foreign ? vi + 1 : 0;   // 0 = the open vault (its ids stay what they always were)
            var everything = store.GetMarks();
            var marks = everything.Where(m => (folder is null || m.FolderId == folder) && (!noFolder || m.FolderId is null)
                && (tagFilter is null || m.Tags.Contains(tagFilter, StringComparer.OrdinalIgnoreCase))).ToList();
            if (all && marks.Count == 0) continue;
            total += marks.Count;
            var folders = FolderTree(store.GetFolders());
            if (!_foldersTouched) { if (all || folders.Count <= 3 || folder is not null) foreach (var f in folders) _openFolders.Add(vnum * FolderKeySpan + f.Id); _openFolders.Add(vnum * FolderKeySpan); if (all) _openFolders.Add(VaultKeyBase + vi); }
            if (all)
            {
                var vopen = _openFolders.Contains(VaultKeyBase + vi);
                rows.Add(new Entry(default, $"{(vopen ? "▾" : "▸")}  ◆ {names[vi]}   ·   {L("{0} Verse", marks.Count)}", -1 - (VaultKeyBase + vi), null));
                if (!vopen) continue;
            }

            // a mark inside a marked verse is the most important part of it: it is shown in the verse, not as an entry of its own
            var wholeVisible = new HashSet<(string, int, int)>();
            foreach (var m in marks.Where(m => m.WholeVerse)) for (var v = m.Ref.Verse; v <= m.LastVerse; v++) wholeVisible.Add((m.Ref.Book, m.Ref.Chapter, v));
            bool IsCore(Mark m) => !m.WholeVerse && Enumerable.Range(m.Ref.Verse, m.LastVerse - m.Ref.Verse + 1).All(v => wholeVisible.Contains((m.Ref.Book, m.Ref.Chapter, v)));
            var cores = marks.Where(IsCore).ToList();
            var shown = marks.Where(m => !IsCore(m)).ToList();

            foreach (var (id, name, depth) in folders.Select(f => (f.Id, f.Name, f.Depth)).Append((0L, L("Ohne Ordner"), 0)))
            {
                var inFolder = shown.Where(m => (m.FolderId ?? 0) == id).OrderBy(m => BookOrder(m.Ref.Book)).ThenBy(m => m.Ref.Chapter).ThenBy(m => m.Ref.Verse).ToList();
                if (inFolder.Count == 0) continue;
                var key = vnum * FolderKeySpan + id;
                var open = _openFolders.Contains(key);
                rows.Add(new Entry(default, $"{new string(' ', depth * 5)}{(open ? "▾" : "▸")}  {name}   ·   {L("{0} Verse", inFolder.Count)}", -1 - key, null));
                if (!open) continue;
                for (var i = 0; i < inFolder.Count; i++)
                {
                    var first = inFolder[i]; var last = first; var run = new List<Mark> { first };
                    while (i + 1 < inFolder.Count && inFolder[i + 1] is var n && n.WholeVerse && last.WholeVerse && n.Ref.Book == last.Ref.Book && n.Ref.Chapter == last.Ref.Chapter
                           && n.Ref.Verse == last.LastVerse + 1 && n.Note == first.Note && n.Color == first.Color && n.FolderId == first.FolderId)
                    { run.Add(n); last = n; i++; }
                    var rowId = foreign ? ForeignRowBase + vnum * ForeignSpan + first.Id : first.Id;
                    rows.Add(new Entry(first.Ref, MarkRowDisplay(run, detail, cores), rowId, first.Color));
                }
            }
        }
        _marksListLoading = true;   // rebuilding the list must not "pick" an entry again (that once jumped to a mark in another book)
        try { MarksList.SelectedItem = null; MarksList.ItemsSource = rows; MarksList.SelectedItem = null; }
        finally { _marksListLoading = false; }
        MarksEmpty.IsVisible = total == 0;
    }

    int BookOrder(string id) { var i = _books.ToList().FindIndex(b => b.Id == id); return i < 0 ? int.MaxValue : i; }

    /// <summary>The text of a marked verse with its most important parts in « », which the list shows highlighted.</summary>
    static string Emphasize(string text, IEnumerable<Mark> cores)
    {
        var extra = new List<string>();
        foreach (var c in cores.OrderByDescending(c => c.Text.Length))
        {
            var core = c.Text.Trim();
            if (core.Length == 0) continue;
            var at = text.IndexOf(core, StringComparison.OrdinalIgnoreCase);
            if (at < 0) { extra.Add("«" + core + "»"); continue; }
            var before = text[..at];
            if (before.Count(ch => ch == '«') > before.Count(ch => ch == '»')) continue;   // already inside another emphasized part
            text = before + "«" + text[at..(at + core.Length)] + "»" + text[(at + core.Length)..];
        }
        return extra.Count == 0 ? text : text + "  ▸ " + string.Join(" … ", extra);
    }

    static string CutKeepingMarks(string text, int max)
    {
        if (text.Length <= max) return text;
        var cut = text[..max].TrimEnd() + "…";
        return cut.Count(c => c == '«') > cut.Count(c => c == '»') ? cut[..^1] + "»…" : cut;
    }

    string MarkRowDisplay(List<Mark> run, bool detail, IReadOnlyList<Mark> cores)
    {
        var m = run[0]; var last = run[^1];
        var label = run.Count == 1 ? MarkLabel(m) : $"{BookName(m.Ref.Book)} {m.Ref.Chapter},{m.Ref.Verse}–{last.LastVerse}";
        if (!detail) return label;
        var lines = new List<string>();
        if (ColorName(m.Color) is { Length: > 0 } meaning) lines.Add("● " + meaning);
        if (!m.WholeVerse) lines.Add(Loc.Quote(Shorten(m.Text, m.MultiVerse ? 220 : 100)));
        else
        {
            var text = string.Join(" ", run.Select(r =>
                Emphasize(r.Text, cores.Where(c => c.Ref.Book == r.Ref.Book && c.Ref.Chapter == r.Ref.Chapter && c.Ref.Verse >= r.Ref.Verse && c.LastVerse <= r.LastVerse))));
            if (text.Length > 0) lines.Add(CutKeepingMarks(text, run.Count > 1 || m.MultiVerse ? 260 : 160));
        }
        if (m.Note.Length > 0) lines.Add("✎ " + Shorten(m.Note.ReplaceLineEndings(" "), 100));
        if (m.Tags.Count > 0) lines.Add(string.Join("  ", m.Tags.Select(t => "#" + t)));
        return lines.Count == 0 ? label : label + "\n" + string.Join("\n", lines);
    }

    string MarkDisplay(Mark m)
    {
        var lines = new List<string>();
        if (!m.WholeVerse) lines.Add(Loc.Quote(Shorten(m.Text, 100)));
        else if (m.Text.Length > 0) lines.Add(Shorten(m.Text, 100));
        if (m.Note.Length > 0) lines.Add("✎ " + Shorten(m.Note.ReplaceLineEndings(" "), 100));
        if (m.Tags.Count > 0) lines.Add(string.Join("  ", m.Tags.Select(t => "#" + t)));
        return lines.Count == 0 ? MarkLabel(m) : MarkLabel(m) + "\n" + string.Join("\n", lines);
    }

    async Task OnMarksListPickedAsync()
    {
        if (_marksListLoading || MarksList.SelectedItem is not Entry { Id: long id } e) return;
        if (id >= ForeignRowBase)   // a mark of another vault: show the verse; changing it needs that vault
        {
            var vname = VaultNames().ElementAtOrDefault((int)((id - ForeignRowBase) / ForeignSpan) - 1) ?? "";
            MarksList.SelectedItem = null;
            await GoToAsync(e.Ref.Book, e.Ref.Chapter, e.Ref.Verse);
            StatusText.Text = L("Diese Markierung gehört zum Vault „{0}“. Zum Bearbeiten den Vault wechseln.", vname);
            return;
        }
        if (id >= DocRowBase)   // document row: open it
        {
            MarksList.SelectedItem = null;
            if (_vault.GetDocuments().FirstOrDefault(d => d.Id == id - DocRowBase) is { } doc) { SideTabs.SelectedIndex = KnowledgeTab; OpenDocument(doc); }
            return;
        }
        if (id < 0)   // folder or vault row: fold in or out
        {
            var key = -1 - id; _foldersTouched = true;
            if (!_openFolders.Remove(key)) _openFolders.Add(key);
            ApplyCollectionFilter(); return;
        }
        await GoToAsync(e.Ref.Book, e.Ref.Chapter, e.Ref.Verse);
        LoadEditor(id);
    }
}
