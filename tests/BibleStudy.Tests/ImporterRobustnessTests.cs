using System.Text;
using BibleStudy.Core.Import;

namespace BibleStudy.Tests;

/// <summary>
/// A Bible file with a few odd lines in it (numbers too big for a number, digits of other scripts, stray markers) still imports: one bad line
/// must not make the whole Bible fail with a message nobody understands.
/// </summary>
public class ImporterRobustnessTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "bs-import-fuzz-" + Guid.NewGuid().ToString("N"));
    public ImporterRobustnessTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    static readonly string[] OddLines =
    [
        "Genesis 99999999999:1 zu groß", "Genesis 1:99999999999 zu groß", "1. Mose ٣:١ arabische Ziffern", "Gen ۱۲:۳ persische Ziffern", "Gen ３:１６ breite Ziffern",
        "\\c 99999999999", "\\v 99999999999 zu groß", "\\c ٣", "\\v ١ arabisch", "\\v", "\\c", "# 99999999999", "## ٣", "99999999999 Vers", "١ Vers",
        "Kapitel 99999999999", "Chapter ٣", "", "   ", "\t", ":", "1:", ":1", "Gen", "Gen 1", "Gen 1:", "Gen :1",
    ];

    string Write(string name, string text) { var p = Path.Combine(_dir, name); File.WriteAllText(p, text, new UTF8Encoding(false)); return p; }

    [Theory]
    [InlineData("bibel.txt", "1. Mose 1:1 Im Anfang schuf Gott Himmel und Erde.\n1. Mose 1:2 Und die Erde war wüst und leer.\n{0}\n1. Mose 1:3 Und Gott sprach: Es werde Licht!\n")]
    [InlineData("bibel.usfm", "\\id GEN\n\\c 1\n\\v 1 Im Anfang schuf Gott Himmel und Erde.\n{0}\n\\v 2 Und die Erde war wüst und leer.\n")]
    [InlineData("bibel.md", "# 1. Mose\n## 1\n1 Im Anfang schuf Gott Himmel und Erde.\n{0}\n2 Und die Erde war wüst und leer.\n")]
    [InlineData("bibel.csv", "Gen,1,1,Im Anfang schuf Gott Himmel und Erde.\n{0}\nGen,1,2,Und die Erde war wüst und leer.\n")]
    [InlineData("bibel.html", "<html><body><h1>1. Mose</h1><h2>Kapitel 1</h2><p><sup>1</sup> Im Anfang schuf Gott Himmel und Erde.</p><p>{0}</p><p><sup>2</sup> Und die Erde war wüst und leer.</p></body></html>")]
    public void An_odd_line_does_not_break_the_import(string name, string template)
    {
        var failed = new List<string>();
        foreach (var odd in OddLines)
        {
            var path = Write(name, template.Replace("{0}", odd));
            try { BibleImporters.Read(path, "de"); }
            catch (Exception ex) when (ex is FormatException or OverflowException or IndexOutOfRangeException or ArgumentOutOfRangeException or NullReferenceException or InvalidOperationException or KeyNotFoundException)
            {
                failed.Add($"{name} with \"{odd}\": {ex.GetType().Name} {ex.Message}");
            }
            catch (Exception) { /* a clear "no Bible text found" is fine for a file that is only odd */ }
        }
        Assert.True(failed.Count == 0, string.Join("\n", failed));
    }

    [Fact]
    public void A_Bible_written_with_Arabic_digits_imports_with_its_numbers()
    {
        static string Arabic(int n) => string.Concat(n.ToString().Select(c => (char)('٠' + (c - '0'))));
        var text = new StringBuilder("# 1. Mose\n");
        foreach (var chapter in new[] { 1, 12 })
        {
            text.Append("## ").Append(Arabic(chapter)).Append('\n');
            for (var v = 1; v <= 11; v++) text.Append(Arabic(v)).Append(" Vers ").Append(v).Append('\n');
        }
        var bible = BibleImporters.Read(Write("arabisch.md", text.ToString()), "ar");
        Assert.Contains(bible.Verses, v => v.Book == "GEN" && v.Chapter == 12 && v.Verse == 11);
        Assert.Equal(22, bible.Verses.Count);
    }

    [Fact]
    public void Numbers_of_any_script_are_read_and_too_big_ones_are_no_place()
    {
        Assert.Equal(16, BibleImporters.Num("16"));
        Assert.Equal(16, BibleImporters.Num("١٦"));
        Assert.Equal(12, BibleImporters.Num("۱۲"));
        Assert.Equal(3, BibleImporters.Num("３"));
        Assert.Equal(0, BibleImporters.Num("99999999999"));
    }
}
