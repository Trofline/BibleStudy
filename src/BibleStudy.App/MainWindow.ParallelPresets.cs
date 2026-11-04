using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using BibleStudy.Core.Models;
using BibleStudy.Core.Storage;

namespace BibleStudy.App;

/// <summary>
/// Parallel columns: change the order (arrows or drag the colored dot onto another column) and save / load arrangements ("presets":
/// which translations and which own passages, in which order).
/// </summary>
public partial class MainWindow
{
    Border? _presetPill;

    /// <summary>Puts the pills in the order of <see cref="_extras"/> (sync switch and presets stay in front), then redraws.</summary>
    async Task ReorderColumnsAsync()
    {
        foreach (var c in _extras) ParallelBar.Children.Remove(c.Root);
        foreach (var c in _extras) ParallelBar.Children.Add(c.Root);
        RecolorPills();
        await RedrawChapterAsync();
    }

    async Task MoveColumnAsync(ExtraColumn col, int to)
    {
        var from = _extras.IndexOf(col);
        to = Math.Clamp(to, 0, _extras.Count - 1);
        if (from < 0 || from == to) return;
        _extras.RemoveAt(from); _extras.Insert(to, col);
        await ReorderColumnsAsync();
    }

    /// <summary>The arrows and the drag handle of one column pill.</summary>
    void AttachColumnMove(ExtraColumn col, Border dot, Button left, Button right)
    {
        left.Click += async (_, _) => await MoveColumnAsync(col, _extras.IndexOf(col) - 1);
        right.Click += async (_, _) => await MoveColumnAsync(col, _extras.IndexOf(col) + 1);
        dot.Cursor = new Cursor(StandardCursorType.SizeAll);
        ToolTip.SetTip(dot, L("Ziehen, um die Spalte zu verschieben"));
        var dragging = false;
        dot.PointerPressed += (_, e) => { dragging = true; e.Pointer.Capture(dot); e.Handled = true; };
        dot.PointerReleased += async (_, e) =>
        {
            if (!dragging) return;
            dragging = false; e.Pointer.Capture(null);
            var p = e.GetPosition(ParallelBar);
            var target = _extras.FirstOrDefault(c => c != col && c.Root.Bounds.Contains(p));
            if (target is not null) await MoveColumnAsync(col, _extras.IndexOf(target));
        };
    }

    // ---- presets ------------------------------------------------------------------------------------------------------------

    void EnsurePresetPill()
    {
        if (_presetPill is not null) return;
        var btn = new Button { Classes = { "tool" }, Width = double.NaN, Padding = new(10, 0), Height = 28, Content = L("Presets ▾"), VerticalAlignment = VerticalAlignment.Center };
        ToolTip.SetTip(btn, L("Anordnung der Spalten speichern und wieder laden"));
        btn.Click += (_, _) => ShowPresetMenu(btn);
        _presetPill = new Border { Classes = { "pill" }, Margin = new(16, 8, 0, 8), Padding = new(4, 0), Child = btn };
    }

    string PresetName() => string.Join(" + ", new[] { (TranslationBox.SelectedItem as TranslationInfo)?.ShortName }.Concat(_extras.Select(e => e.Translation?.ShortName)).Where(n => !string.IsNullOrEmpty(n)));

    void ShowPresetMenu(Control anchor)
    {
        var menu = new MenuFlyout();
        var save = new MenuItem { Header = L("Aktuelle Spalten speichern ({0})", PresetName()), IsEnabled = _extras.Count > 0 };
        save.Click += (_, _) => SaveParallelPreset();
        menu.Items.Add(save);
        if (_settings.ParallelPresets.Count > 0) menu.Items.Add(new Separator());
        foreach (var preset in _settings.ParallelPresets.ToList())
        {
            var p = preset;
            var item = new MenuItem { Header = p.Name };
            item.Click += async (_, _) => await ApplyParallelPresetAsync(p);
            menu.Items.Add(item);
        }
        if (_settings.ParallelPresets.Count > 0)
        {
            var remove = new MenuItem { Header = L("Gespeicherte Anordnung löschen") };
            foreach (var preset in _settings.ParallelPresets.ToList())
            {
                var p = preset;
                var del = new MenuItem { Header = p.Name };
                del.Click += (_, _) => { _settings.ParallelPresets.Remove(p); SaveSettings(); StatusText.Text = L("„{0}“ gelöscht.", p.Name); };
                remove.Items.Add(del);
            }
            menu.Items.Add(new Separator());
            menu.Items.Add(remove);
        }
        menu.ShowAt(anchor);
    }

    void SaveParallelPreset()
    {
        var name = PresetName();
        var preset = new ParallelPreset
        {
            Name = name,
            Columns = _extras.Where(e => e.Translation is not null).Select(e => new ParallelPresetColumn { TranslationId = e.Translation!.Id, Book = e.Own?.Book, Chapter = e.Own?.Chapter ?? 0 }).ToList(),
        };
        _settings.ParallelPresets.RemoveAll(x => x.Name == name);
        _settings.ParallelPresets.Add(preset);
        SaveSettings();
        StatusText.Text = L("Anordnung „{0}“ gespeichert.", name);
    }

    async Task ApplyParallelPresetAsync(ParallelPreset preset)
    {
        foreach (var c in _extras.ToList()) ParallelBar.Children.Remove(c.Root);
        _extras.Clear();
        foreach (var column in preset.Columns.Take(MaxExtras))
        {
            if (_all.FirstOrDefault(t => t.Id == column.TranslationId) is not { } t) continue;   // a translation that is no longer there is skipped
            AddColumn(t);
            if (column.Book is not null && _extras[^1] is { } added) { added.Own = (column.Book, Math.Max(1, column.Chapter)); UpdatePassageButton(added); }
        }
        ParallelBar.IsVisible = _extras.Count > 0;
        await RedrawChapterAsync();
    }
}
