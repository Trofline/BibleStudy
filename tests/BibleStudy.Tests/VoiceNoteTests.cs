using BibleStudy.Core.Models;
using BibleStudy.Core.Storage;

namespace BibleStudy.Tests;

public class VoiceNoteTests
{
    static string NewDir() { var d = Path.Combine(Path.GetTempPath(), "bs_voice_" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(d); return d; }

    [Fact]
    public void A_voice_note_is_kept_once_comes_back_and_a_merge_brings_it_along()
    {
        var dir = NewDir();
        var home = new VaultStore(Path.Combine(dir, "home.db"), "home");
        var phonePath = Path.Combine(dir, "phone.db");
        var phone = new VaultStore(phonePath, "phone");
        var r = new VerseRef("JHN", 3, 16);
        byte[] sound = [1, 2, 3, 4, 5, 6, 7, 8];

        var id = phone.AddVoiceNote(r, sound, 3, "Gedanke");
        Assert.Equal(id, phone.AddVoiceNote(r, sound, 3));   // the same sound: the same note
        var n = Assert.Single(phone.GetVoiceNotes("JHN", 3));
        Assert.Equal((r, 3, "Gedanke"), (n.Ref, n.Seconds, n.Title));
        Assert.Equal(sound, phone.GetVoiceData(id));
        Assert.Empty(home.GetVoiceNotes());

        home.MergeFrom(phonePath, "Handy");
        var got = Assert.Single(home.GetVoiceNotes("JHN", 3));
        Assert.Equal(sound, home.GetVoiceData(got.Id));
        home.MergeFrom(phonePath, "Handy");
        Assert.Single(home.GetVoiceNotes());   // merged twice: still once
    }

    [Fact]
    public void A_voice_note_deleted_on_one_device_is_deleted_on_the_other_and_a_new_name_is_taken_over()
    {
        var dir = NewDir();
        var pcPath = Path.Combine(dir, "pc.db");
        var pc = new VaultStore(pcPath, "pc");
        var keep = pc.AddVoiceNote(new VerseRef("GEN", 1, 1), [9, 9, 9, 9], 2);
        var gone = pc.AddVoiceNote(new VerseRef("GEN", 1, 2), [8, 8, 8, 8], 2);
        for (var i = 0; i < 6; i++) pc.AddTodo("t" + i, null);   // enough entries that one deletion is not held back as a lost copy
        var basePath = Path.Combine(dir, "base.db"); File.Copy(pcPath, basePath);
        var phonePath = Path.Combine(dir, "phone.db"); File.Copy(pcPath, phonePath);

        var phone = new VaultStore(phonePath, "phone");
        phone.DeleteVoiceNote(phone.GetVoiceNotes().Single(n => n.Ref.Verse == 2).Id);
        phone.RenameVoiceNote(phone.GetVoiceNotes().Single().Id, "Schöpfung");

        var result = pc.MergeFrom(phonePath, "Handy", basePath);
        var left = Assert.Single(pc.GetVoiceNotes());
        Assert.Equal(keep, left.Id);
        Assert.Equal("Schöpfung", left.Title);
        Assert.Null(pc.GetVoiceData(gone));
        Assert.Equal(0, result.Held);
    }

    [Fact]
    public void Wiping_a_vault_removes_its_voice_notes()
    {
        var v = new VaultStore(Path.Combine(NewDir(), "v.db"), "v");
        v.AddVoiceNote(new VerseRef("PSA", 23, 1), [1, 2, 3], 1);
        v.Wipe();
        Assert.Empty(v.GetVoiceNotes());
    }
}
