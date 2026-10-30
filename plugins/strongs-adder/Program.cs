using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace StrongsAdder;

// BibleStudy plugin: adds Strong's numbers to a translation that has none. It is only an estimate (see Aligner).
// BibleStudy starts this program and sends one JSON line per request (see PLUGINS.md). The work itself is done in the background
// through the program's local interface, which also shows the dialogs.
static class Program
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(10) };
    static readonly object OutLock = new();
    static int _running;

    // The reference translation per language: a free translation whose Strong's numbers come from its source.
    static readonly (string Code, string Name, string[] Aliases, string Reference)[] Languages =
    [
        ("deu", "Deutsch", ["deu", "German", "Deutsch", "de"], "deu_l12"),
        ("eng", "English", ["eng", "English", "en"], "eng_kjv"),
        ("spa", "Español", ["spa", "Spanish", "Español", "es"], "spa_r09"),
        ("fra", "Français", ["fra", "French", "Français", "fr"], "fra_ost"),
    ];

    static readonly string[] Books =
    [
        "GEN", "EXO", "LEV", "NUM", "DEU", "JOS", "JDG", "RUT", "1SA", "2SA", "1KI", "2KI", "1CH", "2CH", "EZR", "NEH", "EST", "JOB", "PSA", "PRO", "ECC", "SNG",
        "ISA", "JER", "LAM", "EZK", "DAN", "HOS", "JOL", "AMO", "OBA", "JON", "MIC", "NAM", "HAB", "ZEP", "HAG", "ZEC", "MAL",
        "MAT", "MRK", "LUK", "JHN", "ACT", "ROM", "1CO", "2CO", "GAL", "EPH", "PHP", "COL", "1TH", "2TH", "1TI", "2TI", "TIT", "PHM", "HEB", "JAS", "1PE", "2PE", "1JN", "2JN", "3JN", "JUD", "REV",
    ];

    static async Task Main()
    {
        Console.OutputEncoding = new UTF8Encoding(false);
        string? line;
        while ((line = await Console.In.ReadLineAsync()) is not null)
        {
            JsonNode? request;
            try { request = JsonNode.Parse(line); } catch (JsonException) { continue; }
            if (request is null) continue;
            var id = request["id"]?.GetValue<int>() ?? 0;
            var method = request["method"]?.GetValue<string>();
            try
            {
                if (method == "command")
                    Reply(id, new JsonObject { ["message"] = Start(request["params"]?["id"]?.GetValue<string>()) });
                else Reply(id, error: "unsupported");
            }
            catch (Exception e) { Reply(id, error: e.Message); }
        }
    }

    static void Reply(int id, JsonNode? result = null, string? error = null)
    {
        var o = new JsonObject { ["id"] = id };
        if (error is not null) o["error"] = error; else o["result"] = result;
        lock (OutLock) { Console.Out.WriteLine(o.ToJsonString()); Console.Out.Flush(); }
    }

    /// <summary>Answers at once (a request must not take longer than 30 seconds) and does the work in the background.</summary>
    static string Start(string? command)
    {
        if (Interlocked.Exchange(ref _running, 1) == 1) return T("Läuft schon.", "Already running.");
        _ = Task.Run(async () =>
        {
            try
            {
                if (command == "remove-strongs") await RemoveAsync();
                else await AddAsync();
            }
            catch (Exception e)
            {
                try { await Host("ui_message", new JsonObject { ["title"] = T("Strong-Nummern ergänzen", "Add Strong's numbers"), ["text"] = T("Es ist ein Fehler aufgetreten: ", "An error occurred: ") + e.Message }); } catch { /* the program is gone */ }
            }
            finally { Interlocked.Exchange(ref _running, 0); }
        });
        return T("gestartet.", "started.");
    }

    // ---- texts ------------------------------------------------------------------------------------------------------

    static string _lang = "";
    static string T(string de, string en)
    {
        if (_lang == "")
        {
            try
            {
                var data = Environment.GetEnvironmentVariable("BIBLESTUDY_DATA") ?? "";
                _lang = JsonNode.Parse(File.ReadAllText(Path.Combine(data, "settings.json")))?["Language"]?.GetValue<string>() ?? "de";
            }
            catch { _lang = "de"; }
        }
        return _lang.StartsWith("de", StringComparison.OrdinalIgnoreCase) ? de : en;
    }

    // ---- the program's interface -----------------------------------------------------------------------------------------

    static async Task<JsonNode> Host(string tool, JsonObject? args = null)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, Environment.GetEnvironmentVariable("BIBLESTUDY_API"))
        {
            Content = new StringContent(new JsonObject { ["tool"] = tool, ["args"] = args ?? [] }.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        req.Headers.Add("X-Token", Environment.GetEnvironmentVariable("BIBLESTUDY_TOKEN") ?? "");
        using var response = await Http.SendAsync(req);
        var node = JsonNode.Parse(await response.Content.ReadAsStringAsync()) ?? new JsonObject();
        if (node["error"] is { } err) throw new InvalidOperationException(err.ToString());
        return node;
    }

    static async Task Status(string text) { try { await Host("ui_status", new JsonObject { ["text"] = text }); } catch { /* only a progress note */ } }

    static async Task<string?> Choose(string title, string text, IEnumerable<(string Id, string Label)> options, string ok, string cancel)
    {
        var arr = new JsonArray();
        foreach (var (id, label) in options) arr.Add(new JsonObject { ["id"] = id, ["label"] = label });
        var answer = await Host("ui_choose", new JsonObject { ["title"] = title, ["text"] = text, ["options"] = arr, ["ok"] = ok, ["cancel"] = cancel });
        return answer["id"]?.GetValue<string>();
    }

    // ---- adding the numbers -----------------------------------------------------------------------------------------

    static async Task AddAsync()
    {
        var title = T("Strong-Nummern nachträglich ergänzen", "Add Strong's numbers afterwards");
        var warning = await Host("ui_message", new JsonObject
        {
            ["title"] = title,
            ["text"] = T(
                "Dieses Plugin versucht, Strong-Nummern in eine Übersetzung einzufügen, die keine hat. Dazu vergleicht es sie Wort für Wort mit einer Übersetzung, die Strong-Nummern hat (Deutsch: Lutherbibel 1912). Das ist nur eine Schätzung.\n\n"
                + "• Es gibt KEINE Garantie, dass die Nummern fehlerfrei sind. Manche Wörter bekommen keine Nummer, manche eine falsche.\n"
                + "• Prüfe das Ergebnis, zum Beispiel stichprobenartig mit einer KI, der du vertraust. Auch eine KI kann sich irren.\n"
                + "• Im Zweifel bleibe lieber bei einer Übersetzung, deren Nummern aus der Quelle stammen (Deutsch: Lutherbibel 1912, Englisch: KJV). Dort sind sie nicht geschätzt.\n"
                + "• Ergänzte Nummern sind im Text gelb gestrichelt unterstrichen.\n"
                + "• Der Text deiner Übersetzung wird nicht verändert und nicht hochgeladen. Gespeichert werden nur die Nummern, auf deinem Computer.",
                "This plugin tries to insert Strong's numbers into a translation that has none. It compares the translation word by word with one that has them (German: Luther 1912). This is only an estimate.\n\n"
                + "• There is NO guarantee that the numbers are free of errors. Some words get no number, some a wrong one.\n"
                + "• Check the result, for example by sampling it with an AI you trust. An AI can be wrong too.\n"
                + "• When in doubt, stay with a translation whose numbers come from the source (German: Luther 1912, English: KJV). There they are not estimated.\n"
                + "• Added numbers are underlined with a dashed yellow line in the text.\n"
                + "• The text of your translation is not changed and not uploaded. Only the numbers are stored, on your computer."),
            ["buttons"] = new JsonArray(T("Verstanden, weiter", "Understood, continue"), T("Abbrechen", "Cancel")),
        });
        if (warning["button"]?.GetValue<int>() != 0) return;

        var langId = await Choose(title, T("Wähle die Sprache der Übersetzung.", "Choose the language of the translation."),
            Languages.Select(l => (l.Code, l.Name)), T("Weiter", "Next"), T("Abbrechen", "Cancel"));
        if (langId is null) return;
        var lang = Languages.First(l => l.Code == langId);

        // translations of that language that have no Strong's numbers (neither from their source nor added before)
        var found = new Dictionary<string, (string Name, string Short)>();
        var withNumbers = new HashSet<string>();
        var referenceKnown = false;
        foreach (var alias in lang.Aliases)
        {
            var list = (await Host("list_translations", new JsonObject { ["language"] = alias }))["translations"]?.AsArray();
            if (list is null) continue;
            foreach (var t in list)
            {
                var tid = t?["id"]?.GetValue<string>() ?? "";
                if (tid == lang.Reference) referenceKnown = true;
                if (t?["hasStrongs"]?.GetValue<bool>() == true) { withNumbers.Add(tid); continue; }
                if (tid.Length > 0 && !tid.StartsWith("plugin:")) found[tid] = (t?["name"]?.GetValue<string>() ?? tid, t?["shortName"]?.GetValue<string>() ?? tid);
            }
        }
        if (!referenceKnown)
        {
            await Host("ui_message", new JsonObject { ["title"] = title, ["text"] = T("Die Vorlage mit Strong-Nummern ist nicht verfügbar: ", "The reference translation is not available: ") + lang.Reference });
            return;
        }
        if (found.Count == 0)
        {
            await Host("ui_message", new JsonObject { ["title"] = title, ["text"] = T("In dieser Sprache gibt es keine Übersetzung ohne Strong-Nummern.", "There is no translation without Strong's numbers in this language.") });
            return;
        }
        var pick = await Choose(title, T("Wähle die Übersetzung, in die Strong-Nummern ergänzt werden sollen. Vorlage: ", "Choose the translation that should get Strong's numbers. Reference: ") + lang.Reference,
            found.OrderBy(x => x.Value.Name).Select(x => (x.Key, $"{x.Value.Name} ({x.Value.Short})")), T("Strong-Nummern ergänzen", "Add Strong's numbers"), T("Abbrechen", "Cancel"));
        if (pick is null) return;

        // ---- the work: chapter by chapter ----
        var bookList = (await Host("list_books", new JsonObject { ["translation"] = pick }))["books"]?.AsArray() ?? [];
        var chapters = bookList.Where(b => Books.Contains(b?["id"]?.GetValue<string>() ?? "")).Select(b => (Id: b!["id"]!.GetValue<string>(), Name: b["name"]?.GetValue<string>() ?? "", Count: b["chapters"]?.GetValue<int>() ?? 0)).ToList();
        var total = chapters.Sum(c => c.Count);
        // 1st pass: what the reference translation teaches about its own words (which word carries which number, which words carry none)
        var lexicon = new Lexicon();
        var learned = 0;
        foreach (var (book, name, count) in chapters)
            for (var ch = 1; ch <= count; ch++)
            {
                learned++;
                if (learned % 5 == 0 || ch == 1) await Status(T($"Strong-Nummern, Schritt 1 von 2 (Vorlage lesen): {name} {ch} – {100 * learned / total} %", $"Strong's numbers, step 1 of 2 (reading the reference): {name} {ch} – {100 * learned / total}%"));
                try
                {
                    var reference = await Host("get_chapter_strongs", new JsonObject { ["translation"] = lang.Reference, ["book"] = book, ["chapter"] = ch });
                    foreach (var rv in reference["verses"]?.AsArray() ?? []) lexicon.Learn(RefWords(rv), lang.Code);
                }
                catch (InvalidOperationException) { /* chapter missing in the reference */ }
            }

        // 2nd pass: the numbers of the chosen translation, chapter by chapter
        var done = 0; long words = 0, tagged = 0;
        foreach (var (book, name, count) in chapters)
            for (var ch = 1; ch <= count; ch++)
            {
                done++;
                if (done % 5 == 0 || ch == 1) await Status(T($"Strong-Nummern, Schritt 2 von 2 (ergänzen): {name} {ch} – {100 * done / total} %", $"Strong's numbers, step 2 of 2 (adding): {name} {ch} – {100 * done / total}%"));
                try
                {
                    var own = await Host("get_chapter_strongs", new JsonObject { ["translation"] = pick, ["book"] = book, ["chapter"] = ch });
                    var reference = await Host("get_chapter_strongs", new JsonObject { ["translation"] = lang.Reference, ["book"] = book, ["chapter"] = ch });
                    var refVerses = (reference["verses"]?.AsArray() ?? []).ToDictionary(v => v!["number"]!.GetValue<int>(), v => v!);
                    var verses = new JsonArray();
                    foreach (var v in own["verses"]?.AsArray() ?? [])
                    {
                        var number = v!["number"]!.GetValue<int>();
                        var ownWords = (v["words"]?.AsArray() ?? []).Select(w => w!["w"]!.GetValue<string>()).ToList();
                        words += ownWords.Count;
                        if (!refVerses.TryGetValue(number, out var rv)) continue;
                        var refWords = RefWords(rv);
                        var tags = Aligner.Align(ownWords, refWords, lang.Code, lexicon);
                        tagged += tags.Count;
                        var obj = new JsonObject();
                        foreach (var (i, nums) in tags) obj[i.ToString()] = new JsonArray(nums.Select(n => (JsonNode)n).ToArray());
                        verses.Add(new JsonObject { ["number"] = number, ["tags"] = obj });
                    }
                    await Host("set_strongs_overlay", new JsonObject { ["translation"] = pick, ["reference"] = lang.Reference, ["book"] = book, ["chapter"] = ch, ["verses"] = verses });
                }
                catch (InvalidOperationException) { /* this chapter is missing in one of the two translations: skipped */ }
            }
        await Host("commit_strongs_overlay", new JsonObject { ["translation"] = pick });
        await Status("");
        var percent = words == 0 ? 0 : (int)(100 * tagged / words);
        await Host("ui_message", new JsonObject
        {
            ["title"] = T("Fertig – bitte prüfen", "Done – please check"),
            ["text"] = T(
                $"Strong-Nummern sind ergänzt: {percent} % der Wörter haben eine Nummer bekommen (die übrigen bleiben ohne).\n\n"
                + "Das ist eine Schätzung und keine Garantie. Lass das Ergebnis am besten schnell von einer KI deines Vertrauens stichprobenartig prüfen. Aber auch das ist keine Garantie, denn eine KI kann Fehler machen.\n\n"
                + "Im Text sind ergänzte Nummern gelb gestrichelt unterstrichen. Schalte die „Wortstudie“ ein und klicke ein Wort an. Im Zweifel nimm die Lutherbibel 1912 (Nummern aus der Quelle).",
                $"Strong's numbers were added: {percent}% of the words got a number (the others stay without).\n\n"
                + "This is an estimate, not a guarantee. It is best to have an AI you trust check a sample quickly. Even that is no guarantee, because an AI can make mistakes.\n\n"
                + "In the text, added numbers are underlined with a dashed yellow line. Turn on “Word study” and click a word. When in doubt, use Luther 1912 (numbers from the source)."),
        });
    }

    static List<(string Word, string[]? Strongs)> RefWords(JsonNode? verse) =>
        (verse?["words"]?.AsArray() ?? []).Select(w => (Word: w!["w"]!.GetValue<string>(), Strongs: w["s"] is JsonArray s ? s.Select(x => x!.GetValue<string>()).ToArray() : null)).ToList();

    static async Task RemoveAsync()
    {
        var title = T("Ergänzte Strong-Nummern entfernen", "Remove added Strong's numbers");
        var list = (await Host("list_strongs_overlays"))["overlays"]?.AsArray() ?? [];
        if (list.Count == 0) { await Host("ui_message", new JsonObject { ["title"] = title, ["text"] = T("Es sind keine ergänzten Strong-Nummern vorhanden.", "There are no added Strong's numbers.") }); return; }
        var pick = await Choose(title, T("Von welcher Übersetzung sollen die ergänzten Nummern entfernt werden? Der Text bleibt unverändert.", "From which translation should the added numbers be removed? The text stays unchanged."),
            list.Select(o => (o!["translation"]!.GetValue<string>(), $"{o["translation"]} ({T("Vorlage", "reference")}: {o["reference"]})")), T("Entfernen", "Remove"), T("Abbrechen", "Cancel"));
        if (pick is null) return;
        await Host("remove_strongs_overlay", new JsonObject { ["translation"] = pick });
        await Host("ui_message", new JsonObject { ["title"] = title, ["text"] = T("Die ergänzten Nummern wurden entfernt.", "The added numbers were removed.") });
    }
}
