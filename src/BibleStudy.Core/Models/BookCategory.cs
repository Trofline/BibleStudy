namespace BibleStudy.Core.Models;

public enum BookCategory { Law, History, Poetry, MajorProphets, MinorProphets, Gospels, Acts, Paul, General, Revelation, Other }

public static class BookCategories
{
    static readonly Dictionary<string, BookCategory> Map = Build();

    public static BookCategory Of(string usfmId) => Map.GetValueOrDefault(usfmId, BookCategory.Other);

    public static bool IsOldTestament(BookCategory c) => c is BookCategory.Law or BookCategory.History or BookCategory.Poetry or BookCategory.MajorProphets or BookCategory.MinorProphets;

    public static bool IsNewTestament(BookCategory c) => c is BookCategory.Gospels or BookCategory.Acts or BookCategory.Paul or BookCategory.General or BookCategory.Revelation;

    static Dictionary<string, BookCategory> Build()
    {
        var d = new Dictionary<string, BookCategory>();
        void Add(BookCategory c, string ids) { foreach (var id in ids.Split(' ')) d[id] = c; }
        Add(BookCategory.Law, "GEN EXO LEV NUM DEU");
        Add(BookCategory.History, "JOS JDG RUT 1SA 2SA 1KI 2KI 1CH 2CH EZR NEH EST");
        Add(BookCategory.Poetry, "JOB PSA PRO ECC SNG");
        Add(BookCategory.MajorProphets, "ISA JER LAM EZK DAN");
        Add(BookCategory.MinorProphets, "HOS JOL AMO OBA JON MIC NAM HAB ZEP HAG ZEC MAL");
        Add(BookCategory.Gospels, "MAT MRK LUK JHN");
        Add(BookCategory.Acts, "ACT");
        Add(BookCategory.Paul, "ROM 1CO 2CO GAL EPH PHP COL 1TH 2TH 1TI 2TI TIT PHM");
        Add(BookCategory.General, "HEB JAS 1PE 2PE 1JN 2JN 3JN JUD");
        Add(BookCategory.Revelation, "REV");
        return d;
    }
}
