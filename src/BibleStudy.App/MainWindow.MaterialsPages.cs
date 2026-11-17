using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using BibleStudy.Core.Materials;

namespace BibleStudy.App;

/// <summary>
/// Go to a page of the open material: a box "Seite [ 12 ] / 40" beside the open files (Ctrl+G puts the cursor in it). It works for what
/// has pages: a PDF (as pages or as text), PowerPoint (slides), and Word and OpenDocument files with the pages as their program last laid them out.
/// While one scrolls, the box shows the page one is on.
/// </summary>
public partial class MainWindow
{
    StackPanel _matPageBox = null!;
    TextBox _matPageInput = null!;
    TextBlock _matPageTotal = null!, _matPageLabel = null!;
    int _matPageCount;
    Func<int, Task>? _matGoToPage;
    Func<int>? _matPageNow;

    Control BuildPageBox()
    {
        _matPageLabel = new TextBlock { Text = L("Seite"), VerticalAlignment = VerticalAlignment.Center, Margin = new(0, 0, 6, 0), Classes = { "caption" } };
        _matPageInput = new TextBox { Width = 58, MinHeight = 0, Height = 30, Padding = new(6, 4), HorizontalContentAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        _matPageTotal = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new(6, 0, 0, 0), Classes = { "caption" } };
        ToolTip.SetTip(_matPageInput, L("Seitenzahl eingeben und Enter drücken (Strg+G)"));
        _matPageInput.KeyDown += async (_, e) =>
        {
            if (e.Key == Key.Escape) { e.Handled = true; ShowCurrentPage(force: true); _matViewer.Focus(); return; }
            if (e.Key != Key.Enter) return;
            e.Handled = true;
            if (!int.TryParse(_matPageInput.Text?.Trim(), out var n) || _matGoToPage is not { } go) { ShowCurrentPage(force: true); return; }
            n = Math.Clamp(n, 1, Math.Max(1, _matPageCount));
            _matPageInput.Text = n.ToString();
            await go(n);
            _matViewer.Focus();
        };
        _matPageInput.LostFocus += (_, _) => ShowCurrentPage(force: true);
        _matPageBox = new StackPanel { Orientation = Orientation.Horizontal, Margin = new(8, 0, 0, 4), VerticalAlignment = VerticalAlignment.Top, Height = 32, IsVisible = false, Children = { _matPageLabel, _matPageInput, _matPageTotal } };
        return _matPageBox;
    }

    /// <summary>The pages of the file just opened: how many, how to go to one, and which one is seen.</summary>
    void SetMaterialPages(MaterialDoc doc, bool pdfPages, string path, int version)
    {
        var pageBlocks = doc.Blocks.Where(b => b.Kind == BlockKind.Page).ToList();
        _matPageCount = pageBlocks.Count;
        _matPageBox.IsVisible = _matPageCount > 1;
        if (_matPageCount <= 1) { _matGoToPage = null; _matPageNow = null; return; }
        var slides = string.Equals(Path.GetExtension(path), ".pptx", StringComparison.OrdinalIgnoreCase);
        _matPageLabel.Text = slides ? L("Folie") : L("Seite");
        _matPageTotal.Text = "/ " + _matPageCount;
        if (pdfPages)
        {
            // the PDF as pictures: pages not drawn yet are drawn first
            _matGoToPage = n => JumpToAsync(new Block(BlockKind.Page, [], Page: n), path, version);
            _matPageNow = () => PageAtTop(_pdfPageMarks.Select(p => (p.Key, (Control)p.Value)));
        }
        else
        {
            // a text with lines "Seite n": the n-th of them
            List<(int, Control)> Marks() => _matBlocks.Where(x => x.Block.Kind == BlockKind.Page).Select((x, i) => (i + 1, x.Control)).ToList();
            _matGoToPage = n =>
            {
                if (Marks().FirstOrDefault(m => m.Item1 == n).Item2 is { } c) ScrollToControl(c);
                return Task.CompletedTask;
            };
            _matPageNow = () => PageAtTop(Marks());
        }
        _matPageInput.Text = "1";
    }

    /// <summary>The page whose beginning is the last one above the top of the view (the page one reads).</summary>
    int PageAtTop(IEnumerable<(int Page, Control Mark)> marks)
    {
        var y = _matScroll.Offset.Y;
        var page = 1;
        foreach (var (n, mark) in marks)
            if (mark.TranslatePoint(new Point(0, 0), _matView) is { } pt && pt.Y <= y + 60 && n > page) page = n;
        return page;
    }

    void ShowCurrentPage(bool force = false)
    {
        if (!_matPageBox.IsVisible || _matPageNow is not { } now) return;
        if (!force && _matPageInput.IsFocused) return;   // what is being typed stays
        _matPageInput.Text = now().ToString();
    }
}
