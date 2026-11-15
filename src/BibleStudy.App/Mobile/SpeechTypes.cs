#if MOBILE
using System.Globalization;

// The reading aloud of the program uses System.Speech on Windows. On a phone the same few members are made with the voice of the phone
// (Android: TextToSpeech, iPhone: AVSpeechSynthesizer), so that MainWindow.Speak works unchanged. The shared types are here.
namespace System.Speech.Synthesis;

public enum SynthesizerState { Ready, Speaking, Paused }
public enum VoiceGender { NotSet, Male, Female, Neutral }
public enum VoiceAge { NotSet, Child, Teen, Adult, Senior }

public sealed class SpeakCompletedEventArgs : EventArgs { }

public sealed class SpeakProgressEventArgs(int position) : EventArgs
{
    public int CharacterPosition { get; } = position;
}
#endif
