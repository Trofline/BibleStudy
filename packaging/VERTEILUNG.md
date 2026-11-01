# Verteilung – was vorbereitet ist und was Elias tun muss

Nichts davon wurde abgeschickt. Alles hier ist Vorbereitung; die Schritte nach außen macht Elias selbst.

| Kanal | Stand | Nächster Schritt (Elias) |
|---|---|---|
| **winget** | `packaging/winget/Trofline.BibleStudy.yaml` für 1.0.21 mit echter Prüfsumme | Auf Windows mit `winget validate` prüfen, Test-Installation, dann Pull Request an `microsoft/winget-pkgs` (oder `wingetcreate`) |
| **Scoop** | `packaging/scoop/biblestudy.json` (portable Version, `persist: Data`) | Eigenes Repository `scoop-bucket` anlegen, Datei in `bucket/` legen, `scoop bucket add` testen |
| **Flathub** | `packaging/flatpak/io.github.Trofline.BibleStudy.yml` (Entwurf, ungetestet) | Auf Linux mit `flatpak-builder` bauen; `metainfo.xml`, `.desktop`, Icon und Screenshots ergänzen; Pull Request an `flathub/flathub` |
| **Code-Signierung (SignPath Foundation)** | Antrag für Open-Source-Projekte: https://signpath.org/ → „Apply“ | Antrag stellen (Projektname, GitHub-Link, Lizenz, Beschreibung, Build über GitHub Actions nötig → `release.yml` ist vorbereitet) |
| **Microsoft Store** | nichts gebaut | Entwicklerkonto nötig (einmalige Gebühr), dann MSIX-Paket bauen; Signatur macht der Store |

Hinweis: Die winget-Prüfsumme gilt nur für die hochgeladene Datei von 1.0.21. Bei jedem Release müssen Version, URL und Prüfsumme neu gesetzt werden (Scoop kann das über `autoupdate` selbst).
