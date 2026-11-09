using BibleStudy.Core.Import;
using BibleStudy.Core.Models;
using BibleStudy.Core.Storage;

// Only calls that every version since 1.6 has: this file is compiled against the old version too.
var mode = args[0]; var dir = args[1];
Directory.CreateDirectory(dir);
var vaultPath = Path.Combine(dir, "vault.db");
var localPath = Path.Combine(dir, "local.db");
VerseRef R(int v) => new("GEN", 1, v);

if (mode == "seed")
{
    var v = new VaultStore(vaultPath, "probe");
    var folder = v.AddFolder("Ordner");
    v.AddMark("local:probe", R(1), 3, 9, "Anfang", "#FFD54F", "Notiz zur Markierung", folder, ["tag"]);
    v.AddMark("local:probe", R(2), -1, -1, "ganzer Vers", "#F26D6D", "", null, []);
    v.AddMark("local:probe", R(3), 0, 4, "über Verse", "#6FA8F2", "", null, [], 5);
    v.SetNote(R(1), "Eine Notiz mit [[Erstes Dokument]]");
    v.AddDocument("Erstes Dokument", "Inhalt des Dokuments", null, false, ["doc"], folder);
    v.AddDocument("Zweites Dokument", "Siehe [[Erstes Dokument]]", null, false, []);
    v.AddTodo("Aufgabe", R(1));
    var local = new LocalBibleStore(localPath);
    local.Save(new ParsedBible("Probe", "PRB", "de", [new ParsedVerse("GEN", 1, 1, [new Segment("Im Anfang schuf Gott")])], []));
    Console.WriteLine("SEEDED");
    return 0;
}

#if VERIFY
var fails = 0;
void Check(string name, bool ok) { Console.WriteLine((ok ? "PASS " : "FAIL ") + name); if (!ok) fails++; }
var store = new VaultStore(vaultPath, "probe");
var marks = store.GetMarks();
Check("three marks", marks.Count == 3);
Check("part mark with note, tag and folder", marks.Any(m => m.Text == "Anfang" && m.Start == 3 && m.End == 9 && m.Note == "Notiz zur Markierung" && m.Tags.Contains("tag") && m.FolderId is not null));
Check("whole verse", marks.Any(m => m.WholeVerse && m.Ref.Verse == 2));
Check("mark over several verses", marks.Any(m => m.MultiVerse && m.LastVerse == 5));
Check("note", store.GetNotes().Single().Text.Contains("Erstes Dokument"));
var docs = store.GetDocuments();
Check("documents", docs.Count == 2 && docs.Any(d => d.Title == "Erstes Dokument" && d.Body == "Inhalt des Dokuments" && d.Tags.Contains("doc")));
Check("link leads to the document", docs.Single(d => d.Title == "Zweites Dokument").Body.Contains("Erstes Dokument"));
Check("folder", store.GetFolders().Single().Name == "Ordner");
Check("to-do", store.GetTodos().Single().Text == "Aufgabe");
Check("new tables work on old data", store.GetMarkPlaces().Count == 0 && store.Fingerprint().Length > 0);
var bible = new LocalBibleStore(localPath);
var list = await bible.GetTranslationsAsync();
Check("imported bible", list.Count == 1 && (await bible.GetChapterAsync(list[0].Id, "GEN", 1)).Verses[0].Text == "Im Anfang schuf Gott");
Check("import still known", bible.GetOutdated().Count <= 1);
Console.WriteLine(fails == 0 ? "ALL OK" : $"{fails} FAILED");
return fails == 0 ? 0 : 1;
#else
return 2;
#endif
