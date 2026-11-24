# Verteilung – was vorbereitet ist und was Elias tun muss

Nichts davon wurde abgeschickt. Alles hier ist Vorbereitung; die Schritte nach außen macht Elias selbst.

| Kanal | Stand | Nächster Schritt (Elias) |
|---|---|---|
| **winget** | `packaging/winget/1.8.0/` für 1.8.0: vier Dateien (version, installer, locale en-US und de-DE), echte Prüfsumme, `winget validate` erfolgreich | Pull Request an `microsoft/winget-pkgs` unter `manifests/t/Trofline/BibleStudy/1.8.0/`, am einfachsten mit `wingetcreate submit packaging\winget\1.8.0`. Spätere Versionen: `wingetcreate update Trofline.BibleStudy -v X -u <Setup-URL> --submit` |
| **Scoop** | `packaging/scoop/biblestudy.json` (portable Version, `persist: Data`) | Eigenes Repository `scoop-bucket` anlegen, Datei in `bucket/` legen, `scoop bucket add` testen |
| **Flathub** | `packaging/flatpak/io.github.Trofline.BibleStudy.yml` (Entwurf, ungetestet) | Auf Linux mit `flatpak-builder` bauen; `metainfo.xml`, `.desktop`, Icon und Screenshots ergänzen; Pull Request an `flathub/flathub` |
| **Code-Signierung (SignPath Foundation)** | Antrag für Open-Source-Projekte: https://signpath.org/ → „Apply“ | Antrag stellen (Projektname, GitHub-Link, Lizenz, Beschreibung, Build über GitHub Actions nötig → `release.yml` ist vorbereitet) |
| **Microsoft Store** | nichts gebaut | Entwicklerkonto nötig (einmalige Gebühr), dann MSIX-Paket bauen; Signatur macht der Store |

Hinweis: Die winget-Prüfsumme gilt nur für die hochgeladene Setup-Datei von 1.8.0. Nur volle Releases mit Installer kommen zu winget, Schnell-Updates nicht; das Programm aktualisiert sich ohnehin selbst. Bei jedem Release müssen Version, URL und Prüfsumme neu gesetzt werden (Scoop kann das über `autoupdate` selbst).
