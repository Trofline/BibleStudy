using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace BibleStudy.Core.Collab.Media;

/// <summary>
/// Microphone and speaker on Windows through the old, always present wave functions of Windows (winmm): no extra program needed.
/// Several buffers of 40 ms are handed to Windows; a small loop picks up the filled ones (microphone) or refills the played ones (speaker).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WinAudio : IDisposable
{
    const int WaveMapper = -1, CallbackNull = 0, WhdrDone = 1, Buffers = 6;

    [StructLayout(LayoutKind.Sequential)]
    struct WaveFormat { public ushort Tag, Channels; public uint SamplesPerSec, AvgBytesPerSec; public ushort BlockAlign, BitsPerSample, Size; }

    [StructLayout(LayoutKind.Sequential)]
    struct WaveHdr { public IntPtr Data; public uint BufferLength, BytesRecorded; public IntPtr User; public uint Flags, Loops; public IntPtr Next, Reserved; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct WaveInCaps { public ushort Mid, Pid; public uint DriverVersion; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Name; public uint Formats; public ushort Channels, Reserved; }

    [DllImport("winmm.dll")] static extern int waveInGetNumDevs();
    [DllImport("winmm.dll", CharSet = CharSet.Unicode)] static extern int waveInGetDevCapsW(IntPtr id, ref WaveInCaps caps, int size);
    [DllImport("winmm.dll")] static extern int waveInOpen(out IntPtr h, int id, ref WaveFormat f, IntPtr cb, IntPtr inst, int flags);
    [DllImport("winmm.dll")] static extern int waveInPrepareHeader(IntPtr h, IntPtr hdr, int size);
    [DllImport("winmm.dll")] static extern int waveInUnprepareHeader(IntPtr h, IntPtr hdr, int size);
    [DllImport("winmm.dll")] static extern int waveInAddBuffer(IntPtr h, IntPtr hdr, int size);
    [DllImport("winmm.dll")] static extern int waveInStart(IntPtr h);
    [DllImport("winmm.dll")] static extern int waveInReset(IntPtr h);
    [DllImport("winmm.dll")] static extern int waveInClose(IntPtr h);
    [DllImport("winmm.dll")] static extern int waveOutOpen(out IntPtr h, int id, ref WaveFormat f, IntPtr cb, IntPtr inst, int flags);
    [DllImport("winmm.dll")] static extern int waveOutPrepareHeader(IntPtr h, IntPtr hdr, int size);
    [DllImport("winmm.dll")] static extern int waveOutUnprepareHeader(IntPtr h, IntPtr hdr, int size);
    [DllImport("winmm.dll")] static extern int waveOutWrite(IntPtr h, IntPtr hdr, int size);
    [DllImport("winmm.dll")] static extern int waveOutReset(IntPtr h);
    [DllImport("winmm.dll")] static extern int waveOutClose(IntPtr h);

    static readonly int HdrSize = Marshal.SizeOf<WaveHdr>();
    readonly bool _input;
    IntPtr _h;
    readonly IntPtr[] _hdrs = new IntPtr[Buffers];
    readonly CancellationTokenSource _cts = new();
    Thread? _loop;

    WinAudio(bool input) => _input = input;

    static WaveFormat Format() => new() { Tag = 1, Channels = 1, SamplesPerSec = AudioFormat.Rate, BitsPerSample = 16, BlockAlign = 2, AvgBytesPerSec = AudioFormat.Rate * 2 };

    /// <summary>The microphones Windows knows (the names are cut to 31 letters by Windows).</summary>
    public static List<MediaDevice> Microphones()
    {
        var list = new List<MediaDevice>();
        for (var i = 0; i < waveInGetNumDevs(); i++)
        {
            var caps = new WaveInCaps();
            if (waveInGetDevCapsW(i, ref caps, Marshal.SizeOf<WaveInCaps>()) == 0) list.Add(new(caps.Name, i.ToString(), false));
        }
        return list;
    }

    /// <summary>Starts the microphone; <paramref name="onPiece"/> gets every 40 ms of sound (on a background thread).</summary>
    public static WinAudio StartMicrophone(MediaDevice? mic, Action<short[]> onPiece)
    {
        var a = new WinAudio(true);
        var f = Format();
        var id = mic is not null && int.TryParse(mic.Id, out var n) ? n : WaveMapper;
        if (waveInOpen(out a._h, id, ref f, IntPtr.Zero, IntPtr.Zero, CallbackNull) != 0) throw new InvalidOperationException("microphone");
        for (var i = 0; i < Buffers; i++)
        {
            a._hdrs[i] = NewHeader(AudioFormat.FrameSamples * 2);
            waveInPrepareHeader(a._h, a._hdrs[i], HdrSize);
            waveInAddBuffer(a._h, a._hdrs[i], HdrSize);
        }
        waveInStart(a._h);
        a._loop = new Thread(() => a.ReadLoop(onPiece)) { IsBackground = true, Name = "mic" };
        a._loop.Start();
        return a;
    }

    /// <summary>Starts the speaker; <paramref name="next"/> is asked for the next 40 ms whenever a buffer is free.</summary>
    public static WinAudio StartSpeaker(Func<short[]> next)
    {
        var a = new WinAudio(false);
        var f = Format();
        if (waveOutOpen(out a._h, WaveMapper, ref f, IntPtr.Zero, IntPtr.Zero, CallbackNull) != 0) throw new InvalidOperationException("speaker");
        for (var i = 0; i < Buffers; i++) a._hdrs[i] = NewHeader(AudioFormat.FrameSamples * 2);
        a._loop = new Thread(() => a.WriteLoop(next)) { IsBackground = true, Name = "speaker" };
        a._loop.Start();
        return a;
    }

    static IntPtr NewHeader(int bytes)
    {
        var hdr = new WaveHdr { Data = Marshal.AllocHGlobal(bytes), BufferLength = (uint)bytes };
        var p = Marshal.AllocHGlobal(HdrSize);
        Marshal.StructureToPtr(hdr, p, false);
        return p;
    }

    static WaveHdr Read(IntPtr p) => Marshal.PtrToStructure<WaveHdr>(p);

    void ReadLoop(Action<short[]> onPiece)
    {
        var next = 0;
        while (!_cts.IsCancellationRequested)
        {
            var hdr = Read(_hdrs[next]);
            if ((hdr.Flags & WhdrDone) == 0) { Thread.Sleep(5); continue; }
            var bytes = new byte[hdr.BytesRecorded];
            Marshal.Copy(hdr.Data, bytes, 0, bytes.Length);
            if (_cts.IsCancellationRequested) break;
            waveInAddBuffer(_h, _hdrs[next], HdrSize);   // back to Windows (it clears the done flag)
            try { if (bytes.Length > 0) onPiece(MuLaw.FromBytes(bytes)); } catch { /* the receiver's problem must not stop the microphone */ }
            next = (next + 1) % Buffers;
        }
    }

    void WriteLoop(Func<short[]> next)
    {
        var used = new bool[Buffers];
        while (!_cts.IsCancellationRequested)
        {
            var wrote = false;
            for (var i = 0; i < Buffers; i++)
            {
                var hdr = Read(_hdrs[i]);
                if (used[i] && (hdr.Flags & WhdrDone) == 0) continue;   // still playing
                if (used[i]) waveOutUnprepareHeader(_h, _hdrs[i], HdrSize);
                var bytes = MuLaw.ToBytes(next());
                Marshal.Copy(bytes, 0, hdr.Data, bytes.Length);
                hdr.BufferLength = (uint)bytes.Length; hdr.Flags = 0;
                Marshal.StructureToPtr(hdr, _hdrs[i], false);
                waveOutPrepareHeader(_h, _hdrs[i], HdrSize);
                waveOutWrite(_h, _hdrs[i], HdrSize);
                used[i] = true; wrote = true;
            }
            if (!wrote) Thread.Sleep(10);
        }
    }

    public void Dispose()
    {
        if (_cts.IsCancellationRequested) return;
        _cts.Cancel();
        _loop?.Join(500);
        if (_input) { waveInReset(_h); foreach (var p in _hdrs) if (p != IntPtr.Zero) waveInUnprepareHeader(_h, p, HdrSize); waveInClose(_h); }
        else { waveOutReset(_h); foreach (var p in _hdrs) if (p != IntPtr.Zero) waveOutUnprepareHeader(_h, p, HdrSize); waveOutClose(_h); }
        foreach (var p in _hdrs) if (p != IntPtr.Zero) { Marshal.FreeHGlobal(Read(p).Data); Marshal.FreeHGlobal(p); }
    }
}
