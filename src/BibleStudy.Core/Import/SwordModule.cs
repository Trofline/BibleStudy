using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace BibleStudy.Core.Import;

/// <summary>One text of a SWORD module: a verse (Verse ≥ 1), the introduction of a chapter (Verse 0) or of a book (Chapter 0, Verse 0).</summary>
public sealed record SwordEntry(string Book, int Chapter, int Verse, string Markup);

/// <summary>A SWORD module as read from its files: the name from its .conf, whether it is a Bible or a commentary, and its texts with their markup.</summary>
public sealed record SwordModuleData(string Name, string Description, bool IsBible, string Language, string SourceType, IReadOnlyList<SwordEntry> Entries);

/// <summary>
/// Modules of the SWORD project (CrossWire; used by Xiphos, BibleTime, AndBible, …): Bibles (RawText, zText) and commentaries (RawCom, zCom),
/// as the .zip that CrossWire hands out (mods.d/*.conf and modules/…) or as a .conf file in an installed SWORD folder. A module that is locked
/// (CipherKey in its .conf) is not read: BibleStudy does not unlock or remove any protection. The verses are found by the versification the module
/// names (KJV when it names none); the verse counts are those of the SWORD project (Data/sword_versifications.txt).
/// </summary>
public static partial class SwordModule
{
    /// <summary>True when the zip holds a SWORD module (a mods.d/*.conf).</summary>
    public static bool IsSwordZip(string path)
    {
        try
        {
            using var zip = ZipFile.OpenRead(path);
            return zip.Entries.Any(e => IsConf(e.FullName));
        }
        catch (InvalidDataException) { return false; }
    }

    static bool IsConf(string entryPath)
    {
        var p = entryPath.Replace('\\', '/');
        return p.EndsWith(".conf", StringComparison.OrdinalIgnoreCase) && (p.StartsWith("mods.d/", StringComparison.OrdinalIgnoreCase) || p.Contains("/mods.d/", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Reads the first module of the wanted kind (Bible or commentary) from a .zip or a .conf.</summary>
    public static SwordModuleData Read(string path, bool wantBible)
    {
        if (path.EndsWith(".conf", StringComparison.OrdinalIgnoreCase))
        {
            var conf = ParseConf(File.ReadAllText(path, Encoding.Latin1));
            // the .conf lies in <root>/mods.d; DataPath is relative to <root>
            var root = Path.TrimEndingDirectorySeparator(Path.GetDirectoryName(Path.GetDirectoryName(Path.GetFullPath(path)))!) + Path.DirectorySeparatorChar;
            return ReadModule(conf, rel =>
            {
                var full = Path.GetFullPath(Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar)));
                // a DataPath with ".." must not lead out of the SWORD folder
                return full.StartsWith(root, StringComparison.OrdinalIgnoreCase) && File.Exists(full) ? File.ReadAllBytes(full) : null;
            }, wantBible) ?? throw new InvalidDataException(KindMissing(wantBible));
        }
        using var zip = ZipFile.OpenRead(path);
        var entries = zip.Entries.ToDictionary(e => Normalize(e.FullName), e => e, StringComparer.OrdinalIgnoreCase);
        foreach (var confEntry in zip.Entries.Where(e => IsConf(e.FullName)))
        {
            var full = confEntry.FullName.Replace('\\', '/');
            var p = full[..full.IndexOf("mods.d/", StringComparison.OrdinalIgnoreCase)];
            using var reader = new StreamReader(confEntry.Open(), Encoding.Latin1);
            var conf = ParseConf(reader.ReadToEnd());
            var module = ReadModule(conf, rel =>
            {
                if (!entries.TryGetValue(Normalize(p + rel), out var e)) return null;
                using var s = e.Open(); using var ms = new MemoryStream(); s.CopyTo(ms); return ms.ToArray();
            }, wantBible);
            if (module is not null) return module;
        }
        throw new InvalidDataException(KindMissing(wantBible));
    }

    static string KindMissing(bool wantBible) => wantBible
        ? L("In diesem SWORD-Paket ist keine Bibel. Kommentare liest du unter „Eigenen Kommentar einlesen“ ein; Wörterbücher und Bücher liest BibleStudy nicht.")
        : L("In diesem SWORD-Paket ist kein Kommentar. Bibeln liest du unter „Eigene Übersetzung importieren“ ein; Wörterbücher und Bücher liest BibleStudy nicht.");

    static string Normalize(string p)
    {
        var parts = new List<string>();
        foreach (var part in p.Replace('\\', '/').Split('/'))
        {
            if (part is "" or ".") continue;
            if (part == "..") { if (parts.Count > 0) parts.RemoveAt(parts.Count - 1); continue; }
            parts.Add(part);
        }
        return string.Join('/', parts);
    }

    /// <summary>The settings of a .conf: "[Name]" and "Key=Value" lines (a line that ends with "\" goes on in the next).</summary>
    public static Dictionary<string, string> ParseConf(string text)
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? key = null;
        foreach (var raw in text.Replace("\r", "").Split('\n'))
        {
            var line = raw.TrimEnd();
            if (key is not null)
            {
                d[key] += "\n" + line.TrimEnd('\\');
                if (!line.EndsWith('\\')) key = null;
                continue;
            }
            if (line.StartsWith('[') && line.EndsWith(']')) { d.TryAdd("@name", line[1..^1].Trim()); continue; }
            var eq = line.IndexOf('=');
            if (eq <= 0 || line.StartsWith('#')) continue;
            var k = line[..eq].Trim(); var v = line[(eq + 1)..].Trim();
            // a key may come more than once (GlobalOptionFilter); the first of the others is kept
            if (!d.ContainsKey(k)) d[k] = v.TrimEnd('\\');
            if (v.EndsWith('\\')) key = k;
        }
        return d;
    }

    static SwordModuleData? ReadModule(Dictionary<string, string> conf, Func<string, byte[]?> file, bool wantBible)
    {
        var driver = conf.GetValueOrDefault("ModDrv", "").Trim().ToLowerInvariant();
        var isBible = driver is "rawtext" or "rawtext4" or "ztext" or "ztext4";
        var isCommentary = driver is "rawcom" or "rawcom4" or "zcom" or "zcom4";
        if (isBible != wantBible || !isBible && !isCommentary) return null;
        // a locked module (bought, with a key to unlock it) is not read, also when its key is given
        if (conf.ContainsKey("CipherKey")) throw BibleImporters.Protected();
        var compress = conf.GetValueOrDefault("CompressType", "ZIP").Trim().ToUpperInvariant();
        var zipped = driver.StartsWith('z');
        if (zipped && compress != "ZIP")
            throw new NotSupportedException(L("Dieses SWORD-Modul ist mit {0} gepackt. BibleStudy liest nur SWORD-Module, die mit ZIP gepackt sind (die meisten von CrossWire).", compress));
        var name = conf.GetValueOrDefault("@name", "SWORD");
        var description = conf.GetValueOrDefault("Description", name).Split('\n')[0].Trim();
        var latin = !conf.GetValueOrDefault("Encoding", "").Trim().Equals("UTF-8", StringComparison.OrdinalIgnoreCase);
        var dataPath = conf.GetValueOrDefault("DataPath", "").Trim();
        if (dataPath.Length == 0) throw new InvalidDataException(L("Die .conf-Datei dieses SWORD-Moduls nennt keinen DataPath."));
        if (!dataPath.EndsWith('/')) dataPath += "/";
        var versification = conf.GetValueOrDefault("Versification", "KJV").Trim();
        var canon = Canon(versification) ?? throw new NotSupportedException(L("Die Verszählung „{0}“ dieses SWORD-Moduls kennt BibleStudy nicht.", versification));
        var four = driver.EndsWith('4');
        var entries = new List<SwordEntry>();
        var found = false;
        foreach (var testament in new[] { "ot", "nt" })
        {
            var books = canon.GetValueOrDefault(testament);
            if (books is null) continue;
            Func<int, byte[]?> textAt;
            if (zipped)
            {
                var letter = new[] { 'b', 'c', 'v' }.FirstOrDefault(l => file(dataPath + testament + "." + l + "zs") is not null);
                if (letter == default) continue;
                var blocks = file(dataPath + testament + "." + letter + "zs")!;
                var index = file(dataPath + testament + "." + letter + "zv");
                var data = file(dataPath + testament + "." + letter + "zz");
                if (index is null || data is null) continue;
                textAt = ZTextReader(blocks, index, data, four);
            }
            else
            {
                var index = file(dataPath + testament + ".vss");
                var data = file(dataPath + testament);
                if (index is null || data is null) continue;
                textAt = RawReader(index, data, four);
            }
            found = true;
            var idx = 2;   // 0: the module, 1: the testament
            foreach (var (osis, counts) in books)
            {
                var book = BibleImporters.BookFromName(osis);
                if (book is not null) Add(book, 0, 0, idx);
                idx++;
                for (var c = 1; c <= counts.Length; c++)
                {
                    if (book is not null) Add(book, c, 0, idx);
                    idx++;
                    for (var v = 1; v <= counts[c - 1]; v++)
                    {
                        if (book is not null) Add(book, c, v, idx);
                        idx++;
                    }
                }
            }

            void Add(string book, int chapter, int verse, int at)
            {
                var bytes = textAt(at);
                if (bytes is null || bytes.Length == 0) return;
                var text = (latin ? Encoding.Latin1 : Encoding.UTF8).GetString(bytes).Trim();
                // a commentary that links a verse to the note of another ("@LINK") keeps the note once
                if (text.Length > 0) entries.Add(new SwordEntry(book, chapter, verse, text));
            }
        }
        if (!found) throw new InvalidDataException(L("Die Dateien des SWORD-Moduls „{0}“ fehlen im Paket.", name));
        return new SwordModuleData(name, description, isBible, conf.GetValueOrDefault("Lang", "").Trim(), conf.GetValueOrDefault("SourceType", "Plaintext").Trim(), entries);
    }

    /// <summary>RawText/RawCom: per verse the start in the text file (4 bytes) and the length (2 bytes; 4 for the "4" drivers).</summary>
    static Func<int, byte[]?> RawReader(byte[] index, byte[] data, bool four)
    {
        var size = four ? 8 : 6;
        return i =>
        {
            if ((long)(i + 1) * size > index.Length) return null;
            var start = BitConverter.ToUInt32(index, i * size);
            var length = four ? BitConverter.ToUInt32(index, i * size + 4) : BitConverter.ToUInt16(index, i * size + 4);
            if (length == 0 || start + (long)length > data.Length) return null;
            return data.AsSpan((int)start, (int)length).ToArray();
        };
    }

    /// <summary>zText/zCom: per verse the block, the start in the unpacked block and the length; per block its place in the packed file.</summary>
    static Func<int, byte[]?> ZTextReader(byte[] blocks, byte[] index, byte[] data, bool four)
    {
        var size = four ? 12 : 10;
        var cache = new Dictionary<uint, byte[]>();
        return i =>
        {
            if ((long)(i + 1) * size > index.Length) return null;
            var block = BitConverter.ToUInt32(index, i * size);
            var start = BitConverter.ToUInt32(index, i * size + 4);
            var length = four ? BitConverter.ToUInt32(index, i * size + 8) : BitConverter.ToUInt16(index, i * size + 8);
            if (length == 0) return null;
            if (!cache.TryGetValue(block, out var unpacked))
            {
                if ((long)(block + 1) * 12 > blocks.Length) return null;
                var offset = BitConverter.ToUInt32(blocks, (int)block * 12);
                var packed = BitConverter.ToUInt32(blocks, (int)block * 12 + 4);
                if (offset + (long)packed > data.Length) return null;
                try
                {
                    using var z = new ZLibStream(new MemoryStream(data, (int)offset, (int)packed), CompressionMode.Decompress);
                    using var ms = new MemoryStream(); z.CopyTo(ms); unpacked = ms.ToArray();
                }
                catch (InvalidDataException) { unpacked = []; }
                if (cache.Count > 64) cache.Clear();
                cache[block] = unpacked;
            }
            if (start + (long)length > unpacked.Length) return null;
            return unpacked.AsSpan((int)start, (int)length).ToArray();
        };
    }

    // ---- versifications --------------------------------------------------------------------------------------------------

    static readonly Lazy<Dictionary<string, Dictionary<string, List<(string Osis, int[] Counts)>>>> Canons = new(LoadCanons);

    static Dictionary<string, Dictionary<string, List<(string, int[])>>> LoadCanons()
    {
        var d = new Dictionary<string, Dictionary<string, List<(string, int[])>>>(StringComparer.OrdinalIgnoreCase);
        using var s = typeof(SwordModule).Assembly.GetManifestResourceStream("data/sword_versifications.txt")!;
        using var r = new StreamReader(s);
        while (r.ReadLine() is { } line)
        {
            var p = line.Split('|');
            if (p.Length != 3) continue;
            if (!d.TryGetValue(p[0], out var canon)) d[p[0]] = canon = [];
            canon[p[1]] = p[2].Length == 0 ? [] : p[2].Split(';').Select(b => { var bc = b.Split(':'); return (bc[0], bc[1].Split(',').Select(int.Parse).ToArray()); }).ToList();
        }
        return d;
    }

    static Dictionary<string, List<(string Osis, int[] Counts)>>? Canon(string name) => Canons.Value.GetValueOrDefault(name.Trim());

    // ---- markup ------------------------------------------------------------------------------------------------------------

    /// <summary>Plain text of a SWORD entry (OSIS, ThML, GBF or plain text): paragraphs and line breaks stay, notes and tags go.</summary>
    public static string ToText(string markup, bool keepNotes = true)
    {
        var s = markup;
        if (!keepNotes) s = NoteElement().Replace(s, "");
        s = GbfToken().Replace(s, m => m.Groups[1].Value switch { "CM" or "CL" => "\n", "RF" => keepNotes ? " (" : "", "Rf" => keepNotes ? ")" : "", _ => "" });
        s = BreakTag().Replace(s, "\n");
        s = Regex.Replace(s, "<[^>]+>", "");
        s = System.Net.WebUtility.HtmlDecode(s);
        var lines = s.Replace("\r", "").Split('\n').Select(l => Regex.Replace(l, @"[ \t]+", " ").Trim());
        return Regex.Replace(string.Join("\n", lines), @"\n{3,}", "\n\n").Trim();
    }

    [GeneratedRegex(@"<note\b[^>]*/>|<note\b.*?</note>", RegexOptions.Singleline | RegexOptions.IgnoreCase)] private static partial Regex NoteElement();
    [GeneratedRegex(@"<(CM|CL|RF|Rf|FI|Fi|FR|Fr|FB|Fb|FU|Fu|FO|Fo|FS|Fs|FV|Fv|TS|Ts|WH\d*|WG\d*|WT[^>]*)>")] private static partial Regex GbfToken();
    [GeneratedRegex(@"<\s*(br|lb|/p|/div|/l|/lg|/item|/title|/head)\b[^>]*>|<div\b[^>]*type=""paragraph""[^>]*/>|<p\s*/>", RegexOptions.IgnoreCase)] private static partial Regex BreakTag();
}
