using BibleStudy.Core.Import;

namespace BibleStudy.App;

/// <summary>
/// Who wrote which book, by the traditional attribution (several are debated: Hebrews is anonymous, many Psalms are not by David,
/// the Pentateuch, Kings and Chronicles are attributed in different ways). Used only to group books for the writers' network.
/// </summary>
static class AuthorMap
{
    static readonly Dictionary<string, string> Writers = Build();

    static Dictionary<string, string> Build()
    {
        var map = new Dictionary<string, string>();
        void Set(string writer, params string[] books) { foreach (var b in books) map[b] = writer; }
        Set(L("Mose"), "GEN", "EXO", "LEV", "NUM", "DEU");
        Set(L("Josua"), "JOS");
        Set(L("Samuel"), "JDG", "RUT", "1SA", "2SA");
        Set(L("Jeremia"), "JER", "LAM", "1KI", "2KI");
        Set(L("Esra"), "1CH", "2CH", "EZR", "NEH");
        Set(L("Unbekannt"), "EST", "JOB", "HEB");
        Set(L("David (u. a.)"), "PSA");
        Set(L("Salomo"), "PRO", "ECC", "SNG");
        Set(L("Jesaja"), "ISA");
        Set(L("Hesekiel"), "EZK");
        Set(L("Daniel"), "DAN");
        Set(L("Hosea"), "HOS"); Set(L("Joel"), "JOL"); Set(L("Amos"), "AMO"); Set(L("Obadja"), "OBA"); Set(L("Jona"), "JON"); Set(L("Micha"), "MIC");
        Set(L("Nahum"), "NAM"); Set(L("Habakuk"), "HAB"); Set(L("Zefanja"), "ZEP"); Set(L("Haggai"), "HAG"); Set(L("Sacharja"), "ZEC"); Set(L("Maleachi"), "MAL");
        Set(L("Matthäus"), "MAT"); Set(L("Markus"), "MRK"); Set(L("Lukas"), "LUK", "ACT");
        Set(L("Johannes"), "JHN", "1JN", "2JN", "3JN", "REV");
        Set(L("Paulus"), "ROM", "1CO", "2CO", "GAL", "EPH", "PHP", "COL", "1TH", "2TH", "1TI", "2TI", "TIT", "PHM");
        Set(L("Jakobus"), "JAS"); Set(L("Petrus"), "1PE", "2PE"); Set(L("Judas"), "JUD");
        return map;
    }

    public static string Of(string book) => Writers.GetValueOrDefault(book, L("Unbekannt"));

    public static string FirstBook(string writer) => Writers.Where(p => p.Value == writer).Select(p => p.Key).OrderBy(b => Array.IndexOf(BookData.UsfmIds, b)).FirstOrDefault() ?? "GEN";

    public static string BooksOf(string writer, Func<string, string> name) =>
        string.Join(", ", Writers.Where(p => p.Value == writer).Select(p => p.Key).OrderBy(b => Array.IndexOf(BookData.UsfmIds, b)).Select(name));
}
