using System.Diagnostics;
using System.Text.Json;
using BibleStudy.Core.Plugins;

namespace BibleStudy.Tests;

public class PluginTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "bs_" + Guid.NewGuid().ToString("N"));

    /// <summary>Answers like a plugin would, without a program behind it.</summary>
    sealed class Fake(Func<string, string, JsonElement, object?> handler) : IPluginTransport
    {
        public List<string> Methods { get; } = [];
        public Task<string> ExchangeAsync(string line, CancellationToken ct)
        {
            using var doc = JsonDocument.Parse(line);
            var id = doc.RootElement.GetProperty("id").GetInt32();
            var method = doc.RootElement.GetProperty("method").GetString()!;
            Methods.Add(method);
            try { return Task.FromResult(JsonSerializer.Serialize(new { id, result = handler(method, line, doc.RootElement.GetProperty("params").Clone()) })); }
            catch (Exception ex) { return Task.FromResult(JsonSerializer.Serialize(new { id, error = ex.Message })); }
        }
        public void Dispose() { }
    }

    sealed class Source(PluginClient client, params string[] ids) : IPluginSource
    {
        public IEnumerable<PluginInfo> Allowed() => ids.Select(i => new PluginInfo(new PluginManifest { Id = i, Name = "Test " + i }, "", "h", null));
        public PluginClient? ClientFor(string pluginId) => ids.Contains(pluginId) ? client : null;
    }

    static object? Handler(string method, string line, JsonElement p) => method switch
    {
        "translations" => new[] { new { id = "t1", name = "Test Bible", shortName = "TB", language = "de", languageName = "Deutsch", attribution = "Test GmbH" } },
        "books" => new object[] { new { id = "GEN", name = "1. Mose", chapters = 50 }, new { id = "XXX", name = "Unknown", chapters = 3 }, new { id = "MAT", name = "Matthäus", chapters = 28 } },
        "chapter" => new { verses = new object[] { new { number = 2, text = "Zweiter." }, new { number = 1, text = " Erster. " }, new { number = 3, text = "" } } },
        _ => throw new Exception("unsupported"),
    };

    [Fact]
    public async Task Provider_MapsTranslationsBooksAndChapters()
    {
        var provider = new PluginBibleProvider(new Source(new PluginClient(new Fake(Handler)), "demo"));

        var t = Assert.Single(await provider.GetTranslationsAsync());
        Assert.Equal("plugin:demo:t1", t.Id);
        Assert.Contains("Test GmbH", t.License.Attribution);
        Assert.True(PluginBibleProvider.Owns(t.Id));

        var books = await provider.GetBooksAsync(t.Id);
        Assert.Equal(["GEN", "MAT"], books.Select(b => b.Id));       // unknown book ids are dropped, Bible order
        Assert.Equal(50, books[0].NumberOfChapters);

        var ch = await provider.GetChapterAsync(t.Id, "GEN", 1);
        Assert.Equal([1, 2], ch.Verses.Select(v => v.Number));        // empty verses dropped, sorted
        Assert.Equal("Erster.", ch.Verses[0].Text);
    }

    [Fact]
    public async Task Chapters_AreKeptInMemory_NotAskedTwice()
    {
        var fake = new Fake(Handler);
        var provider = new PluginBibleProvider(new Source(new PluginClient(fake), "demo"));
        await provider.GetChapterAsync("plugin:demo:t1", "GEN", 1);
        await provider.GetChapterAsync("plugin:demo:t1", "GEN", 1);
        Assert.Equal(1, fake.Methods.Count(m => m == "chapter"));
    }

    [Fact]
    public async Task WholeText_IsOptional_AndSaysSo()
    {
        var provider = new PluginBibleProvider(new Source(new PluginClient(new Fake(Handler)), "demo"));
        await Assert.ThrowsAsync<NotSupportedException>(() => provider.GetAllVersesAsync("plugin:demo:t1"));

        var withAll = new PluginBibleProvider(new Source(new PluginClient(new Fake((m, l, p) =>
            m == "all" ? new[] { new { book = "GEN", chapter = 1, number = 1, text = "Erster." } } : Handler(m, l, p))), "demo"));
        var all = await withAll.GetAllVersesAsync("plugin:demo:t1");
        Assert.Equal("Erster.", Assert.Single(all).Text);
    }

    [Fact]
    public async Task BrokenPlugin_DoesNotHideTheOthers_AndNotAllowedPluginsAreRefused()
    {
        var ok = new PluginClient(new Fake(Handler));
        var bad = new PluginClient(new Fake((m, l, p) => throw new Exception("kaputt")));
        var source = new MultiSource(("good", ok), ("bad", bad));
        var provider = new PluginBibleProvider(source);

        var list = await provider.GetTranslationsAsync();
        Assert.Equal(["plugin:good:t1"], list.Select(t => t.Id));
        Assert.Contains("kaputt", provider.Problems["bad"]);

        await Assert.ThrowsAsync<PluginException>(() => provider.GetBooksAsync("plugin:other:t1"));   // not allowed / not there
    }

    sealed class MultiSource(params (string Id, PluginClient Client)[] plugins) : IPluginSource
    {
        public IEnumerable<PluginInfo> Allowed() => plugins.Select(p => new PluginInfo(new PluginManifest { Id = p.Id, Name = p.Id }, "", "h", null));
        public PluginClient? ClientFor(string id) => plugins.FirstOrDefault(p => p.Id == id).Client;
    }

    [Fact]
    public void Manager_NeedsPermission_AndAgainAfterAChange()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "a"));
        Directory.CreateDirectory(Path.Combine(_dir, "bad"));
        File.WriteAllText(Path.Combine(_dir, "a", "plugin.json"), """{ "id": "a", "name": "A", "command": "node", "args": ["x.js"] }""");
        File.WriteAllText(Path.Combine(_dir, "bad", "plugin.json"), """{ "id": "Bad Id!", "name": "B", "command": "node" }""");
        var approved = new Dictionary<string, string>();
        using var manager = new PluginManager(_dir, () => approved);

        var found = manager.Discover();
        var a = found.Single(p => p.Manifest.Id == "a");
        Assert.NotNull(found.Single(p => p.Manifest.Name == "B").Problem);   // invalid id
        Assert.False(manager.IsAllowed(a));                                   // nothing runs without permission
        Assert.Null(manager.ClientFor("a"));

        approved["a"] = a.Hash;
        Assert.True(manager.IsAllowed(manager.Discover().Single(p => p.Manifest.Id == "a")));

        File.WriteAllText(Path.Combine(_dir, "a", "plugin.json"), """{ "id": "a", "name": "A", "command": "evil" }""");
        Assert.False(manager.IsAllowed(manager.Discover().Single(p => p.Manifest.Id == "a")));   // changed: allow again
    }

    [Fact]
    public async Task CommandsPlugin_IsNotAskedForTranslations_AndHasItsCommandsListed()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "c"));
        File.WriteAllText(Path.Combine(_dir, "c", "plugin.json"),
            """{ "id": "c", "name": "C", "provides": ["commands"], "commands": [ { "id": "x", "title": "Do X" }, { "id": "", "title": "no id" } ], "command": "node", "args": ["m.js"] }""");
        var approved = new Dictionary<string, string>();
        using var manager = new PluginManager(_dir, () => approved);
        var info = manager.Discover().Single();
        Assert.False(info.Manifest.ProvidesBibles);
        Assert.Empty(manager.Commands());                           // nothing before the user allowed it
        approved["c"] = info.Hash;
        var (plugin, command) = Assert.Single(manager.Commands());  // entries without an id are dropped
        Assert.Equal(("c", "x", "Do X"), (plugin.Manifest.Id, command.Id, command.Title));

        var fake = new Fake((m, l, p) => m == "command" ? new { message = "ok " + p.GetProperty("id").GetString() } : throw new Exception("unsupported"));
        var source = new Source(new PluginClient(fake), "c");
        var withoutBibles = new PluginBibleProvider(new OnlyCommands(source));
        Assert.Empty(await withoutBibles.GetTranslationsAsync());   // never asked for translations
        Assert.Empty(fake.Methods);

        // an old plugin.json without "provides" still means translations
        Assert.True(new PluginManifest().ProvidesBibles);
        Assert.False(new PluginManifest { Commands = [new() { Id = "a", Title = "A" }] }.ProvidesBibles);
        Assert.True(new PluginManifest { Provides = ["bibles", "commands"] }.ProvidesBibles);
    }

    sealed class OnlyCommands(IPluginSource inner) : IPluginSource
    {
        public IEnumerable<PluginInfo> Allowed() => inner.Allowed().Select(p => p with { Manifest = new PluginManifest { Id = p.Manifest.Id, Name = p.Manifest.Name, Provides = ["commands"] } });
        public PluginClient? ClientFor(string pluginId) => inner.ClientFor(pluginId);
    }

    [Fact]
    public void Command_CannotLeaveThePluginFolder()
    {
        Assert.Equal("node", ProcessTransport.ResolveCommand("node", _dir));
        Assert.StartsWith(Path.GetFullPath(_dir), ProcessTransport.ResolveCommand("./run.sh", _dir));
        Assert.Throws<PluginException>(() => ProcessTransport.ResolveCommand("../../evil.exe", _dir));
    }

    static bool HasNode()
    {
        try { using var p = Process.Start(new ProcessStartInfo("node", "-v") { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true }); p!.WaitForExit(5000); return p.ExitCode == 0; }
        catch { return false; }
    }

    [Fact]
    public async Task ExamplePlugin_RunsAsARealProgram()
    {
        if (!HasNode()) return;   // needs Node.js; the protocol itself is covered by the tests above
        var example = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../examples/plugin-local-file"));
        if (!Directory.Exists(example)) return;
        var manifest = JsonSerializer.Deserialize<PluginManifest>(File.ReadAllText(Path.Combine(example, "plugin.json")), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        var source = new Source(new PluginClient(new ProcessTransport(manifest, example)), manifest.Id);
        var provider = new PluginBibleProvider(source);

        var t = Assert.Single(await provider.GetTranslationsAsync());
        var ch = await provider.GetChapterAsync(t.Id, "GEN", 1);
        Assert.Equal(5, ch.Verses.Count);
        Assert.StartsWith("In the beginning", ch.Verses[0].Text);
        await Assert.ThrowsAsync<NotSupportedException>(() => provider.GetAllVersesAsync(t.Id));
    }

    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }
}
