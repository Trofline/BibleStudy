using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace BibleStudy.Core.Sync;

/// <summary>
/// The pairing code: 16 random bytes, written as 26 letters and digits (typed in groups of four). Every device that knows the code belongs to the same
/// group and may exchange vaults; nobody else can even open the connection. The code never travels over the network.
/// </summary>
public static class SyncCode
{
    const string Abc = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
    public const string UrlStart = "biblestudy-sync://pair?k=";

    public static string NewKey() => Encode(RandomNumberGenerator.GetBytes(16));

    public static string Encode(byte[] bytes)
    {
        var sb = new StringBuilder();
        int buf = 0, bits = 0;
        foreach (var b in bytes)
        {
            buf = (buf << 8) | b; bits += 8;
            while (bits >= 5) { sb.Append(Abc[(buf >> (bits - 5)) & 31]); bits -= 5; }
            buf &= (1 << bits) - 1;
        }
        if (bits > 0) sb.Append(Abc[(buf << (5 - bits)) & 31]);
        return sb.ToString();
    }

    /// <summary>The 16 bytes of a code, or null when it is not a valid one.</summary>
    public static byte[]? Decode(string code)
    {
        if (code.Length != 26) return null;
        var bytes = new List<byte>();
        int buf = 0, bits = 0;
        foreach (var ch in code)
        {
            var v = Abc.IndexOf(ch);
            if (v < 0) return null;
            buf = (buf << 5) | v; bits += 5;
            if (bits >= 8) { bytes.Add((byte)((buf >> (bits - 8)) & 255)); bits -= 8; }
            buf &= (1 << bits) - 1;
        }
        return bytes.Count == 16 ? [.. bytes] : null;
    }

    /// <summary>"ABCD-EFGH-…" for showing and typing.</summary>
    public static string Pretty(string key) => string.Join('-', Enumerable.Range(0, (key.Length + 3) / 4).Select(i => key.Substring(i * 4, Math.Min(4, key.Length - i * 4))));

    /// <summary>The address a QR code holds. It carries the addresses of this device in the network (h=), so that the other one can connect at once.</summary>
    public static string ToUrl(string key, IEnumerable<string>? hosts = null)
    {
        var h = hosts?.Take(3).ToList();
        return UrlStart + key + (h is { Count: > 0 } ? "&h=" + string.Join(',', h) : "");
    }

    /// <summary>The addresses (ip:port) in a pairing address.</summary>
    public static IReadOnlyList<string> ParseHosts(string text)
    {
        var at = text.IndexOf("h=", StringComparison.OrdinalIgnoreCase);
        if (at < 0) return [];
        return [.. text[(at + 2)..].Split([',', '&', ' '], StringSplitOptions.RemoveEmptyEntries).Where(s => s.Contains(':') && System.Net.IPAddress.TryParse(s[..s.LastIndexOf(':')], out _)).Take(3)];
    }

    /// <summary>The code from what a person typed or from the address a QR code holds (spaces, dashes and small letters do not matter; 0, 1 and 8 are read as O, I and B).</summary>
    public static string? Parse(string text)
    {
        text = text.Trim();
        var at = text.IndexOf("k=", StringComparison.OrdinalIgnoreCase);
        if (text.Contains("://", StringComparison.Ordinal) && at >= 0) { text = text[(at + 2)..]; var amp = text.IndexOf('&'); if (amp >= 0) text = text[..amp]; }
        var sb = new StringBuilder();
        foreach (var ch in text.ToUpperInvariant())
        {
            if (!char.IsLetterOrDigit(ch)) continue;
            sb.Append(ch switch { '0' => 'O', '1' => 'I', '8' => 'B', _ => ch });
        }
        var key = sb.ToString();
        return Decode(key) is null ? null : key;
    }
}

/// <summary>This device's part of the comparison: who it is, the pairing code, and what the last comparison with each device brought (so nothing is sent twice).</summary>
public sealed class SyncConfig
{
    public string DeviceId { get; set; } = Guid.NewGuid().ToString("N");
    public string DeviceName { get; set; } = Environment.MachineName;
    /// <summary>The pairing code (null = not set up).</summary>
    public string? Key { get; set; }
    /// <summary>Look for the other devices in the same network by itself.</summary>
    public bool Auto { get; set; } = true;
    /// <summary>"device|vault" → "my fingerprint|their fingerprint" at the end of the last comparison.</summary>
    public Dictionary<string, string> Done { get; } = [];
    /// <summary>Imported translations go to the other devices too (see <see cref="ISyncBibles"/>).</summary>
    public bool Bibles { get; set; } = true;
    public string? LastTime { get; set; }
    public string? LastPeer { get; set; }
    /// <summary>Addresses (ip:port) of other devices of the group: the call-out may not reach them (an iPhone hears none), a direct connection does.</summary>
    public List<string> Hosts { get; } = [];
    /// <summary>The cloud folder used as a mailbox: a path on a computer, or a bookmark of the system (a phone). Null = none.</summary>
    public string? CloudPath { get; set; }
    public string? CloudBookmark { get; set; }

    public IReadOnlyList<string> HostList() { lock (Hosts) return [.. Hosts]; }

    /// <summary>Remembers where a device was reached. Returns true when something changed.</summary>
    public bool AddHost(string ip, int port)
    {
        var entry = ip + ":" + port;
        lock (Hosts)
        {
            if (Hosts.Contains(entry)) return false;
            Hosts.RemoveAll(h => h.StartsWith(ip + ":", StringComparison.Ordinal));
            Hosts.Insert(0, entry);
            if (Hosts.Count > 8) Hosts.RemoveRange(8, Hosts.Count - 8);
            return true;
        }
    }

    public void RememberHost(System.Net.IPAddress address, int port, string path)
    {
        if (System.Net.IPAddress.IsLoopback(address)) return;
        if (AddHost(address.ToString(), port)) Save(path);
    }

    public byte[]? KeyBytes => Key is null ? null : SyncCode.Decode(Key);

    public static SyncConfig Load(string path)
    {
        var c = new SyncConfig();
        try
        {
            if (!File.Exists(path)) return c;
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var r = doc.RootElement;
            string? S(string n) => r.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            if (S("id") is { Length: > 0 } id) c.DeviceId = id;
            if (S("name") is { Length: > 0 } name) c.DeviceName = name;
            c.Key = S("key");
            if (r.TryGetProperty("auto", out var a) && a.ValueKind is JsonValueKind.True or JsonValueKind.False) c.Auto = a.GetBoolean();
            if (r.TryGetProperty("bibles", out var bb) && bb.ValueKind is JsonValueKind.True or JsonValueKind.False) c.Bibles = bb.GetBoolean();
            c.LastTime = S("lastTime"); c.LastPeer = S("lastPeer");
            c.CloudPath = S("cloud"); c.CloudBookmark = S("cloudBookmark");
            if (r.TryGetProperty("hosts", out var hs) && hs.ValueKind == JsonValueKind.Array)
                foreach (var h in hs.EnumerateArray()) if (h.GetString() is { } hv) c.Hosts.Add(hv);
            if (r.TryGetProperty("done", out var d) && d.ValueKind == JsonValueKind.Object)
                foreach (var p in d.EnumerateObject()) if (p.Value.GetString() is { } v) c.Done[p.Name] = v;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { /* a broken file: start without it */ }
        return c;
    }

    readonly object _saveGate = new();

    public void Save(string path)
    {
        // a comparison that is asked and one that asks can save at the same moment: one after the other, or one of them would be lost
        lock (_saveGate)
        try
        {
            using var ms = new MemoryStream();
            using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true }))
            {
                w.WriteStartObject();
                w.WriteString("id", DeviceId); w.WriteString("name", DeviceName);
                if (Key is not null) w.WriteString("key", Key);
                w.WriteBoolean("auto", Auto);
                w.WriteBoolean("bibles", Bibles);
                if (LastTime is not null) w.WriteString("lastTime", LastTime);
                if (LastPeer is not null) w.WriteString("lastPeer", LastPeer);
                if (CloudPath is not null) w.WriteString("cloud", CloudPath);
                if (CloudBookmark is not null) w.WriteString("cloudBookmark", CloudBookmark);
                w.WriteStartArray("hosts");
                lock (Hosts) foreach (var h in Hosts) w.WriteStringValue(h);
                w.WriteEndArray();
                w.WriteStartObject("done");
                lock (Done) foreach (var (k, v) in Done) w.WriteString(k, v);
                w.WriteEndObject();
                w.WriteEndObject();
            }
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var tmp = path + ".tmp";
            File.WriteAllBytes(tmp, ms.ToArray());
            File.Move(tmp, path, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* tried again at the next change */ }
    }
}
