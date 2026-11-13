using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using BibleStudy.Core.Diagram;

namespace BibleStudy.App;

public partial class MainWindow
{
    /// <summary>
    /// The sentence diagram of one sentence at a time with its controls: previous / next / jump, original text or meanings, zoom, save as picture.
    /// It is used in the side tab and, bigger, in its own window.
    /// </summary>
    sealed class SentenceViewer : UserControl
    {
        readonly MainWindow _w;
        readonly ScaleTransform _scale = new(1, 1);
        readonly StackPanel _head = new() { Spacing = 6 };
        readonly Border _stage = new() { MinHeight = 120 };
        readonly TextBlock _text = new() { TextWrapping = TextWrapping.Wrap, Margin = new(0, 2, 0, 4) };
        List<DSentence> _list = [];
        int _index;
        bool _meaning;
        Canvas? _canvas;
        DScene? _scene;
        ScrollViewer? _scroll;
        bool _userZoomed;

        public SentenceViewer(MainWindow w)
        {
            _w = w;
            Content = new DockPanel { LastChildFill = true, Children = { Docked(_head, Avalonia.Controls.Dock.Top), _stage } };
        }

        static Control Docked(Control c, Dock d) { DockPanel.SetDock(c, d); return c; }

        public int Index => _index;
        public DSentence? Current => _index >= 0 && _index < _list.Count ? _list[_index] : null;

        public void SetSentences(List<DSentence> list, int index)
        {
            _list = list; _index = Math.Clamp(index, 0, Math.Max(0, list.Count - 1)); _userZoomed = false;
            Render();
        }

        public void Go(int index)
        {
            if (_list.Count == 0) return;
            _index = Math.Clamp(index, 0, _list.Count - 1); _userZoomed = false;
            Render();
        }

        // ---- the page ------------------------------------------------------------------------------------------------------

        void Render()
        {
            _head.Children.Clear();
            _stage.Child = null; _canvas = null; _scene = null; _scroll = null;
            if (_list.Count == 0) return;
            var s = _list[_index];

            // navigation
            var prev = new Button { Content = "◀", Classes = { "tool" }, Width = 32, Height = 30, IsEnabled = _index > 0 };
            var next = new Button { Content = "▶", Classes = { "tool" }, Width = 32, Height = 30, IsEnabled = _index < _list.Count - 1 };
            prev.Click += (_, _) => Go(_index - 1);
            next.Click += (_, _) => Go(_index + 1);
            var jump = new ComboBox { MinWidth = 150, MaxWidth = 340, ItemsSource = _list.Select(Describe).ToList(), SelectedIndex = _index };
            jump.SelectionChanged += (_, _) => { if (jump.SelectedIndex >= 0 && jump.SelectedIndex != _index) Go(jump.SelectedIndex); };
            var nav = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { prev, next, jump } };
            _head.Children.Add(nav);

            // what is written on the lines: original words or their meanings
            var modes = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            foreach (var (meaning, label) in new[] { (false, L("Urtext")), (true, L("Bedeutung")) })
            {
                var b = new Button { Classes = { "seg" }, Content = label };
                b.Classes.Set("active", meaning == _meaning);
                b.Click += (_, _) => { _meaning = meaning; Render(); };
                modes.Children.Add(b);
            }
            var minus = new Button { Content = "−", Classes = { "tool" }, Width = 32, Height = 30 };
            var plus = new Button { Content = "+", Classes = { "tool" }, Width = 32, Height = 30 };
            var fit = new Button { Content = L("Fit"), Classes = { "tool" }, Width = double.NaN, Padding = new(10, 0), Height = 30 };
            minus.Click += (_, _) => Zoom(1 / 1.2);
            plus.Click += (_, _) => Zoom(1.2);
            fit.Click += (_, _) => { _userZoomed = false; Fit(); };
            var png = new Button { Content = L("Als Bild speichern"), Classes = { "tool" }, Width = double.NaN, Padding = new(10, 0), Height = 30 };
            var svg = new Button { Content = L("Als SVG speichern"), Classes = { "tool" }, Width = double.NaN, Padding = new(10, 0), Height = 30 };
            png.Click += (_, _) => { if (_canvas is not null) _w.ExportControlImage(_canvas, L("Satzdiagramm") + "_" + Title(s)); };
            svg.Click += (_, _) => SaveSvg(s);
            _head.Children.Add(new WrapPanel { Children = { Spaced(modes), Spaced(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { minus, plus, fit } }), Spaced(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { png, svg } }) } });

            // the sentence itself in reading order
            var hebrew = s.Hebrew;
            _text.Text = string.Join(" ", s.Words.Select(x => hebrew ? _w.Heb(x.Text) : x.Text));
            _text.FontFamily = hebrew ? HebrewFont : _w.ScriptureFont;
            _text.FontSize = hebrew ? 20 : 17;
            _text.FlowDirection = hebrew ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
            _head.Children.Add(_text);
            _head.Children.Add(new TextBlock { Text = L("Strg + Mausrad: zoomen. Ein Klick auf ein Wort schlägt es im Strong-Wörterbuch nach."), Classes = { "caption" }, TextWrapping = TextWrapping.Wrap });

            DrawDiagram(s);
            Dispatcher.UIThread.Post(() => { if (!_userZoomed) Fit(); }, DispatcherPriority.Loaded);
        }

        static Control Spaced(Control c) { c.Margin = new(0, 0, 14, 4); return c; }

        string Describe(DSentence s)
        {
            var verses = s.FirstVerse == s.LastVerse ? s.FirstVerse.ToString() : $"{s.FirstVerse}–{s.LastVerse}";
            var start = string.Join(" ", s.Words.Where(x => x.Chapter == s.Chapter).Take(4).Select(x => s.Hebrew ? _w.Heb(x.Text) : x.Text));
            var verseWord = L("V.");
            return $"{s.Index} · {verseWord} {verses} · {start} …";
        }

        string Title(DSentence s) => $"{_w.BookName(_w._curBook)} {s.Chapter},{s.FirstVerse}" + (s.LastVerse != s.FirstVerse ? "-" + s.LastVerse : "");

        // ---- drawing -------------------------------------------------------------------------------------------------------

        string WordLabel(DWord w, bool hebrew) => hebrew ? _w.Heb(w.Text) : w.Text;

        string Main(DWord w, bool hebrew) => _meaning ? (w.Gloss.Length > 0 ? _w.Mean(w.Gloss) : WordLabel(w, hebrew)) : WordLabel(w, hebrew);

        string? Small(DWord w, bool hebrew) => _meaning ? WordLabel(w, hebrew) : (w.Gloss.Length > 0 ? _w.Mean(w.Gloss) : null);

        static string Implied(string key) => key switch
        {
            "first/singular" => L("ich"), "second/singular" => L("du"), "third/singular" => L("er/sie/es"),
            "first/plural" => L("wir"), "second/plural" => L("ihr"), "third/plural" => L("sie"),
            _ => L("er/sie/es"),
        };

        void DrawDiagram(DSentence s)
        {
            var hebrew = s.Hebrew;
            var face = new Typeface(hebrew ? HebrewFont : _w.ScriptureFont);
            var cache = new Dictionary<(string, double), double>();
            double Measure(string t, double size)
            {
                if (cache.TryGetValue((t, size), out var v)) return v;
                return cache[(t, size)] = new FormattedText(t, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, face, size, Brushes.Black).Width;
            }
            var layout = new DiagramLayout(new DiagramOptions(x => Main(x, hebrew), x => Small(x, hebrew), Implied), Measure);
            _scene = layout.Layout(s);

            var dark = _w.ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark;
            var ink = (_w.Res("BsText") as ISolidColorBrush)?.Color ?? (dark ? Colors.Gainsboro : Colors.Black);
            var canvas = SceneCanvas.Build(_scene, hebrew ? HebrewFont : _w.ScriptureFont, _w.Res("BsBackground"), ink, dark,
                word =>
                {
                    var tip = $"{WordLabel(word, hebrew)}  {word.Lemma}  {word.Strong}".Trim();
                    return word.Gloss.Length > 0 ? tip + "\n" + _w.Mean(word.Gloss) : tip;
                },
                async word => { _w.StrongSearchBox.Text = word.Strong; _w.RevealSideTab(StrongTab); await _w.SearchStrongAsync(); });
            _canvas = canvas;

            var zoomHost = new LayoutTransformControl { Child = canvas, LayoutTransform = _scale };
            _scroll = new ScrollViewer { Content = zoomHost, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, MinHeight = 140 };
            _scroll.PointerWheelChanged += (_, e) =>
            {
                if (!e.KeyModifiers.HasFlag(KeyModifiers.Control)) return;
                Zoom(e.Delta.Y > 0 ? 1.1 : 1 / 1.1); e.Handled = true;
            };
            _stage.Child = _scroll;

            // the meanings arrive a moment later when they still have to be translated
            var glosses = s.Words.Select(x => x.Gloss).Where(g => g.Length > 0).Distinct().ToList();
            if (glosses.Any(_w._meanings.IsMissing)) _ = _w.WarmMeaningsAsync(glosses, () => { if (Current == s) { var keep = _userZoomed; Render(); _userZoomed = keep; } });
        }

        void Zoom(double factor)
        {
            _userZoomed = true;
            _scale.ScaleX = _scale.ScaleY = Math.Clamp(_scale.ScaleX * factor, 0.25, 3);
        }

        void Fit()
        {
            if (_scene is null || _scroll is null) return;
            var avail = _scroll.Bounds.Width - 24;
            if (avail < 100) avail = Math.Max(300, (_w.SideTabs.Bounds.Width) - 40);
            _scale.ScaleX = _scale.ScaleY = Math.Clamp(avail / _scene.Width, 0.25, 1.15);
        }

        void SaveSvg(DSentence s)
        {
            if (_scene is null) return;
            try
            {
                var svg = DiagramSvg.Write(_scene, false, Title(s));
                var name = string.Concat((L("Satzdiagramm") + "_" + Title(s)).Select(ch => char.IsLetterOrDigit(ch) ? ch : '_'));
                Directory.CreateDirectory(ExportFolder);
                var path = System.IO.Path.Combine(ExportFolder, name + ".svg");
                File.WriteAllText(path, svg, new System.Text.UTF8Encoding(false));
                _w.StatusText.Text = L("Bild gespeichert: {0}", path);
                try { Shell.Open(path); } catch { /* the path is in the status line */ }
            }
            catch (Exception ex) { _w.StatusText.Text = L("Fehler: ") + ex.Message; }
        }
    }
}
