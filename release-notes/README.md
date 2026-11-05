# Release notes

One file per version: `<version>.md` with a German section (`## Neu in <version>`) and an English one (`## New in <version>`).
`publish-release.ps1` uses the file as the text of the GitHub release, and the program shows it in the patch notes.

## Kind labels (always use them)

Every item starts with a label in the form `**[Label]**`, so users see at a glance what an update is about.
The program shows the label as a colored tag. The first line of each section says which kinds the update contains.

| Deutsch | English | When | Color |
|---|---|---|---|
| `**[Neues Feature]**` | `**[New feature]**` | a bigger new function | green |
| `**[Mini-Feature]**` | `**[Mini feature]**` | a small addition or convenience | blue |
| `**[Verbesserung]**` | `**[Improvement]**` | something that existed got better or faster | purple |
| `**[Fehler behoben]**` | `**[Bug fix]**` | a defect is fixed | orange |
| `**[Hotfix]**` | `**[Hotfix]**` | an urgent fix released right after a version | red |
| `**[Sicherheit]**` | `**[Security]**` | a security or privacy change | crimson |

Example:

```
## Neu in 1.5.2
**Art dieses Updates:** Mini-Feature, Verbesserung
- **[Mini-Feature]** **Klick auf markierte Wörter:** …
- **[Verbesserung]** **Updatesuche:** …
```

## Keep bug fixes short

Features and improvements get a real explanation. A `[Fehler behoben]` / `[Hotfix]` item is only one short line
saying what now works (e.g. "Kapitel-Pfeile bleiben am Anfang und Ende der Bibel stehen"), without describing the defect or its cause.
