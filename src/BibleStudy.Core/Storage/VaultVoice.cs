using System.Globalization;
using System.Security.Cryptography;
using BibleStudy.Core.Models;

namespace BibleStudy.Core.Storage;

/// <summary>A spoken note on a verse: the sound itself stays in the vault (as G.711 μ-law, 16 kHz, mono: about 8 KB a second), so backups and synchronizing take it along.</summary>
public sealed record VoiceNote(long Id, string Uid, VerseRef Ref, int Seconds, string Title, DateTime Created);

public sealed partial class VaultStore
{
    /// <summary>The longest spoken note (the vault is copied whole when it is synchronized).</summary>
    public const int VoiceMaxSeconds = 300;

    void InitVoice() => Exec("CREATE TABLE IF NOT EXISTS voice_notes(id INTEGER PRIMARY KEY, uid TEXT NOT NULL UNIQUE, book TEXT, chapter INT, verse INT, secs INT, title TEXT, created TEXT, data BLOB NOT NULL)");

    /// <summary>Keeps a spoken note; <paramref name="mulaw"/> is the sound as μ-law bytes. The id across devices is made from the sound, so the same note is kept once.</summary>
    public long AddVoiceNote(VerseRef r, byte[] mulaw, int seconds, string title = "")
    {
        var uid = Convert.ToHexString(SHA256.HashData(mulaw))[..16].ToLowerInvariant();
        Exec("INSERT OR IGNORE INTO voice_notes(uid,book,chapter,verse,secs,title,created,data) VALUES($u,$b,$c,$v,$s,$t,$d,$x)", c =>
        {
            c.Parameters.AddWithValue("$u", uid); c.Parameters.AddWithValue("$b", r.Book); c.Parameters.AddWithValue("$c", r.Chapter); c.Parameters.AddWithValue("$v", r.Verse);
            c.Parameters.AddWithValue("$s", seconds); c.Parameters.AddWithValue("$t", title); c.Parameters.AddWithValue("$d", DateTime.Now.ToString("o", CultureInfo.InvariantCulture));
            c.Parameters.AddWithValue("$x", mulaw);
        });
        var ids = Query("SELECT id FROM voice_notes WHERE uid=$u", c => c.Parameters.AddWithValue("$u", uid), x => x.GetInt64(0));
        return ids.Count > 0 ? ids[0] : 0;
    }

    static VoiceNote ReadVoice(Microsoft.Data.Sqlite.SqliteDataReader x) => new(x.GetInt64(0), x.GetString(1), new VerseRef(x.GetString(2), x.GetInt32(3), x.GetInt32(4)),
        x.IsDBNull(5) ? 0 : x.GetInt32(5), x.IsDBNull(6) ? "" : x.GetString(6),
        DateTime.TryParse(x.IsDBNull(7) ? "" : x.GetString(7), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var d) ? d : DateTime.MinValue);

    const string VoiceColumns = "id,uid,book,chapter,verse,secs,title,created";

    /// <summary>The spoken notes of a chapter (without the sound), oldest first.</summary>
    public List<VoiceNote> GetVoiceNotes(string book, int chapter) =>
        Query($"SELECT {VoiceColumns} FROM voice_notes WHERE book=$b AND chapter=$c ORDER BY verse, id", c => { c.Parameters.AddWithValue("$b", book); c.Parameters.AddWithValue("$c", chapter); }, ReadVoice);

    /// <summary>All spoken notes (without the sound), in Bible order is up to the caller.</summary>
    public List<VoiceNote> GetVoiceNotes() => Query($"SELECT {VoiceColumns} FROM voice_notes ORDER BY id", _ => { }, ReadVoice);

    public byte[]? GetVoiceData(long id)
    {
        var rows = Query("SELECT data FROM voice_notes WHERE id=$i", c => c.Parameters.AddWithValue("$i", id), x => x.GetFieldValue<byte[]>(0));
        return rows.Count == 0 ? null : rows[0];
    }

    public void RenameVoiceNote(long id, string title) =>
        Exec("UPDATE voice_notes SET title=$t WHERE id=$i", c => { c.Parameters.AddWithValue("$t", title.Trim()); c.Parameters.AddWithValue("$i", id); });

    public void DeleteVoiceNote(long id) => Exec("DELETE FROM voice_notes WHERE id=$i", c => c.Parameters.AddWithValue("$i", id));
}
