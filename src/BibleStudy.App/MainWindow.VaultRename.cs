using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using BibleStudy.Core.Storage;
using Microsoft.Data.Sqlite;

namespace BibleStudy.App;

/// <summary>Giving a vault another name: its folder is renamed, everything inside stays.</summary>
public partial class MainWindow
{
    void ShowRenameVault()
    {
        var old = VaultBox.SelectedItem as string ?? _vault.Name;
        if (IsGuestVault(old)) { VaultInfo.Text = L("Ein Gast-Vault verschwindet beim Schließen und lässt sich nicht umbenennen."); return; }
        var box = new TextBox { Text = old, Width = 240, SelectionStart = 0, SelectionEnd = old.Length };
        var save = new Button { Content = L("Umbenennen"), Classes = { "primary" }, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left };
        var info = new TextBlock { Classes = { "caption" }, TextWrapping = TextWrapping.Wrap, MaxWidth = 240 };
        var fly = new Flyout { Content = new StackPanel { Spacing = 8, Children = { new TextBlock { Text = L("Neuer Name für „{0}“", old), FontWeight = FontWeight.SemiBold, MaxWidth = 240, TextWrapping = TextWrapping.Wrap }, box, save, info } } };
        async Task Do()
        {
            var name = (box.Text ?? "").Trim();
            if (name == old) { fly.Hide(); return; }
            if (!MarkLogic.IsValidVaultName(name)) { info.Text = L("Ungültiger Name (keine Sonderzeichen wie \\ / : * ? \" < > |)."); return; }
            // another vault with that name (also the same name in other capitals, which Windows treats as one folder)
            if (VaultNames().Any(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase) && !string.Equals(n, old, StringComparison.Ordinal))) { info.Text = L("Es gibt schon einen Vault mit diesem Namen."); return; }
            fly.Hide();
            await RenameVaultAsync(old, name);
        }
        save.Click += async (_, _) => await Do();
        box.KeyDown += async (_, e) => { if (e.Key == Key.Enter) await Do(); };
        fly.ShowAt(RenameVaultBtn);
        box.Focus();
    }

    async Task RenameVaultAsync(string old, string name)
    {
        var active = old == _vault.Name;
        try
        {
            // what is open belongs to the vault: saved first, then the file is let go
            if (active) { CloseEditor(); CloseDocument(); SaveNoteDraft(); SaveSermonBeforeVaultChange(); }
            _otherVaults.Remove(old);
            SqliteConnection.ClearAllPools();
            var from = Path.Combine(_root, "vaults", old); var to = Path.Combine(_root, "vaults", name);
            if (string.Equals(old, name, StringComparison.OrdinalIgnoreCase))
            {
                // only the capitals change: Windows sees one folder, so it goes by a detour
                var temp = to + ".umbenennen";
                Directory.Move(from, temp); Directory.Move(temp, to);
            }
            else Directory.Move(from, to);
            if (_settings.Vault == old) { _settings.Vault = name; SaveSettings(); }
            if (active) await SwitchVaultAsync(name, alreadySaved: true); else RefreshVaultUi();
            VaultInfo.Text = L("Vault „{0}“ heißt jetzt „{1}“.", old, name);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            VaultInfo.Text = L("Der Vault ließ sich nicht umbenennen: {0}", ex.Message);
            if (active) await SwitchVaultAsync(old, alreadySaved: true);
        }
    }

    /// <summary>The sub-marks get the new color, in this vault and in the others (they were made in the old color and belong together).</summary>
    void ChangeSubMarkColor(string newColor)
    {
        var old = SubMarkColor();
        _settings.SubMarkColor = newColor; SaveSettings();
        var now = SubMarkColor();
        if (!string.Equals(old, now, StringComparison.OrdinalIgnoreCase))
            foreach (var name in VaultNames().Where(n => !IsGuestVault(n)))
                try
                {
                    var store = StoreOf(name);
                    if (MarkLogic.SubMarkIds(store.GetMarks(), old) is { Count: > 0 } ids) store.RecolorMarks(ids, now);
                }
                catch { /* a vault that cannot be read keeps its colors */ }
        BuildSubMarkPanel();
        RefreshCollection(); RefreshCells();
    }
}
