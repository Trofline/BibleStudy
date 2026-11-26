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
            using (await MediaIO.StartCameraAsync(new MediaDevice("Testbild", "fake", true), CameraQuality.Medium, _ => Interlocked.Increment(ref pictures)))
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

    [Theory]
    [InlineData("674d402ada01e0089f970110000003001000000788f1832a", 1920, 1080)]   // main profile, cropped from 1088, with an escaped 03
    [InlineData("67640016acd940d83de6f0110000030001000003000a0f162d96", 854, 480)] // high profile, cropped from 864
    public void The_picture_size_is_read_from_the_sps(string hex, int width, int height) =>
        Assert.Equal((width, height), H264.Size(Convert.FromHexString(hex)));

    static byte[] Au(bool key, byte fill) => key
        ? [0, 0, 0, 1, 0x09, 0xF0, 0, 0, 0, 1, 0x67, 0x4D, 0, 0, 0, 1, 0x68, 0xEE, 0, 0, 1, 0x65, fill, fill]
        : [0, 0, 0, 1, 0x09, 0xF0, 0, 0, 1, 0x41, fill, fill, fill];

    [Fact]
    public void Video_pictures_are_cut_at_their_delimiters_even_when_split_anywhere()
    {
        var stream = Au(true, 1).Concat(Au(false, 2)).Concat(Au(false, 3)).Concat(Au(true, 4)).ToArray();
        for (var cut = 1; cut < stream.Length; cut++)
        {
            var s = new H264Splitter();
            var got = s.Push(stream.AsSpan(0, cut)).Concat(s.Push(stream.AsSpan(cut))).ToList();
            Assert.Equal(3, got.Count);   // the last one is complete only when the next begins
            Assert.Equal(Au(false, 2), got[1]);
            Assert.True(H264.IsKeyframe(got[0]));
            Assert.False(H264.IsKeyframe(got[2]));
        }
    }

    [Fact]
    public void A_key_picture_gives_its_parameter_sets_and_converts_for_the_iphone()
    {
        var key = Au(true, 7);
        var sets = H264.ParameterSets(key);
        Assert.NotNull(sets);
        Assert.Equal(new byte[] { 0x67, 0x4D }, sets!.Value.Sps);
        Assert.Equal(new byte[] { 0x68, 0xEE }, sets.Value.Pps);
        Assert.Equal(new byte[] { 0, 0, 0, 3, 0x65, 7, 7 }, H264.ToLengthPrefixed(key));
        Assert.Equal(key[6..], H264.WithoutDelimiter(key));
        Assert.Null(H264.ParameterSets(Au(false, 1)));
    }

    [Fact]
    public void Video_colors_become_screen_colors()
    {
        // 2x2 pixels: white, black / grey, grey; U and V neutral
        byte[] y = [235, 16, 126, 126], u = [128], v = [128];
        var bgra = new byte[16];
        Yuv.ToBgra(y, 0, 2, u, 0, v, 0, 1, 1, 2, 2, bgra);
        Assert.Equal(new byte[] { 255, 255, 255, 255, 0, 0, 0, 255 }, bgra[..8]);
        Assert.InRange(bgra[8], 126, 130);
        // only every second pixel: one pixel, the first
        var small = new byte[4];
        Yuv.ToBgra(y, 0, 2, u, 0, v, 0, 1, 1, 2, 2, small, every: 2);
        Assert.Equal(new byte[] { 255, 255, 255, 255 }, small);
    }

    [Fact]
    public void Sixty_pictures_a_second_need_half_as_much_data_again()
    {
        var hd = CameraQuality.Of(2, 30);
        var hd60 = CameraQuality.Of(2, 60);
        Assert.Equal((1280, 720, 30), (hd.Width, hd.Height, hd.Fps));
        Assert.Equal((60, hd.Kbps * 3 / 2), (hd60.Fps, hd60.Kbps));
        Assert.Equal(1920, CameraQuality.Of(3).Width);
        Assert.Equal(15, CameraQuality.Of(0, 60).Fps);   // the low level stays low
        Assert.Equal(15, CameraQuality.Of(3, 60).JpegFps);
    }

    [Fact]
    public async Task The_test_camera_goes_through_h264_and_back_into_pictures()
    {
        if (Ffmpeg.Find() is null) return;   // no ffmpeg on this computer: nothing to try
        Environment.SetEnvironmentVariable("BIBLESTUDY_FAKEMEDIA", "1");
        try
        {
            if (!Ffmpeg.CanH264()) return;
            var units = new List<byte[]>();
            var pictures = 0; var size = (0, 0);
            using var decoder = MediaIO.StartH264Decoder((w, h, bgra) => { size = (w, h); Assert.Equal(w * h * 4, bgra.Length); Interlocked.Increment(ref pictures); })!;
            using (await MediaIO.StartCameraAsync(new MediaDevice("Testbild", "fake", true), CameraQuality.Medium, au => { lock (units) units.Add(au); decoder.Push(au); }, h264: true))
                await Task.Delay(2500);
            await Task.Delay(500);
            lock (units)
            {
                Assert.True(units.Count >= 20, $"units {units.Count}");
                Assert.True(H264.IsKeyframe(units[0]));
                Assert.Equal((640, 360), H264.Size(H264.ParameterSets(units[0])!.Value.Sps));
                Assert.True(units.Average(u => u.Length) < 40_000, "a video picture is small");
            }
            Assert.True(pictures >= 15, $"pictures {pictures}");
            Assert.Equal((640, 360), size);

            // the own preview: the same stream, made smaller
            var small = (0, 0);
            using var preview = MediaIO.StartH264Decoder((w, h, _) => small = (w, h), maxWidth: 320)!;
            lock (units) foreach (var u in units) preview.Push(u);
            await Task.Delay(1500);
            Assert.Equal((320, 180), small);
        }
        finally { Environment.SetEnvironmentVariable("BIBLESTUDY_FAKEMEDIA", null); }
    }
}
