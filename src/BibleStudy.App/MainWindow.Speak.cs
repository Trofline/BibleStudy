using System.Globalization;
using System.Speech.Synthesis;
using Avalonia.Controls;
using BibleStudy.Core;
using BibleStudy.Core.Models;

namespace BibleStudy.App;

/// <summary>
/// Reading aloud (the Windows voice or a natural voice made by Piper) and the speed buttons of the bar at the bottom, which serves
/// reading aloud and the audio Bible alike.
/// </summary>
public partial class MainWindow
{
    static readonly double[] NaturalSpeeds = [0.5, 0.75, 1, 1.25, 1.5, 1.75, 2, 2.5];

    readonly AudioPlayer _voice = new();
    CancellationTokenSource? _voiceCts;
    CancellationTokenSource? _speakRestartCts;
    bool _speechMode, _speakNatural, _speakRestart;
    string _speakFull = "";
    int _speakOffset, _speakAbs;

    // ---- the bar -------------------------------------------------------------------------------------------------------

    /// <summary>The bar shows chapter buttons and position for the recording, only speed and stop for reading aloud.</summary>
    void ShowBarMode(bool speech)
    {
        _speechMode = speech;
        AudioPrevBtn.IsVisible = AudioPlayBtn.IsVisible = AudioNextBtn.IsVisible = AudioSlider.IsVisible = AudioTime.IsVisible = !speech;
        if (speech) AudioTitle.Text = L("Vorlesen");
        ShowSpeed();
    }

    void ShowSpeed()
    {
        var value = _speechMode && !_speakNatural
            ? (_settings.SpeakRate == 0 ? "±0" : _settings.SpeakRate.ToString("+0;−0"))
            : (_speechMode ? _settings.NaturalSpeed : _audio.Speed).ToString("0.##", CultureInfo.CurrentCulture) + "×";
        AudioSpeedBtn.Content = L("Tempo") + " " + value;
    }

    static double StepIn(double[] list, double current, int dir)
    {
        var i = Array.FindIndex(list, v => v >= current - 0.01);
        return list[Math.Clamp((i < 0 ? list.Length - 1 : i) + dir, 0, list.Length - 1)];
    }

    /// <summary>One step slower (-1) or faster (+1); 0 goes back to the normal speed.</summary>
    void StepSpeed(int dir)
    {
        if (_speechMode && !_speakNatural)
        {
            _settings.SpeakRate = dir == 0 ? 0 : Math.Clamp(_settings.SpeakRate + dir, -5, 8);
            SpeakSlider.Value = _settings.SpeakRate;
            RestartSpeakSoon();
        }
        else if (_speechMode)
        {
            _settings.NaturalSpeed = dir == 0 ? 1 : StepIn(NaturalSpeeds, _settings.NaturalSpeed, dir);
            _voice.SetSpeed(_settings.NaturalSpeed);
        }
        else
        {
            _settings.AudioSpeed = dir == 0 ? 1 : StepIn(AudioSpeeds, _settings.AudioSpeed, dir);
            _audio.SetSpeed(_settings.AudioSpeed);
        }
        SaveSettings(); ShowSpeed();
    }

    void InitSpeed()
    {
        AudioSlowerBtn.Click += (_, _) => StepSpeed(-1);
        AudioFasterBtn.Click += (_, _) => StepSpeed(1);
        AudioSpeedBtn.Click += (_, _) => StepSpeed(0);
        _voice.SetSpeed(_settings.NaturalSpeed);
        ShowSpeed();
    }

    // ---- settings ------------------------------------------------------------------------------------------------------

    void InitSpeak()
    {
        SpeakVoiceBox.ItemsSource = new[]
        {
            L("Windows-Stimme (technisch, ohne Download)"),
            L("Natürliche Stimme (Piper; lädt beim ersten Mal ca. 85 MB)"),
        };
        SpeakVoiceBox.SelectedIndex = _settings.SpeakVoice == "natural" ? 1 : 0;
        SpeakVoiceBox.SelectionChanged += (_, _) =>
        {
            _settings.SpeakVoice = SpeakVoiceBox.SelectedIndex == 1 ? "natural" : "windows";
            SaveSettings();
        };
        SpeakVoiceInfo.Text = L("Piper läuft offline auf deinem Computer (nur Windows, 64 Bit). Stimmen: Deutsch Thorsten, Englisch LJ Speech, Spanisch DaveFX, Französisch SIWIS, Russisch Denis; alle frei lizenziert. Für andere Sprachen bleibt die Windows-Stimme.");
    }

    // ---- reading aloud ------------------------------------------------------------------------------------------------

    async Task ToggleSpeakAsync()
    {
        if (!OperatingSystem.IsWindows()) { StatusText.Text = L("Vorlesen ist derzeit nur unter Windows verfügbar."); SpeakBtn.IsChecked = false; return; }
        if (_speechMode) { StopSpeak(); return; }
        if (_curChapterData is null || TranslationBox.SelectedItem is not TranslationInfo t) { SpeakBtn.IsChecked = false; return; }
        var verses = _selected is int v ? _curChapterData.Verses.Where(x => x.Number == v) : _curChapterData.Verses;
        var text = string.Join(" ", verses.Select(x => x.Text));
        if (string.IsNullOrWhiteSpace(text)) { SpeakBtn.IsChecked = false; return; }
        StopAudio();
        var culture = CultureInfo.GetCultures(CultureTypes.NeutralCultures).FirstOrDefault(c => c.ThreeLetterISOLanguageName == t.Language || c.Name == t.Language);
        if (_settings.SpeakVoice == "natural" && NaturalVoice.IsAvailable)
        {
            if (NaturalVoice.For(culture?.TwoLetterISOLanguageName) is { } voice) { await SpeakNaturalAsync(text, voice); return; }
            StatusText.Text = L("Für {0} gibt es keine natürliche Stimme; es wird die Windows-Stimme genutzt.", t.LanguageName);
        }
        SpeakWindows(text, culture, t);
    }

    void SpeakWindows(string text, CultureInfo? culture, TranslationInfo t)
    {
        try
        {
            if (_speech is null)
            {
                _speech = new SpeechSynthesizer();
                _speech.SpeakCompleted += OnSpeakCompleted;
                _speech.SpeakProgress += (_, e) => _speakAbs = _speakOffset + e.CharacterPosition;
            }
            _speech.Rate = Math.Clamp(_settings.SpeakRate, -10, 10);
            if (culture is not null)
                try { _speech.SelectVoiceByHints(VoiceGender.NotSet, VoiceAge.NotSet, 0, culture); }
                catch (ArgumentException) { StatusText.Text = L("Keine Windows-Stimme für {0} installiert; es wird die Standardstimme genutzt.", t.LanguageName); }
            _speakFull = text; _speakOffset = 0; _speakAbs = 0; _speakNatural = false;
            _speech.SpeakAsync(text);
            SpeakBtn.IsChecked = true;
            ShowBarMode(true); AudioBar.IsVisible = true;
        }
        catch (Exception ex) { StatusText.Text = L("Vorlesen nicht möglich: ") + ex.Message; SpeakBtn.IsChecked = false; }
    }

    void OnSpeakCompleted(object? sender, SpeakCompletedEventArgs e) => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
    {
        if (_speakRestart)
        {
            // the speed changed: go on at the word where it was
            _speakRestart = false;
            _speakOffset = Math.Clamp(_speakAbs, 0, _speakFull.Length);
            _speech!.Rate = Math.Clamp(_settings.SpeakRate, -10, 10);
            if (_speakOffset < _speakFull.Length) { _speech.SpeakAsync(_speakFull[_speakOffset..]); return; }
        }
        if (_speakNatural) return;
        SpeakBtn.IsChecked = false;
        if (_speechMode) { AudioBar.IsVisible = false; _speechMode = false; }
    });

    /// <summary>Several clicks on the speed buttons make one restart, after the last click.</summary>
    async void RestartSpeakSoon()
    {
        _speakRestartCts?.Cancel();
        var cts = _speakRestartCts = new CancellationTokenSource();
        try { await Task.Delay(500, cts.Token); } catch (TaskCanceledException) { return; }
        if (_speech is { State: SynthesizerState.Speaking }) { _speakRestart = true; _speech.SpeakAsyncCancelAll(); }
    }

    async Task SpeakNaturalAsync(string text, NaturalVoice.Voice voice)
    {
        _speakNatural = true;
        ShowBarMode(true); AudioBar.IsVisible = true; SpeakBtn.IsChecked = true;
        var cts = _voiceCts = new CancellationTokenSource();
        var ct = cts.Token;
        var dir = Path.Combine(_root, "cache", "voice", "tmp");
        try
        {
            if (!NaturalVoice.IsReady(_root, voice))
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
                http.DefaultRequestHeaders.UserAgent.ParseAdd("BibleStudy");
                await NaturalVoice.EnsureAsync(http, _root, voice, new Progress<string>(m => AudioTitle.Text = L("Natürliche Stimme wird geladen: {0}", m)), ct);
            }
            AudioTitle.Text = $"{L("Vorlesen")} · {voice.Name}";
            if (Directory.Exists(dir)) try { Directory.Delete(dir, true); } catch (IOException) { }
            Directory.CreateDirectory(dir);
            var chunks = NaturalVoice.Chunks(text);
            var ready = chunks.Select(_ => new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously)).ToArray();
            // the next pieces are made while one is played, so there is hardly a wait
            _ = Task.Run(async () =>
            {
                for (var i = 0; i < chunks.Count; i++)
                {
                    try
                    {
                        var wav = Path.Combine(dir, $"{i}.wav");
                        await NaturalVoice.SynthesizeAsync(_root, voice, chunks[i], wav, ct);
                        ready[i].TrySetResult(wav);
                    }
                    catch (Exception ex) { for (var j = i; j < ready.Length; j++) ready[j].TrySetException(ex); return; }
                }
            }, ct);
            for (var i = 0; i < chunks.Count; i++)
            {
                var wav = await ready[i].Task.WaitAsync(ct);
                _voice.SetSpeed(_settings.NaturalSpeed);
                if (!_voice.Play(wav)) throw new IOException(_voice.Error ?? "");
                while (true)
                {
                    await Task.Delay(100, ct);
                    _voice.Poll();
                    if (_voice.State != AudioPlayer.PlayState.Playing) break;
                }
                try { File.Delete(wav); } catch (IOException) { }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is IOException or HttpRequestException or InvalidOperationException or UnauthorizedAccessException)
        {
            StatusText.Text = L("Vorlesen nicht möglich: ") + ex.Message;
        }
        finally
        {
            if (_voiceCts == cts)
            {
                _voiceCts = null; _voice.Stop();
                SpeakBtn.IsChecked = false; AudioBar.IsVisible = false; _speechMode = false; _speakNatural = false;
            }
        }
    }

    void StopSpeak()
    {
        if (!_speechMode) return;
        _speakRestart = false; _speakRestartCts?.Cancel();
        var c = _voiceCts; _voiceCts = null; c?.Cancel();
        _voice.Stop();
        if (_speech is { State: SynthesizerState.Speaking }) _speech.SpeakAsyncCancelAll();
        SpeakBtn.IsChecked = false; AudioBar.IsVisible = false; _speechMode = false; _speakNatural = false;
    }
}
