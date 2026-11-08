using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using BibleStudy.Core.Models;
using BibleStudy.Core.Storage;

namespace BibleStudy.App;

/// <summary>
/// Own headings between the verses: a theme heading in front of a verse, shown in the text like the headings of a printed Bible. It is no note
/// (it does not appear in the notes) and belongs to the vault, so it is backed up and exported with it.
/// </summary>
public partial class MainWindow
{
    List<Heading> _headings = [];
    bool OwnHeadingsShown => _settings.HeadingMode is not ("Translation" or "None");
    readonly List<(Heading H, Control C)> _headingControls = [];

    /// <summary>The headings that belong to the translation itself (an imported Bible with headings): plain, not editable, in front of the verse.</summary>
    void AddTranslationHeadings(Panel panel, Chapter? chapter, int verse)
    {
        if (chapter?.Headings is not { Count: > 0 } list || _settings.HeadingMode is "Mine" or "None") return;
        if (_settings.HeadingMode == "Smart" && _headings.Any(m => m.Ref.Verse == verse)) return;   // mine win where both stand in front of one verse
        foreach (var h in list.Where(h => h.BeforeVerse == verse))
        {
            var text = new TextBlock { Text = h.Text, FontSize = 19, FontWeight = FontWeight.SemiBold, FontFamily = UiSerif, TextWrapping = TextWrapping.Wrap };
            text.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("BsAccent"));
            panel.Children.Add(new Border { Padding = new(10, 16, 10, 2), Child = text });
        }
    }

    /// <summary>Puts the headings that stand in front of a verse into the page (before the verse is added).</summary>
    void AddHeadings(Panel panel, int verse)
    {
        if (!OwnHeadingsShown) return;
        foreach (var h in _headings.Where(h => h.Ref.Verse == verse))
        {
            var c = HeadingControl(h);
            _headingControls.Add((h, c));
            panel.Children.Add(c);
        }
    }

    /// <summary>Draws the headings again where they belong (after one was added, changed or deleted).</summary>
    void RebuildHeadings()
    {
        foreach (var (_, c) in _headingControls) (c.Parent as Panel)?.Children.Remove(c);
        _headingControls.Clear();
        if (!OwnHeadingsShown) return;
        foreach (var h in _headings)
        {
            if (!_verseBoxes.TryGetValue(h.Ref.Verse, out var box) || box.Parent is not Panel panel) continue;
            var c = HeadingControl(h);
            panel.Children.Insert(panel.Children.IndexOf(box), c);
            _headingControls.Add((h, c));
        }
    }

    Control HeadingControl(Heading h)
    {
        var (size, weight) = h.Level switch { 1 => (26.0, FontWeight.Bold), 2 => (21.0, FontWeight.SemiBold), _ => (16.0, FontWeight.SemiBold) };
        var text = new TextBlock { Text = h.Level == 3 ? h.Text.ToUpperInvariant() : h.Text, FontSize = size, FontWeight = weight, FontFamily = UiSerif, TextWrapping = TextWrapping.Wrap, LetterSpacing = h.Level == 3 ? 1.5 : 0 };
        text.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable(h.Level == 3 ? "BsMuted" : "BsAccent"));
        var edit = new Button { Content = "✎", Classes = { "tool" }, Width = 28, Height = 28, Opacity = 0, VerticalAlignment = VerticalAlignment.Center, Margin = new(8, 0, 0, 0) };
        ToolTip.SetTip(edit, L("Überschrift bearbeiten"));
        var line = new Border { Height = 2, Margin = new(0, 4, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, Width = h.Level == 1 ? 56 : 0, CornerRadius = new(1) };
        line.Bind(Border.BackgroundProperty, this.GetResourceObservable("BsAccent"));
        var row = new DockPanel { Children = { edit, text } };
        DockPanel.SetDock(edit, Dock.Right);
        var box = new Border
        {
            Padding = new(10, h.Level == 1 ? 18 : 12, 10, 4), Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand),
            Child = new StackPanel { Children = { row, line } },
        };
        edit.Click += (_, _) => ShowHeadingEditor(h.Ref, h, box);
        box.PointerEntered += (_, _) => edit.Opacity = 0.75;
        box.PointerExited += (_, _) => edit.Opacity = 0;
        box.DoubleTapped += (_, _) => ShowHeadingEditor(h.Ref, h, box);
        var menu = new MenuFlyout();
        var change = new MenuItem { Header = L("Überschrift bearbeiten") };
        change.Click += (_, _) => ShowHeadingEditor(h.Ref, h, box);
        var delete = new MenuItem { Header = L("Überschrift löschen") };
        delete.Click += (_, _) => { _vault.DeleteHeading(h.Id); ReloadHeadings(); };
        menu.Items.Add(change); menu.Items.Add(delete);
        box.ContextFlyout = menu;
        return box;
    }

    void ReloadHeadings()
    {
        _headings = _vault.GetHeadings(_curBook, _curChapter);
        RebuildHeadings();
    }

    /// <summary>The small window for a heading: the text, how big it is, save or delete.</summary>
    void ShowHeadingEditor(VerseRef before, Heading? existing, Control anchor)
    {
        var text = new TextBox { Text = existing?.Text ?? "", PlaceholderText = L("Überschrift, z. B. „Der gute Hirte“"), MaxLength = 140, Width = 320, AcceptsReturn = false };
        var level = existing?.Level ?? 2;
        var levels = new List<ToggleButton>();
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        foreach (var (n, label) in new[] { (1, L("Groß")), (2, L("Mittel")), (3, L("Klein")) })
        {
            var b = new ToggleButton { Content = label, IsChecked = n == level, Classes = { "seg" } };
            var nn = n;
            b.Click += (_, _) => { level = nn; foreach (var o in levels) o.IsChecked = ReferenceEquals(o, b); };
            levels.Add(b); bar.Children.Add(b);
        }
        var save = new Button { Content = L("Speichern"), Classes = { "primary" } };
        var delete = new Button { Content = L("Löschen"), IsVisible = existing is not null };
        var fly = new Flyout { Placement = PlacementMode.Bottom };
        void Done()
        {
            var t = (text.Text ?? "").Trim();
            if (t.Length == 0) return;
            if (existing is null) _vault.AddHeading(before, t, level); else _vault.UpdateHeading(existing.Id, t, level);
            fly.Hide();
            ReloadHeadings();
            StatusText.Text = L("Überschrift gespeichert.");
        }
        save.Click += (_, _) => Done();
        text.KeyDown += (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; Done(); } };
        delete.Click += (_, _) => { if (existing is not null) _vault.DeleteHeading(existing.Id); fly.Hide(); ReloadHeadings(); };
        fly.Content = new StackPanel
        {
            Spacing = 8, Width = 330,
            Children =
            {
                new TextBlock { Text = existing is null ? L("Überschrift vor {0}", Label(before)) : L("Überschrift bearbeiten"), FontWeight = FontWeight.SemiBold },
                text, bar,
                new TextBlock { Text = L("Eigene Überschriften sind keine Notizen: Sie stehen im Text vor dem Vers, so wie in einer gedruckten Bibel."), Classes = { "caption" }, TextWrapping = TextWrapping.Wrap },
                new WrapPanel { Children = { save, delete } },
            },
        };
        fly.ShowAt(anchor);
        text.Focus();
    }

    /// <summary>The heading that stands in front of a verse (null if none).</summary>
    Heading? HeadingBefore(int verse) => _headings.FirstOrDefault(h => h.Ref.Verse == verse);
}
