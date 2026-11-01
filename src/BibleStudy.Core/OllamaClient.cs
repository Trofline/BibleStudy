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

    /// <summary>The installed models, or an empty list when Ollama is not running.</summary>
    public async Task<IReadOnlyList<string>> ModelsAsync(CancellationToken ct = default)
    {
        try
        {
            using var doc = JsonDocument.Parse(await http.GetStringAsync(Address + "/api/tags", ct));
            return doc.RootElement.GetProperty("models").EnumerateArray().Select(m => m.GetProperty("name").GetString() ?? "").Where(n => n.Length > 0).ToList();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException) { return []; }
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
