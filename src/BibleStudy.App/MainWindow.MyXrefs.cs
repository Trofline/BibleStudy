using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using BibleStudy.Core.Models;
using BibleStudy.Core.Search;
using BibleStudy.Core.Storage;

namespace BibleStudy.App;

/// <summary>
/// Your own cross references: written to a verse (right click), shown in the text as golden chips under the verse – and as a way back under the
/// verse they point to. Stored in the vault, so they travel with backups and are separate from the printed references.
/// </summary>
public partial class MainWindow
{
    List<MyXref>? _myXrefs;
    string? _myXrefKey;

    List<MyXref> MyXrefsOfChapter()
    {
        var key = $"{_vault.Name}|{_curBook}|{_curChapter}";
        if (_myXrefs is null || _myXrefKey != key) { _myXrefs = _vault.GetMyXrefs(_curBook, _curChapter); _myXrefKey = key; }
        return _myXrefs;
    }

    List<MyXref> MyXrefsAt(int verse) =>
        MyXrefsOfChapter().Where(x => (x.From.Book == _curBook && x.From.Chapter == _curChapter && x.From.Verse == verse)
            || (x.To.Book == _curBook && x.To.Chapter == _curChapter && verse >= x.To.Verse && verse <= x.EndVerse)).ToList();

    Border MyXrefChip(MyXref x, int verse, string translationId)
    {
        var outgoing = x.From.Book == _curBook && x.From.Chapter == _curChapter && x.From.Verse == verse;
        var target = outgoing ? x.To : x.From;
        var end = outgoing ? x.EndVerse : target.Verse;
        var text = "★ " + (outgoing ? "" : "↩ ") + Label(target) + (end > target.Verse ? "–" + end : "");
        if (x.Note.Length > 0) text += " · " + Shorten(x.Note, 24);
        var chip = Chip(text, () => _ = GoToAsync(target.Book, target.Chapter, target.Verse), translationId, new CrossRef(target, end, 0));
        chip.Bind(Border.BackgroundProperty, this.GetResourceObservable("BsAccentSoft"));
        chip.BorderBrush = Res("BsAccent") as IBrush ?? Brushes.Goldenrod;
        var del = new MenuItem { Header = L("Eigenen Querverweis löschen") };
        del.Click += async (_, _) => { _vault.DeleteMyXref(x.Id); _myXrefKey = null; await ShowChapterAsync(_selected); };
        chip.ContextMenu = new ContextMenu { Items = { del } };
        return chip;
    }

    /// <summary>A small card at the verse: where does it point, and why (optional).</summary>
    void ShowMyXrefEditor(VerseRef from, Control anchor)
    {
        var target = new TextBox { Watermark = L("Bibelstelle, z. B. Joh 3,16 oder Röm 8,28-30") };
        var note = new TextBox { Watermark = L("Kurze Notiz dazu (freiwillig)") };
        var info = new TextBlock { Classes = { "caption" }, TextWrapping = TextWrapping.Wrap };
        var save = new Button { Content = L("Querverweis speichern"), Classes = { "primary" }, HorizontalAlignment = HorizontalAlignment.Left };
        var fly = new Flyout { Content = new StackPanel { Spacing = 8, Width = 300, Children = { new TextBlock { Text = L("Eigener Querverweis von {0}", Label(from)), FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap }, target, note, save, info } } };

        async Task Commit()
        {
            if (PassageParser.Parse(target.Text ?? "", out _) is not { Count: > 0 } p) { info.Text = L("„{0}“ ist keine Bibelstelle.", target.Text ?? ""); return; }
            var first = p[0];
            var to = new VerseRef(first.Book, first.Chapter, first.FromVerse ?? 1);
            if (to == from) { info.Text = L("Ein Vers kann nicht auf sich selbst verweisen."); return; }
            _vault.AddMyXref(from, to, first.ToChapter is null ? first.ToVerse ?? to.Verse : to.Verse, note.Text?.Trim() ?? "");
            _myXrefKey = null;
            fly.Hide();
            StatusText.Text = L("Querverweis {0} → {1} gespeichert.", Label(from), Label(to));
            await ShowChapterAsync(_selected);
        }
        save.Click += async (_, _) => await Commit();
        target.KeyDown += async (_, e) => { if (e.Key == Key.Enter) await Commit(); };
        note.KeyDown += async (_, e) => { if (e.Key == Key.Enter) await Commit(); };
        fly.ShowAt(anchor);
        target.Focus();
    }
}
