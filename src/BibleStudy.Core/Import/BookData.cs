namespace BibleStudy.Core.Import;

/// <summary>The 66 books in canonical order with USFM ids and German names; used by importers.</summary>
public static class BookData
{
    public static readonly string[] UsfmIds =
        ("GEN EXO LEV NUM DEU JOS JDG RUT 1SA 2SA 1KI 2KI 1CH 2CH EZR NEH EST JOB PSA PRO ECC SNG ISA JER LAM EZK DAN HOS JOL AMO OBA JON MIC NAM HAB ZEP HAG ZEC MAL " +
         "MAT MRK LUK JHN ACT ROM 1CO 2CO GAL EPH PHP COL 1TH 2TH 1TI 2TI TIT PHM HEB JAS 1PE 2PE 1JN 2JN 3JN JUD REV").Split(' ');

    public static readonly string[] GermanNames =
    [
        "1. Mose", "2. Mose", "3. Mose", "4. Mose", "5. Mose", "Josua", "Richter", "Rut", "1. Samuel", "2. Samuel", "1. Könige", "2. Könige", "1. Chronika", "2. Chronika",
        "Esra", "Nehemia", "Esther", "Hiob", "Psalm", "Sprüche", "Prediger", "Hoheslied", "Jesaja", "Jeremia", "Klagelieder", "Hesekiel", "Daniel", "Hosea", "Joel", "Amos",
        "Obadja", "Jona", "Micha", "Nahum", "Habakuk", "Zephanja", "Haggai", "Sacharja", "Maleachi", "Matthäus", "Markus", "Lukas", "Johannes", "Apostelgeschichte", "Römer",
        "1. Korinther", "2. Korinther", "Galater", "Epheser", "Philipper", "Kolosser", "1. Thessalonicher", "2. Thessalonicher", "1. Timotheus", "2. Timotheus", "Titus",
        "Philemon", "Hebräer", "Jakobus", "1. Petrus", "2. Petrus", "1. Johannes", "2. Johannes", "3. Johannes", "Judas", "Offenbarung",
    ];

    public static readonly string[] EnglishNames =
    [
        "Genesis", "Exodus", "Leviticus", "Numbers", "Deuteronomy", "Joshua", "Judges", "Ruth", "1 Samuel", "2 Samuel", "1 Kings", "2 Kings", "1 Chronicles", "2 Chronicles",
        "Ezra", "Nehemiah", "Esther", "Job", "Psalms", "Proverbs", "Ecclesiastes", "Song of Solomon", "Isaiah", "Jeremiah", "Lamentations", "Ezekiel", "Daniel", "Hosea", "Joel", "Amos",
        "Obadiah", "Jonah", "Micah", "Nahum", "Habakkuk", "Zephaniah", "Haggai", "Zechariah", "Malachi", "Matthew", "Mark", "Luke", "John", "Acts", "Romans",
        "1 Corinthians", "2 Corinthians", "Galatians", "Ephesians", "Philippians", "Colossians", "1 Thessalonians", "2 Thessalonians", "1 Timothy", "2 Timothy", "Titus",
        "Philemon", "Hebrews", "James", "1 Peter", "2 Peter", "1 John", "2 John", "3 John", "Jude", "Revelation",
    ];

    /// <summary>German abbreviations as used by many German Bible sites ("1. Mo", "Röm", "Off").</summary>
    public static readonly string[] GermanAbbreviations =
    [
        "1. Mo", "2. Mo", "3. Mo", "4. Mo", "5. Mo", "Jos", "Ri", "Rt", "1. Sam", "2. Sam", "1. Kön", "2. Kön", "1. Chr", "2. Chr", "Esra", "Neh", "Est", "Hiob", "Ps", "Spr", "Pred", "Hld",
        "Jes", "Jer", "Klgl", "Hes", "Dan", "Hos", "Joel", "Amos", "Obad", "Jona", "Mich", "Nah", "Hab", "Zeph", "Hag", "Sach", "Mal", "Mt", "Mk", "Lk", "Joh", "Apg", "Röm",
        "1. Kor", "2. Kor", "Gal", "Eph", "Phil", "Kol", "1. Thes", "2. Thes", "1. Tim", "2. Tim", "Tit", "Phlm", "Heb", "Jak", "1. Pet", "2. Pet", "1. Joh", "2. Joh", "3. Joh", "Jud", "Off",
    ];

    public static string NameOf(string usfm, string languageCode)
    {
        var i = Array.IndexOf(UsfmIds, usfm);
        if (i < 0) return usfm;
        return languageCode.StartsWith("de", StringComparison.OrdinalIgnoreCase) ? GermanNames[i] : EnglishNames[i];
    }
}
