using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Avalonia.Threading;

namespace BibleStudy.App;

/// <summary>
/// Local control API for the MCP server: POST /call {"tool","args"} on 127.0.0.1 only, protected by a random token
/// that is written to control.json (readable by the current user only, because it lives in their profile).
/// Tools run on the UI thread, so they see and change the same state as the user.
/// </summary>
public sealed class ControlServer : IDisposable
{
    static readonly JsonSerializerOptions Json = new() { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };

    readonly HttpListener _listener = new();
    readonly Func<string, JsonElement, Task<object?>> _handler;
    readonly string _infoPath;
    readonly string _token = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));

    public int Port { get; private set; }

    public ControlServer(Func<string, JsonElement, Task<object?>> handler, string infoPath)
    {
        _handler = handler;
        _infoPath = infoPath;
    }

    public bool Start()
    {
        for (var port = 49200; port < 49300 && Port == 0; port++)
        {
            try
            {
                _listener.Prefixes.Clear();
                _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
                _listener.Start();
                Port = port;
            }
            catch (HttpListenerException) { /* port taken, try the next */ }
        }
        if (Port == 0) return false;
        Directory.CreateDirectory(Path.GetDirectoryName(_infoPath)!);
        File.WriteAllText(_infoPath, JsonSerializer.Serialize(new { port = Port, token = _token, pid = Environment.ProcessId }));
        _ = Task.Run(LoopAsync);
        return true;
    }

    async Task LoopAsync()
    {
        while (_listener.IsListening)
        {
            HttpListenerContext ctx;
            try { ctx = await _listener.GetContextAsync(); }
            catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException or InvalidOperationException) { break; }
            _ = Task.Run(() => HandleAsync(ctx));
        }
    }

    async Task HandleAsync(HttpListenerContext ctx)
    {
        try
        {
            if (ctx.Request.Headers["X-Token"] != _token) { Respond(ctx, 401, """{"error":"unauthorized"}"""); return; }
            if (ctx.Request.HttpMethod != "POST" || ctx.Request.Url?.AbsolutePath != "/call") { Respond(ctx, 404, """{"error":"not found"}"""); return; }
            using var doc = await JsonDocument.ParseAsync(ctx.Request.InputStream);
            var tool = doc.RootElement.GetProperty("tool").GetString() ?? "";
            var args = doc.RootElement.TryGetProperty("args", out var a) ? a.Clone() : default;
            var result = await Dispatcher.UIThread.InvokeAsync(() => _handler(tool, args));
            Respond(ctx, 200, JsonSerializer.Serialize(result ?? new { ok = true }, Json));
        }
        catch (Exception ex)
        {
            try { Respond(ctx, 500, JsonSerializer.Serialize(new { error = ex.Message })); } catch { /* client gone */ }
        }
    }

    static void Respond(HttpListenerContext ctx, int status, string body)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        ctx.Response.StatusCode = status;
        ctx.Response.ContentType = "application/json; charset=utf-8";
        ctx.Response.ContentLength64 = bytes.Length;
        ctx.Response.OutputStream.Write(bytes);
        ctx.Response.Close();
    }

    public void Dispose()
    {
        try { if (_listener.IsListening) _listener.Stop(); _listener.Close(); } catch { /* shutting down */ }
        try { if (File.Exists(_infoPath) && File.ReadAllText(_infoPath).Contains($"\"pid\":{Environment.ProcessId}")) File.Delete(_infoPath); } catch { /* best effort */ }
    }
}
