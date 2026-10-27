using System.Text;
using System.Text.RegularExpressions;
using BibleStudy.L10n;

// Translation helper.
//   wrap  <src>              one-off: wraps texts that are assigned to UI properties in L("…") (interpolated ones become L("…{0}", arg))
//   list  <src>              lists string literals that still look like UI text and are not wrapped (for review)
//   keys  <src>              prints every text the program asks for
//   check <src> <langDir>    reports texts missing in a language file
var cmd = args.Length > 0 ? args[0] : "";
var src = args.Length > 1 ? Path.GetFullPath(args[1]) : Directory.GetCurrentDirectory();

switch (cmd)
{
    case "wrap": Wrap(src); break;
    case "list": List(src, args.Skip(2).ToArray()); break;
    case "keys": foreach (var k in Extractor.AllKeys(src).Order()) Console.WriteLine(k.Replace("\n", "\\n")); break;
    case "check": Check(src, args.Length > 2 ? args[2] : ""); break;
    default: Console.WriteLine("usage: wrap|list|keys|check <src> [langDir]"); break;
}

static IEnumerable<string> SourceFiles(string src) =>
    Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories).Where(f =>
        !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
        && !f.Contains("BibleStudy.Setup") && !f.EndsWith("Loc.cs") && !f.EndsWith("LocXaml.cs") && !f.EndsWith("Program.cs") && !f.Contains("BibleStudy.Tests"));

static bool IsAlreadyWrapped(string text, int start)
{
    var before = text[Math.Max(0, start - 6)..start].TrimEnd();
    return before.EndsWith("L(") && !before.EndsWith("IL(");
}

static void Wrap(string src)
{
    var sinkAssign = new Regex(@"\b(?:Text|Content|Header|Watermark|Title|PlaceholderText)\s*\+?=\s*[^;""()\[\]{}]*$");
    var sinkTip = new Regex(@"SetTip\(\s*[^,()]+,\s*[^;""()\[\]{}]*$");
    int wrapped = 0, files = 0;
    foreach (var file in SourceFiles(src))
    {
        var text = File.ReadAllText(file);
        var lits = Extractor.Lex(text);
        var sb = new StringBuilder(); var pos = 0; var changed = 0;
        foreach (var l in lits)
        {
            if (l.Raw || l.Verbatim) continue;
            if (IsAlreadyWrapped(text, l.Start)) continue;
            var lineStart = text.LastIndexOf('\n', Math.Max(0, l.Start - 1)) + 1;
            var prefix = text[lineStart..l.Start];
            if (!(sinkAssign.IsMatch(prefix) || sinkTip.IsMatch(prefix))) continue;
            string replacement;
            if (l.Interpolated)
            {
                var split = Extractor.SplitInterpolated(l.Content);
                if (split is null) { Console.WriteLine($"MANUAL {Path.GetFileName(file)}: {text[l.Start..l.End]}"); continue; }
                var (fmt, a) = split.Value;
                // only text that has letters outside the holes is worth translating
                if (!Extractor.HasLetters(Regex.Replace(fmt, @"\{\d+[^}]*\}", ""))) continue;
                replacement = $"L(\"{fmt}\"" + string.Concat(a.Select(x => ", " + x)) + ")";
            }
            else
            {
                if (!Extractor.HasLetters(l.Content)) continue;
                replacement = $"L(\"{l.Content}\")";
            }
            sb.Append(text, pos, l.Start - pos).Append(replacement); pos = l.End; changed++;
        }
        if (changed == 0) continue;
        sb.Append(text, pos, text.Length - pos);
        File.WriteAllText(file, sb.ToString(), new UTF8Encoding(true));
        wrapped += changed; files++;
    }
    Console.WriteLine($"wrapped {wrapped} texts in {files} files");
}

static void List(string src, string[] only)
{
    var sql = new Regex(@"^\s*(SELECT|INSERT|UPDATE|DELETE|CREATE|PRAGMA|ALTER|DROP|WITH|VACUUM)\b", RegexOptions.IgnoreCase);
    foreach (var file in SourceFiles(src))
    {
        if (only.Length > 0 && !only.Any(o => Path.GetFileName(file).Contains(o))) continue;
        var text = File.ReadAllText(file);
        foreach (var l in Extractor.Lex(text))
        {
            if (l.Raw || IsAlreadyWrapped(text, l.Start)) continue;
            var c = l.Content;
            if (c.Length < 3 || !Extractor.HasLetters(c)) continue;
            if (Regex.IsMatch(c, @"^[a-z_][a-z0-9_.\-]*$") || c.Contains("://") || c.StartsWith('#') || c.StartsWith("avares") || sql.IsMatch(c)) continue;
            if (Regex.IsMatch(c, @"^[A-Za-z0-9_.\-/\\:*]+$") && !c.Any(char.IsWhiteSpace)) continue;   // identifiers, paths, ids
            var line = text.Take(l.Start).Count(ch => ch == '\n') + 1;
            var shown = c.Length > 110 ? c[..110] + "…" : c;
            Console.WriteLine($"{Path.GetFileName(file)}:{line}: {(l.Interpolated ? "$" : "")}\"{shown}\"");
        }
    }
}

static void Check(string src, string langDir)
{
    var keys = Extractor.AllKeys(src);
    Console.WriteLine($"{keys.Count} texts in the program");
    foreach (var f in Directory.EnumerateFiles(langDir, "*.json"))
    {
        var code = Path.GetFileNameWithoutExtension(f);
        if (code == "de") continue;
        var map = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(f)) ?? [];
        var missing = keys.Where(k => !map.TryGetValue(k, out var v) || v.Length == 0).ToList();
        var unused = map.Keys.Where(k => !k.StartsWith('@') && !keys.Contains(k)).ToList();
        Console.WriteLine($"{code}: {missing.Count} missing, {unused.Count} unused");
    }
}
