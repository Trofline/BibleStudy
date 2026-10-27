using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace BibleStudy.App;

/// <summary>One thing the Claude CLI reported while answering.</summary>
public abstract record ClaudeEvent;
public sealed record ClaudeTextDelta(string Text) : ClaudeEvent;
public sealed record ClaudeToolUse(string Name) : ClaudeEvent;
public sealed record ClaudeResult(string Text, string? SessionId, bool IsError) : ClaudeEvent;

/// <summary>
/// Talks to the user's own Claude Code installation in non-interactive mode (claude -p). It uses the user's existing login,
/// so BibleStudy needs no API key and creates no API costs. Only our MCP server is loaded and built-in tools are off:
/// Claude can use the Bible tools and nothing else.
/// </summary>
public static class ClaudeCli
{
    public static string? FindExecutable()
    {
        var names = OperatingSystem.IsWindows() ? new[] { "claude.exe", "claude.cmd" } : ["claude"];
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            foreach (var name in names)
            {
                try { var p = Path.Combine(dir.Trim('"'), name); if (File.Exists(p)) return p; } catch (ArgumentException) { }
            }
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        foreach (var p in new[] { Path.Combine(home, ".local", "bin", names[0]), Path.Combine(home, ".claude", "local", names[0]) })
            if (File.Exists(p)) return p;
        return null;
    }

    /// <summary>Writes the MCP config that points Claude at our server; returns its path.</summary>
    public static string WriteMcpConfig(string dir, string mcpExe)
    {
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "mcp-config.json");
        File.WriteAllText(path, JsonSerializer.Serialize(new { mcpServers = new { biblestudy = new { command = mcpExe, args = Array.Empty<string>() } } }));
        return path;
    }

    public static async Task RunAsync(string exe, string workDir, string prompt, string? sessionId, string systemPrompt, string mcpConfig,
        string? model, Action<ClaudeEvent> onEvent, CancellationToken ct)
    {
        Directory.CreateDirectory(workDir);
        var psi = new ProcessStartInfo(exe)
        {
            WorkingDirectory = workDir, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            UseShellExecute = false, CreateNoWindow = true,
            StandardInputEncoding = new UTF8Encoding(false), StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var arg in new[]
        {
            "-p", "--output-format", "stream-json", "--verbose", "--include-partial-messages",
            "--mcp-config", mcpConfig, "--strict-mcp-config", "--tools", "", "--allowedTools", "mcp__biblestudy",
            "--append-system-prompt", systemPrompt,
        }) psi.ArgumentList.Add(arg);
        if (!string.IsNullOrWhiteSpace(sessionId)) { psi.ArgumentList.Add("--resume"); psi.ArgumentList.Add(sessionId); }
        if (!string.IsNullOrWhiteSpace(model)) { psi.ArgumentList.Add("--model"); psi.ArgumentList.Add(model); }

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Claude konnte nicht gestartet werden.");
        using var kill = ct.Register(() => { try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { } });
        var stderr = process.StandardError.ReadToEndAsync();
        await process.StandardInput.WriteAsync(prompt);
        process.StandardInput.Close();

        var sawResult = false;
        string? line;
        while ((line = await process.StandardOutput.ReadLineAsync(ct)) is not null)
        {
            if (Parse(line) is { } ev) { if (ev is ClaudeResult) sawResult = true; onEvent(ev); }
        }
        await process.WaitForExitAsync(ct);
        if (!sawResult)
        {
            var err = (await stderr).Trim();
            onEvent(new ClaudeResult(err.Length > 0 ? err : $"Claude wurde ohne Antwort beendet (Code {process.ExitCode}).", sessionId, true));
        }
    }

    static ClaudeEvent? Parse(string line)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            switch (root.GetProperty("type").GetString())
            {
                case "stream_event":
                    var ev = root.GetProperty("event");
                    if (ev.GetProperty("type").GetString() == "content_block_delta" && ev.GetProperty("delta") is { } d && d.GetProperty("type").GetString() == "text_delta")
                        return new ClaudeTextDelta(d.GetProperty("text").GetString() ?? "");
                    if (ev.GetProperty("type").GetString() == "content_block_start" && ev.GetProperty("content_block") is { } cb && cb.GetProperty("type").GetString() == "tool_use")
                        return new ClaudeToolUse(cb.GetProperty("name").GetString() ?? "");
                    return null;
                case "result":
                    return new ClaudeResult(root.TryGetProperty("result", out var r) && r.ValueKind == JsonValueKind.String ? r.GetString()! : "",
                        root.TryGetProperty("session_id", out var s) ? s.GetString() : null,
                        root.TryGetProperty("is_error", out var e) && e.ValueKind == JsonValueKind.True);
                default: return null;
            }
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException) { return null; }
    }
}
