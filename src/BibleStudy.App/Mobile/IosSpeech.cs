#if IOS
using System.Globalization;
using AVFoundation;
using Foundation;

namespace System.Speech.Synthesis;

/// <summary>Reading aloud on an iPhone: the voice of the phone (AVSpeechSynthesizer), with the members of the Windows class that the program uses.</summary>
public sealed class SpeechSynthesizer : IDisposable
{
    readonly AVSpeechSynthesizer _tts = new();
    readonly Watcher _watcher;
    CultureInfo? _culture;

    public event EventHandler<SpeakCompletedEventArgs>? SpeakCompleted;
    public event EventHandler<SpeakProgressEventArgs>? SpeakProgress;

    /// <summary>-10 (slow) … 10 (fast), as on Windows.</summary>
    public int Rate { get; set; }
    public SynthesizerState State { get; private set; } = SynthesizerState.Ready;

    public SpeechSynthesizer()
    {
        _watcher = new Watcher(this);
        _tts.Delegate = _watcher;
    }

    public void SelectVoiceByHints(VoiceGender gender, VoiceAge age, int position, CultureInfo culture) => _culture = culture;

    public void SpeakAsync(string text)
    {
        try
        {
            var session = AVAudioSession.SharedInstance();
            session.SetCategory(AVAudioSession.CategoryPlayback, out _);
            session.SetActive(true, out _);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) { /* the voice then speaks as the system lets it */ }
        State = SynthesizerState.Speaking;
        var utterance = new AVSpeechUtterance(text)
        {
            Rate = Math.Clamp(AVSpeechUtterance.DefaultSpeechRate + Rate * 0.04f, AVSpeechUtterance.MinimumSpeechRate + 0.05f, AVSpeechUtterance.MaximumSpeechRate - 0.05f),
        };
        if (_culture is not null && AVSpeechSynthesisVoice.FromLanguage(_culture.Name) is { } voice) utterance.Voice = voice;
        else if (_culture is not null && AVSpeechSynthesisVoice.FromLanguage(_culture.TwoLetterISOLanguageName) is { } plain) utterance.Voice = plain;
        _tts.SpeakUtterance(utterance);
    }

    public void SpeakAsyncCancelAll()
    {
        _tts.StopSpeaking(AVSpeechBoundary.Immediate);
        Finished();
    }

    void Finished()
    {
        if (State == SynthesizerState.Ready) return;
        State = SynthesizerState.Ready;
        SpeakCompleted?.Invoke(this, new SpeakCompletedEventArgs());
    }

    public void Dispose() { try { _tts.StopSpeaking(AVSpeechBoundary.Immediate); } catch (Exception ex) when (ex is not OutOfMemoryException) { } }

    sealed class Watcher(SpeechSynthesizer owner) : AVSpeechSynthesizerDelegate
    {
        public override void WillSpeakRangeOfSpeechString(AVSpeechSynthesizer synthesizer, NSRange characterRange, AVSpeechUtterance utterance) =>
            owner.SpeakProgress?.Invoke(owner, new SpeakProgressEventArgs((int)characterRange.Location));
        public override void DidFinishSpeechUtterance(AVSpeechSynthesizer synthesizer, AVSpeechUtterance utterance) => owner.Finished();
        public override void DidCancelSpeechUtterance(AVSpeechSynthesizer synthesizer, AVSpeechUtterance utterance) { }
    }
}
#endif
