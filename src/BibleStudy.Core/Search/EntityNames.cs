using System.Text.RegularExpressions;
using BibleStudy.Core.Models;

namespace BibleStudy.Core.Search;

/// <summary>
/// Persons and places of the Bible as people write them: the data (Theographic) has English names (Isaac, Moses, Egypt), a German Bible says Isaak,
/// Mose, Ägypten. And the data does not list every verse that names someone: where a name belongs to one person or place only, the verses
/// whose text names it are added.
/// </summary>
public static partial class EntityNames
{
    /// <summary>German (and a few other) names of the data's English ones.</summary>
    static readonly Dictionary<string, string> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        // people
        ["Mose"] = "Moses", ["Isaak"] = "Isaac", ["Jakob"] = "Jacob", ["Josua"] = "Joshua", ["Johannes"] = "John", ["Johannes der Täufer"] = "John the Baptist", ["Täufer"] = "John the Baptist", ["Petrus"] = "Peter", ["Simon Petrus"] = "Simon Peter", ["Kephas"] = "Peter", ["Paulus"] = "Paul",
        ["Jesaja"] = "Isaiah", ["Jeremia"] = "Jeremiah", ["Hesekiel"] = "Ezekiel", ["Ezechiel"] = "Ezekiel", ["Elia"] = "Elijah", ["Elias"] = "Elijah", ["Elisa"] = "Elisha",
        ["Eva"] = "Eve", ["Kain"] = "Cain", ["Henoch"] = "Enoch", ["Sara"] = "Sarah", ["Sarai"] = "Sarah", ["Rebekka"] = "Rebekah", ["Rahel"] = "Rachel", ["Lea"] = "Leah",
        ["Josef"] = "Joseph", ["Joseph"] = "Joseph", ["Juda"] = "Judah", ["Mirjam"] = "Miriam", ["Simson"] = "Samson", ["Salomo"] = "Solomon", ["Batseba"] = "Bathsheba",
        ["Bathseba"] = "Bathsheba", ["Jonatan"] = "Jonathan", ["Noomi"] = "Naomi", ["Boas"] = "Boaz", ["Hiob"] = "Job", ["Ijob"] = "Job", ["Jona"] = "Jonah",
        ["Micha"] = "Micah", ["Habakuk"] = "Habakkuk", ["Zefanja"] = "Zephaniah", ["Zephanja"] = "Zephaniah", ["Sacharja"] = "Zechariah", ["Maleachi"] = "Malachi",
        ["Esra"] = "Ezra", ["Nehemia"] = "Nehemiah", ["Ester"] = "Esther", ["Maria"] = "Mary", ["Mirjam von Magdala"] = "Mary Magdalene", ["Maria Magdalena"] = "Mary Magdalene",
        ["Jakobus"] = "James", ["Andreas"] = "Andrew", ["Philippus"] = "Philip", ["Bartholomäus"] = "Bartholomew", ["Matthäus"] = "Matthew", ["Markus"] = "Mark",
        ["Lukas"] = "Luke", ["Timotheus"] = "Timothy", ["Stephanus"] = "Stephen", ["Nikodemus"] = "Nicodemus", ["Marta"] = "Martha", ["Zachäus"] = "Zacchaeus",
        ["Pilatus"] = "Pilate", ["Pontius Pilatus"] = "Pontius Pilate", ["Herodes"] = "Herod", ["Kaiphas"] = "Caiaphas", ["Kajaphas"] = "Caiaphas", ["Pharao"] = "Pharaoh", ["Nebukadnezar"] = "Nebuchadnezzar",
        ["Kyrus"] = "Cyrus", ["Kores"] = "Cyrus", ["Isebel"] = "Jezebel", ["Hiskia"] = "Hezekiah", ["Josia"] = "Josiah", ["Debora"] = "Deborah", ["Ismael"] = "Ishmael",
        ["Melchisedek"] = "Melchizedek", ["Jesus"] = "Jesus Christ", ["Jesus Christus"] = "Jesus Christ", ["Christus"] = "Jesus Christ", ["Jesu"] = "Jesus Christ", ["Priszilla"] = "Priscilla", ["Aquila"] = "Aquila", ["Apollos"] = "Apollos",
        ["Thomas"] = "Thomas", ["Matthias"] = "Matthias", ["Barnabas"] = "Barnabas", ["Silas"] = "Silas", ["Titus"] = "Titus", ["Philemon"] = "Philemon",
        ["Gideon"] = "Gideon", ["Simeon"] = "Simeon", ["Hanna"] = "Hannah", ["Eli"] = "Eli", ["Elisabeth"] = "Elizabeth", ["Zacharias"] = "Zechariah",
        ["Achab"] = "Ahab", ["Ahab"] = "Ahab", ["Rehabeam"] = "Rehoboam", ["Jerobeam"] = "Jeroboam", ["Usija"] = "Uzziah", ["Manasse"] = "Manasseh", ["Ephraim"] = "Ephraim",
        ["Ruben"] = "Reuben", ["Sebulon"] = "Zebulun", ["Naftali"] = "Naphtali", ["Naphtali"] = "Naphtali", ["Issaschar"] = "Issachar", ["Asser"] = "Asher",
        ["Goliat"] = "Goliath", ["Goliath"] = "Goliath", ["Delila"] = "Delilah", ["Kornelius"] = "Cornelius", ["Lydia"] = "Lydia", ["Tabita"] = "Tabitha",
        ["Ananias"] = "Ananias", ["Saphira"] = "Sapphira", ["Judas Iskariot"] = "Judas Iscariot", ["Jesse"] = "Jesse", ["Isai"] = "Jesse", ["Abimelech"] = "Abimelech",
        // places
        ["Ägypten"] = "Egypt", ["Galiläa"] = "Galilee", ["Judäa"] = "Judea", ["Kanaan"] = "Canaan", ["Rom"] = "Rome", ["Korinth"] = "Corinth", ["Antiochia"] = "Antioch",
        ["Damaskus"] = "Damascus", ["Kapernaum"] = "Capernaum", ["Golgatha"] = "Golgotha", ["Ninive"] = "Nineveh", ["Gomorra"] = "Gomorrah", ["Assyrien"] = "Assyria",
        ["Persien"] = "Persia", ["Mesopotamien"] = "Mesopotamia", ["Thessalonich"] = "Thessalonica", ["Galatien"] = "Galatia", ["Kreta"] = "Crete", ["Zypern"] = "Cyprus",
        ["Mazedonien"] = "Macedonia", ["Makedonien"] = "Macedonia", ["Athen"] = "Athens", ["Tyrus"] = "Tyre", ["Libanon"] = "Lebanon", ["Ölberg"] = "Mount of Olives",
        ["Rotes Meer"] = "Red Sea", ["Totes Meer"] = "Dead Sea", ["Salzmeer"] = "Dead Sea", ["See Genezareth"] = "Sea of Galilee", ["Betlehem"] = "Bethlehem",
        ["Bethlehem"] = "Bethlehem", ["Nazaret"] = "Nazareth", ["Zion"] = "Zion", ["Sinai"] = "Sinai", ["Horeb"] = "Horeb", ["Betanien"] = "Bethany", ["Bethanien"] = "Bethany",
        ["Kana"] = "Cana", ["Sichem"] = "Shechem", ["Silo"] = "Shiloh", ["Hebron"] = "Hebron", ["Beerscheba"] = "Beersheba", ["Gaza"] = "Gaza", ["Philister"] = "Philistia",
        ["Edom"] = "Edom", ["Moab"] = "Moab", ["Babel"] = "Babylon", ["Patmos"] = "Patmos", ["Kolossä"] = "Colossae", ["Laodizea"] = "Laodicea", ["Smyrna"] = "Smyrna",
        ["Pergamon"] = "Pergamum", ["Thyatira"] = "Thyatira", ["Sardes"] = "Sardis", ["Philadelphia"] = "Philadelphia", ["Euphrat"] = "Euphrates", ["Tigris"] = "Tigris",
        ["Emmaus"] = "Emmaus", ["Gethsemane"] = "Gethsemane", ["Arimathäa"] = "Arimathea", ["Cäsarea"] = "Caesarea", ["Joppe"] = "Joppa", ["Jafo"] = "Joppa",
    };

    /// <summary>The name to look for in the data: the English one of a German name, or the name as it was typed.</summary>
    public static string DataName(string typed) => Names.TryGetValue(typed.Trim(), out var english) ? english : typed.Trim();

    /// <summary>
    /// The entry the typed name means, among the items of one kind: the same name (or the English one of a German name) with the most references,
    /// then one that starts with it, then one spelled nearly alike (Isaak → Isaac, a letter apart).
    /// </summary>
    public static LexiconItem? Find(IEnumerable<LexiconItem> items, string kind, string typed) => Candidates(items, kind, typed).FirstOrDefault();

    /// <summary>
    /// All entries the typed name may mean, the most likely first: those whose name is it or holds it as a word ("Peter" → Simon Peter,
    /// "John" → John the Baptist, John the Apostle …), ordered by how often the Bible names them; failing that, one that starts with it,
    /// then one spelled nearly alike (Isac → Isaac).
    /// </summary>
    public static List<LexiconItem> Candidates(IEnumerable<LexiconItem> items, string kind, string typed)
    {
        var of = items.Where(i => i.Kind == kind).ToList();
        var asked = new[] { DataName(typed), typed.Trim() }.Select(n => SearchQuery.Fold(n, true)).Where(n => n.Length > 0).Distinct().ToList();
        if (asked.Count == 0) return [];
        static IEnumerable<string> WordsOf(string folded) => NameWords().Matches(folded).Select(m => m.Value);
        bool Holds(LexiconItem i)
        {
            var name = SearchQuery.Fold(i.Name, true);
            var before = name.IndexOf('(') is var p and > 0 ? name[..p].Trim() : name;   // "mary (mother of jesus)": the name is before the bracket
            return asked.Any(q => name == q || before == q || (q.Contains(' ') ? before.StartsWith(q + " ", StringComparison.Ordinal) : WordsOf(before).Contains(q)));
        }
        var found = of.Where(Holds).OrderByDescending(i => i.References).ToList();
        if (found.Count > 0) return found;
        var start = asked[0];
        if (start.Length >= 3 && of.Where(i => SearchQuery.Fold(i.Name, true).StartsWith(start, StringComparison.Ordinal)).OrderByDescending(i => i.References).ToList() is { Count: > 0 } begins) return begins;
        if (start.Length < 4) return [];
        var max = start.Length <= 6 ? 1 : 2;
        return of.Select(i => (Item: i, D: SearchEngine.Distance(SearchQuery.Fold(i.Name, true), start, max)))
            .Where(x => x.D <= max).OrderBy(x => x.D).ThenByDescending(x => x.Item.References).Select(x => x.Item).ToList();
    }

    [GeneratedRegex(@"[\p{L}\p{N}]+")] private static partial Regex NameWords();

    /// <summary>
    /// The names under which the verses of an entry can be found in the text: what was typed, its name in the data and its other names,
    /// each only if no other person or place has it (Maria and Juda are several; Abraham, Abram and Jerusalem are one).
    /// </summary>
    public static List<string> UniqueNames(IEnumerable<LexiconItem> items, LexiconEntry entry, string typed)
    {
        var counts = items.GroupBy(i => SearchQuery.Fold(i.Name, true)).ToDictionary(g => g.Key, g => g.Count());
        var mine = SearchQuery.Fold(entry.Name, true);
        if (counts.GetValueOrDefault(mine) > 1) return [];   // the name itself is shared: nothing from the text, it might be the other one
        var names = new List<string>();
        foreach (var n in new[] { typed.Trim(), entry.Name }.Concat(entry.AlsoCalled))
        {
            var f = SearchQuery.Fold(n, true);
            if (n.Length < 3 || n.Contains(' ') || names.Contains(n, StringComparer.OrdinalIgnoreCase)) continue;
            // an other name of the entry that is also the name of someone or something else (Jacob is also called Israel) is left out
            if (f != mine && counts.GetValueOrDefault(f) > 0) continue;
            // a typed German name is checked against the data the same way: Juda typed for one Judah while there are several
            if (n == typed.Trim() && DataName(typed) is var english && SearchQuery.Fold(english, true) != mine && counts.GetValueOrDefault(SearchQuery.Fold(english, true)) > 0) continue;
            names.Add(n);
        }
        return names;
    }

    /// <summary>The verses whose text names one of the names, as a word of its own or with an s (Abrahams); capitals count, a name is written with one.</summary>
    public static HashSet<VerseRef> NamedIn(IEnumerable<CorpusVerse> corpus, IReadOnlyCollection<string> names)
    {
        if (names.Count == 0) return [];
        var rx = new Regex(@"(?<![\p{L}\p{N}])(?:" + string.Join("|", names.Select(n => Regex.Escape(SearchQuery.Composed(n)))) + @")s?(?![\p{L}\p{N}])", RegexOptions.CultureInvariant);
        return corpus.Where(v => rx.IsMatch(v.Text)).Select(v => v.Ref).ToHashSet();
    }
}
