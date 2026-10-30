using Microsoft.Data.Sqlite;

namespace BibleStudy.App;

/// <summary>Deleting a vault: the whole folder of the vault, after a backup and an explicit confirmation.</summary>
public partial class MainWindow
{
    async Task DeleteVaultAsync()
    {
        var name = VaultBox.SelectedItem as string ?? _vault.Name;
        var names = VaultNames();
        var only = names.Count <= 1;
        var text = L("Der Vault „{0}“ wird endgültig gelöscht, mit allem, was darin ist: Markierungen, Notizen, Dokumente, Merkliste, Leseliste, Aufgaben, Gebete und dein Lesefortschritt.\n\nVorher legt das Programm eine Sicherung an. Danach lässt sich der Vault nur noch aus dieser Sicherung zurückholen.", name)
            + (only ? "\n\n" + L("Es ist dein einziger Vault: Er wird geleert und bleibt als leerer Vault bestehen.") : "");
        if (await AskAsync(L("Vault löschen?"), text, L("Sicherung anlegen und löschen"), L("Abbrechen")) != 0) return;

        string backup;
        try { backup = BackupData("vor-dem-Loeschen"); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            VaultInfo.Text = L("Die Sicherung ließ sich nicht anlegen, deshalb wurde nichts gelöscht: {0}", ex.Message);
            return;
        }

        // an open document or an unsaved note belongs to the vault that is deleted: save it there first, so it cannot reappear in another vault
        CloseEditor(); CloseDocument(); SaveNoteDraft();
        _noteRef = null; NoteEditor.Text = _noteLoaded = "";
        try
        {
            if (only)
            {
                _vault.Wipe();
                await SwitchVaultAsync(_vault.Name);
            }
            else
            {
                var other = names.First(n => n != name);
                if (name == _vault.Name) await SwitchVaultAsync(other);
                SqliteConnection.ClearAllPools();   // the file may still be held by the connection pool
                Directory.Delete(Path.Combine(_root, "vaults", name), true);
                RefreshVaultUi();
            }
            VaultInfo.Text = L("Vault „{0}“ gelöscht. Sicherung: {1}", name, backup);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            VaultInfo.Text = L("Der Vault ließ sich nicht löschen: {0}", ex.Message);
        }
    }
}
