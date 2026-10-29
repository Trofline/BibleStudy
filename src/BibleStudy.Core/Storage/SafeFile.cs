using System.Text;

namespace BibleStudy.Core.Storage;

/// <summary>
/// Writes a file so that it is either the old or the new one, never half of it: first into a file next to it, then swapped in.
/// A crash, a full disk or a cancelled download while writing would otherwise leave a broken file behind
/// (a broken settings or learning file means starting over; a broken cache file breaks that page until it is deleted).
/// </summary>
public static class SafeFile
{
    public static void WriteAllText(string path, string text, Encoding? encoding = null) =>
        Swap(path, tmp => File.WriteAllText(tmp, text, encoding ?? new UTF8Encoding(false)));

    public static void WriteAllBytes(string path, byte[] bytes) => Swap(path, tmp => File.WriteAllBytes(tmp, bytes));

    /// <summary>For downloaded data: the download may be cancelled, the writing is not (it is short).</summary>
    public static Task WriteAllTextAsync(string path, string text) => Task.Run(() => WriteAllText(path, text));

    public static Task WriteAllBytesAsync(string path, byte[] bytes) => Task.Run(() => WriteAllBytes(path, bytes));

    static void Swap(string path, Action<string> write)
    {
        if (Path.GetDirectoryName(path) is { Length: > 0 } dir) Directory.CreateDirectory(dir);
        var tmp = path + ".tmp";
        write(tmp);
        File.Move(tmp, path, overwrite: true);
    }
}
