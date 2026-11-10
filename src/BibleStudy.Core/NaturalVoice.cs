using System.Diagnostics;
using System.IO.Compression;
using System.Text;

namespace BibleStudy.Core;

/// <summary>
/// Natural-sounding voices for reading aloud, made on the computer with Piper (MIT licence, offline). Program and voice are downloaded once,
/// when the user chooses the natural voice; the voices are freely licensed recordings (see <see cref="Voices"/>).
/// </summary>
public static class NaturalVoice
{
    public sealed record Voice(string Language, string Id, string Name, string Folder, string License, int SizeMb = 63);

    public const string PiperUrl = "https://github.com/rhasspy/piper/releases/download/2023.11.14-2/piper_windows_amd64.zip";
    /// <summary>SHA-256 of that very file: what is downloaded and then started must be exactly it.</summary>
    public const string PiperSha256 = "f3c58906402b24f3a96d92145f58acba6d86c9b5db896d207f78dc80811efcea";
    public const string VoiceBase = "https://huggingface.co/rhasspy/piper-voices/resolve/main/";
    public const string SourceUrl = "https://github.com/rhasspy/piper";

    /// <summary>Voices with a licence that allows free use, several per language (two-letter code). The first one of a language is its default.</summary>
    public static readonly Voice[] Voices =
    [
        new("de", "de_DE-thorsten-medium", "Thorsten", "de/de_DE/thorsten/medium", "CC0"),
        new("de", "de_DE-thorsten-high", "Thorsten high", "de/de_DE/thorsten/high", "CC0", 113),
        new("de", "de_DE-thorsten_emotional-medium", "Thorsten emotional", "de/de_DE/thorsten_emotional/medium", "CC0", 76),
        new("en", "en_US-ljspeech-medium", "LJ Speech", "en/en_US/ljspeech/medium", "Public Domain"),
        new("en", "en_GB-cori-high", "Cori (British) high", "en/en_GB/cori/high", "Public Domain", 114),
        new("en", "en_US-bryce-medium", "Bryce", "en/en_US/bryce/medium", "Public Domain"),
        new("en", "en_US-john-medium", "John", "en/en_US/john/medium", "Public Domain"),
        new("en", "en_US-norman-medium", "Norman", "en/en_US/norman/medium", "Public Domain"),
        new("en", "en_GB-alba-medium", "Alba (Scottish)", "en/en_GB/alba/medium", "CC BY 4.0"),
        new("en", "en_US-kristin-medium", "Kristin", "en/en_US/kristin/medium", "Public Domain"),
        new("en", "en_US-joe-medium", "Joe", "en/en_US/joe/medium", "CC0"),
        new("en", "en_US-sam-medium", "Sam", "en/en_US/sam/medium", "Apache-2.0"),
        new("es", "es_ES-davefx-medium", "DaveFX", "es/es_ES/davefx/medium", "CC0"),
        new("es", "es_MX-claude-high", "Claude (México) high", "es/es_MX/claude/high", "Apache-2.0"),
        new("es", "es_MX-ald-medium", "Ald (México)", "es/es_MX/ald/medium", "Unlicense"),
        new("fr", "fr_FR-siwis-medium", "SIWIS", "fr/fr_FR/siwis/medium", "CC BY 4.0"),
        new("fr", "fr_FR-gilles-low", "Gilles", "fr/fr_FR/gilles/low", "CC0"),
        new("ru", "ru_RU-denis-medium", "Denis", "ru/ru_RU/denis/medium", "CC0"),
        new("ru", "ru_RU-dmitri-medium", "Dmitri", "ru/ru_RU/dmitri/medium", "CC0"),
    ];

    public static bool IsAvailable => OperatingSystem.IsWindows() && Environment.Is64BitOperatingSystem
        && System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture == System.Runtime.InteropServices.Architecture.X64;

    /// <summary>The chosen voice of the language, or its first one.</summary>
    public static Voice? For(string? language, string? id = null) => Voices.FirstOrDefault(v => v.Language == language && v.Id == id) ?? Voices.FirstOrDefault(v => v.Language == language);

    static string Dir(string root) => Path.Combine(root, "cache", "voice");
    static string PiperExe(string root) => Path.Combine(Dir(root), "piper", "piper.exe");
    static string ModelPath(string root, Voice v) => Path.Combine(Dir(root), v.Id + ".onnx");

    public static bool IsReady(string root, Voice v) => File.Exists(PiperExe(root)) && File.Exists(ModelPath(root, v)) && File.Exists(ModelPath(root, v) + ".json");

    /// <summary>Downloads the program and the voice when they are missing. <paramref name="progress"/> gets a short text such as "Piper 40 %".</summary>
    public static async Task EnsureAsync(HttpClient http, string root, Voice v, IProgress<string>? progress, CancellationToken ct)
    {
        var dir = Dir(root);
        Directory.CreateDirectory(dir);
        if (!File.Exists(PiperExe(root)))
        {
            var zip = Path.Combine(dir, "piper.zip.part");
            await DownloadAsync(http, PiperUrl, zip, "Piper", progress, ct);
            string hash;
            await using (var s = File.OpenRead(zip)) hash = Convert.ToHexStringLower(await System.Security.Cryptography.SHA256.HashDataAsync(s, ct));
            if (hash != PiperSha256) { File.Delete(zip); throw new InvalidDataException(Loc.L("Piper: Die heruntergeladene Datei ist nicht die erwartete (Prüfsumme weicht ab).")); }
            var target = Path.Combine(dir, "piper");
            if (Directory.Exists(target)) Directory.Delete(target, true);
            ZipFile.ExtractToDirectory(zip, dir, overwriteFiles: true);   // the zip holds one folder "piper"
            File.Delete(zip);
        }
        foreach (var ext in new[] { ".onnx", ".onnx.json" })
        {
            var file = Path.Combine(dir, v.Id + ext);
            if (File.Exists(file)) continue;
            await DownloadAsync(http, $"{VoiceBase}{v.Folder}/{v.Id}{ext}", file + ".part", v.Name, progress, ct, file);
        }
    }

    static async Task DownloadAsync(HttpClient http, string url, string part, string label, IProgress<string>? progress, CancellationToken ct, string? final = null)
    {
        try
        {
            using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            resp.EnsureSuccessStatusCode();
            var total = resp.Content.Headers.ContentLength ?? 0;
            await using (var src = await resp.Content.ReadAsStreamAsync(ct))
            await using (var dst = File.Create(part))
            {
                var buf = new byte[81920]; long done = 0; var last = -1;
                int n;
                while ((n = await src.ReadAsync(buf, ct)) > 0)
                {
                    await dst.WriteAsync(buf.AsMemory(0, n), ct);
                    done += n;
                    if (total > 0 && done * 100 / total is var pct && pct != last) { last = (int)pct; progress?.Report($"{label} {pct} %"); }
                }
            }
            if (final is not null) File.Move(part, final, overwrite: true);
        }
        catch { try { File.Delete(part); } catch (IOException) { } throw; }
    }

    /// <summary>Makes a WAV file of the text. Throws when the program fails.</summary>
    public static async Task SynthesizeAsync(string root, Voice v, string text, string wavPath, CancellationToken ct)
    {
        var exe = PiperExe(root);
        var psi = new ProcessStartInfo(exe)
        {
            WorkingDirectory = Path.GetDirectoryName(exe)!, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardError = true, RedirectStandardOutput = true,
            StandardInputEncoding = new UTF8Encoding(false),
        };
        psi.ArgumentList.Add("--model"); psi.ArgumentList.Add(ModelPath(root, v));
        psi.ArgumentList.Add("--output_file"); psi.ArgumentList.Add(wavPath);
        using var p = Process.Start(psi) ?? throw new IOException("piper");
        var err = p.StandardError.ReadToEndAsync(ct);
        _ = p.StandardOutput.ReadToEndAsync(ct);
        await p.StandardInput.WriteAsync(text.ReplaceLineEndings(" "));
        p.StandardInput.Close();
        try { await p.WaitForExitAsync(ct); }
        catch (OperationCanceledException) { try { p.Kill(true); } catch (InvalidOperationException) { } throw; }
        if (p.ExitCode != 0 || !File.Exists(wavPath)) throw new IOException((await err).Trim().Split('\n').LastOrDefault() ?? "piper");
    }

    /// <summary>Splits the text at sentence ends into pieces of about <paramref name="max"/> characters, so the first one is ready quickly.</summary>
    public static List<string> Chunks(string text, int max = 450)
    {
        var parts = new List<string>(); var cur = new StringBuilder();
        foreach (var raw in System.Text.RegularExpressions.Regex.Split(text.Trim(), @"(?<=[.!?;:])\s+"))
        {
            if (cur.Length > 0 && cur.Length + raw.Length + 1 > max) { parts.Add(cur.ToString()); cur.Clear(); }
            if (cur.Length > 0) cur.Append(' ');
            cur.Append(raw);
        }
        if (cur.Length > 0) parts.Add(cur.ToString());
        return parts;
    }

    /// <summary>Removes one voice (its model files); when it was the last one, the speech program goes too.</summary>
    public static void Remove(string root, Voice v)
    {
        try
        {
            File.Delete(ModelPath(root, v)); File.Delete(ModelPath(root, v) + ".json");
            if (!Voices.Any(x => IsReady(root, x))) Delete(root);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    public static void Delete(string root) { try { var d = Dir(root); if (Directory.Exists(d)) Directory.Delete(d, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
}
