using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using BibleStudy.Core.Models;
using BibleStudy.Core.Storage;

namespace BibleStudy.App;

/// <summary>
/// Studying together: larger things are sent in pieces (a whole vault, all highlights of a book or of the vault, notes). The receiver sees a card
/// and decides: a vault is opened as a guest (gone when the program closes) or imported as a new vault; highlights and notes are added to the
/// own vault. Nothing is saved without a click, and everything received is checked first.
/// </summary>
public partial class MainWindow
{
    const int PieceBytes = 48 * 1024;
    const long MaxBlob = 300L * 1024 * 1024;
    const int MaxBulkMarks = 20_000;
    readonly Dictionary<string, (string Kind, string Name, long Size, int Parts, MemoryStream Data, int Next, TextBlock Progress)> _incoming = [];

    // ---- sending ----------------------------------------------------------------------------------------------------------

    /// <summary>Sends data in pieces; <paramref name="done"/> null sends it quietly (no line in the log, as for the pictures of a document). False when the connection went.</summary>
    async Task<bool> SendBlobAsync(string kind, string name, byte[] data, string? done)
    {
        var id = Guid.NewGuid().ToString("N")[..12];
        var parts = (int)Math.Max(1, (data.LongLength + PieceBytes - 1) / PieceBytes);
        var progress = new TextBlock { Classes = { "caption" }, TextWrapping = TextWrapping.Wrap };
        if (done is not null) AddCollabItem(progress);
        CollabSend(new { t = "blob", id, kind, name, size = data.LongLength, parts });

        for (var n = 0; n < parts; n++)
        {
            if (!CollabOpen) { progress.Text = L("Senden abgebrochen: die Verbindung ist getrennt."); return false; }
            var len = (int)Math.Min(PieceBytes, data.LongLength - (long)n * PieceBytes);
            CollabSend(new { t = "part", id, n, data = Convert.ToBase64String(data, n * PieceBytes, len) });
            if (n % 8 == 0) progress.Text = L("Sende „{0}“ … {1} %", name, n * 100 / parts);
            await Task.Delay(n % 4 == 0 ? 15 : 2);   // gives the connection time; a burst of megabytes could overflow it
        }
        if (done is not null) progress.Text = done;
        return true;
    }

    // ---- the pictures of a shared document ------------------------------------------------------------------------------

    const long MaxSharedImage = 16L * 1024 * 1024;
    static readonly string[] ImageMimes = ["image/png", "image/jpeg", "image/gif", "image/webp", "image/bmp"];
    /// <summary>Pictures received for documents (by their id), until the document is saved; only the latest few are kept.</summary>
    readonly Dictionary<string, (string Mime, byte[] Data)> _receivedImages = [];

    /// <summary>The pictures a document shows, sent before it (each in pieces, as a quiet blob "image" named "id mime").</summary>
    async Task SendDocImagesAsync(string body)
    {
        foreach (var id in DocImageRef().Matches(body).Select(m => m.Groups["id"].Value).Distinct().Take(40))
        {
            if (_vault.GetImage(id) is not { } img || img.Data.LongLength > MaxSharedImage) continue;
            if (!await SendBlobAsync("image", id + " " + img.Mime, img.Data, null)) return;
        }
    }

    /// <summary>A picture arrived: kept only when it is what its id says (the id is made from the picture itself) and of a known kind.</summary>
    void ReceiveImage(string name, byte[] data)
    {
        var parts = name.Split(' ', 2);
        if (parts.Length != 2 || !ImageMimes.Contains(parts[1])) return;
        if (VaultStore.ImageId(data) != parts[0]) return;
        if (_receivedImages.Count >= 60) _receivedImages.Remove(_receivedImages.Keys.First());
        _receivedImages[parts[0]] = (parts[1], data);
    }

    /// <summary>The received pictures that a document shows go into the own vault (before the document is saved).</summary>
    void KeepReceivedImages(string body)
    {
        foreach (Match m in DocImageRef().Matches(body))
            if (_receivedImages.TryGetValue(m.Groups["id"].Value, out var img))
                try { _vault.AddImage(img.Data, img.Mime); } catch (Exception ex) { ErrorLog.Write(ex); }
    }

    void ShowShareMarksMenu(Control anchor)
    {
        var menu = new MenuFlyout();
        void Add(string header, Func<List<Mark>> pick, string what)
        {
            var item = new MenuItem { Header = header };
            item.Click += async (_, _) => await SendMarksAsync(pick(), what);
            menu.Items.Add(item);
        }
        var chosen = _selVerses.Count > 0 ? _selVerses.ToHashSet() : _selected is int s ? [s] : [];
        if (chosen.Count > 0)
            Add(L("Gewählte Verse"), () => _vault.GetMarks(_curBook, _curChapter).Where(m => chosen.Contains(m.Ref.Verse)).ToList(), L("den gewählten Versen"));
        Add(L("Dieses Kapitel ({0})", $"{BookName(_curBook)} {_curChapter}"), () => _vault.GetMarks(_curBook, _curChapter), $"{BookName(_curBook)} {_curChapter}");
        Add(L("Dieses Buch ({0})", BookName(_curBook)), () => _vault.GetMarks(_curBook), BookName(_curBook));
        Add(L("Alle meine Markierungen"), () => _vault.GetMarks(), L("dem ganzen Vault"));
        menu.ShowAt(anchor);
    }

    async Task SendMarksAsync(List<Mark> marks, string what)
    {
        if (marks.Count == 0) { AddCollabLog(L("Dort gibt es noch keine Markierungen von dir."), system: true); return; }
        var items = marks.Take(MaxBulkMarks).Select(m => new { b = m.Ref.Book, c = m.Ref.Chapter, v = m.Ref.Verse, e = m.EndVerse, col = m.Color, t = Clip(m.Text, 300), n = Clip(m.Note, 500) }).ToList();
        var bytes = JsonSerializer.SerializeToUtf8Bytes(items);
        await SendBlobAsync("marks", what, bytes, L("Du hast {0} Markierungen aus {1} gesendet.", items.Count, what));
    }

    void ShowShareNotesMenu(Control anchor)
    {
        var menu = new MenuFlyout();
        void Add(string header, Func<Note, bool> pick, string what)
        {
            var item = new MenuItem { Header = header };
            item.Click += async (_, _) => await SendNotesAsync(_vault.GetNotes().Where(pick).ToList(), what);
            menu.Items.Add(item);
        }
        Add(L("Dieses Kapitel ({0})", $"{BookName(_curBook)} {_curChapter}"), n => n.Ref.Book == _curBook && n.Ref.Chapter == _curChapter, $"{BookName(_curBook)} {_curChapter}");
        Add(L("Dieses Buch ({0})", BookName(_curBook)), n => n.Ref.Book == _curBook, BookName(_curBook));
        Add(L("Alle meine Notizen"), _ => true, L("dem ganzen Vault"));
        menu.ShowAt(anchor);
    }

    async Task SendNotesAsync(List<Note> notes, string what)
    {
        notes = notes.Where(n => n.Text.Trim().Length > 0).ToList();
        if (notes.Count == 0) { AddCollabLog(L("Dort gibt es noch keine Notizen von dir."), system: true); return; }
        var items = notes.Take(MaxBulkMarks).Select(n => new { b = n.Ref.Book, c = n.Ref.Chapter, v = n.Ref.Verse, t = Clip(n.Text, 20_000) }).ToList();
        await SendBlobAsync("notes", what, JsonSerializer.SerializeToUtf8Bytes(items), L("Du hast {0} Notizen aus {1} gesendet.", items.Count, what));
    }

    void ShowSendVaultMenu(Control anchor)
    {
        var menu = new MenuFlyout();
        foreach (var name in VaultNames().Where(n => !IsGuestVault(n)))
        {
            var vault = name;
            var item = new MenuItem { Header = vault };
            item.Click += async (_, _) => await SendVaultAsync(vault);
            menu.Items.Add(item);
        }
        menu.ShowAt(anchor);
    }

    /// <summary>The vault as the same zip the export makes (database, name, colors), without the reading progress and without private settings.</summary>
    async Task SendVaultAsync(string name)
    {
        var snapshot = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"bs_{Guid.NewGuid():N}.db");
        var zipPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"bs_{Guid.NewGuid():N}.zip");
        try
        {
            AddCollabLog(L("Vault „{0}“ wird vorbereitet …", name), mine: true, system: true);
            await Task.Run(() =>
            {
                StoreOf(name).BackupTo(snapshot);
                using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);
                zip.CreateEntryFromFile(snapshot, "vault.db", CompressionLevel.Optimal);
                var shared = new AppSettings { CustomColors = _settings.CustomColors, ColorNames = _settings.ColorNames, DefaultColor = _settings.DefaultColor, SubMarkColor = _settings.SubMarkColor, CustomSubColors = _settings.CustomSubColors };
                using (var s = new StreamWriter(zip.CreateEntry("settings.json").Open())) s.Write(JsonSerializer.Serialize(shared));
                using (var n = new StreamWriter(zip.CreateEntry("name.txt").Open(), new System.Text.UTF8Encoding(false))) n.Write(name);
            });
            var bytes = await File.ReadAllBytesAsync(zipPath);
            if (bytes.LongLength > MaxBlob) { AddCollabLog(L("Dieser Vault ist zu groß zum Senden."), system: true); return; }
            await SendBlobAsync("vault", name, bytes, L("Du hast den Vault „{0}“ gesendet ({1}).", name, SizeText(bytes.LongLength)));
        }
        catch (Exception ex) { AddCollabLog(L("Fehler: ") + ex.Message, system: true); }
        finally { TryDeleteFile(snapshot); TryDeleteFile(zipPath); }
    }

    static void TryDeleteFile(string path) { try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { } }

    // ---- receiving (untrusted) --------------------------------------------------------------------------------------------

    bool HandleCollabShare(string type, JsonElement root)
    {
        string Str(string key, int max) => root.TryGetProperty(key, out var e) && e.ValueKind == JsonValueKind.String ? Clip(e.GetString(), max) : "";
        long Long(string key) => root.TryGetProperty(key, out var e) && e.ValueKind == JsonValueKind.Number && e.TryGetInt64(out var n) ? n : -1;
        switch (type)
        {
            case "blob":
            {
                var id = Str("id", 16); var kind = Str("kind", 10); var size = Long("size"); var parts = (int)Long("parts");
                if (id.Length == 0 || kind is not ("vault" or "marks" or "notes" or "voice" or "image" or "paths" or "file" or "bible") || size < 1 || size > MaxBlob || parts < 1 || parts != (size + PieceBytes - 1) / PieceBytes) return true;
                if (kind == "image" && size > MaxSharedImage) return true;
                if (kind == "paths" && size > MaxSentPathBytes) return true;
                if (kind == "file" && size > MaxPcFile) return true;
                if (kind == "file" && size > MaxSentFile)
                {
                    AddCollabLog(L("{0} wollte dir die Datei „{1}“ schicken ({2}). Auf dem Handy lassen sich Dateien bis {3} empfangen; am PC geht mehr.", PeerWho, SafeIncomingFileName(Str("name", 100)), SizeText(size), SizeText(MaxSentFile)), system: true);
                    return true;
                }
                if (_incoming.Count >= 3) return true;
                var progress = new TextBlock { Classes = { "caption" }, TextWrapping = TextWrapping.Wrap, Text = L("{0} sendet etwas …", PeerWho) };
                if (kind != "image") AddCollabItem(progress);   // the pictures of a document come quietly
                _incoming[id] = (kind, Str("name", 100).Trim(), size, parts, new MemoryStream(), 0, progress);
                return true;
            }
            case "part":
            {
                var id = Str("id", 16);
                if (!_incoming.TryGetValue(id, out var b)) return true;
                if (Long("n") != b.Next) { Drop(id, L("Die Übertragung ist durcheinandergekommen. Bitte noch einmal senden.")); return true; }
                byte[] piece;
                try { piece = Convert.FromBase64String(Str("data", PieceBytes * 2)); } catch (FormatException) { Drop(id, L("Die Übertragung war fehlerhaft.")); return true; }
                if (piece.Length > PieceBytes || b.Data.Length + piece.Length > b.Size) { Drop(id, L("Die Übertragung war fehlerhaft.")); return true; }
                b.Data.Write(piece);
                b.Next++;
                _incoming[id] = b;
                if (b.Next % 8 == 0) b.Progress.Text = L("Empfange „{0}“ … {1} %", b.Name, b.Next * 100 / b.Parts);
                if (b.Next == b.Parts)
                {
                    _incoming.Remove(id);
                    _collabLog.Children.Remove(b.Progress);
                    if (b.Data.Length != b.Size) return true;
                    var data = b.Data.ToArray();
                    switch (b.Kind)
                    {
                        case "vault": ShowReceivedVault(b.Name, data); break;
                        case "marks": ShowReceivedMarks(b.Name, data); break;
                        case "notes": ShowReceivedNotes(b.Name, data); break;
                        case "voice": ShowVoiceMessage(PeerWho, b.Name, data, false); break;
                        case "image": ReceiveImage(b.Name, data); break;
                        case "paths": ShowReceivedPaths(data); break;
                        case "file": ShowReceivedFile(b.Name, data); break;
                        case "bible": ShowReceivedTranslation(b.Name, data); break;
                    }
                }
                return true;
            }
        }
        return false;
    }

    void Drop(string id, string why)
    {
        if (_incoming.Remove(id, out var b)) b.Progress.Text = why;
    }

    void ShowReceivedVault(string name, byte[] zip)
    {
        if (name.Length == 0) name = L("Geteilter Vault");
        var file = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"bs_shared_{Guid.NewGuid():N}.zip");
        var guest = new Button { Content = L("Als Gast ansehen"), Classes = { "tool" }, Width = double.NaN, Padding = new(10, 0), Margin = new(0, 0, 6, 6) };
        var keep = new Button { Content = L("Als neuen Vault speichern"), Classes = { "tool" }, Width = double.NaN, Padding = new(10, 0), Margin = new(0, 0, 6, 6) };
        ToolTip.SetTip(guest, L("Er verschwindet, wenn du das Programm schließt; deine eigenen Daten bleiben unberührt."));
        var info = new TextBlock { Classes = { "caption" }, TextWrapping = TextWrapping.Wrap };
        async Task Import(bool asGuest)
        {
            guest.IsEnabled = keep.IsEnabled = false;
            try
            {
                using (var check = new ZipArchive(new MemoryStream(zip))) if (check.GetEntry("vault.db") is null) { info.Text = L("Die Datei enthält keinen Vault (vault.db)."); return; }
                await File.WriteAllBytesAsync(file, zip);
                await ImportFromPathAsync(file, asGuest);
                info.Text = BackupInfo.Text;
            }
            catch (Exception ex) { info.Text = L("Import fehlgeschlagen: ") + ex.Message; guest.IsEnabled = keep.IsEnabled = true; }
            finally { TryDeleteFile(file); }
        }
        guest.Click += async (_, _) => await Import(true);
        keep.Click += async (_, _) => await Import(false);
        AddCollabItem(Card(new StackPanel
        {
            Spacing = 6,
            Children =
            {
                new TextBlock { Text = L("{0} sendet den Vault „{1}“ ({2})", PeerWho, name, SizeText(zip.LongLength)), FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap },
                new WrapPanel { Children = { guest, keep } },
                info,
            },
        }));
        NotifyChat(L("{0} sendet den Vault „{1}“ ({2})", PeerWho, name, SizeText(zip.LongLength)));
    }

    void ShowReceivedMarks(string what, byte[] json)
    {
        var list = new List<(VerseRef Ref, int End, string Color, string Text, string Note)>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return;
            foreach (var e in doc.RootElement.EnumerateArray().Take(MaxBulkMarks))
            {
                if (e.ValueKind != JsonValueKind.Object) continue;
                var book = e.TryGetProperty("b", out var b) ? Clip(b.GetString(), 8) : "";
                int Get(string k) => e.TryGetProperty(k, out var x) && x.ValueKind == JsonValueKind.Number && x.TryGetInt32(out var n) ? n : 0;
                var (c, v, end) = (Get("c"), Get("v"), Get("e"));
                if (_books.All(x => x.Id != book) || c is < 1 or > 200 || v is < 1 or > 200) continue;
                if (end <= v || end > 200) end = 0;
                var color = e.TryGetProperty("col", out var col) && col.GetString() is { } cs && Regex.IsMatch(cs, "^#[0-9A-Fa-f]{6}$") ? cs : _settings.DefaultColor;
                list.Add((new VerseRef(book, c, v), end, color, e.TryGetProperty("t", out var t) ? Clip(t.GetString(), 300) : "", e.TryGetProperty("n", out var n) ? Clip(n.GetString(), 500) : ""));
            }
        }
        catch (JsonException) { return; }
        catch (InvalidOperationException) { return; }
        if (list.Count == 0) return;
        var who = PeerWho;
        var take = new Button { Content = L("Übernehmen"), Classes = { "tool" }, Width = double.NaN, Padding = new(10, 0), HorizontalAlignment = HorizontalAlignment.Left };
        take.Click += (_, _) =>
        {
            // a highlight that is already there (same verse, same color) is not added twice
            var have = _vault.GetMarks().Select(m => (m.Ref.Book, m.Ref.Chapter, m.Ref.Verse, m.Color.ToUpperInvariant())).ToHashSet();
            var added = 0;
            foreach (var m in list)
            {
                if (!have.Add((m.Ref.Book, m.Ref.Chapter, m.Ref.Verse, m.Color.ToUpperInvariant()))) continue;
                _vault.AddMark(Mark.AnyTranslation, m.Ref, -1, -1, m.Text, m.Color, (m.Note.Length > 0 ? m.Note + " · " : "") + L("Geteilt von {0}", who), null, AutoTags(m.Ref), m.End);
                added++;
            }
            take.IsEnabled = false; take.Content = L("Übernommen ✓ ({0} neu)", added);
            LoadChapterMarks(); Paint(); RefreshCollection();
        };
        var chapters = list.Select(m => (m.Ref.Book, m.Ref.Chapter)).Distinct().Count();
        var dots = new WrapPanel();
        foreach (var color in list.Select(m => m.Color.ToUpperInvariant()).Distinct().Take(20))
            dots.Children.Add(new Border { Width = 10, Height = 10, CornerRadius = new(5), Margin = new(0, 0, 4, 4), Background = new SolidColorBrush(Color.Parse(color)) });
        var text = L("{0} sendet {1} Markierungen aus {2} ({3} Kapitel)", who, list.Count, what, chapters);
        AddCollabItem(Card(new StackPanel
        {
            Spacing = 6,
            Children =
            {
                new TextBlock { Text = text, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap },
                dots,
                new TextBlock { Text = string.Join(", ", list.Select(m => $"{BookName(m.Ref.Book)} {m.Ref.Chapter},{m.Ref.Verse}").Take(8)) + (list.Count > 8 ? " …" : ""), Classes = { "caption" }, TextWrapping = TextWrapping.Wrap },
                take,
            },
        }));
        NotifyChat(text);
    }

    void ShowReceivedNotes(string what, byte[] json)
    {
        var list = new List<(VerseRef Ref, string Text)>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return;
            foreach (var e in doc.RootElement.EnumerateArray().Take(MaxBulkMarks))
            {
                if (e.ValueKind != JsonValueKind.Object) continue;
                var book = e.TryGetProperty("b", out var b) ? Clip(b.GetString(), 8) : "";
                int Get(string k) => e.TryGetProperty(k, out var x) && x.ValueKind == JsonValueKind.Number && x.TryGetInt32(out var n) ? n : 0;
                var (c, v) = (Get("c"), Get("v"));
                var t = e.TryGetProperty("t", out var tt) ? Clip(tt.GetString(), 20_000).Trim() : "";
                if (_books.Any(x => x.Id == book) && c is >= 1 and <= 200 && v is >= 0 and <= 200 && t.Length > 0) list.Add((new VerseRef(book, c, v), t));
            }
        }
        catch (JsonException) { return; }
        catch (InvalidOperationException) { return; }
        if (list.Count == 0) return;
        var who = PeerWho;
        var take = new Button { Content = L("Übernehmen"), Classes = { "tool" }, Width = double.NaN, Padding = new(10, 0), HorizontalAlignment = HorizontalAlignment.Left };
        ToolTip.SetTip(take, L("Wo du schon eine Notiz hast, wird die andere darunter angehängt; nichts wird überschrieben."));
        take.Click += (_, _) =>
        {
            foreach (var (r, text) in list)
            {
                var mine = _vault.GetNote(r)?.Text ?? "";
                var theirs = L("{0} schreibt:", who) + "\n" + text;
                if (mine.Contains(text, StringComparison.Ordinal)) continue;   // taken over before
                _vault.SetNote(r, mine.Trim().Length == 0 ? theirs : mine.TrimEnd() + "\n\n" + theirs);
            }
            take.IsEnabled = false; take.Content = L("Übernommen ✓");
            RefreshCollection();
            if (_curChapterData is not null) _ = GoToAsync(_curBook, _curChapter, _selected);
        };
        var text = L("{0} sendet {1} Notizen aus {2}", who, list.Count, what);
        AddCollabItem(Card(new StackPanel
        {
            Spacing = 6,
            Children =
            {
                new TextBlock { Text = text, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap },
                new TextBlock { Text = Shorten(list[0].Text.ReplaceLineEndings(" "), 200), Classes = { "caption" }, TextWrapping = TextWrapping.Wrap },
                take,
            },
        }));
        NotifyChat(text);
    }
}
