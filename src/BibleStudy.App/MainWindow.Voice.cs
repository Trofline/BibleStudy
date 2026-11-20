using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using BibleStudy.Core.Collab.Media;
using BibleStudy.Core.Models;
using BibleStudy.Core.Storage;

namespace BibleStudy.App;

/// <summary>
/// Spoken notes on a verse: recorded with the microphone of the computer or the phone, kept in the vault (so they synchronize like everything else),
/// shown with a ♪ next to the verse number and played from the verse menu and the verse overview.
/// </summary>
public partial class MainWindow
{
    HashSet<int> _chapterVoice = [];
    AudioPlayer? _voiceNotePlayer;
    VoiceRecording? _noteRec;

    void LoadChapterVoice() => _chapterVoice = _vault.GetVoiceNotes(_curBook, _curChapter).Select(n => n.Ref.Verse).ToHashSet();

    List<VoiceNote> VoiceNotesOf(VerseRef r) => [.. _vault.GetVoiceNotes(r.Book, r.Chapter).Where(n => n.Ref.Verse == r.Verse)];

    string VoiceLabel(VoiceNote n) => (n.Title.Length > 0 ? n.Title + " · " : "") + Duration(n.Seconds) + (n.Created > DateTime.MinValue ? " · " + n.Created.ToString("d") : "");

    /// <summary>The entries of the verse menu: record, and for each spoken note of the verse play and delete.</summary>
    IEnumerable<Control> VoiceMenuItems(VerseRef r, Control anchor, Func<string, Action, bool, MenuItem> make, Func<string, Control[], MenuItem> group)
    {
        yield return make(L("Sprachnotiz aufnehmen …"), () => ShowVoiceRecorder(r, anchor), true);
        var notes = VoiceNotesOf(r);
        if (notes.Count == 0) yield break;
        yield return group(L("Sprachnotizen ({0})", notes.Count), [.. notes.Select(n => (Control)group(VoiceLabel(n),
        [
            make(L("Abspielen"), () => PlayVoiceNote(n.Id), true),
            make(L("Löschen …"), () => _ = DeleteVoiceNoteAsync(n), true),
        ]))]);
    }

    /// <summary>A row of the verse overview: the note with a button to play it.</summary>
    Control VoiceRow(VoiceNote n)
    {
        var play = new Button { Content = "▶", Classes = { "tool" }, Width = 32, Height = 28, Padding = new(0), VerticalAlignment = VerticalAlignment.Center };
        ToolTip.SetTip(play, L("Abspielen"));
        play.Click += (_, _) => PlayVoiceNote(n.Id);
        var label = new TextBlock { Text = "♪ " + VoiceLabel(n), TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Margin = new(8, 0, 0, 0) };
        var row = new DockPanel { Children = { play, label } };
        DockPanel.SetDock(play, Dock.Left);
        row.ContextMenu = new ContextMenu { Items = { MenuAction(L("Löschen …"), () => _ = DeleteVoiceNoteAsync(n)) } };
        return row;
    }

    void PlayVoiceNote(long id)
    {
        if (_vault.GetVoiceData(id) is not { } data) return;
        PlayPcm(MuLaw.Decode(data));
    }

    /// <summary>Plays sound through a WAV file in the temporary folder (the player of every system plays files).</summary>
    void PlayPcm(short[] pcm)
    {
        if (!AppPlatform.IsMobile && !AudioPlayer.HasPlayer) { StatusText.Text = L("Kein Audio-Player gefunden. Installiere mpv, ffplay oder mpg123."); return; }
        _voiceNotePlayer?.Stop();   // Windows keeps the file open while it plays
        var wav = Path.Combine(Path.GetTempPath(), $"bs_voicenote_{Environment.ProcessId}.wav");
        try
        {
            Wav.Write(wav, pcm);
            if (!(_voiceNotePlayer ??= new AudioPlayer()).Play(wav)) StatusText.Text = L("Abspielen nicht möglich: {0}", _voiceNotePlayer.Error ?? "");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { StatusText.Text = L("Abspielen nicht möglich: {0}", ex.Message); }
    }

    async Task DeleteVoiceNoteAsync(VoiceNote n)
    {
        if (await AskAsync(L("Sprachnotiz löschen?"), L("Die Sprachnotiz zu {0} ({1}) lässt sich danach nicht mehr zurückholen.", Label(n.Ref), VoiceLabel(n)), L("Abbrechen"), L("Löschen")) != 1) return;
        _voiceNotePlayer?.Stop();
        _vault.DeleteVoiceNote(n.Id);
        AfterVoiceChange(n.Ref);
        StatusText.Text = L("Sprachnotiz gelöscht.");
    }

    void AfterVoiceChange(VerseRef r)
    {
        if (r.Book == _curBook && r.Chapter == _curChapter) RefreshCells();   // the ♪ next to the verse number
        if (GuidePanel.Children.Count > 0) _ = ShowGuideAsync(r);
    }

    /// <summary>The small recorder at the verse: record, stop, listen, name it, keep it. Whatever was recorded is kept when it is closed without "Verwerfen".</summary>
    void ShowVoiceRecorder(VerseRef r, Control anchor)
    {
        short[]? taken = null;
        var start = DateTime.UtcNow;
        var time = new TextBlock { Text = "0:00", FontSize = 22, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new(12, 0, 0, 0) };
        var level = new ProgressBar { Minimum = 0, Maximum = 1, Height = 6, MinWidth = 0 };
        var record = new Button { Content = L("● Aufnehmen"), Classes = { "primary" }, MinWidth = 130 };
        var listen = new Button { Content = L("▶ Anhören"), IsEnabled = false };
        var name = new TextBox { PlaceholderText = L("Titel (freiwillig)"), MaxLength = 80 };
        var save = new Button { Content = L("Speichern"), Classes = { "primary" }, IsEnabled = false };
        var drop = new Button { Content = L("Verwerfen"), IsEnabled = false };
        var info = new TextBlock { Classes = { "caption" }, TextWrapping = TextWrapping.Wrap, Text = L("Höchstens {0} Minuten. Die Aufnahme bleibt im Vault und wird mit deinen Geräten synchronisiert.", VaultStore.VoiceMaxSeconds / 60) };
        var fly = new Flyout { Placement = Avalonia.Controls.PlacementMode.Bottom };
        DispatcherTimer? tick = null;

        void Show(TimeSpan t) => time.Text = $"{(int)t.TotalMinutes}:{t.Seconds:00}";
        void Ready(bool has) { listen.IsEnabled = save.IsEnabled = drop.IsEnabled = has; }
        void StopRecording()
        {
            tick?.Stop(); tick = null;
            if (_noteRec is not { } rec) return;
            _noteRec = null;
            var pcm = rec.Stop();
            level.Value = 0;
            record.Content = L("● Neu aufnehmen");
            taken = pcm.Length >= AudioFormat.Rate / 2 ? pcm : null;   // less than half a second is a slip
            Show(TimeSpan.FromSeconds((double)(taken?.Length ?? 0) / AudioFormat.Rate));
            Ready(taken is not null);
            if (taken is null) info.Text = L("Nichts aufgenommen. Ist das richtige Mikrofon eingestellt?");
        }
        void Keep()
        {
            if (taken is not { } pcm) return;
            var secs = Math.Max(1, (pcm.Length + AudioFormat.Rate / 2) / AudioFormat.Rate);
            _vault.AddVoiceNote(r, MuLaw.Encode(pcm), secs, (name.Text ?? "").Trim());
            taken = null;
            AfterVoiceChange(r);
            StatusText.Text = L("Sprachnotiz zu {0} gespeichert ({1}).", Label(r), Duration(secs));
        }

        record.Click += async (_, _) =>
        {
            if (_noteRec is not null) { StopRecording(); return; }
            _voiceNotePlayer?.Stop();
            Ready(false); taken = null;
            record.IsEnabled = false;
            try { _noteRec = await VoiceRecorder.StartAsync(VaultStore.VoiceMaxSeconds); }
            catch (Exception ex) { info.Text = ex is InvalidOperationException ? ex.Message : L("Das Mikrofon ließ sich nicht öffnen: {0}", ex.Message); record.IsEnabled = true; return; }
            record.IsEnabled = true;
            record.Content = L("■ Stopp");
            start = DateTime.UtcNow;
            tick = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            tick.Tick += (_, _) =>
            {
                var t = DateTime.UtcNow - start;
                Show(t);
                level.Value = _noteRec?.Level ?? 0;
                if (t.TotalSeconds >= VaultStore.VoiceMaxSeconds) StopRecording();
            };
            tick.Start();
        };
        listen.Click += (_, _) => { if (taken is { } pcm) PlayPcm(pcm); };
        save.Click += (_, _) => { Keep(); fly.Hide(); };
        drop.Click += (_, _) => { taken = null; fly.Hide(); };
        fly.Closed += (_, _) => { StopRecording(); Keep(); };   // closed by a tap beside it: what was spoken is not lost

        fly.Content = new StackPanel
        {
            Spacing = 10, Width = 370,
            Children =
            {
                new TextBlock { Text = L("Sprachnotiz zu {0}", Label(r)), FontWeight = FontWeight.SemiBold },
                new StackPanel { Orientation = Orientation.Horizontal, Children = { record, time } },
                level,
                name,
                new WrapPanel { Children = { listen, save, drop }, ItemSpacing = 6, LineSpacing = 6 },
                info,
            },
        };
        fly.ShowAt(anchor, showAtPointer: !AppPlatform.IsMobile);
    }
}
