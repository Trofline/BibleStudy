#if ANDROID
using System.Globalization;
using Android.Speech.Tts;

// The reading aloud of the program uses System.Speech on Windows. On Android the same few members are made here with the voice of
// the phone (TextToSpeech), so that MainWindow.Speak works unchanged.
namespace System.Speech.Synthesis;

public enum SynthesizerState { Ready, Speaking, Paused }
public enum VoiceGender { NotSet, Male, Female, Neutral }
public enum VoiceAge { NotSet, Child, Teen, Adult, Senior }

public sealed class SpeakCompletedEventArgs : EventArgs { }

public sealed class SpeakProgressEventArgs(int position) : EventArgs
{
    public int CharacterPosition { get; } = position;
}

public sealed class SpeechSynthesizer : IDisposable
{
    readonly TextToSpeech _tts;
    readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    readonly Listener _listener;
    CultureInfo? _culture;
    int _pending;   // pieces given to the phone's voice and not finished yet

    public event EventHandler<SpeakCompletedEventArgs>? SpeakCompleted;
    public event EventHandler<SpeakProgressEventArgs>? SpeakProgress;

    /// <summary>-10 (slow) … 10 (fast), as on Windows.</summary>
    public int Rate { get; set; }
    public SynthesizerState State { get; private set; } = SynthesizerState.Ready;

    public SpeechSynthesizer()
    {
        _listener = new Listener(this);
        _tts = new TextToSpeech(Android.App.Application.Context, _listener);
        _tts.SetOnUtteranceProgressListener(new Progress(this));
    }

    public void SelectVoiceByHints(VoiceGender gender, VoiceAge age, int position, CultureInfo culture) => _culture = culture;

    /// <summary>Speaks the text; a long text goes in pieces (the phone's voice takes about 4000 characters at once).</summary>
    public async void SpeakAsync(string text)
    {
        State = SynthesizerState.Speaking;
        await _ready.Task;
        if (State != SynthesizerState.Speaking) return;
        if (_culture is not null) _tts.SetLanguage(new Java.Util.Locale(_culture.TwoLetterISOLanguageName));
        _tts.SetSpeechRate((float)Math.Pow(2, Rate / 10.0 * 1.6));   // 0 → 1.0, 10 → about 3, -10 → about 0.33
        var offset = 0;
        foreach (var piece in Pieces(text, TextToSpeech.MaxSpeechInputLength - 50))
        {
            Interlocked.Increment(ref _pending);
            _tts.Speak(piece, QueueMode.Add, null, offset.ToString(CultureInfo.InvariantCulture));
            offset += piece.Length;
        }
        if (_pending == 0) Finished();
    }

    static IEnumerable<string> Pieces(string text, int max)
    {
        var start = 0;
        while (start < text.Length)
        {
            var len = Math.Min(max, text.Length - start);
            if (start + len < text.Length)
            {
                var cut = text.LastIndexOfAny(['.', '!', '?', ';', ' '], start + len - 1, len);
                if (cut > start) len = cut - start + 1;
            }
            yield return text.Substring(start, len);
            start += len;
        }
    }

    public void SpeakAsyncCancelAll()
    {
        _pending = 0;
        _tts.Stop();
        Finished();
    }

    void Finished()
    {
        if (State == SynthesizerState.Ready) return;
        State = SynthesizerState.Ready;
        SpeakCompleted?.Invoke(this, new SpeakCompletedEventArgs());
    }

    public void Dispose() { try { _tts.Stop(); _tts.Shutdown(); } catch (Java.Lang.Exception) { } }

    sealed class Listener(SpeechSynthesizer owner) : Java.Lang.Object, TextToSpeech.IOnInitListener
    {
        public void OnInit(OperationResult status) => owner._ready.TrySetResult();
    }

    sealed class Progress(SpeechSynthesizer owner) : UtteranceProgressListener
    {
        public override void OnStart(string? utteranceId) { }
        public override void OnRangeStart(string? utteranceId, int start, int end, int frame)
        {
            if (int.TryParse(utteranceId, out var offset)) owner.SpeakProgress?.Invoke(owner, new SpeakProgressEventArgs(offset + start));
        }
        public override void OnDone(string? utteranceId) { if (Interlocked.Decrement(ref owner._pending) <= 0) owner.Finished(); }
        public override void OnError(string? utteranceId) => OnDone(utteranceId);
        public override void OnStop(string? utteranceId, bool interrupted) { }
    }
}
#endif
