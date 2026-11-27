using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using BibleStudy.Core.Collab.Media;

namespace BibleStudy.App;

/// <summary>
/// Studying together with voice and camera: hold a button to talk (push to talk), switch the microphone on for a call, send a voice message,
/// switch the camera on. Sound goes as 40 ms pieces packed with μ-law, pictures as small JPEGs (about 8 a second); everything through the same
/// encrypted connections as the rest. Received sound is mixed and played only while "Hear the others" is on; nothing is recorded or saved,
/// except a voice message the person sends on purpose.
/// </summary>
public partial class MainWindow
{
    IDisposable? _mic, _speaker, _camera;
    readonly AudioMixer _mixer = new();
    bool _callMic, _pttHeld, _recording, _camOn;
    readonly List<short> _voiceRec = [];
    DateTime _recStart;
    readonly WrapPanel _videoTiles = new();
    readonly Dictionary<string, (Border Tile, Image Image, DateTime Last)> _tiles = [];
    readonly TextBlock _speaking = new() { Classes = { "caption" }, TextWrapping = TextWrapping.Wrap };
    readonly TextBlock _mediaInfo = new() { Classes = { "caption" }, TextWrapping = TextWrapping.Wrap };
    DispatcherTimer? _mediaTimer;
    Window? _bigVideo;
    UniformGrid? _bigGrid;
    double _myLevel;
    AudioPlayer? _voicePlayer;

    void InitMedia()
    {
        Ffmpeg.ToolsDir = FfmpegDownload.Dir(_root);
        if (_mediaTimer is not null) return;
        _mediaTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _mediaTimer.Tick += (_, _) => UpdateSpeaking();
        _mediaTimer.Start();
    }

    // ---- the card --------------------------------------------------------------------------------------------------------

    Control BuildMediaBox()
    {
        InitMedia();
        Detach(_videoTiles); Detach(_speaking); Detach(_mediaInfo);
        if (AppPlatform.IsMobile)   // the phone shows the others' cameras and plays voice messages; its own microphone and camera come later
            return new StackPanel { Spacing = 6, Children = { _videoTiles, _mediaInfo } };
        var ptt = new Button { Content = L("Gedrückt halten zum Sprechen"), Classes = { "primary" }, Margin = new(0, 0, 8, 6) };
        ToolTip.SetTip(ptt, L("Solange du drückst, hören dich die anderen (Leertaste geht auch, wenn der Knopf gewählt ist)."));
        ptt.AddHandler(PointerPressedEvent, (_, e) => { e.Pointer.Capture(ptt); SetPtt(true); }, handledEventsToo: true);
        ptt.AddHandler(PointerReleasedEvent, (_, _) => SetPtt(false), handledEventsToo: true);
        ptt.AddHandler(PointerCaptureLostEvent, (_, _) => SetPtt(false), handledEventsToo: true);
        ptt.KeyDown += (_, e) => { if (e.Key == Key.Space) { SetPtt(true); e.Handled = true; } };
        ptt.KeyUp += (_, e) => { if (e.Key == Key.Space) { SetPtt(false); e.Handled = true; } };

        // switches as plain buttons: colored while on, so the state is seen at a glance
        static void Mark(Button b, bool on) => b.Classes.Set("primary", on);
        var call = _callBtn = new Button { Margin = new(0, 0, 8, 6) };
        ToolTip.SetTip(call, L("Wie ein Anruf: Die anderen hören dich, bis du es wieder ausschaltest. Mit Kopfhörern gibt es kein Echo."));
        call.Click += (_, _) => PanelCallClick();

        var voice = new Button { Content = _recording ? L("■ Senden") : L("Sprachnachricht"), Margin = new(0, 0, 8, 6) };
        ToolTip.SetTip(voice, L("Antippen zum Aufnehmen, noch einmal zum Senden (höchstens 2 Minuten)."));
        voice.Click += (_, _) => { if (_recording) StopVoiceMessage(true); else StartVoiceMessage(); voice.Content = _recording ? L("■ Senden") : L("Sprachnachricht"); Mark(voice, _recording); };

        var cam = _camBtn = new Button { Margin = new(0, 0, 8, 6) };
        cam.Click += async (_, _) => { cam.IsEnabled = false; await PanelCameraClickAsync(); cam.IsEnabled = true; };
        RefreshMediaButtons();

        var pin = new Button { Content = _dockWanted ? L("Video nicht festhalten") : L("Video festhalten"), Margin = new(0, 0, 8, 6) };
        Mark(pin, _dockWanted);
        ToolTip.SetTip(pin, L("Zeigt die Bilder der anderen in einem kleinen Feld über dem Text, damit du sie beim Lesen immer siehst. Das Feld lässt sich verkleinern, vergrößern, in eine andere Ecke setzen oder als eigenes Fenster lösen."));
        pin.Click += (_, _) => { ToggleDock(); pin.Content = _dockWanted ? L("Video nicht festhalten") : L("Video festhalten"); Mark(pin, _dockWanted); };
        var big = new Button { Content = L("Videos groß"), Classes = { "tool" }, Width = double.NaN, Padding = new(10, 0), Margin = new(0, 0, 8, 6) };
        big.Click += (_, _) => ShowBigVideo();
        var hear = new CheckBox { Content = L("Die anderen hören"), IsChecked = _settings.CollabHear };
        hear.IsCheckedChanged += (_, _) => { _settings.CollabHear = hear.IsChecked == true; SaveSettings(); if (!_settings.CollabHear) _mixer.ClearAll(); };

        var devices = new StackPanel { Spacing = 6, Margin = new(0, 6, 0, 0) };
        var expander = new Expander { Header = L("Mikrofon und Kamera wählen"), Content = devices };
        expander.Expanding += async (_, _) => await FillDevicesAsync(devices);

        return new Expander
        {
            Header = L("Sprechen und Kamera"), IsExpanded = _callMic || _micMuted || _camOn || _camPaused || _tiles.Count > 0,
            Content = new StackPanel
            {
                Spacing = 6, Margin = new(0, 6, 0, 0),
                Children =
                {
                    new WrapPanel { Children = { ptt, call, voice, cam, pin, big } },
                    hear, _speaking, _mediaInfo, _videoTiles, expander,
                    new TextBlock { Text = L("Mit Kopfhörern klingt es am besten. Nichts wird aufgezeichnet, außer einer Sprachnachricht, die du selbst sendest."), Classes = { "caption" }, TextWrapping = TextWrapping.Wrap },
                },
            },
        };
    }

    async Task FillDevicesAsync(StackPanel to)
    {
        to.Children.Clear();
        to.Children.Add(new TextBlock { Text = L("Suche Geräte …"), Classes = { "caption" } });
        List<MediaDevice> mics = [], cams = [];
        try { mics = await MediaIO.MicrophonesAsync(); cams = await MediaIO.CamerasAsync(); } catch (Exception ex) { _mediaInfo.Text = ex.Message; }
        to.Children.Clear();
        ComboBox Box(List<MediaDevice> list, string chosen, Action<string> save)
        {
            var box = new ComboBox { ItemsSource = new[] { L("Standard") }.Concat(list.Select(d => d.Name)).ToList(), HorizontalAlignment = HorizontalAlignment.Stretch };
            box.SelectedIndex = Math.Max(0, list.FindIndex(d => d.Name == chosen) + 1);
            box.SelectionChanged += (_, _) => { save(box.SelectedIndex <= 0 ? "" : list[box.SelectedIndex - 1].Name); SaveSettings(); };
            return box;
        }
        to.Children.Add(new TextBlock { Text = L("Mikrofon"), FontWeight = FontWeight.SemiBold });
        to.Children.Add(Box(mics, _settings.CollabMic, v => _settings.CollabMic = v));
        to.Children.Add(new TextBlock { Text = L("Kamera"), FontWeight = FontWeight.SemiBold });
        if (cams.Count > 0)
        {
            to.Children.Add(Box(cams, _settings.CollabCamera, v => _settings.CollabCamera = v));
            var quality = new ComboBox
            {
                ItemsSource = new[] { L("Bildqualität: niedrig (für langsames Internet)"), L("Bildqualität: mittel"), L("Bildqualität: HD (720p)") },
                SelectedIndex = Math.Clamp(_settings.CollabCameraQuality, 0, 2), HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            quality.SelectionChanged += async (_, _) =>
            {
                _settings.CollabCameraQuality = quality.SelectedIndex; SaveSettings();
                if (_camOn && _camera is not null) { await SetCameraAsync(false); await SetCameraAsync(true); }   // takes effect at once
            };
            to.Children.Add(quality);
            to.Children.Add(new TextBlock { Text = L("Ruckelt das Bild oder kommt es spät an, wähle eine niedrigere Qualität: HD braucht bei jeder Person mit Kamera etwa 5 Mbit/s zum Senden und bei allen anderen ebenso viel zum Empfangen."), Classes = { "caption" }, TextWrapping = TextWrapping.Wrap });
        }
        else to.Children.Add(new TextBlock { Text = Ffmpeg.Find() is null ? FfmpegHint() : L("Keine Kamera gefunden."), Classes = { "caption" }, TextWrapping = TextWrapping.Wrap });
    }

    string FfmpegHint() => OperatingSystem.IsWindows()
        ? L("Für die Kamera braucht BibleStudy das kostenlose FFmpeg. Lade es im Download-Manager unter „Kamera und Ton“ herunter.")
        : OperatingSystem.IsMacOS() ? L("Für Ton und Kamera braucht BibleStudy das kostenlose FFmpeg. Installiere es im Terminal mit „brew install ffmpeg“.")
        : L("Für Ton und Kamera braucht BibleStudy das kostenlose FFmpeg. Installiere es mit der Paketverwaltung, zum Beispiel „sudo apt install ffmpeg“.");

    async Task<MediaDevice?> PickAsync(bool video)
    {
        var list = video ? await MediaIO.CamerasAsync() : await MediaIO.MicrophonesAsync();
        var chosen = video ? _settings.CollabCamera : _settings.CollabMic;
        return list.FirstOrDefault(d => d.Name == chosen) ?? list.FirstOrDefault();
    }

    // ---- microphone ------------------------------------------------------------------------------------------------------

    void SetPtt(bool on) { if (_pttHeld == on) return; _pttHeld = on; UpdateMic(); }

    /// <summary>The microphone runs while it is needed: talking button held, call on, or a voice message being recorded.</summary>
    async void UpdateMic()
    {
        var need = CollabOpen && (_pttHeld || _callMic) || _recording;
        if (need && _mic is null)
        {
            if (MediaIO.SoundNeedsFfmpeg && Ffmpeg.Find() is null) { _mediaInfo.Text = FfmpegHint(); return; }
            try { var device = await PickAsync(false); _mic ??= MediaIO.StartMicrophone(device, OnMicPiece); _mediaInfo.Text = ""; }
            catch (Exception ex) { _mediaInfo.Text = L("Das Mikrofon ließ sich nicht öffnen: {0}", ex.Message); }
        }
        else if (!need && _mic is not null) { _mic.Dispose(); _mic = null; _myLevel = 0; }
        UpdateSpeaking();
    }

    /// <summary>Every 40 ms of the microphone (on a background thread): into the voice message, and to the others while talking.</summary>
    void OnMicPiece(short[] pcm)
    {
        _myLevel = MuLaw.Level(pcm);
        if (_recording) lock (_voiceRec) { if (_voiceRec.Count < AudioFormat.Rate * 120) _voiceRec.AddRange(pcm); }
        if ((_pttHeld || _callMic) && _group is { Any: true } g)
            try { g.Send(new { t = "au", d = Convert.ToBase64String(MuLaw.Encode(pcm)) }); } catch { }
    }

    void StartVoiceMessage()
    {
        lock (_voiceRec) _voiceRec.Clear();
        _recording = true; _recStart = DateTime.UtcNow;
        UpdateMic();
        DispatcherTimer.RunOnce(() => { if (_recording && (DateTime.UtcNow - _recStart).TotalSeconds >= 119) StopVoiceMessage(true); }, TimeSpan.FromSeconds(120));
    }

    async void StopVoiceMessage(bool send)
    {
        _recording = false;
        UpdateMic();
        short[] pcm; lock (_voiceRec) { pcm = _voiceRec.ToArray(); _voiceRec.Clear(); }
        if (!send || pcm.Length < AudioFormat.Rate / 4) return;
        var data = MuLaw.Encode(pcm);
        var secs = (pcm.Length + AudioFormat.Rate / 2) / AudioFormat.Rate;
        ShowVoiceMessage(_settings.CollabName, secs.ToString(), data, true);
        await SendBlobAsync("voice", secs.ToString(), data, L("Sprachnachricht gesendet ({0})", Duration(secs)));
    }

    static string Duration(int secs) => $"{secs / 60}:{secs % 60:00}";

    /// <summary>A voice message in the chat with a button to play it.</summary>
    void ShowVoiceMessage(string who, string name, byte[] mulaw, bool mine)
    {
        var secs = int.TryParse(name, out var s) ? Math.Clamp(s, 0, 600) : mulaw.Length / AudioFormat.Rate;
        var play = new Button { Content = L("▶ Abspielen"), Classes = { "tool" }, Width = double.NaN, Padding = new(10, 0), HorizontalAlignment = HorizontalAlignment.Left };
        play.Click += (_, _) =>
        {
            if (AppPlatform.IsMobile)
            {
                // the phone plays files: the message as a WAV file in the temporary folder
                var wav = Path.Combine(Path.GetTempPath(), "bs_voice.wav");
                try { Wav.Write(wav, MuLaw.Decode(mulaw)); (_voicePlayer ??= new AudioPlayer()).Play(wav); } catch (Exception ex) { _mediaInfo.Text = ex.Message; }
            }
            else if (EnsureSpeaker()) _mixer.Add("vm" + Guid.NewGuid().ToString("N"), MuLaw.Decode(mulaw));
        };
        var card = Card(new StackPanel
        {
            Spacing = 4,
            Children = { new TextBlock { Text = L("{0}: Sprachnachricht ({1})", who, Duration(secs)), FontWeight = FontWeight.SemiBold }, play },
        });
        card.HorizontalAlignment = mine ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        AddCollabItem(card);
        _meeting.Add((DateTime.Now, who, L("Sprachnachricht ({0})", Duration(secs))));
        if (!mine) NotifyChat(L("Sprachnachricht ({0})", Duration(secs)));
    }

    // ---- speaker ---------------------------------------------------------------------------------------------------------

    bool EnsureSpeaker()
    {
        if (_speaker is not null) return true;
        if (MediaIO.SoundNeedsFfmpeg && Ffmpeg.Find() is null) { _mediaInfo.Text = FfmpegHint(); return false; }
        try { _speaker = MediaIO.StartSpeaker(_mixer.Next); return true; }
        catch (Exception ex) { _mediaInfo.Text = L("Der Lautsprecher ließ sich nicht öffnen: {0}", ex.Message); return false; }
    }

    const int MaxPicture = 400_000;   // one camera picture; an HD picture is about 60 to 150 KB
    volatile byte[]? _ownFrame;
    int _ownFramePosted;

    // ---- camera ----------------------------------------------------------------------------------------------------------

    async Task SetCameraAsync(bool on)
    {
        if (on == _camOn) return;
        if (!on) { _camera?.Dispose(); _camera = null; _camOn = false; CollabSend(new { t = "vf", off = true }); RemoveTile("me"); return; }
        if (Ffmpeg.Find() is null) { _mediaInfo.Text = FfmpegHint(); return; }
        var cam = await PickAsync(true);
        if (cam is null) { _mediaInfo.Text = L("Keine Kamera gefunden."); return; }
        try
        {
            _camOn = true;   // while it opens: a second tap does not start it twice
            _camera = await MediaIO.StartCameraAsync(cam, CameraQuality.Of(_settings.CollabCameraQuality), jpeg =>
            {
                if (jpeg.Length > MaxPicture) return;
                if (_group is { Any: true } g) try { g.Send(new { t = "vf", d = Convert.ToBase64String(jpeg) }); } catch { }
                _ownFrame = jpeg;
                if (Interlocked.Exchange(ref _ownFramePosted, 1) == 0)   // only the newest picture is shown: a busy screen never falls behind
                    OnUi(() => { Interlocked.Exchange(ref _ownFramePosted, 0); if (_ownFrame is { } f && _camOn) ShowFrame("me", L("Du"), PeopleInkOf(null), f); });
            });
            if (!_camOn) { _camera.Dispose(); _camera = null; return; }   // switched off (or left) while it opened
            _mediaInfo.Text = "";
        }
        catch (Exception ex) { _camOn = false; _mediaInfo.Text = L("Die Kamera ließ sich nicht öffnen: {0}", ex.Message); }
    }

    Color PeopleInkOf(CollabPerson? p) => p?.Ink ?? Color.Parse("#30A46C");

    void ShowFrame(string id, string name, Color ink, byte[] jpeg)
    {
        Bitmap bmp;
        try { using var ms = new MemoryStream(jpeg); bmp = new Bitmap(ms); } catch { return; }
        if (!_tiles.TryGetValue(id, out var t))
        {
            var image = new Image { Width = 160, Height = 120, Stretch = Stretch.UniformToFill };
            var tile = new Border
            {
                CornerRadius = new(8), ClipToBounds = true, Margin = new(0, 0, 6, 6), BorderThickness = new(2), BorderBrush = new SolidColorBrush(ink),
                Child = new Grid { Children = { image, new Border { Background = new SolidColorBrush(ink), CornerRadius = new(4), Padding = new(5, 1), Margin = new(4), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom, Child = new TextBlock { Text = name, Foreground = Brushes.White, FontSize = 11 } } } },
            };
            t = (tile, image, DateTime.UtcNow);
            _videoTiles.Children.Add(tile);
        }
        var old = t.Image.Source as Bitmap;
        t.Image.Source = bmp;
        _tiles[id] = (t.Tile, t.Image, DateTime.UtcNow);
        _tileInfo[id] = (name, ink);
        UpdateBigVideo(id, name, ink, bmp);
        UpdateDock(id, bmp);
        old?.Dispose();
    }

    void RemoveTile(string id)
    {
        if (_bigGrid?.Children.FirstOrDefault(c => c.Tag as string == id) is { } b) _bigGrid.Children.Remove(b);
        _tileInfo.Remove(id);
        if (_tiles.Remove(id, out var t)) { _videoTiles.Children.Remove(t.Tile); RefreshDock(); (t.Image.Source as Bitmap)?.Dispose(); }   // the field no longer shows it before the picture goes
    }

    // ---- the line "who speaks" and old pictures ----------------------------------------------------------------------------

    void UpdateSpeaking()
    {
        var names = _mixer.Active.Select(id => _people.TryGetValue(id, out var p) ? p.Name : null).OfType<string>().ToList();
        if (_mic is not null && (_pttHeld || _callMic) && _myLevel > 0.03) names.Insert(0, L("Du"));
        _speaking.Text = names.Count > 0 ? L("Es spricht: {0}", string.Join(", ", names))
            : _recording ? L("Aufnahme läuft … {0}", Duration((int)(DateTime.UtcNow - _recStart).TotalSeconds))
            : _callMic ? L("Dein Mikrofon ist an.") : _micMuted ? L("Dein Mikrofon ist stummgeschaltet.") : "";
        foreach (var (id, t) in _tiles.ToList())
            if (id != "me" && (DateTime.UtcNow - t.Last).TotalSeconds > 5) RemoveTile(id);   // the camera of someone who left or stopped
        if (!CollabOpen && (_mic is not null || _camOn || _speaker is not null)) StopMedia();
    }

    void StopMedia()
    {
        _pttHeld = false; _callMic = false; _micMuted = false; _camPaused = false;
        if (_recording) StopVoiceMessage(false);
        _mic?.Dispose(); _mic = null;
        _camera?.Dispose(); _camera = null; _camOn = false;
        _speaker?.Dispose(); _speaker = null;
        _mixer.ClearAll();
        foreach (var id in _tiles.Keys.ToList()) RemoveTile(id);
    }

    // ---- receiving --------------------------------------------------------------------------------------------------------

    bool HandleCollabMedia(string type, JsonElement root)
    {
        var p = _from!;
        switch (type)
        {
            case "au":
            {
                if (AppPlatform.IsMobile || !_settings.CollabHear || !root.TryGetProperty("d", out var d) || d.ValueKind != JsonValueKind.String) return true;
                byte[] data;
                try { data = Convert.FromBase64String(d.GetString()!); } catch (FormatException) { return true; }
                if (data.Length is 0 or > 4 * AudioFormat.FrameSamples) return true;
                if (EnsureSpeaker()) _mixer.Add(p.Id, MuLaw.Decode(data));
                return true;
            }
            case "vf":
            {
                if (root.TryGetProperty("off", out var off) && off.ValueKind == JsonValueKind.True) { RemoveTile(p.Id); return true; }
                if (!root.TryGetProperty("d", out var d) || d.ValueKind != JsonValueKind.String) return true;
                byte[] jpeg;
                try { jpeg = Convert.FromBase64String(d.GetString()!); } catch (FormatException) { return true; }
                if (jpeg.Length is < 4 or > MaxPicture || jpeg[0] != 0xFF || jpeg[1] != 0xD8) return true;   // only a JPEG picture
                ShowFrame(p.Id, p.Name, p.Ink, jpeg);
                return true;
            }
        }
        return false;
    }
}
