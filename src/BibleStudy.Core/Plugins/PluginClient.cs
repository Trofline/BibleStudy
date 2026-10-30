using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace BibleStudy.Core.Plugins;

public sealed class PluginException(string message) : Exception(message);

/// <summary>Sends one request line to a plugin and returns its answer line. The real one talks to a program; tests use a fake.</summary>
public interface IPluginTransport : IDisposable
{
    Task<string> ExchangeAsync(string requestLine, CancellationToken ct);
}

/// <summary>
/// The conversation with a plugin: one JSON object per line in, one per line out ({"id":1,"method":"chapter","params":{…}} →
/// {"id":1,"result":…} or {"id":1,"error":"…"}). See PLUGINS.md for the methods.
/// </summary>
public sealed class PluginClient(IPluginTransport transport) : IDisposable
{
    int _next;

    public async Task<JsonElement> CallAsync(string method, object? args = null, CancellationToken ct = default)
    {
        var id = Interlocked.Increment(ref _next);
        var request = JsonSerializer.Serialize(new { id, method, @params = args ?? new { } });
        var line = await transport.ExchangeAsync(request, ct);
        JsonDocument doc;
        try { doc = JsonDocument.Parse(line); }
        catch (JsonException) { throw new PluginException(L("Das Plugin hat keine gültige Antwort gegeben.")); }
        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) throw new PluginException(L("Das Plugin hat keine gültige Antwort gegeben."));
            if (root.TryGetProperty("error", out var err) && err.ValueKind != JsonValueKind.Null)
                throw new PluginException(err.ValueKind == JsonValueKind.String ? err.GetString() ?? "" : err.ToString());
            if (!root.TryGetProperty("id", out var answered) || !answered.TryGetInt32(out var n) || n != id)
                throw new PluginException(L("Das Plugin hat auf eine andere Anfrage geantwortet."));
            return root.TryGetProperty("result", out var result) ? result.Clone() : default;
        }
    }

    public void Dispose() => transport.Dispose();
}

/// <summary>Runs the plugin as its own program (any language) and talks to it through standard input and output.</summary>
public sealed class ProcessTransport(PluginManifest manifest, string dir, Func<IReadOnlyDictionary<string, string>>? environment = null) : IPluginTransport
{
    static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);
    const int MaxLine = 16 * 1024 * 1024;

    readonly SemaphoreSlim _gate = new(1, 1);
    Process? _process;

    /// <summary>The program to start: a file inside the plugin folder, or a program found on the PATH (python, node …).</summary>
    public static string ResolveCommand(string command, string dir)
    {
        if (command.IndexOfAny(['/', '\\']) < 0) return command;
        var full = Path.GetFullPath(Path.Combine(dir, command));
        var root = Path.GetFullPath(dir).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new PluginException(L("Der Startbefehl liegt außerhalb des Plugin-Ordners."));
        return full;
    }

    /// <summary>
    /// Linux/macOS: a zip made on Windows may lack the "executable" flag, and on macOS a program from the internet is killed unless it is signed
    /// and not marked as downloaded. The user has allowed this plugin explicitly, so its program is made startable here.
    /// </summary>
    static void PrepareUnixProgram(string program)
    {
        if (OperatingSystem.IsWindows() || !File.Exists(program)) return;
        try { File.SetUnixFileMode(program, File.GetUnixFileMode(program) | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* the start reports what is wrong */ }
        if (!OperatingSystem.IsMacOS()) return;
        Run("xattr", "-d", "com.apple.quarantine", program);
        if (Run("codesign", "--verify", program) != 0) Run("codesign", "--force", "--sign", "-", program);
    }

    static int Run(string file, params string[] args)
    {
        try
        {
            var info = new ProcessStartInfo(file) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var a in args) info.ArgumentList.Add(a);
            using var p = Process.Start(info);
            if (p is null) return -1;
            p.StandardOutput.ReadToEnd(); p.StandardError.ReadToEnd();
            return p.WaitForExit(20000) ? p.ExitCode : -1;
        }
        catch (Exception e) when (e is IOException or System.ComponentModel.Win32Exception or InvalidOperationException) { return -1; }
    }

    Process Start()
    {
        var program = ResolveCommand(manifest.Command, dir);
        PrepareUnixProgram(program);
        var info = new ProcessStartInfo(program)
        {
            WorkingDirectory = dir, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false), StandardOutputEncoding = Encoding.UTF8, UseShellExecute = false, CreateNoWindow = true,
        };
        foreach (var a in manifest.Args) info.ArgumentList.Add(a);
        // how a plugin reaches the program (BIBLESTUDY_API, BIBLESTUDY_TOKEN, BIBLESTUDY_DATA): see PLUGINS.md
        if (environment is not null) foreach (var (k, v) in environment()) info.Environment[k] = v;
        var p = Process.Start(info) ?? throw new PluginException(L("Das Plugin ließ sich nicht starten."));
        p.ErrorDataReceived += (_, _) => { };   // read and dropped, so a chatty plugin cannot block on a full pipe
        p.BeginErrorReadLine();
        return p;
    }

    public async Task<string> ExchangeAsync(string requestLine, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (_process is null || _process.HasExited) { _process?.Dispose(); _process = Start(); }
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
            limit.CancelAfter(Timeout);
            try
            {
                await _process.StandardInput.WriteLineAsync(requestLine.AsMemory(), limit.Token);
                await _process.StandardInput.FlushAsync(limit.Token);
                while (true)
                {
                    var line = await _process.StandardOutput.ReadLineAsync(limit.Token);
                    if (line is null) throw new PluginException(L("Das Plugin wurde beendet."));
                    if (line.Length > MaxLine) throw new PluginException(L("Die Antwort des Plugins ist zu groß."));
                    if (line.TrimStart().StartsWith('{')) return line;   // anything else (log lines …) is skipped
                }
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                Kill();
                throw new PluginException(L("Das Plugin antwortet nicht (nach {0} Sekunden abgebrochen).", (int)Timeout.TotalSeconds));
            }
            catch (IOException) { Kill(); throw new PluginException(L("Das Plugin wurde beendet.")); }
        }
        finally { _gate.Release(); }
    }

    void Kill()
    {
        try { _process?.Kill(true); } catch { /* already gone */ }
        _process?.Dispose(); _process = null;
    }

    public void Dispose() { Kill(); _gate.Dispose(); }
}
