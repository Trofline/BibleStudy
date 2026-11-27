using System.IO.Compression;
using System.Text;
using BibleStudy.Core.Import;

namespace BibleStudy.Core.Sync;

/// <summary>
/// The imported translations of a <see cref="LocalBibleStore"/> for the synchronisation between the own devices: each one goes over whole and
/// without loss, as the program's .txt format (<see cref="BibleTextFile"/>, with Strong's numbers, footnotes, headings and the words of Jesus), packed.
/// </summary>
public sealed class LocalBibles(LocalBibleStore store, Action<string>? imported = null) : ISyncBibles
{
    /// <summary>A received translation may unpack to at most this much text (a whole Bible with Strong's numbers is around 15 MB).</summary>
    public const long MaxText = 200L * 1024 * 1024;

    public IReadOnlyList<string> Names() =>
        [.. store.GetTranslationsAsync().GetAwaiter().GetResult().Select(t => t.Name).Where(n => n.Trim().Length > 0).Distinct(StringComparer.Ordinal)];

    public Task<byte[]?> ExportAsync(string name) => Task.Run(() =>
    {
        var id = store.GetTranslationsAsync().GetAwaiter().GetResult().FirstOrDefault(t => t.Name == name)?.Id;
        return id is not null && store.Load(id) is { Verses.Count: > 0 } bible ? Pack(bible) : null;
    });

    public Task<bool> ImportAsync(string name, byte[] packed) => Task.Run(() =>
    {
        if (Unpack(packed) is not { } text) return false;
        var bible = BibleTextFile.Read(text.Split('\n'), name, Loc.Code);
        if (bible.Verses.Count == 0) return false;
        // the name the other device lists is the one both sides know it by (the next comparison finds it here then)
        if (bible.Name != name) bible = bible with { Name = name };
        lock (store)
        {
            if (store.GetTranslationsAsync().GetAwaiter().GetResult().Any(t => t.Name == name)) return false;   // imported here meanwhile: left as it is
            var id = store.Save(bible);
            imported?.Invoke(id);
        }
        return true;
    });

    /// <summary>A translation as packed .txt (text shrinks to about a quarter).</summary>
    public static byte[] Pack(ParsedBible bible)
    {
        using var packed = new MemoryStream();
        using (var gz = new GZipStream(packed, CompressionLevel.Optimal, true))
            gz.Write(new UTF8Encoding(false).GetBytes(BibleTextFile.Write(bible)));
        return packed.ToArray();
    }

    /// <summary>Unpacks received text, never more than <see cref="MaxText"/>; null when it is not packed text of the program's format.</summary>
    public static string? Unpack(byte[] data)
    {
        try
        {
            using var gz = new GZipStream(new MemoryStream(data), CompressionMode.Decompress);
            using var text = new MemoryStream();
            var buffer = new byte[81920];
            int n;
            while ((n = gz.Read(buffer)) > 0)
            {
                if (text.Length + n > MaxText) return null;
                text.Write(buffer, 0, n);
            }
            var result = new UTF8Encoding(false).GetString(text.GetBuffer(), 0, (int)text.Length);
            return BibleTextFile.IsOwnFormat(result[..Math.Min(result.Length, 400)].Split('\n')) ? result : null;
        }
        catch (InvalidDataException) { return null; }
    }
}
