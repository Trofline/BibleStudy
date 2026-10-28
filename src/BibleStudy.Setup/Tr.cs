using System.Globalization;
using System.Text.RegularExpressions;

namespace BibleStudy.Setup;

/// <summary>The few texts of the installer, in the language of the program (settings.json) or else of Windows. English is the fallback.</summary>
static class Tr
{
    static readonly string[] Codes = ["en", "de", "es", "fr", "ru"];

    // order: en, de, es, fr, ru
    static readonly Dictionary<string, string[]> Texts = new()
    {
        ["folderNotEmpty"] = [
            "The folder \"{0}\" is not empty and does not contain BibleStudy. Please choose an empty or new folder.",
            "Der Ordner \"{0}\" ist nicht leer und enthält kein BibleStudy. Bitte wähle einen leeren oder neuen Ordner.",
            "La carpeta \"{0}\" no está vacía y no contiene BibleStudy. Elige una carpeta vacía o nueva.",
            "Le dossier « {0} » n'est pas vide et ne contient pas BibleStudy. Choisissez un dossier vide ou nouveau.",
            "Папка «{0}» не пуста и не содержит BibleStudy. Выберите пустую или новую папку."],
        ["waiting"] = [
            "Waiting for BibleStudy to close …", "Warte, bis BibleStudy beendet ist …", "Esperando a que BibleStudy se cierre …",
            "Attente de la fermeture de BibleStudy …", "Ожидание закрытия BibleStudy …"],
        ["unpacking"] = [
            "Unpacking the program …", "Entpacke Programm …", "Descomprimiendo el programa …", "Décompression du programme …", "Распаковка программы …"],
        ["noPayload"] = [
            "This setup does not contain a program package.", "Dieses Setup enthält kein Programmpaket.", "Este instalador no contiene el paquete del programa.",
            "Ce programme d'installation ne contient pas le paquet du programme.", "В этом установщике нет пакета программы."],
        ["shortcuts"] = [
            "Setting up shortcuts …", "Richte Verknüpfungen ein …", "Creando accesos directos …", "Création des raccourcis …", "Создание ярлыков …"],
        ["done"] = ["Done.", "Fertig.", "Listo.", "Terminé.", "Готово."],
        ["stillRunning"] = [
            "BibleStudy is still running. Please close it and start the installation again.",
            "BibleStudy läuft noch. Bitte beende es und starte die Installation erneut.",
            "BibleStudy sigue en ejecución. Ciérralo e inicia de nuevo la instalación.",
            "BibleStudy est encore en cours d'exécution. Fermez-le et relancez l'installation.",
            "BibleStudy ещё работает. Закройте его и запустите установку снова."],
        ["noShortcuts"] = [
            "Shortcuts are not supported.", "Verknüpfungen werden nicht unterstützt.", "No se admiten los accesos directos.",
            "Les raccourcis ne sont pas pris en charge.", "Ярлыки не поддерживаются."],
        ["description"] = [
            "BibleStudy – Bible study", "BibleStudy – Bibelstudium", "BibleStudy – Estudio bíblico", "BibleStudy – Étude biblique", "BibleStudy – изучение Библии"],
        ["desktopShortcut"] = [
            "Create a shortcut on the desktop", "Verknüpfung auf dem Desktop anlegen", "Crear un acceso directo en el escritorio",
            "Créer un raccourci sur le Bureau", "Создать ярлык на рабочем столе"],
        ["install"] = ["Install", "Installieren", "Instalar", "Installer", "Установить"],
        ["titleUpdate"] = [
            "Update BibleStudy", "BibleStudy aktualisieren", "Actualizar BibleStudy", "Mettre à jour BibleStudy", "Обновить BibleStudy"],
        ["titleInstall"] = [
            "Install BibleStudy", "BibleStudy installieren", "Instalar BibleStudy", "Installer BibleStudy", "Установка BibleStudy"],
        ["info1"] = [
            "Free Bible study software (MIT license). The program contains no Bible texts; they are loaded from the original sources on request.",
            "Freie Bibelstudien-Software (MIT-Lizenz). Das Programm enthält keine Bibeltexte; sie werden auf Wunsch von den Originalquellen geladen.",
            "Software libre de estudio bíblico (licencia MIT). El programa no contiene textos bíblicos; se cargan de las fuentes originales cuando lo pides.",
            "Logiciel libre d'étude biblique (licence MIT). Le programme ne contient aucun texte biblique ; ils sont chargés depuis les sources d'origine à la demande.",
            "Свободная программа для изучения Библии (лицензия MIT). В программе нет текстов Библии; по вашему запросу они загружаются из первоисточников."],
        ["info2"] = [
            "Your data (vaults, notes, documents, settings) is kept separate from the program in %LOCALAPPDATA%\\BibleStudy and stays untouched by updates and reinstalls.",
            "Deine Daten (Vaults, Notizen, Dokumente, Einstellungen) liegen getrennt vom Programm in %LOCALAPPDATA%\\BibleStudy und bleiben bei Updates und Neuinstallationen unberührt.",
            "Tus datos (bóvedas, notas, documentos, ajustes) se guardan aparte del programa en %LOCALAPPDATA%\\BibleStudy y no se tocan con las actualizaciones ni las reinstalaciones.",
            "Vos données (coffres, notes, documents, réglages) sont stockées séparément du programme dans %LOCALAPPDATA%\\BibleStudy et ne sont pas modifiées par les mises à jour ni les réinstallations.",
            "Ваши данные (хранилища, заметки, документы, настройки) хранятся отдельно от программы в %LOCALAPPDATA%\\BibleStudy и не затрагиваются при обновлении и переустановке."],
        ["folder"] = ["Installation folder", "Installationsordner", "Carpeta de instalación", "Dossier d'installation", "Папка установки"],
        ["change"] = ["Change …", "Ändern …", "Cambiar …", "Modifier …", "Изменить …"],
        ["close"] = ["Close", "Schließen", "Cerrar", "Fermer", "Закрыть"],
        ["installed"] = [
            "BibleStudy is installed.", "BibleStudy ist installiert.", "BibleStudy está instalado.", "BibleStudy est installé.", "BibleStudy установлена."],
        ["start"] = ["Start", "Starten", "Iniciar", "Lancer", "Запустить"],
        ["error"] = ["Error: ", "Fehler: ", "Error: ", "Erreur : ", "Ошибка: "],
    };

    static readonly int Index = Pick();

    static int Pick()
    {
        string? code = null;
        try
        {
            var file = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BibleStudy", "settings.json");
            if (File.Exists(file) && Regex.Match(File.ReadAllText(file), "\"Language\"\\s*:\\s*\"([a-zA-Z-]*)\"") is { Success: true } m) code = m.Groups[1].Value;
        }
        catch { }
        code ??= CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        var i = Array.IndexOf(Codes, code.ToLowerInvariant());
        return i < 0 ? 0 : i;
    }

    public static string T(string id, params object[] args)
    {
        var s = Texts[id][Index];
        return args.Length == 0 ? s : string.Format(s, args);
    }
}
