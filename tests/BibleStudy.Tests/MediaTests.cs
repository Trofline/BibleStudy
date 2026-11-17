using BibleStudy.Core.Collab.Media;
using Xunit;

namespace BibleStudy.Tests;

public class MediaTests
{
    [Fact]
    public void Speech_survives_mu_law_closely()
    {
        var pcm = Enumerable.Range(0, 1600).Select(i => (short)(Math.Sin(i / 10.0) * 12000)).ToArray();
        var back = MuLaw.Decode(MuLaw.Encode(pcm));
        Assert.Equal(pcm.Length, back.Length);
        for (var i = 0; i < pcm.Length; i++) Assert.True(Math.Abs(pcm[i] - back[i]) <= Math.Abs(pcm[i]) / 16 + 40, $"{pcm[i]} became {back[i]}");
        Assert.Equal(pcm, MuLaw.FromBytes(MuLaw.ToBytes(pcm)));
    }

    [Fact]
    public void Silence_stays_silence_and_loud_is_loud()
    {
        Assert.True(MuLaw.Level(new short[640]) < 0.01);
        Assert.True(MuLaw.Level(Enumerable.Repeat((short)20000, 640).ToArray()) > 0.9);
    }

    [Fact]
    public void Pictures_are_cut_out_of_the_camera_stream_even_when_split_anywhere()
    {
        byte[] Jpeg(byte fill) => [0xFF, 0xD8, fill, fill, 0xFF, 0x00, fill, 0xFF, 0xD9];
        var stream = Jpeg(1).Concat(Jpeg(2)).Concat(Jpeg(3)).ToArray();
        for (var cut = 1; cut < stream.Length; cut++)
        {
            var s = new MjpegSplitter();
            var got = s.Push(stream.AsSpan(0, cut)).Concat(s.Push(stream.AsSpan(cut))).ToList();
            Assert.Equal(3, got.Count);
            Assert.Equal(Jpeg(2), got[1]);
        }
    }

    [Fact]
    public void Two_voices_are_mixed_after_a_short_wait_and_a_late_voice_catches_up()
    {
        var m = new AudioMixer();
        short[] Piece(short v) => Enumerable.Repeat(v, AudioFormat.FrameSamples).ToArray();
        m.Add("ben", Piece(100));
        Assert.All(m.Next(), s => Assert.Equal(0, s));   // waits until 120 ms are there
        m.Add("ben", Piece(100)); m.Add("ben", Piece(100));
        m.Add("carla", Piece(50)); m.Add("carla", Piece(50)); m.Add("carla", Piece(50));
        Assert.All(m.Next(), s => Assert.Equal(150, s));
        for (var i = 0; i < 80; i++) m.Add("dan", Piece(1));
        Assert.True(m.Queued("dan") <= 50);
    }

    [Fact]
    public void Device_lists_of_windows_and_mac_are_read()
    {
        const string win = """
            [in#0 @ 0000] "HD Webcam" (video)
            [in#0 @ 0000]   Alternative name "@device_pnp_\\?\usb#vid"
            [in#0 @ 0000] "Mikrofon (USB Audio Device)" (audio)
            [in#0 @ 0000]   Alternative name "@device_cm_{33D9}\wave_{25E4}"
            """;
        var w = Ffmpeg.ParseDshow(win);
        Assert.Equal([new MediaDevice("HD Webcam", "HD Webcam", true), new MediaDevice("Mikrofon (USB Audio Device)", "Mikrofon (USB Audio Device)", false)], w);

        const string mac = """
            [AVFoundation indev @ 0x1] AVFoundation video devices:
            [AVFoundation indev @ 0x1] [0] FaceTime HD Camera
            [AVFoundation indev @ 0x1] [1] Capture screen 0
            [AVFoundation indev @ 0x1] AVFoundation audio devices:
            [AVFoundation indev @ 0x1] [0] MacBook Pro Microphone
            """;
        var m = Ffmpeg.ParseAvfoundation(mac);
        Assert.Equal([new MediaDevice("FaceTime HD Camera", "0", true), new MediaDevice("MacBook Pro Microphone", "0", false)], m);
    }

    [Fact]
    public async Task The_test_camera_and_test_tone_deliver_pictures_and_sound_through_ffmpeg()
    {
        if (Ffmpeg.Find() is null) return;   // no ffmpeg on this computer: nothing to try
        Environment.SetEnvironmentVariable("BIBLESTUDY_FAKEMEDIA", "1");
        try
        {
            var pictures = 0; var pieces = 0;
            using (MediaIO.StartCamera(new MediaDevice("Testbild", "fake", true), _ => Interlocked.Increment(ref pictures)))
            using (MediaIO.StartMicrophone(null, p => { if (p.Length == AudioFormat.FrameSamples && MuLaw.Level(p) > 0.1) Interlocked.Increment(ref pieces); }))
                await Task.Delay(2500);
            Assert.True(pictures >= 5, $"pictures {pictures}");
            Assert.True(pieces >= 20, $"sound pieces {pieces}");
        }
        finally { Environment.SetEnvironmentVariable("BIBLESTUDY_FAKEMEDIA", null); }
    }

    [Fact]
    public async Task Windows_lists_its_microphones_and_its_speaker_takes_sound()
    {
        if (!OperatingSystem.IsWindows()) return;
        var mics = WinAudio.Microphones();
        Assert.All(mics, m => Assert.False(string.IsNullOrWhiteSpace(m.Name)));
        var asked = 0;
        // a build server has no sound card: there the speaker cannot start, which is no fault of the program
        if (Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true")
            try { WinAudio.StartSpeaker(() => new short[AudioFormat.FrameSamples]).Dispose(); } catch (InvalidOperationException) { return; }
        using (WinAudio.StartSpeaker(() => { Interlocked.Increment(ref asked); return new short[AudioFormat.FrameSamples]; }))   // silence: nothing to hear
            await Task.Delay(600);
        Assert.True(asked >= 10, $"asked {asked}");
    }
}
