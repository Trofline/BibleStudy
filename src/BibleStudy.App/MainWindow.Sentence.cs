using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using BibleStudy.Core.Diagram;
using BibleStudy.Core.Providers;

namespace BibleStudy.App;

/// <summary>
/// The sentence diagram tab: the sentence of the clicked verse as a classical sentence diagram (Reed–Kellogg), drawn from the syntax trees of the
/// Greek and Hebrew text. Also in a window of its own, for long sentences.
/// </summary>
public partial class MainWindow
{
    const int SentenceTab = 28;

    readonly Dictionary<(string Book, int Chapter), List<DSentence>> _sentenceCache = [];
    SentenceViewer? _sentenceViewer;
    (string Book, int Chapter) _sentenceShown;
    Window? _sentenceWindow;

    async Task<List<DSentence>> SentencesOfAsync(string book, int chapter)
    {
        if (_sentenceCache.TryGetValue((book, chapter), out var cached)) return cached;
        var list = await _syntax.GetSentencesAsync(book, chapter);
        if (_sentenceCache.Count > 12) _sentenceCache.Clear();
        return _sentenceCache[(book, chapter)] = list;
    }

    /// <summary>The index of the sentence that holds the verse (or the first one).</summary>
    static int SentenceAt(List<DSentence> list, int? verse)
    {
        if (verse is not int v) return 0;
        var i = list.FindIndex(s => v >= s.FirstVerse && v <= s.LastVerse);
        return i >= 0 ? i : Math.Max(0, list.FindLastIndex(s => s.FirstVerse <= v));
    }

    /// <summary>Builds the tab (once) and shows the sentence of the clicked verse; <paramref name="follow"/> = keep the sentence when the chapter has not changed.</summary>
    async Task ShowSentenceAsync(bool follow)
    {
        if (_sentenceViewer is null)
        {
            _sentenceViewer = new SentenceViewer(this);
            _sentenceShown = ("", 0);
        }
        var (book, chapter) = (_curBook, _curChapter);
        if (SentencePanel.Children.Count == 0 || SentencePanel.Children[0] is not TextBlock)
        {
            SentencePanel.Children.Clear();
            SentencePanel.Children.Add(new TextBlock { Text = L("SATZDIAGRAMM"), Classes = { "eyebrow" } });
            SentencePanel.Children.Add(_sentenceStatus);
            var big = new Button { Content = L("In eigenem Fenster öffnen"), HorizontalAlignment = HorizontalAlignment.Left, Padding = new(10, 0), Height = 30 };
            big.Click += async (_, _) => await OpenSentenceWindowAsync();
            SentencePanel.Children.Add(big);
            SentencePanel.Children.Add(_sentenceViewer);
        }
        if (book == "" || !SyntaxProvider.IsAvailable(book))
        {
            _sentenceViewer.SetSentences([], 0);
            _sentenceStatus.Text = L("Für dieses Buch gibt es keine Syntaxdaten.");
            return;
        }
        _sentenceStatus.Text = L("Syntaxdaten werden geladen …");
        List<DSentence> list;
        try { list = await SentencesOfAsync(book, chapter); }
        catch (Exception ex) { _sentenceStatus.Text = L("Syntaxdaten nicht verfügbar: ") + ex.Message; return; }
        if (_curBook != book || _curChapter != chapter) return;   // the reader moved on meanwhile
        if (list.Count == 0) { _sentenceViewer.SetSentences([], 0); _sentenceStatus.Text = L("Für dieses Kapitel gibt es keine Syntaxdaten."); return; }

        var same = _sentenceShown == (book, chapter);
        _sentenceShown = (book, chapter);
        var target = SentenceAt(list, _selected);
        if (follow && same && _selected is null) target = _sentenceViewer.Index;
        _sentenceStatus.Text = $"{Label(new BibleStudy.Core.Models.VerseRef(book, chapter, _selected ?? 1)).Split(':')[0]} · " + L("{0} Sätze", list.Count);
        _sentenceViewer.SetSentences(list, target);
        _sentenceViewer.Margin = new(0, 6, 0, 0);
        if (_sentenceAttribution is null)
        {
            _sentenceAttribution = new TextBlock { Text = SyntaxProvider.Attribution, Classes = { "caption" }, TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 0) };
            SentencePanel.Children.Add(BuildSentenceLegend());
            SentencePanel.Children.Add(_sentenceAttribution);
        }
    }

    readonly TextBlock _sentenceStatus = new() { Classes = { "caption" }, TextWrapping = TextWrapping.Wrap };
    TextBlock? _sentenceAttribution;

    /// <summary>The chapter or the clicked verse changed while the tab is open.</summary>
    void FollowSentence(bool chapterChanged) { if (TabActive(SentenceTab)) _ = ShowSentenceAsync(!chapterChanged); }

    Control BuildSentenceLegend()
    {
        static TextBlock Line(string text, string? color = null)
        {
            var t = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 12.5 };
            if (color is not null) t.Foreground = new SolidColorBrush(Color.Parse(color));
            return t;
        }
        var dark = ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark;
        string C(DTextKind k) => DiagramSvg.ColorOf(k, dark);
        var colors = new WrapPanel
        {
            Children =
            {
                Chip(L("Subjekt"), C(DTextKind.Subject)), Chip(L("Verb"), C(DTextKind.Verb)), Chip(L("Objekt"), C(DTextKind.Object)),
                Chip(L("Ergänzung"), C(DTextKind.Complement)), Chip(L("Präposition"), C(DTextKind.Prep)), Chip(L("Verbindung"), C(DTextKind.Conj)),
            },
        };
        var body = new StackPanel
        {
            Spacing = 4, Margin = new(0, 6, 0, 0),
            Children =
            {
                colors,
                Line(L("Waagrechte Linie: ein Satz. Der senkrechte Strich, der die Linie kreuzt, trennt Subjekt und Verb. Ein kurzer Strich trennt Verb und Objekt, ein schräger Strich Verb und Ergänzung (zum Beispiel „war“ + „Gott“).")),
                Line(L("Schräge Linien darunter: Wörter, die etwas näher bestimmen, zum Beispiel Artikel, Adjektive und Adverbien. Bei einer Präposition steht sie auf der schrägen Linie und ihr Objekt auf der waagrechten.")),
                Line(L("Gestrichelte Linien: Nebensätze und Verbindungen zwischen Sätzen, mit der Konjunktion darauf. Steht ein Nebensatz auf einem durchgezogenen Pfosten, ist er Subjekt, Objekt oder Ergänzung des Satzes darüber.")),
                Line(L("Grau und kursiv über einem Wort: seine Bedeutung (oder das Urtextwort). In Klammern: das Subjekt steckt nur in der Verbform.")),
                Line(L("Wie bei jedem Satzdiagramm geht die Wortstellung verloren; sie steht im Satztext über dem Bild. Hebräische Sätze laufen von rechts nach links.")),
            },
        };
        return new Expander { Header = L("So liest du das Diagramm"), Content = body, Margin = new(0, 8, 0, 0) };
    }

    static Control Chip(string text, string color) =>
        new Border { CornerRadius = new(8), Padding = new(8, 2), Margin = new(0, 0, 6, 4), Background = new SolidColorBrush(Color.Parse(color), 0.15), Child = new TextBlock { Text = text, FontSize = 12, FontWeight = FontWeight.SemiBold, Foreground = new SolidColorBrush(Color.Parse(color)) } };

    /// <summary>The diagram in a window of its own, as large as the screen allows.</summary>
    async Task OpenSentenceWindowAsync()
    {
        if (_selected is null && _curBook == "") return;
        List<DSentence> list;
        try { list = SyntaxProvider.IsAvailable(_curBook) ? await SentencesOfAsync(_curBook, _curChapter) : []; }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or System.Xml.XmlException)
        {
            StatusText.Text = L("Syntaxdaten nicht verfügbar: ") + ex.Message;
            return;
        }
        if (list.Count == 0) { StatusText.Text = L("Für dieses Buch gibt es keine Syntaxdaten."); return; }
        var viewer = new SentenceViewer(this);
        viewer.SetSentences(list, SentenceAt(list, _selected));
        _sentenceWindow?.Close();
        var win = new Window
        {
            Title = L("Satzdiagramm") + " · " + BookName(_curBook) + " " + _curChapter, Width = 1100, Height = 760, MinWidth = 420, MinHeight = 320,
            Background = Res("BsBackground"), Icon = Icon, WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = new Border { Padding = new(16), Child = viewer },
        };
        win.Closed += (_, _) => _sentenceWindow = null;
        _sentenceWindow = win;
        win.Show(this);
    }
}
