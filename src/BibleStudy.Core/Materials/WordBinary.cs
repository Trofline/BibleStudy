using System.Text;
using System.Text.RegularExpressions;
using static BibleStudy.Core.Loc;

namespace BibleStudy.Core.Materials;

/// <summary>
/// Reads old Word files (.doc, Word 97–2003) without Word: the text with its paragraphs, headings (from the built-in heading styles),
/// lists, table rows, the table of contents with its jump targets (bookmarks and HYPERLINK / PAGEREF fields), and nothing is written.
/// A .doc file is a "compound file" (a small file system in one file); the text sits in the stream "WordDocument", the tables that say
/// where it is in "0Table" or "1Table". Pictures are not read.
/// </summary>
public static class WordBinary
{
    // ---- the compound file -------------------------------------------------------------------------------------------------

    /// <summary>The streams of a compound file by name.</summary>
    public sealed class CompoundFile
    {
        readonly byte[] _d;
        readonly int _sector, _miniSector;
        readonly uint _miniCutoff;
        readonly List<uint> _fat = [], _miniFat = [];
        readonly byte[] _miniStream;
        readonly Dictionary<string, (uint Start, long Size)> _entries = new(StringComparer.OrdinalIgnoreCase);
        const uint EndOfChain = 0xFFFFFFFE, Free = 0xFFFFFFFF;

        public CompoundFile(byte[] data)
        {
            _d = data;
            if (data.Length < 512 || BitConverter.ToUInt64(data, 0) != 0xE11AB1A1E011CFD0) throw new InvalidDataException(L("Das ist keine gültige Word-Datei (.doc)."));
            _sector = 1 << BitConverter.ToUInt16(data, 0x1E);
            _miniSector = 1 << BitConverter.ToUInt16(data, 0x20);
            var fatSectors = BitConverter.ToInt32(data, 0x2C);
            var dirStart = BitConverter.ToUInt32(data, 0x30);
            _miniCutoff = BitConverter.ToUInt32(data, 0x38);
            var miniFatStart = BitConverter.ToUInt32(data, 0x3C);
            var difatStart = BitConverter.ToUInt32(data, 0x44);
            // the sectors of the allocation table: 109 in the header, the rest in a chain of DIFAT sectors
            var fatList = new List<uint>();
            for (var i = 0; i < 109 && fatList.Count < fatSectors; i++) fatList.Add(BitConverter.ToUInt32(data, 0x4C + i * 4));
            var guard = 0;
            for (var s = difatStart; s < EndOfChain - 2 && fatList.Count < fatSectors && guard++ < 100000; )
            {
                var off = Offset(s);
                for (var i = 0; i < _sector / 4 - 1 && fatList.Count < fatSectors; i++) fatList.Add(BitConverter.ToUInt32(data, off + i * 4));
                s = BitConverter.ToUInt32(data, off + _sector - 4);
            }
            foreach (var s in fatList)
            {
                var off = Offset(s);
                for (var i = 0; i < _sector / 4; i++) _fat.Add(off + i * 4 + 4 <= data.Length ? BitConverter.ToUInt32(data, off + i * 4) : Free);
            }
            var dir = Chain(dirStart);
            for (var e = 0; e + 128 <= dir.Length; e += 128)
            {
                var nameLen = BitConverter.ToUInt16(dir, e + 0x40);
                if (nameLen < 2 || nameLen > 64) continue;
                var name = Encoding.Unicode.GetString(dir, e, nameLen - 2);
                var type = dir[e + 0x42];
                var start = BitConverter.ToUInt32(dir, e + 0x74);
                var size = BitConverter.ToUInt32(dir, e + 0x78);
                if (type == 5) _entries["\u0001root"] = (start, size);   // the root holds the mini stream
                else if (type == 2) _entries.TryAdd(name, (start, size));
            }
            _miniStream = _entries.TryGetValue("\u0001root", out var root) ? Chain(root.Start) : [];
            if (miniFatStart < EndOfChain - 2)
            {
                var mf = Chain(miniFatStart);
                for (var i = 0; i + 4 <= mf.Length; i += 4) _miniFat.Add(BitConverter.ToUInt32(mf, i));
            }
        }

        int Offset(uint sector) => checked((int)((sector + 1) * (long)_sector));

        byte[] Chain(uint start)
        {
            using var ms = new MemoryStream();
            var guard = 0;
            for (var s = start; s < EndOfChain - 2 && s < _fat.Count && guard++ < 1_000_000; s = _fat[(int)s])
            {
                var off = Offset(s);
                if (off >= _d.Length) break;
                ms.Write(_d, off, Math.Min(_sector, _d.Length - off));
            }
            return ms.ToArray();
        }

        public byte[]? Stream(string name)
        {
            if (!_entries.TryGetValue(name, out var e)) return null;
            byte[] raw;
            if (e.Size < _miniCutoff)
            {
                using var ms = new MemoryStream();
                var guard = 0;
                for (var s = e.Start; s < EndOfChain - 2 && s < _miniFat.Count && guard++ < 1_000_000; s = _miniFat[(int)s])
                {
                    var off = (int)s * _miniSector;
                    if (off >= _miniStream.Length) break;
                    ms.Write(_miniStream, off, Math.Min(_miniSector, _miniStream.Length - off));
                }
                raw = ms.ToArray();
            }
            else raw = Chain(e.Start);
            return raw.Length > e.Size ? raw[..(int)e.Size] : raw;
        }
    }

    // ---- the Word document ---------------------------------------------------------------------------------------------------

    static int U16(byte[] b, int i) => i + 2 <= b.Length ? BitConverter.ToUInt16(b, i) : 0;
    static int I32(byte[] b, int i) => i + 4 <= b.Length ? BitConverter.ToInt32(b, i) : 0;

    sealed record Piece(int CpStart, int CpEnd, int Fc, bool Compressed);
    sealed record ParaProps(int FcStart, int FcEnd, int Istd, bool List);

    public static List<Block> Read(string path) => Read(File.ReadAllBytes(path));

    public static List<Block> Read(byte[] file)
    {
        var cfb = new CompoundFile(file);
        var wd = cfb.Stream("WordDocument") ?? throw new InvalidDataException(L("Das ist keine gültige Word-Datei (.doc)."));
        if (U16(wd, 0) != 0xA5EC) throw new InvalidDataException(L("Das ist keine gültige Word-Datei (.doc)."));
        var flags = U16(wd, 0x0A);
        if ((flags & 0x0100) != 0) throw new InvalidDataException(L("Die Datei ist mit einem Kennwort geschützt."));
        var table = cfb.Stream((flags & 0x0200) != 0 ? "1Table" : "0Table") ?? throw new InvalidDataException(L("Das ist keine gültige Word-Datei (.doc)."));

        // the file information block: counts, then the places of the tables (pairs of offset and length)
        var pos = 32;
        pos += 2 + U16(wd, pos) * 2;
        var lw = pos + 2;
        pos += 2 + U16(wd, pos) * 4;
        var ccpText = I32(wd, lw + 12);
        var fcBase = pos + 2;
        (int Fc, int Lcb) Fc(int i) => (I32(wd, fcBase + i * 8), I32(wd, fcBase + i * 8 + 4));

        var pieces = Pieces(table, Fc(33));
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var cp1252 = Encoding.GetEncoding(1252);
        var text = new StringBuilder(ccpText);
        foreach (var p in pieces)
        {
            if (p.CpStart >= ccpText) break;
            var n = Math.Min(p.CpEnd, ccpText) - p.CpStart;
            if (p.Compressed) { if (p.Fc + n <= wd.Length) text.Append(cp1252.GetString(wd, p.Fc, n)); }
            else if (p.Fc + n * 2 <= wd.Length) text.Append(Encoding.Unicode.GetString(wd, p.Fc, n * 2));
        }

        var styles = StyleIds(table, Fc(1));
        var paras = ParagraphProps(wd, table, Fc(13));
        var bookmarks = Bookmarks(table, Fc(21), Fc(22));

        int? FcOf(int cp)
        {
            foreach (var p in pieces) if (cp >= p.CpStart && cp < p.CpEnd) return p.Fc + (cp - p.CpStart) * (p.Compressed ? 1 : 2);
            return null;
        }
        ParaProps? PropsAt(int cp)
        {
            if (FcOf(cp) is not int fc) return null;
            int lo = 0, hi = paras.Count - 1;
            while (lo <= hi)
            {
                var mid = (lo + hi) / 2;
                if (fc < paras[mid].FcStart) hi = mid - 1;
                else if (fc >= paras[mid].FcEnd) lo = mid + 1;
                else return paras[mid];
            }
            return null;
        }

        // walk through the text: fields (\x13 code \x14 result \x15) show only their result; paragraph marks end a block
        var blocks = new List<Block>();
        var cur = new StringBuilder();
        var cells = new List<string>();
        var fields = new Stack<(StringBuilder Code, bool InResult)>();
        var tocDepth = 0;
        string? link = null;
        var paraStart = 0;
        var marks = bookmarks.OrderBy(b => b.Cp).ToList();
        var markAt = 0;
        var anchors = new List<string>();

        void EndParagraph(int cp, bool cell)
        {
            var line = Regex.Replace(cur.ToString().Replace('\u000B', '\n'), @" +", " ").Trim(' ', '\t');   // tabs stay: a line of the table of contents is "title<TAB>page"
            while (markAt < marks.Count && marks[markAt].Cp <= cp) anchors.Add(marks[markAt++].Name);
            cur.Clear();
            if (cell)
            {
                // a cell ends with \x07; an empty "cell" right after the cells ends the row
                if (line.Length == 0 && cells.Count > 0) { if (cells.Any(c => c.Length > 0)) blocks.Add(Block.Of(BlockKind.TableRow, string.Join("  │  ", cells))); cells.Clear(); }
                else cells.Add(line);
                paraStart = cp + 1;
                return;
            }
            if (line.Length > 0)
            {
                var props = PropsAt(cp);
                var sti = props is not null && styles.TryGetValue(props.Istd, out var s) ? s : 0;
                Block b;
                if (sti is >= 19 and <= 27 || tocDepth > 0)
                {
                    // a line of the table of contents: "Title<TAB>page"; its link is in a field around it
                    var parts = line.Split('\t', StringSplitOptions.RemoveEmptyEntries);
                    var title = parts.Length > 1 && Regex.IsMatch(parts[^1].Trim(), @"^[\divxlcIVXLC]+$") ? string.Join(" ", parts[..^1]) : line.Replace('\t', ' ');
                    var page = parts.Length > 1 ? parts[^1].Trim() : "";
                    b = new Block(BlockKind.TocEntry, [new Span(title.Trim()), new Span(page)], LinkTo: link, Level: sti is >= 19 and <= 27 ? sti - 18 : 1);
                }
                else
                {
                    var kind = sti switch { 1 or 62 => BlockKind.Heading1, 2 => BlockKind.Heading2, >= 3 and <= 9 => BlockKind.Heading3, _ => props?.List == true ? BlockKind.ListItem : BlockKind.Paragraph };
                    b = Block.Of(kind, line.Replace('\t', ' '));
                }
                if (anchors.Count > 0) b = b with { Anchors = anchors.ToList() };
                blocks.Add(b);
                anchors.Clear();
            }
            link = null;
            paraStart = cp + 1;
        }

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '\u0013') { fields.Push((new StringBuilder(), false)); continue; }
            if (c == '\u0014' && fields.Count > 0)
            {
                var f = fields.Pop();
                var code = f.Code.ToString().Trim();
                if (Regex.Match(code, @"^(?:HYPERLINK\s.*\\l\s*""?([^""\s]+)|PAGEREF\s+(\S+))", RegexOptions.IgnoreCase) is { Success: true } m) link ??= m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value;
                if (code.StartsWith("TOC", StringComparison.OrdinalIgnoreCase)) tocDepth++;
                fields.Push((f.Code, true));
                continue;
            }
            if (c == '\u0015' && fields.Count > 0)
            {
                var f = fields.Pop();
                if (f.InResult && f.Code.ToString().TrimStart().StartsWith("TOC", StringComparison.OrdinalIgnoreCase)) tocDepth = Math.Max(0, tocDepth - 1);
                if (!f.InResult && Regex.Match(f.Code.ToString().Trim(), @"^(?:HYPERLINK\s.*\\l\s*""?([^""\s]+)|PAGEREF\s+(\S+))", RegexOptions.IgnoreCase) is { Success: true } m) link ??= m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value;
                continue;
            }
            if (fields.Count > 0 && !fields.Peek().InResult) { fields.Peek().Code.Append(c); continue; }
            switch (c)
            {
                case '\r' or '\u000C': EndParagraph(i, false); break;
                case '\u0007': EndParagraph(i, true); break;
                case '\u000B': cur.Append('\n'); break;
                case '\t': cur.Append('\t'); break;
                case '\u001E': cur.Append('-'); break;
                case ' ': cur.Append(' '); break;
                default: if (c >= ' ') cur.Append(c); break;   // pictures (\x01), footnote marks (\x02), objects (\x08) and others are left out
            }
        }
        EndParagraph(text.Length, false);
        return blocks;
    }

    /// <summary>The pieces of the text: which characters stand where in the file, as single bytes (Windows-1252) or as UTF-16.</summary>
    static List<Piece> Pieces(byte[] table, (int Fc, int Lcb) clx)
    {
        var list = new List<Piece>();
        var i = clx.Fc;
        var end = Math.Min(table.Length, clx.Fc + clx.Lcb);
        while (i < end && table[i] == 1) i += 3 + U16(table, i + 1);   // formatting of pieces, not needed for the text
        if (i >= end || table[i] != 2) throw new InvalidDataException(L("Das ist keine gültige Word-Datei (.doc)."));
        var lcb = I32(table, i + 1);
        var plc = i + 5;
        var n = (lcb - 4) / 12;
        for (var k = 0; k < n; k++)
        {
            var cpStart = I32(table, plc + k * 4);
            var cpEnd = I32(table, plc + (k + 1) * 4);
            var fcRaw = (uint)I32(table, plc + (n + 1) * 4 + k * 8 + 2);
            var compressed = (fcRaw & 0x40000000) != 0;
            var fc = compressed ? (int)((fcRaw & ~0x40000000u) / 2) : (int)fcRaw;
            list.Add(new Piece(cpStart, cpEnd, fc, compressed));
        }
        return list;
    }

    /// <summary>The built-in kind of every style (istd → sti): 1–9 are the headings, 19–27 the levels of the table of contents.</summary>
    static Dictionary<int, int> StyleIds(byte[] table, (int Fc, int Lcb) stsh)
    {
        var map = new Dictionary<int, int>();
        if (stsh.Lcb <= 0 || stsh.Fc + stsh.Lcb > table.Length) return map;
        var cbStshi = U16(table, stsh.Fc);
        var stshi = stsh.Fc + 2;
        var cstd = U16(table, stshi);
        var p = stshi + cbStshi;
        var end = stsh.Fc + stsh.Lcb;
        for (var istd = 0; istd < cstd && p + 2 <= end; istd++)
        {
            var cb = U16(table, p);
            if (cb > 0) map[istd] = U16(table, p + 2) & 0x0FFF;
            p += 2 + cb;
        }
        return map;
    }

    /// <summary>The paragraph properties (style, list or not) by file position, from the formatted disk pages of the paragraphs.</summary>
    static List<ParaProps> ParagraphProps(byte[] wd, byte[] table, (int Fc, int Lcb) bte)
    {
        var list = new List<ParaProps>();
        if (bte.Lcb <= 4 || bte.Fc + bte.Lcb > table.Length) return list;
        var n = (bte.Lcb - 4) / 8;
        for (var k = 0; k < n; k++)
        {
            var pn = I32(table, bte.Fc + (n + 1) * 4 + k * 4) & 0x3FFFFF;
            var page = pn * 512;
            if (page + 512 > wd.Length) continue;
            var crun = wd[page + 511];
            for (var r = 0; r < crun; r++)
            {
                var fcStart = I32(wd, page + r * 4);
                var fcEnd = I32(wd, page + (r + 1) * 4);
                var bOffset = wd[page + (crun + 1) * 4 + r * 13];
                var istd = 0; var isList = false;
                if (bOffset != 0)
                {
                    var p = page + bOffset * 2;
                    var cb = wd[p];
                    int start, len;
                    if (cb == 0) { len = wd[p + 1] * 2; start = p + 2; } else { len = cb * 2 - 1; start = p + 1; }
                    istd = U16(wd, start);
                    // the properties after the style: a list number (sprmPIlfo, sprmPIlvl) makes a list item
                    var q = start + 2;
                    var stop = Math.Min(start + len, page + 511);
                    while (q + 2 <= stop)
                    {
                        var sprm = U16(wd, q);
                        if (sprm is 0x460B or 0x260A) { if (sprm == 0x260A || U16(wd, q + 2) != 0) isList = true; }
                        var size = (sprm >> 13) switch { 0 or 1 => 1, 2 or 4 or 5 => 2, 3 => 4, 7 => 3, _ => -1 };
                        if (size < 0) size = sprm == 0xD608 ? U16(wd, q + 2) + 1 : wd[q + 2] + 1;   // variable length
                        q += 2 + size;
                    }
                }
                list.Add(new ParaProps(fcStart, fcEnd, istd, isList));
            }
        }
        list.Sort((a, b) => a.FcStart.CompareTo(b.FcStart));
        return list;
    }

    /// <summary>The bookmarks: name and the character where each begins (what the table of contents jumps to).</summary>
    static List<(string Name, int Cp)> Bookmarks(byte[] table, (int Fc, int Lcb) names, (int Fc, int Lcb) starts)
    {
        var list = new List<(string, int)>();
        if (names.Lcb <= 0 || starts.Lcb <= 4 || names.Fc + names.Lcb > table.Length || starts.Fc + starts.Lcb > table.Length) return list;
        var p = names.Fc;
        var extended = U16(table, p) == 0xFFFF;
        if (extended) p += 2;
        var count = U16(table, p); var extra = U16(table, p + 2); p += 4;
        var nameList = new List<string>();
        for (var i = 0; i < count && p < names.Fc + names.Lcb; i++)
        {
            if (extended) { var cch = U16(table, p); nameList.Add(Encoding.Unicode.GetString(table, p + 2, cch * 2)); p += 2 + cch * 2 + extra; }
            else { var cch = table[p]; nameList.Add(Encoding.Latin1.GetString(table, p + 1, cch)); p += 1 + cch + extra; }
        }
        var n = (starts.Lcb - 4) / 8;
        for (var i = 0; i < Math.Min(n, nameList.Count); i++) list.Add((nameList[i], I32(table, starts.Fc + i * 4)));
        return list;
    }
}
