using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using BibleStudy.Core.Storage;

namespace BibleStudy.App;

/// <summary>
/// The documents area made simple: one "New" menu instead of many buttons, categories as colored chips (a document has at most one),
/// and the rarely needed things (tags, folder, template, connections) folded away. A category is stored on the document as the tag
/// "kat:Name"; a document whose plain tag equals a category name (e.g. sermons: "Predigt") counts as in it too.
/// </summary>
public partial class MainWindow
{
    const string CategoryPrefix = "kat:";
    static readonly (string Hex, string Dot)[] CategoryColors =
    [
        ("#E5484D", "🔴"), ("#F76B15", "🟠"), ("#F5C518", "🟡"), ("#30A46C", "🟢"), ("#3E63DD", "🔵"), ("#8E4EC6", "🟣"), ("#A87B4F", "🟤"),
    ];

    string? _docCategory;   // null = all, "" = without category, otherwise the category name

    readonly MenuItem _miOpenFile = new(), _miFromTemplate = new(), _miDelete = new();

    static bool IsCategoryTag(string tag) => tag.StartsWith(CategoryPrefix, StringComparison.OrdinalIgnoreCase);

    List<DocCategory> Categories
    {
        get
        {
            if (!_settings.CategoriesSeeded)
            {
                _settings.CategoriesSeeded = true;
                if (_settings.Categories.Count == 0)
                    _settings.Categories =
                    [
                        new() { Name = L("Predigt"), Color = 5 }, new() { Name = L("Wortstudie"), Color = 4 }, new() { Name = L("Personenstudie"), Color = 1 },
                        new() { Name = L("Themenstudie"), Color = 3 }, new() { Name = L("Gebet"), Color = 2 }, new() { Name = L("Frage"), Color = 0 },
                    ];
                SaveSettings();
            }
            return _settings.Categories;
        }
    }

    static bool InCategory(Document d, string name) =>
        d.Tags.Any(t => string.Equals(t, CategoryPrefix + name, StringComparison.OrdinalIgnoreCase) || string.Equals(t, name, StringComparison.OrdinalIgnoreCase));

    DocCategory? CategoryOf(Document d)
    {
        // an explicit assignment wins over a plain tag that happens to carry a category's name
        var explicitTag = d.Tags.FirstOrDefault(IsCategoryTag);
        if (explicitTag is not null) return Categories.FirstOrDefault(c => string.Equals(CategoryPrefix + c.Name, explicitTag, StringComparison.OrdinalIgnoreCase));
        return Categories.FirstOrDefault(c => InCategory(d, c.Name));
    }

    // ---- menus ---------------------------------------------------------------------------------------------------------

    void BuildDocMenus()
    {
        // "New" collects everything that creates a document
        var menu = new MenuFlyout();
        var blank = new MenuItem { Header = L("Leeres Dokument") };
        blank.Click += (_, _) => OpenDocument(new Document(0, "", "", null, false, DateTime.Now, _docCategory is { Length: > 0 } c ? [CategoryPrefix + c] : []));
        var fromVerse = new MenuItem { Header = L("Aus dem angeklickten Vers") };
        fromVerse.Click += (_, _) => NewDocumentFromVerse();
        var templates = new MenuItem { Header = L("Aus Studienvorlage") };
        foreach (var t in StudyTemplates)
        {
            var item = new MenuItem { Header = t.Title };
            item.Click += (_, _) => NewDocumentFromTemplate(t);
            templates.Items.Add(item);
        }
        var file = new MenuItem { Header = L("Datei verknüpfen …") };
        file.Click += async (_, _) => await LinkFileAsync();
        menu.Items.Add(blank); menu.Items.Add(fromVerse); menu.Items.Add(templates); menu.Items.Add(new Separator()); menu.Items.Add(file);
        DocNewBtn.Flyout = menu;

        // "more" in the editor
        var more = new MenuFlyout();
        var print = new MenuItem { Header = L("Drucken / PDF") };
        print.Click += (_, _) => PrintDocument();
        _miOpenFile.Header = L("Datei öffnen"); _miOpenFile.Click += (_, _) => OpenLinkedFile();
        _miFromTemplate.Header = L("Neu aus dieser Vorlage");
        _miFromTemplate.Click += (_, _) => { if (_doc is { } d) OpenDocument(new Document(0, L("Neu: ") + d.Title, d.Body, null, false, DateTime.Now, d.Tags)); };
        _miDelete.Header = L("Löschen");
        _miDelete.Click += (_, _) => { if (_doc is { Id: > 0 } d) { _vault.DeleteDocument(d.Id); CloseDocument(); } };
        more.Items.Add(print); more.Items.Add(_miOpenFile); more.Items.Add(_miFromTemplate); more.Items.Add(new Separator()); more.Items.Add(_miDelete);
        DocMoreBtn.Flyout = more;
    }

    // ---- category chips ------------------------------------------------------------------------------------------------

    void BuildCategoryChips()
    {
        DocCategoryChips.Children.Clear();
        var docs = _vault.GetDocuments();
        Control Chip(string label, string? key, Color? color, int count)
        {
            var active = _docCategory == key;
            var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            if (color is { } c) content.Children.Add(new Border { Width = 10, Height = 10, CornerRadius = new(5), Background = new SolidColorBrush(c), VerticalAlignment = VerticalAlignment.Center });
            content.Children.Add(new TextBlock { Text = label + (count >= 0 ? $"  {count}" : ""), VerticalAlignment = VerticalAlignment.Center });
            var b = new Button { Classes = { "seg" }, Content = content, Margin = new(0, 0, 6, 6) };
            b.Classes.Set("active", active);
            b.Click += (_, _) => { _docCategory = active ? null : key; RefreshDocs(); };
            if (key is { Length: > 0 }) b.ContextMenu = CategoryMenu(key);
            if (key is not null)   // a document dragged onto the chip gets that category
                _dropTargets.Add(new DropTarget(b, d => SetCategory(d, key.Length == 0 ? null : Categories.FirstOrDefault(c => c.Name == key)), on => b.Classes.Set("active", on || active)));
            return b;
        }
        DocCategoryChips.Children.Add(Chip(L("Alle"), null, null, docs.Count));
        foreach (var c in Categories)
            DocCategoryChips.Children.Add(Chip(c.Name, c.Name, Color.Parse(CategoryColors[Math.Clamp(c.Color, 0, CategoryColors.Length - 1)].Hex), docs.Count(d => InCategory(d, c.Name))));
        DocCategoryChips.Children.Add(Chip(L("Ohne Kategorie"), "", null, docs.Count(d => CategoryOf(d) is null)));

        var add = new Button { Content = L("＋ Kategorie"), Classes = { "seg" }, Margin = new(0, 0, 6, 6) };
        var name = new TextBox { Watermark = L("Name der Kategorie"), Width = 200 };
        var color = 4;
        var swatches = new WrapPanel { Margin = new(0, 6, 0, 6), Width = 200 };
        void DrawSwatches()
        {
            swatches.Children.Clear();
            for (var i = 0; i < CategoryColors.Length; i++)
            {
                var idx = i;
                var s = new Button { Width = 26, Height = 26, Padding = new(0), Margin = new(0, 0, 6, 6), CornerRadius = new(13), Background = new SolidColorBrush(Color.Parse(CategoryColors[i].Hex)), BorderThickness = new(idx == color ? 3 : 0), BorderBrush = Res("BsText") ?? Brushes.White };
                s.Click += (_, _) => { color = idx; DrawSwatches(); };
                swatches.Children.Add(s);
            }
        }
        DrawSwatches();
        var create = new Button { Content = L("Anlegen"), Classes = { "primary" } };
        var fly = new Flyout { Content = new StackPanel { Spacing = 2, Children = { new TextBlock { Text = L("Neue Kategorie"), FontWeight = FontWeight.SemiBold, Margin = new(0, 0, 0, 6) }, name, swatches, create } } };
        create.Click += (_, _) =>
        {
            var n = (name.Text ?? "").Trim().Replace(":", "");
            if (n.Length == 0 || Categories.Any(c => string.Equals(c.Name, n, StringComparison.OrdinalIgnoreCase))) return;
            Categories.Add(new DocCategory { Name = n, Color = color }); SaveSettings();
            name.Text = ""; fly.Hide(); RefreshDocs();
        };
        add.Flyout = fly;
        DocCategoryChips.Children.Add(add);
    }

    ContextMenu CategoryMenu(string name)
    {
        var del = new MenuItem { Header = L("Kategorie löschen") };
        del.Click += (_, _) =>
        {
            Categories.RemoveAll(c => c.Name == name); SaveSettings();
            if (_docCategory == name) _docCategory = null;
            RefreshDocs();
        };
        return new ContextMenu { Items = { del } };
    }

    // ---- category in the editor ----------------------------------------------------------------------------------------

    void FillCategoryBox(Document d)
    {
        var items = new List<object> { L("(keine)") };
        items.AddRange(Categories.Select(c => (object)c));
        DocCategoryBox.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<object>((o, _) => new TextBlock { Text = o is DocCategory c ? CategoryColors[Math.Clamp(c.Color, 0, CategoryColors.Length - 1)].Dot + " " + c.Name : o.ToString() });
        DocCategoryBox.ItemsSource = items;
        var current = CategoryOf(d) ?? (d.Id == 0 && _docCategory is { Length: > 0 } pre ? Categories.FirstOrDefault(c => c.Name == pre) : null);
        DocCategoryBox.SelectedIndex = current is null ? 0 : Math.Max(0, Categories.IndexOf(current) + 1);
    }

    IEnumerable<string> SelectedCategoryTag() => DocCategoryBox.SelectedItem is DocCategory c ? [CategoryPrefix + c.Name] : [];
}
