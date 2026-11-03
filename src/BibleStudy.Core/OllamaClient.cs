using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace BibleStudy.Core;

public sealed record OllamaMessage(string Role, string Content);

/// <summary>
/// A local AI through Ollama (https://ollama.com): everything stays on the user's computer, the program only talks to Ollama on the
/// same machine (127.0.0.1). No account, no cloud, no cost. This simple chat has no Bible tools (those belong to Claude Code); the
/// current verse is handed over as text instead.
/// </summary>
public sealed class OllamaClient(HttpClient http, string address = "http://127.0.0.1:11434")
{
    public string Address { get; } = address.TrimEnd('/');

    static System.Diagnostics.Process? _started;   // the Ollama that this program started itself (and stops again at the end)
    static DateTime _lastTry = DateTime.MinValue;

    /// <summary>Where Ollama is installed on this computer (a normal install or a copy on the PATH), or null.</summary>
    public static string? FindExe()
    {
        var name = OperatingSystem.IsWindows() ? "ollama.exe" : "ollama";
        var candidates = new List<string>();
        if (OperatingSystem.IsWindows()) candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Ollama", name));
        else candidates.AddRange(["/usr/local/bin/ollama", "/opt/homebrew/bin/ollama", "/usr/bin/ollama", "/Applications/Ollama.app/Contents/Resources/ollama"]);
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            try { candidates.Add(Path.Combine(dir.Trim('"'), name)); } catch (ArgumentException) { }
        return candidates.FirstOrDefault(File.Exists);
    }

    async Task<bool> PingAsync(CancellationToken ct)
    {
        try { using var r = await http.GetAsync(Address + "/api/version", ct); return r.IsSuccessStatusCode; }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { return false; }
    }

    /// <summary>
    /// Makes sure Ollama answers: when it is installed but not running, it is started in the background without a window (like "ollama serve")
    /// and stopped again when BibleStudy ends. Returns false when it is not installed or does not come up.
    /// </summary>
    public async Task<bool> EnsureRunningAsync(CancellationToken ct = default)
    {
        if (await PingAsync(ct)) return true;
        if (_started is not { HasExited: false })
        {
            if (DateTime.UtcNow - _lastTry < TimeSpan.FromSeconds(30)) return false;   // it did not come up a moment ago: not again at every request
            _lastTry = DateTime.UtcNow;
            if (FindExe() is not { } exe) return false;
            try
            {
                _started = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe, "serve")
                {
                    UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
                    WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden,
                });
                _started?.BeginOutputReadLine(); _started?.BeginErrorReadLine();   // nobody reads it, but the pipes must not fill up
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { return false; }
        }
        for (var i = 0; i < 40 && !ct.IsCancellationRequested; i++)
        {
            if (await PingAsync(ct)) return true;
            try { await Task.Delay(500, ct); } catch (TaskCanceledException) { break; }
        }
        return false;
    }

    /// <summary>Ends the Ollama that this program started (one that was running before stays).</summary>
    public static void StopStarted()
    {
        try { if (_started is { HasExited: false } p) p.Kill(true); } catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
        _started = null;
    }

    /// <summary>The installed models, or an empty list when Ollama is not running.</summary>
    public async Task<IReadOnlyList<string>> ModelsAsync(CancellationToken ct = default)
    {
        try
        {
            await EnsureRunningAsync(ct);
            using var doc = JsonDocument.Parse(await http.GetStringAsync(Address + "/api/tags", ct));
            return doc.RootElement.GetProperty("models").EnumerateArray().Select(m => m.GetProperty("name").GetString() ?? "").Where(n => n.Length > 0).ToList();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException) { return []; }
    }

    /// <summary>Downloads a model into Ollama (the same as "ollama pull"); <paramref name="progress"/> gets a short text such as "pulling 40 %".</summary>
    public async Task PullAsync(string model, IProgress<string>? progress, CancellationToken ct)
    {
        await EnsureRunningAsync(ct);
        using var req = new HttpRequestMessage(HttpMethod.Post, Address + "/api/pull") { Content = JsonContent.Create(new { model, stream = true }) };
        using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!resp.IsSuccessStatusCode) throw new HttpRequestException(await resp.Content.ReadAsStringAsync(ct));
        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);
        while (await reader.ReadLineAsync(ct) is { } line)
        {
            if (line.Length == 0) continue;
            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                if (root.TryGetProperty("error", out var err)) throw new InvalidOperationException(err.GetString());
                var status = root.TryGetProperty("status", out var s) ? s.GetString() ?? "" : "";
                if (root.TryGetProperty("total", out var t) && root.TryGetProperty("completed", out var c) && t.GetInt64() > 0)
                    status += $" {100 * c.GetInt64() / t.GetInt64()} %";
                progress?.Report(status);
            }
            catch (JsonException) { /* a line that is not JSON carries nothing */ }
        }
    }

    /// <summary>The text piece of one line of the streamed answer (null when the line carries none); <paramref name="done"/> is true on the last line.</summary>
    public static string? ParseLine(string line, out bool done)
    {
        done = false;
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (root.TryGetProperty("error", out var err)) throw new InvalidOperationException(err.GetString());
            done = root.TryGetProperty("done", out var d) && d.ValueKind == JsonValueKind.True;
            return root.TryGetProperty("message", out var m) && m.TryGetProperty("content", out var c) ? c.GetString() : null;
        }
        catch (JsonException) { return null; }
    }

    /// <summary>Sends the conversation and hands every piece of the answer to <paramref name="onText"/> while it arrives; returns the whole answer.</summary>
    public async Task<string> ChatAsync(string model, IEnumerable<OllamaMessage> messages, Action<string> onText, CancellationToken ct)
    {
        await EnsureRunningAsync(ct);
        var body = new { model, stream = true, messages = messages.Select(m => new { role = m.Role, content = m.Content }) };
        using var req = new HttpRequestMessage(HttpMethod.Post, Address + "/api/chat") { Content = JsonContent.Create(body) };
        using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!resp.IsSuccessStatusCode) throw new HttpRequestException(await resp.Content.ReadAsStringAsync(ct));
        var all = new StringBuilder();
        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);
        while (await reader.ReadLineAsync(ct) is { } line)
        {
            if (line.Length == 0) continue;
            if (ParseLine(line, out var done) is { Length: > 0 } piece) { all.Append(piece); onText(piece); }
            if (done) break;
        }
        return all.ToString();
    }
}
