using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using BibleStudy.Core.Models;
using BibleStudy.Core.Storage;

namespace BibleStudy.App;

/// <summary>
/// Sermon editor: title, text, core message, sections with notes, verses and minutes. A sermon is saved as a normal document
/// (tag "Predigt", structure kept in a comment at its end), so it is searchable, linkable and part of the Obsidian export.
/// Export writes a printable HTML handout.
/// </summary>
public partial class MainWindow
{
    const int SermonTab = 19;
    const string SermonMark = "<!--sermon:";

    sealed class SermonSection { public string Heading { get; set; } = ""; public string Notes { get; set; } = ""; public int Minutes { get; set; } = 5; public List<string> Verses { get; set; } = []; }
    sealed class SermonData
    {
        public string Title { get; set; } = "";
        public string Passage { get; set; } = "";
        public string Message { get; set; } = "";
        public int Minutes { get; set; } = 30;
        public List<SermonSection> Sections { get; set; } = [];
    }

    SermonData _sermon = NewSermon();
    long? _sermonDocId;
    string? _sermonVault;
    bool _sermonLoading;

    static SermonData NewSermon() => new()
    {
        Sections =
        [
            new() { Heading = "Einleitung", Minutes = 4 }, new() { Heading = "Punkt 1", Minutes = 8 }, new() { Heading = "Punkt 2", Minutes = 8 },
            new() { Heading = "Punkt 3", Minutes = 6 }, new() { Heading = "Schluss und Aufruf", Minutes = 4 },
        ],
    };

    static readonly JsonSerializerOptions SermonJson = new() { WriteIndented = false };

    string VerseLabel(string key)
    {
        var p = key.Split('|');
        return p.Length == 3 ? $"{BookName(p[0])} {p[1]},{p[2]}" : key;
    }

    void BuildSermonUi()
    {
        if (_sermonVault != _vault.Name) { _sermonVault = _vault.Name; _sermon = NewSermon(); _sermonDocId = null; }
        SermonPanel.Children.Clear();

        // choose / create
        var saved = _vault.GetDocuments(tag: "Predigt").Where(d => d.Body.Contains(SermonMark)).ToList();
        var pick = new ComboBox { MinWidth = 220, PlaceholderText = saved.Count == 0 ? "Noch keine gespeicherte Predigt" : "Gespeicherte Predigt öffnen …", ItemsSource = saved.Select(d => d.Title).ToList() };
        pick.SelectionChanged += (_, _) =>
        {
            if (_sermonLoading || pick.SelectedIndex < 0) return;
            LoadSermon(saved[pick.SelectedIndex]);
        };
        var neu = new Button { Content = "Neue Predigt" };
        neu.Click += (_, _) => { _sermon = NewSermon(); _sermonDocId = null; BuildSermonUi(); };
        SermonPanel.Children.Add(new TextBlock { Text = "PREDIGT-EDITOR", Classes = { "eyebrow" } });
        SermonPanel.Children.Add(new WrapPanel { Children = { new Border { Classes = { "pill" }, Child = pick, Margin = new(0, 0, 8, 4) }, neu } });

        // head fields
        TextBox Field(string label, string value, Action<string> set, bool multi = false, string? hint = null)
        {
            var box = new TextBox { Text = value, AcceptsReturn = multi, TextWrapping = multi ? TextWrapping.Wrap : TextWrapping.NoWrap, MinHeight = multi ? 60 : 0, Watermark = hint };
            box.TextChanged += (_, _) => set(box.Text ?? "");
            SermonPanel.Children.Add(new TextBlock { Text = label, Classes = { "caption" }, Margin = new(0, 4, 0, 0) });
            SermonPanel.Children.Add(box);
            return box;
        }
        Field("Titel", _sermon.Title, v => _sermon.Title = v, hint: "z. B. Treue im Exil");
        var passage = Field("Predigttext", _sermon.Passage, v => _sermon.Passage = v, hint: "z. B. Daniel 1,1–21");
        var useCurrent = new Button { Content = "Aktuellen Vers als Predigttext", HorizontalAlignment = HorizontalAlignment.Left };
        useCurrent.Click += (_, _) => passage.Text = _selected is int v ? $"{BookName(_curBook)} {_curChapter},{v}" : $"{BookName(_curBook)} {_curChapter}";
        SermonPanel.Children.Add(useCurrent);
        Field("Kernaussage (ein Satz)", _sermon.Message, v => _sermon.Message = v, multi: true, hint: "Was sollen alle mitnehmen?");

        var total = new TextBlock { Classes = { "caption" }, TextWrapping = TextWrapping.Wrap };
        void UpdateTotal()
        {
            var sum = _sermon.Sections.Sum(s => s.Minutes);
            total.Text = $"Geplant: {sum} min von {_sermon.Minutes} min" + (sum > _sermon.Minutes ? $" – {sum - _sermon.Minutes} min zu viel" : sum < _sermon.Minutes ? $" – {_sermon.Minutes - sum} min frei" : " – passt genau");
        }
        var goal = new NumericUpDown { Minimum = 5, Maximum = 180, Value = _sermon.Minutes, FormatString = "0", Width = 120, Increment = 5 };
        goal.ValueChanged += (_, _) => { _sermon.Minutes = (int)(goal.Value ?? 30); UpdateTotal(); };
        SermonPanel.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { new TextBlock { Text = "Zielzeit (min)", VerticalAlignment = VerticalAlignment.Center }, goal } });
        SermonPanel.Children.Add(total);

        // sections
        SermonPanel.Children.Add(new TextBlock { Text = "GLIEDERUNG", Classes = { "eyebrow" }, Margin = new(0, 8, 0, 0) });
        for (var i = 0; i < _sermon.Sections.Count; i++)
        {
            var sec = _sermon.Sections[i]; var idx = i;
            var head = new TextBox { Text = sec.Heading, Watermark = "Überschrift", FontWeight = FontWeight.SemiBold };
            head.TextChanged += (_, _) => sec.Heading = head.Text ?? "";
            var min = new NumericUpDown { Minimum = 0, Maximum = 120, Value = sec.Minutes, FormatString = "0", Width = 100 };
            ToolTip.SetTip(min, "Minuten für diesen Teil");
            min.ValueChanged += (_, _) => { sec.Minutes = (int)(min.Value ?? 0); UpdateTotal(); };
            var notes = new TextBox { Text = sec.Notes, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 80, Watermark = "Gedanken, Erklärung, Beispiele …" };
            notes.TextChanged += (_, _) => sec.Notes = notes.Text ?? "";

            var verses = new WrapPanel();
            void DrawVerses()
            {
                verses.Children.Clear();
                foreach (var key in sec.Verses.ToList())
                {
                    var k = key; var p = k.Split('|');
                    var chip = FilterChip(VerseLabel(k), false, null, async () => { if (p.Length == 3) await GoToAsync(p[0], int.Parse(p[1]), int.Parse(p[2])); }, "Springt zur Stelle");
                    var del = new Button { Classes = { "tool" }, Width = 22, Height = 24, Content = new Icon { Data = Geometry.Parse("M18 6L6 18 M6 6l12 12"), IconSize = 10 }, Margin = new(0, 0, 6, 4) };
                    del.Click += (_, _) => { sec.Verses.Remove(k); DrawVerses(); };
                    verses.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Children = { chip, del } });
                }
            }
            DrawVerses();

            var addVerse = new Button { Content = "＋ Vers" };
            ToolTip.SetTip(addVerse, "Den angeklickten Vers dieser Stelle zuordnen");
            addVerse.Click += (_, _) => { if (_selected is int v) { var key = $"{_curBook}|{_curChapter}|{v}"; if (!sec.Verses.Contains(key)) sec.Verses.Add(key); DrawVerses(); } else StatusText.Text = "Klicke zuerst im Bibeltext einen Vers an."; };
            var addText = new Button { Content = "＋ Verstext zitieren" };
            ToolTip.SetTip(addText, "Fügt den angeklickten Vers als Zitat in die Notizen ein");
            addText.Click += async (_, _) =>
            {
                if (_selected is not int v || TranslationBox.SelectedItem is not TranslationInfo t) { StatusText.Text = "Klicke zuerst im Bibeltext einen Vers an."; return; }
                var ch = await _provider.GetChapterAsync(t.Id, _curBook, _curChapter);
                var text = ch.Verses.FirstOrDefault(x => x.Number == v)?.Text.Trim() ?? "";
                var quote = $"„{text}“ ({BookName(_curBook)} {_curChapter},{v}, {t.ShortName})";
                notes.Text = string.IsNullOrWhiteSpace(notes.Text) ? quote : notes.Text.TrimEnd() + "\n\n" + quote;
            };
            var up = new Button { Content = "▲", Classes = { "tool" }, Width = 30 }; var down = new Button { Content = "▼", Classes = { "tool" }, Width = 30 };
            var remove = new Button { Content = "Teil löschen", Classes = { "tool" }, Width = double.NaN, Padding = new(10, 0) };
            up.Click += (_, _) => { if (idx > 0) { (_sermon.Sections[idx - 1], _sermon.Sections[idx]) = (_sermon.Sections[idx], _sermon.Sections[idx - 1]); BuildSermonUi(); } };
            down.Click += (_, _) => { if (idx < _sermon.Sections.Count - 1) { (_sermon.Sections[idx + 1], _sermon.Sections[idx]) = (_sermon.Sections[idx], _sermon.Sections[idx + 1]); BuildSermonUi(); } };
            remove.Click += (_, _) => { _sermon.Sections.RemoveAt(idx); BuildSermonUi(); };

            var top = new DockPanel();
            var tools = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, Children = { min, up, down } };
            DockPanel.SetDock(tools, Dock.Right); top.Children.Add(tools); top.Children.Add(head);
            SermonPanel.Children.Add(new Border
            {
                Classes = { "card" },
                Child = new StackPanel { Spacing = 6, Children = { top, notes, verses, new WrapPanel { Children = { addVerse, addText, remove } } } },
            });
        }
        var addSection = new Button { Content = "＋ Teil hinzufügen", HorizontalAlignment = HorizontalAlignment.Left };
        addSection.Click += (_, _) => { _sermon.Sections.Add(new SermonSection { Heading = $"Punkt {_sermon.Sections.Count}", Minutes = 5 }); BuildSermonUi(); };
        SermonPanel.Children.Add(addSection);
        UpdateTotal();

        // save / export
        var save = new Button { Content = "Speichern", Classes = { "primary" } };
        save.Click += (_, _) => SaveSermon();
        var export = new Button { Content = "Als Handout (HTML) exportieren" };
        export.Click += (_, _) => ExportSermon();
        SermonPanel.Children.Add(new WrapPanel { Margin = new(0, 8, 0, 0), Children = { save, export } });
        SermonPanel.Children.Add(new TextBlock { Text = "Die Predigt wird als Dokument mit dem Tag „Predigt“ im aktuellen Vault gespeichert und ist dort auch durchsuchbar und verlinkbar.", Classes = { "caption" }, TextWrapping = TextWrapping.Wrap });
    }

    void LoadSermon(Document d)
    {
        var i = d.Body.LastIndexOf(SermonMark, StringComparison.Ordinal);
        if (i < 0) return;
        try
        {
            var json = d.Body[(i + SermonMark.Length)..];
            json = json[..json.IndexOf("-->", StringComparison.Ordinal)];
            _sermon = JsonSerializer.Deserialize<SermonData>(json, SermonJson) ?? NewSermon();
            _sermonDocId = d.Id;
            _sermonLoading = true; BuildSermonUi(); _sermonLoading = false;
            StatusText.Text = $"Predigt „{_sermon.Title}“ geöffnet.";
        }
        catch (Exception ex) { StatusText.Text = "Predigt nicht lesbar: " + ex.Message; }
    }

    string SermonMarkdown()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# {(_sermon.Title.Length > 0 ? _sermon.Title : "Predigt")}").AppendLine();
        if (_sermon.Passage.Length > 0) sb.AppendLine($"**Predigttext:** {_sermon.Passage}  ");
        sb.AppendLine($"**Zielzeit:** {_sermon.Minutes} min").AppendLine();
        if (_sermon.Message.Length > 0) sb.AppendLine($"> {_sermon.Message.ReplaceLineEndings(" ")}").AppendLine();
        foreach (var s in _sermon.Sections)
        {
            sb.AppendLine($"## {s.Heading} ({s.Minutes} min)").AppendLine();
            if (s.Verses.Count > 0) sb.AppendLine("Verse: " + string.Join(", ", s.Verses.Select(VerseLabel))).AppendLine();
            if (s.Notes.Length > 0) sb.AppendLine(s.Notes).AppendLine();
        }
        sb.AppendLine(SermonMark + JsonSerializer.Serialize(_sermon, SermonJson) + "-->");
        return sb.ToString();
    }

    void SaveSermon()
    {
        var title = "Predigt: " + (_sermon.Title.Length > 0 ? _sermon.Title : "ohne Titel");
        var body = SermonMarkdown();
        var tags = new[] { "Predigt" };
        if (_sermonDocId is long id && _vault.GetDocuments().Any(d => d.Id == id)) _vault.UpdateDocument(id, title, body, null, false, tags);
        else _sermonDocId = _vault.AddDocument(title, body, null, false, tags);
        RefreshDocs();
        StatusText.Text = $"Gespeichert: {title}";
        BuildSermonUi();
    }

    void ExportSermon()
    {
        static string H(string s) => WebUtility.HtmlEncode(s);
        var sb = new StringBuilder("<!doctype html><html lang=\"de\"><meta charset=\"utf-8\"><title>" + H(_sermon.Title) + "</title><style>body{font-family:Georgia,serif;max-width:720px;margin:40px auto;line-height:1.55;color:#222}h1{margin-bottom:0}h2{border-bottom:1px solid #ccc;padding-bottom:4px;margin-top:28px}.meta{color:#666}blockquote{border-left:4px solid #c9a227;margin:16px 0;padding:4px 14px;background:#faf6e8}.v{color:#555;font-style:italic}small{color:#888}</style>");
        sb.Append($"<h1>{H(_sermon.Title)}</h1><p class=\"meta\">{H(_sermon.Passage)} · {_sermon.Minutes} min</p>");
        if (_sermon.Message.Length > 0) sb.Append($"<blockquote>{H(_sermon.Message)}</blockquote>");
        foreach (var s in _sermon.Sections)
        {
            sb.Append($"<h2>{H(s.Heading)} <small>({s.Minutes} min)</small></h2>");
            if (s.Verses.Count > 0) sb.Append($"<p class=\"v\">{H(string.Join(" · ", s.Verses.Select(VerseLabel)))}</p>");
            foreach (var para in s.Notes.Split("\n\n", StringSplitOptions.RemoveEmptyEntries)) sb.Append($"<p>{H(para).Replace("\n", "<br>")}</p>");
        }
        sb.Append("</html>");
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "BibleStudy", "Export");
        Directory.CreateDirectory(dir);
        var name = string.Concat((_sermon.Title.Length > 0 ? _sermon.Title : "Predigt").Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        var path = Path.Combine(dir, name + ".html");
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        StatusText.Text = $"Handout gespeichert: {path}";
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); } catch { /* no default browser: the path is in the status line */ }
    }
}
