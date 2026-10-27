using System.Net.Http.Json;
using System.Text.Json;

/// <summary>Calls the control API of the running BibleStudy app (port and token are in control.json).</summary>
public sealed class AppClient
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(5) };

    /// <summary>Portable installs keep their data next to the program (this server sits in its "mcp" subfolder).</summary>
    static string InfoPath
    {
        get
        {
            var app = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, ".."));
            return File.Exists(Path.Combine(app, "portable.flag")) ? Path.Combine(app, "Data", "control.json")
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BibleStudy", "control.json");
        }
    }

    static string Error(string message) => JsonSerializer.Serialize(new { error = message });

    public async Task<string> CallAsync(string tool, object? args = null)
    {
        if (!File.Exists(InfoPath)) return Error("BibleStudy is not running. Ask the user to start the app.");
        int port; string token;
        try
        {
            using var info = JsonDocument.Parse(await File.ReadAllTextAsync(InfoPath));
            port = info.RootElement.GetProperty("port").GetInt32();
            token = info.RootElement.GetProperty("token").GetString()!;
        }
        catch (Exception ex) when (ex is IOException or JsonException or KeyNotFoundException) { return Error("Could not read the app's control info: " + ex.Message); }

        using var request = new HttpRequestMessage(HttpMethod.Post, $"http://127.0.0.1:{port}/call") { Content = JsonContent.Create(new { tool, args = args ?? new { } }) };
        request.Headers.Add("X-Token", token);
        try
        {
            using var response = await Http.SendAsync(request);
            return await response.Content.ReadAsStringAsync();
        }
        catch (HttpRequestException) { return Error("BibleStudy is not reachable (was the app closed?)."); }
    }
}
