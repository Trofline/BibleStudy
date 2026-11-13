using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using BibleStudy.Core.Models;

namespace BibleStudy.App;

/// <summary>
/// The projector view (see <see cref="ProjectorWindow"/>): verses from the reader or pieces of the materials go to a second screen.
/// While it is open, a chip in the status bar shows what it shows and offers on, back, black and close.
/// </summary>
public partial class MainWindow
{
    ProjectorWindow? _projector;
    (VerseRef First, int End, string Translation)? _projectVerses;   // null while it shows a piece of the materials

    ProjectorWindow Projector()
    {
        if (_projector is { } p) return p;
        p = new ProjectorWindow(ScriptureFont) { Title = L("BibleStudy – Beamer") };
        p.Step += dir => _ = StepProjectorAsync(dir);
        p.Closed += (_, _) => { _projector = null; _projectVerses = null; UpdateProjectorChip(); };
        _projector = p;
        p.OpenBeside(this);
        StatusText.Text = Screens.All.Count > 1
            ? L("Die Beamer-Ansicht ist auf dem zweiten Bildschirm. Pfeiltasten: weiter und zurück, B: schwarz, Esc: beenden.")
            : L("Die Beamer-Ansicht ist ein eigenes Fenster: zum Beamer ziehen, F11 macht es bildschirmfüllend.");
        return p;
    }

    string CurrentTranslationId => (TranslationBox.SelectedItem as TranslationInfo)?.Id ?? "";

    /// <summary>Verses (first to end) of the translation that is read now, large on the projector.</summary>
    async Task ProjectVersesAsync(VerseRef first, int end = 0, string? translation = null)
    {
        translation ??= CurrentTranslationId;
        if (translation.Length == 0) return;
        try
        {
            if (await VersesToShowAsync(first, end, translation) is not { } shown) return;
            _projectVerses = (first, Math.Max(end, first.Verse), translation);
            Projector().Show(shown.Title, shown.Text);
            LiveMirror(shown.Title, shown.Text, new(first.Book, first.Chapter, first.Verse, Math.Max(end, first.Verse)));
            UpdateProjectorChip();
        }
        catch (Exception ex) { StatusText.Text = L("Der Vers lässt sich nicht zeigen: {0}", ex.Message); }
    }

    /// <summary>The verses (first to end) as the projector and the phones show them: a title with reference and translation, and the text.</summary>
    async Task<(string Title, string Text)?> VersesToShowAsync(VerseRef first, int end, string translation)
    {
        var ch = await _provider.GetChapterAsync(translation, first.Book, first.Chapter);
        end = Math.Max(end, first.Verse);
        var verses = ch.Verses.Where(v => v.Number >= first.Verse && v.Number <= end).ToList();
        if (verses.Count == 0) return null;
        var text = verses.Count == 1 ? verses[0].Text : string.Join("  ", verses.Select(v => Superscript(v.Number) + " " + v.Text));
        var short_ = (TranslationBox.Items.OfType<TranslationInfo>().FirstOrDefault(t => t.Id == translation)?.ShortName) is { Length: > 0 } s ? s : translation.ToUpperInvariant();
        return (Label(first) + (end > first.Verse ? "–" + end : "") + "  ·  " + short_, text);
    }

    static string Superscript(int n) => string.Concat(n.ToString().Select(c => "⁰¹²³⁴⁵⁶⁷⁸⁹"[c - '0']));

    /// <summary>A piece of text (from the materials) on the projector.</summary>
    void ProjectText(string title, string text)
    {
        text = text.Trim();
        if (text.Length == 0) return;
        _projectVerses = null;
        Projector().Show(title, text);
        LiveMirror(title, text, null);
        UpdateProjectorChip();
    }

    /// <summary>On or back: the next verses as many as are shown now (Joh 3,16 → 3,17; 16–18 → 19–21).</summary>
    async Task StepProjectorAsync(int dir)
    {
        if (_projectVerses is not { } p) return;
        var span = p.End - p.First.Verse + 1;
        var from = p.First.Verse + dir * span;
        int last;
        try { last = (await _provider.GetChapterAsync(p.Translation, p.First.Book, p.First.Chapter)).Verses.Max(v => v.Number); }
        catch { return; }
        if (from > last || from + span - 1 < 1) return;
        from = Math.Max(1, from);
        await ProjectVersesAsync(new VerseRef(p.First.Book, p.First.Chapter, from), Math.Min(last, from + span - 1), p.Translation);
    }

    void CloseProjector() { _projector?.Close(); _projector = null; _projectVerses = null; UpdateProjectorChip(); }

    void UpdateProjectorChip()
    {
        ProjectorChip.IsVisible = _projector is not null;
        if (_projector is not { } p) return;
        var what = p.ShownTitle.Split("  ·  ")[0];
        ProjectorChipText.Text = (p.IsBlack ? "■ " : "▣ ") + L("Beamer") + (what.Length > 0 ? ": " + (what.Length > 40 ? what[..40] + "…" : what) : "");
    }

    void InitProjector()
    {
        ProjectorChip.Click += (_, _) =>
        {
            if (_projector is not { } p) return;
            var flyout = new Flyout { Placement = PlacementMode.TopEdgeAlignedRight };
            var panel = new StackPanel { Spacing = 8, Width = 260 };
            panel.Children.Add(new TextBlock { Text = L("BEAMER"), Classes = { "eyebrow" } });
            panel.Children.Add(new TextBlock { Text = p.ShownTitle, TextWrapping = TextWrapping.Wrap, FontWeight = FontWeight.SemiBold });
            Button B(string text, Action act, bool primary = false)
            {
                var b = new Button { Content = text, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center };
                if (primary) b.Classes.Add("primary");
                b.Click += (_, _) => { act(); UpdateProjectorChip(); };
                return b;
            }
            if (_projectVerses is not null)
                panel.Children.Add(new UniformGrid { Columns = 2, Children = { B(L("‹ Zurück"), () => _ = StepProjectorAsync(-1)), B(L("Weiter ›"), () => _ = StepProjectorAsync(1), true) } });
            panel.Children.Add(B(p.IsBlack ? L("Wieder zeigen") : L("Schwarz schalten"), () => { p.ToggleBlack(); flyout.Hide(); }));
            panel.Children.Add(B(L("Vollbild an/aus"), () => p.ToggleFullScreen()));
            panel.Children.Add(B(L("Beamer-Ansicht schließen"), () => { CloseProjector(); flyout.Hide(); }));
            flyout.Content = panel;
            flyout.ShowAt(ProjectorChip);
        };
    }

    /// <summary>The chosen verses (or the verse clicked last) on the projector; with the view open, the palette closes it.</summary>
    Task ProjectChosenVersesAsync()
    {
        if (_curBook.Length == 0) return Task.CompletedTask;
        var chosen = _selVerses.Count > 0 ? _selVerses.OrderBy(v => v).ToList() : [_selected ?? 1];
        return ProjectVersesAsync(new VerseRef(_curBook, _curChapter, chosen[0]), chosen[^1]);
    }
}
