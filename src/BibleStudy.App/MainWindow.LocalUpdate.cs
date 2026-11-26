using Avalonia.Platform.Storage;
using BibleStudy.Core;

namespace BibleStudy.App;

/// <summary>
/// Installing an update from a file: private test versions (made with <c>release.ps1 -Beta</c>) reach testers as a file instead of a public release.
/// The file holds only what is new in that version; it is installed the same way as a quick update (backup first, old files kept until the new
/// version has started, so a failure undoes itself). The next published release updates a tester as usual.
/// </summary>
public partial class MainWindow
{
    void InitLocalUpdate()
    {
        // the phones install a test version as an APK (Android) or with SideStore (iPhone); the Store copy may not change its own files
        InstallUpdateFileBtn.IsVisible = !AppPlatform.IsMobile && !AppPaths.IsStore;
        InstallUpdateFileBtn.Click += async (_, _) => await InstallUpdateFileAsync();
    }

    async Task InstallUpdateFileAsync()
    {
        if (_updating) { UpdateStatus.Text = L("Es läuft schon ein Update."); return; }
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = L("Update-Datei installieren"),
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType(L("BibleStudy-Update")) { Patterns = ["*" + DeltaUpdate.LocalExtension] }, FilePickerFileTypes.All],
        });
        if (files.Count == 0 || files[0].TryGetLocalPath() is not { } path) return;

        var root = DeltaUpdate.RootOf(AppPaths.ProgramDir);
        if (DeltaUpdate.LoadInstalled(root) is not { } installed || installed.Rid != DeltaUpdate.Rid)
        {
            UpdateStatus.Text = L("Eine Update-Datei lässt sich nur in eine installierte oder portable Version einspielen, nicht in eine selbst gebaute.");
            return;
        }
        UpdateStatus.Text = L("Prüfe die Update-Datei …");
        DeltaUpdate.LocalPackage? package = null;
        try
        {
            package = await Task.Run(() => DeltaUpdate.OpenLocal(path, DeltaUpdate.Rid));
            if (package is null)
            {
                UpdateStatus.Text = L("Diese Update-Datei enthält nichts für dieses System ({0}).", DeltaUpdate.Rid);
                return;
            }
            var version = package.Manifest.Version;
            if (version == AppPaths.Version)
            {
                UpdateStatus.Text = L("Version {0} ist bereits installiert.", version);
                return;
            }
            if (!await Task.Run(() => DeltaUpdate.CanWrite(root)))
            {
                UpdateStatus.Text = L("BibleStudy darf seinen Programmordner nicht ändern, deshalb lässt sich die Update-Datei hier nicht einspielen.");
                return;
            }
            var plan = await Task.Run(() => DeltaUpdate.CreatePlan(package.Manifest, installed, root, trustInstalled: OperatingSystem.IsMacOS()));
            var fromNet = plan.Download.Where(f => f.Pack is null || !package.Packs.ContainsKey(f.Pack)).Sum(f => f.Length);
            var text = L("Die Datei enthält BibleStudy {0} (du hast {1}). Testversionen sind noch nicht für alle freigegeben und können Fehler haben.\n\nVorher wird eine Sicherung deiner Daten angelegt, danach startet das Programm neu. Geht etwas schief, kommt die bisherige Version von selbst zurück. Mit der nächsten normalen Version wirst du wie gewohnt aktualisiert.", version, AppPaths.Version);
            if (Updater.Parse(version) is { } v && Updater.Parse(AppPaths.Version) is { } cur && v < cur)
                text += "\n\n" + L("Achtung: Das ist eine ältere Version als die, die du hast.");
            if (fromNet > 0) text += "\n\n" + L("Einige Dateien ({0:0.0} MB) kommen aus den veröffentlichten Versionen auf GitHub: Dafür braucht es eine Internetverbindung.", fromNet / 1048576.0);
            if (await AskAsync(L("Testversion {0} installieren?", version), text, L("Jetzt installieren"), L("Abbrechen")) != 0) return;
            var info = new UpdateInfo(version, "", "", "", null, 0, "");
            await InstallUpdateAsync(info, plan, localPacks: package.Packs);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or System.Text.Json.JsonException or HttpRequestException)
        {
            ErrorLog.Write(ex);
            UpdateStatus.Text = L("Die Update-Datei ließ sich nicht lesen: ") + ex.Message;
        }
        finally
        {
            if (package is not null) DeltaUpdate.DeleteLocal(package.Folder);
        }
    }
}
