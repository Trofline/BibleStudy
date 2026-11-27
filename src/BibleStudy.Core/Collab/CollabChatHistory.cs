using System.Text.Json;

namespace BibleStudy.Core.Collab;

/// <summary>One saved line of a chat: a message or a shared verse.</summary>
public sealed class CollabChatLine
{
    /// <summary>The id of the message in the group (empty for old lines); the same line is never shown twice.</summary>
    public string Mid { get; set; } = "";
    public DateTime When { get; set; }
    public string Who { get; set; } = "";
    public string Text { get; set; } = "";
    public bool Mine { get; set; }
    /// <summary>"chat" or "verse".</summary>
    public string Kind { get; set; } = "chat";
    public string Book { get; set; } = "";
    public int Chapter { get; set; }
    public int Verse { get; set; }
    public string Translation { get; set; } = "";
}

/// <summary>
/// The chats of "studying together", kept only on this device, one history per other person (by their lasting id, see <c>AppSettings.CollabId</c>).
/// When the same person is in the room again, their history comes back; someone else gets an empty chat. A line written while several
/// people were there is kept with each of them. Nothing of this is ever sent anywhere.
/// </summary>
public sealed class CollabChatHistory
{
    /// <summary>The newest lines kept per person; older ones drop out.</summary>
    public const int MaxPerPerson = 1000;
    const int MaxText = 4000;

    readonly string _path;
    Dictionary<string, List<CollabChatLine>> _byPerson = [];

    CollabChatHistory(string path) => _path = path;

    /// <summary>Reads the file; a missing or damaged file is an empty history (never an error).</summary>
    public static CollabChatHistory Load(string path)
    {
        var h = new CollabChatHistory(path);
        try
        {
            if (File.Exists(path) && JsonSerializer.Deserialize<Dictionary<string, List<CollabChatLine>>>(File.ReadAllText(path)) is { } data)
                h._byPerson = data.Where(x => IsId(x.Key) && x.Value is not null).ToDictionary(x => x.Key, x => x.Value.Where(l => l is not null).ToList());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException) { }
        return h;
    }

    /// <summary>A lasting id as the program makes it: short, letters and digits only (it comes from the other side, so it is checked).</summary>
    static bool IsId(string id) => id.Length is > 0 and <= 32 && id.All(char.IsAsciiLetterOrDigit);

    /// <summary>How many people there is a saved chat with.</summary>
    public int People => _byPerson.Count(x => x.Value.Count > 0);

    /// <summary>How many lines are saved in all.</summary>
    public int Lines => _byPerson.Values.Sum(l => l.Count);

    /// <summary>Keeps a line with each of the given people (those in the room when it was written).</summary>
    public void Add(IEnumerable<string> people, CollabChatLine line)
    {
        if (line.Text.Length > MaxText) line.Text = line.Text[..MaxText];
        var any = false;
        foreach (var id in people.Distinct())
        {
            if (!IsId(id)) continue;
            if (!_byPerson.TryGetValue(id, out var list)) _byPerson[id] = list = [];
            if (line.Mid.Length > 0 && list.Any(l => l.Mid == line.Mid)) continue;
            list.Add(line);
            if (list.Count > MaxPerPerson) list.RemoveRange(0, list.Count - MaxPerPerson);
            any = true;
        }
        if (any) Save();
    }

    /// <summary>The saved chat with these people, oldest first, each line once (a line kept with two of them comes once).</summary>
    public List<CollabChatLine> For(IEnumerable<string> people)
    {
        var seen = new HashSet<string>();
        var all = new List<CollabChatLine>();
        foreach (var id in people.Distinct())
            if (_byPerson.TryGetValue(id, out var list))
                foreach (var l in list)
                    if (l.Mid.Length == 0 || seen.Add(l.Mid)) all.Add(l);
        return all.OrderBy(l => l.When).ToList();
    }

    public bool Has(string person) => _byPerson.TryGetValue(person, out var l) && l.Count > 0;

    /// <summary>Forgets every saved chat (and deletes the file).</summary>
    public void Clear()
    {
        _byPerson.Clear();
        try { if (File.Exists(_path)) File.Delete(_path); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var tmp = _path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(_byPerson));
            File.Move(tmp, _path, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* the chat is a convenience; never crash on save */ }
    }
}
