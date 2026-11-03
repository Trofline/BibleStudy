using System.Xml.Linq;

namespace BibleStudy.Core.Diagram;

/// <summary>A word of the original text as it appears in the sentence diagram.</summary>
public sealed record DWord(string Text, string Gloss, string Class, string Strong, string Lemma, int Chapter, int Verse, string? Person = null, string? Number = null, string? Mood = null);

/// <summary>How a modifier hangs below the line it belongs to.</summary>
public enum DModKind
{
    /// <summary>One word (article, adjective, adverb …) written along a slanted line.</summary>
    Word,
    /// <summary>A preposition on a slanted line, its object on a horizontal shelf.</summary>
    Prep,
    /// <summary>A noun in the genitive: slanted line without a word, the noun on a shelf.</summary>
    Genitive,
    /// <summary>Any other nominal phrase that depends on the line (indirect object, second object …).</summary>
    Plain,
    /// <summary>A phrase joined by a conjunction (the conjunction on a dashed slanted line).</summary>
    Conjunct,
    /// <summary>A phrase in apposition.</summary>
    Apposition,
    /// <summary>A whole clause that depends on a word (adverbial, relative or noun clause); drawn on its own line below.</summary>
    Clause,
}

public sealed class DMod
{
    public DModKind Kind;
    /// <summary>The word itself (Word) or the preposition / conjunction on the slanted line (Prep, Conjunct).</summary>
    public DWord? Word;
    public DPhrase? Phrase;
    public DClause? Clause;
    /// <summary>A noun clause (subject, object, complement) stands on a solid "stilt" instead of hanging on a dashed line.</summary>
    public bool Stilt;
}

/// <summary>A nominal phrase: its head word(s) on the line and everything that depends on it hanging below.</summary>
public sealed class DPhrase
{
    public List<DWord> Head = [];
    public List<DMod> Mods = [];
}

public sealed class DClause
{
    /// <summary>The conjunction that introduces the clause (shown on the link when the clause depends on something).</summary>
    public DWord? Conj;
    public DPhrase? Subject;
    /// <summary>"third/singular" and the like, when the subject is only in the verb ending (the diagram shows it in brackets).</summary>
    public string? ImpliedSubject;
    public List<DWord> Verb = [];
    public DPhrase? Object;
    public DPhrase? Complement;
    public List<DMod> Mods = [];
    /// <summary>No verb at all (a title, a greeting): only the phrase is drawn.</summary>
    public bool Fragment;
}

/// <summary>One clause, or several clauses joined by conjunctions (drawn one below the other and linked by a dashed line).</summary>
public sealed class DUnit
{
    public DClause? Clause;
    public List<DUnit> Items = [];
    /// <summary>Conjs[i] joins Items[i] and Items[i + 1].</summary>
    public List<DWord?> Conjs = [];
}

public sealed class DSentence
{
    public int Index;
    public int Chapter, FirstVerse, LastVerse;
    public bool Hebrew;
    public DUnit Root = new();
    public List<DWord> Words = [];
}

/// <summary>
/// Builds the diagram model of every sentence of a chapter from the MACULA syntax trees (Clear Bible, CC BY 4.0). The tree is read like this:
/// a clause (class cl) has a subject (role s), verb (v / vc / aux), object (o), complement (p) and adverbials (adv); a noun phrase has a head word,
/// words that depend on it (article, adjective) and phrases that depend on it (preposition, genitive); a group without a class joins its parts with a conjunction.
/// </summary>
public static class SentenceModel
{
    static string? Attr(XElement e, string name) => (string?)e.Attribute(name);
    static bool IsConj(string? c) => c is "conj" or "cj";
    static bool IsClause(XElement e) => e.Name == "wg" && Attr(e, "class") == "cl";
    static bool IsGroup(XElement e) => e.Name == "wg" && Attr(e, "class") is null;
    static IEnumerable<XElement> Kids(XElement e) => e.Elements().Where(k => k.Name == "w" || k.Name == "wg");

    static readonly System.Text.RegularExpressions.Regex RefPattern = new(@"(\d+):(\d+)!\d+$", System.Text.RegularExpressions.RegexOptions.Compiled);

    public static List<DSentence> ParseChapter(string xml, int chapter)
    {
        var result = new List<DSentence>();
        var doc = XDocument.Parse(xml);
        var index = 0;
        foreach (var sentence in doc.Descendants("sentence"))
        {
            var words = sentence.Descendants("w").Select(WordOf).ToList();
            if (words.Count == 0 || words.All(w => w.Chapter != chapter)) continue;
            var inChapter = words.Where(w => w.Chapter == chapter).ToList();
            var s = new DSentence
            {
                Index = ++index, Chapter = chapter, FirstVerse = inChapter.Min(w => w.Verse), LastVerse = inChapter.Max(w => w.Verse),
                Hebrew = sentence.Descendants("w").Any(w => Attr(w, "lang") == "H"),
                Words = words,
            };
            var top = Kids(sentence).FirstOrDefault(k => k.Name == "wg");
            if (top is not null) s.Root = Unit(top);
            else if (Kids(sentence).FirstOrDefault() is { } only) s.Root = new DUnit { Clause = Fragment(only) };
            else continue;
            result.Add(s);
        }
        return result;
    }

    static DWord WordOf(XElement w)
    {
        var hebrew = Attr(w, "lang") == "H";
        var strong = hebrew
            ? Attr(w, "strongnumberx") is { Length: > 0 } sx && System.Text.RegularExpressions.Regex.Match(sx, @"^\d+") is { Success: true } m ? "H" + int.Parse(m.Value) : ""
            : Attr(w, "strong") is { Length: > 0 } s && int.TryParse(s, out var n) ? "G" + n : "";
        int chapter = 0, verse = 0;
        if (Attr(w, "ref") is { } r && RefPattern.Match(r) is { Success: true } rm) { chapter = int.Parse(rm.Groups[1].Value); verse = int.Parse(rm.Groups[2].Value); }
        var gloss = (Attr(w, "gloss") ?? Attr(w, "english") ?? "").Replace('.', ' ').Trim();
        return new DWord(w.Value.Trim(), gloss, Attr(w, "class") ?? "", strong, Attr(w, "lemma") ?? "", chapter, verse,
            Attr(w, "person"), Attr(w, "number"), Attr(w, "mood") ?? Attr(w, "type"));
    }

    // ---- units and clauses -------------------------------------------------------------------------------------------------

    static bool HasVerbRole(XElement e) => Kids(e).Any(k => Attr(k, "role") is "v" or "vc" or "aux" || (k.Name == "wg" && Attr(k, "class") == "vp"));

    /// <summary>A clause (or a group) that has no verb of its own and only joins other clauses: "and … and …".</summary>
    static bool Coordinates(XElement e) =>
        (IsClause(e) || IsGroup(e)) && !HasVerbRole(e) && Kids(e).All(k => Attr(k, "role") is null) && Kids(e).Any(k => IsClause(k) || (IsGroup(k) && Kids(k).Any(IsClause)));

    static DUnit Unit(XElement e)
    {
        if (IsClause(e) && !Coordinates(e)) return new DUnit { Clause = Clause(e) };
        if (Coordinates(e))
        {
            var unit = new DUnit();
            DWord? pending = null;
            foreach (var k in Kids(e))
            {
                if (k.Name == "w" && IsConj(Attr(k, "class"))) { pending = WordOf(k); continue; }
                var item = Unit(k);
                if (unit.Items.Count > 0)
                {
                    // the conjunction goes on the dashed line between the two clauses (also when it was found inside a nested group)
                    var joint = pending ?? item.Clause?.Conj;
                    if (pending is null && item.Clause is not null) item.Clause.Conj = null;
                    unit.Conjs.Add(joint);
                }
                else if (pending is not null && item.Clause is not null) item.Clause.Conj ??= pending;
                unit.Items.Add(item); pending = null;
            }
            return unit.Items.Count == 1 ? unit.Items[0] : unit;
        }
        return new DUnit { Clause = Fragment(e) };
    }

    static DClause Fragment(XElement e) => new() { Fragment = true, Subject = Phrase(e) };

    static DClause Clause(XElement cl)
    {
        var c = new DClause();
        void Consume(XElement kid)
        {
            var role = Attr(kid, "role");
            var cls = Attr(kid, "class");
            if (kid.Name == "w")
            {
                if (role is "v" or "vc" or "aux") { c.Verb.Add(WordOf(kid)); return; }
                if (IsConj(cls)) { if (c.Conj is null) c.Conj = WordOf(kid); else c.Mods.Add(WordMod(kid)); return; }
                if (role == "s" && c.Subject is null) { c.Subject = Phrase(kid); return; }
                if (role == "o" && c.Object is null) { c.Object = Phrase(kid); return; }
                if (role == "p" && c.Complement is null) { c.Complement = Phrase(kid); return; }
                if (role is "io" or "o2") { c.Mods.Add(new DMod { Kind = DModKind.Plain, Phrase = Phrase(kid) }); return; }
                c.Mods.Add(WordMod(kid));
                return;
            }
            // a group of words
            if (IsGroup(kid) && role is null) { foreach (var g in Kids(kid)) Consume(g); return; }   // transparent: its parts belong to this clause
            if (cls == "vp") { foreach (var w in kid.Descendants("w")) c.Verb.Add(WordOf(w)); return; }
            if (cls == "cl")
            {
                var sub = Clause(kid);
                if ((role is "s" or "o" or "p") && SlotFree(c, role)) { SetSlot(c, role, new DPhrase { Mods = { new DMod { Kind = DModKind.Clause, Clause = sub, Stilt = true } } }); return; }
                c.Mods.Add(new DMod { Kind = DModKind.Clause, Clause = sub });
                return;
            }
            if (cls == "pp") { c.Mods.Add(PrepMod(kid)); return; }
            if (role == "s" && c.Subject is null) { c.Subject = Phrase(kid); return; }
            if (role == "o" && c.Object is null) { c.Object = Phrase(kid); return; }
            if (role == "p" && c.Complement is null) { c.Complement = Phrase(kid); return; }
            c.Mods.Add(new DMod { Kind = DModKind.Plain, Phrase = Phrase(kid) });
        }
        foreach (var kid in Kids(cl)) Consume(kid);

        // a clause that only holds one other clause (and perhaps a conjunction) is that clause: "but so that …" is one link, not an empty line in between
        if (c.Verb.Count == 0 && c.Subject is null && c.Object is null && c.Complement is null && c.Mods.Count == 1 && c.Mods[0] is { Kind: DModKind.Clause, Stilt: false, Clause: { } inner })
        {
            inner.Conj = c.Conj is null ? inner.Conj : inner.Conj is null ? c.Conj : Join([c.Conj, inner.Conj]);
            return inner;
        }
        if (c.Verb.Count == 0 && c.Subject is not null && c.Object is null && c.Complement is null && c.Mods.Count == 0) c.Fragment = true;
        if (c.Subject is null && c.Verb.Count > 0)
        {
            var v = c.Verb[^1];
            var finite = v.Mood is "indicative" or "subjunctive" or "imperative" or "optative" or "qatal" or "yiqtol" or "wayyiqtol" or "cohortative" or "jussive";
            if (finite && v.Person is { Length: > 0 } p) c.ImpliedSubject = p + "/" + (v.Number ?? "singular");
        }
        return c;
    }

    static bool SlotFree(DClause c, string? role) => role switch { "s" => c.Subject is null, "o" => c.Object is null, "p" => c.Complement is null, _ => false };

    static void SetSlot(DClause c, string? role, DPhrase p)
    {
        if (role == "s") c.Subject = p; else if (role == "o") c.Object = p; else if (role == "p") c.Complement = p;
    }

    // ---- phrases -----------------------------------------------------------------------------------------------------------

    static DMod WordMod(XElement w) => new() { Kind = DModKind.Word, Word = WordOf(w) };

    static DMod PrepMod(XElement pp)
    {
        var preps = Kids(pp).Where(k => k.Name == "w" && Attr(k, "class") == "prep").ToList();
        var rest = Kids(pp).Where(k => !preps.Contains(k)).ToList();
        var mod = new DMod { Kind = DModKind.Prep, Word = preps.Count > 0 ? Join(preps.Select(WordOf)) : null };
        // the object: the first nominal part; anything else of the prepositional phrase depends on it
        mod.Phrase = rest.Count == 1 ? Phrase(rest[0]) : rest.Count > 1 ? Phrase(new XElement("wg", new XAttribute("class", "np"), rest.Select(r => new XElement(r)))) : new DPhrase();
        return mod;
    }

    static DWord Join(IEnumerable<DWord> words)
    {
        var list = words.ToList();
        return list.Count == 1 ? list[0] : list[0] with { Text = string.Join(" ", list.Select(w => w.Text)), Gloss = string.Join(" ", list.Select(w => w.Gloss)) };
    }

    static bool IsNoun(string? c) => c is "noun" or "pron" or "num";
    static bool IsMinorWord(string? c) => c is "det" or "art" or "conj" or "cj" or "prep" or "om" or "ptcl" or "adv" or "ij" or "intj";

    public static DPhrase Phrase(XElement e)
    {
        if (e.Name == "w") return new DPhrase { Head = [WordOf(e)] };
        var cls = Attr(e, "class");
        if (cls == "cl") return new DPhrase { Mods = { new DMod { Kind = DModKind.Clause, Clause = Clause(e), Stilt = true } } };
        if (cls == "pp") return new DPhrase { Mods = { PrepMod(e) } };

        var kids = Kids(e).ToList();
        var ph = new DPhrase();
        XElement? head = kids.FirstOrDefault(k => k.Name == "w" && IsNoun(Attr(k, "class")))
                      ?? kids.FirstOrDefault(k => k.Name == "w" && !IsMinorWord(Attr(k, "class")))
                      ?? kids.FirstOrDefault(k => k.Name == "wg" && (Attr(k, "class") is "np" or "nump" or "adjp" or "relp" or null) && Attr(k, "type") != "apposition");
        if (head is not null && head.Name == "w") ph.Head = [WordOf(head)];
        else if (head is not null) ph = Phrase(head);
        else if (kids.FirstOrDefault() is { } first) ph = Phrase(first);

        DWord? pending = null;
        void AddKid(XElement k)
        {
            if (ReferenceEquals(k, head)) return;
            if (k.Name == "w")
            {
                if (IsConj(Attr(k, "class"))) { pending = WordOf(k); return; }
                ph.Mods.Add(WordMod(k));
                return;
            }
            var kc = Attr(k, "class");
            if (kc == "pp") { ph.Mods.Add(PrepMod(k)); pending = null; return; }
            if (kc == "cl") { ph.Mods.Add(new DMod { Kind = DModKind.Clause, Clause = Clause(k) }); pending = null; return; }
            if (kc == "adjp") { foreach (var w in k.Descendants("w").Where(w => !IsConj(Attr(w, "class")))) ph.Mods.Add(WordMod(w)); pending = null; return; }
            if (kc is null) { foreach (var g in Kids(k)) AddKid(g); return; }   // a group: its parts follow the same rules
            var nested = Phrase(k);
            if (pending is not null) ph.Mods.Add(new DMod { Kind = DModKind.Conjunct, Word = pending, Phrase = nested });
            else if (Attr(k, "type") == "apposition") ph.Mods.Add(new DMod { Kind = DModKind.Apposition, Phrase = nested });
            else if (k.Descendants("w").Any(w => Attr(w, "case") == "genitive" && IsNoun(Attr(w, "class")))) ph.Mods.Add(new DMod { Kind = DModKind.Genitive, Phrase = nested });
            else ph.Mods.Add(new DMod { Kind = DModKind.Plain, Phrase = nested });
            pending = null;
        }
        foreach (var k in kids) AddKid(k);
        return ph;
    }
}
