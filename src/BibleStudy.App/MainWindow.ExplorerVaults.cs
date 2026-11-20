using Avalonia.Controls;
using BibleStudy.Core.Models;

namespace BibleStudy.App;

/// <summary>Marks and notes into another vault: copied (they stay here too) or moved (here they go into the trash, so "Rückgängig" brings them back).</summary>
public partial class MainWindow
{
    MenuItem OtherVaultMenu(List<ExItem> items)
    {
        var others = VaultNames().Where(n => n != _vault.Name && !IsGuestVault(n)).ToList();
        var copyable = items.Where(i => i.Kind == ExKind.Mark || (i.Kind == ExKind.Note && i.Ref is not null)).ToList();
        var menu = new MenuItem { Header = L("In anderen Vault"), IsEnabled = copyable.Count > 0 };
        if (others.Count == 0) { menu.Items.Add(MenuAction(L("Es gibt keinen anderen Vault."), () => { }, enabled: false)); return menu; }
        foreach (var name in others)
        {
            var vault = new MenuItem { Header = name };
            vault.Items.Add(MenuAction(L("Kopieren"), () => CopyToVault(copyable, name, move: false)));
            vault.Items.Add(MenuAction(L("Verschieben"), () => CopyToVault(copyable, name, move: true)));
            menu.Items.Add(vault);
        }
        return menu;
    }

    async void CopyToVault(List<ExItem> items, string name, bool move)
    {
        var target = StoreOf(name);
        List<long> marks; List<VerseRef> notes;
        try
        {
            marks = _vault.CopyMarksTo(target, items.Where(i => i.Kind == ExKind.Mark).Select(i => i.Id));
            notes = _vault.CopyNotesTo(target, items.Where(i => i.Kind == ExKind.Note && i.Ref is not null).Select(i => i.Ref!.Value));
        }
        catch (Exception ex) when (ex is IOException or Microsoft.Data.Sqlite.SqliteException) { ExNote(L("Der Vault „{0}“ ließ sich nicht öffnen: {1}", name, ex.Message), false); return; }
        var count = marks.Count + notes.Count;
        if (count == 0) return;
        var what = count == 1 ? "„" + Shorten(items.First(i => i.Kind == ExKind.Mark ? marks.Contains(i.Id) : notes.Contains(i.Ref!.Value)).Title, 40) + "“" : L("{0} Einträge", count);
        if (!move) { AfterExplorerChange(); ExNote(L("{0} in den Vault „{1}“ kopiert.", what, name), false); return; }

        // moved: here into the trash, "Rückgängig" takes them out again (the copy in the other vault stays)
        var trash = new List<long>();
        foreach (var id in marks) if (_vault.TrashMark(id) is > 0 and var t) trash.Add(t);
        foreach (var r in notes)
        {
            if (_vault.TrashNote(r) is > 0 and var t) trash.Add(t);
            if (_noteRef == r) NoteEditor.Text = _noteLoaded = "";
        }
        trash.Reverse();
        ExRemember(L("{0} in „{1}“ verschoben", what, name), () => _ = RestoreTrashAsync(trash, quiet: true), marks.Count > 0);
        _exSel.Clear();
        AfterExplorerChange();
        ExNote(L("{0} in den Vault „{1}“ verschoben.", what, name), true);
        if (marks.Count > 0) { LoadChapterMarks(); await RedrawChapterAsync(); }
        RefreshCells();
    }
}
