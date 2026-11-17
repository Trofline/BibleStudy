using System.Security.Cryptography;

namespace BibleStudy.Core.Storage;

public sealed partial class VaultStore
{
    /// <summary>Pictures of the documents: kept inside the vault (so backups, synchronizing and sharing take them along). The id is made from the picture itself, so the same picture is stored once.</summary>
    void InitImages() => Exec("CREATE TABLE IF NOT EXISTS images(id TEXT PRIMARY KEY, mime TEXT NOT NULL, data BLOB NOT NULL)");

    public string AddImage(byte[] data, string mime)
    {
        var id = Convert.ToHexString(SHA256.HashData(data))[..16].ToLowerInvariant();
        Exec("INSERT OR IGNORE INTO images(id,mime,data) VALUES($i,$m,$d)", c =>
        {
            c.Parameters.AddWithValue("$i", id); c.Parameters.AddWithValue("$m", mime); c.Parameters.AddWithValue("$d", data);
        });
        return id;
    }

    public (string Mime, byte[] Data)? GetImage(string id)
    {
        var rows = Query("SELECT mime,data FROM images WHERE id=$i", c => c.Parameters.AddWithValue("$i", id), r => (r.GetString(0), r.GetFieldValue<byte[]>(1)));
        return rows.Count == 0 ? null : rows[0];
    }
}
