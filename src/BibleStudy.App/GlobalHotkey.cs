using System.Runtime.InteropServices;

namespace BibleStudy.App;

/// <summary>
/// A key combination of the whole system (Windows): it also works when another program is in front or this one lies hidden in the tray.
/// The combination is registered for a thread of its own that waits for the message; the program is told through <c>pressed</c> (on that thread).
/// </summary>
sealed class GlobalHotkey : IDisposable
{
    public const uint Alt = 1, Control = 2, Shift = 4;

    [StructLayout(LayoutKind.Sequential)] struct Msg { public IntPtr Hwnd; public uint Message; public UIntPtr WParam; public IntPtr LParam; public uint Time; public int X, Y; }
    [DllImport("user32.dll", SetLastError = true)] static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint vk);
    [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    [DllImport("user32.dll")] static extern int GetMessage(out Msg msg, IntPtr hWnd, uint min, uint max);
    [DllImport("user32.dll")] static extern bool PostThreadMessage(uint threadId, uint msg, UIntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();

    const int Id = 0x4253;
    const uint WmHotkey = 0x0312, WmQuit = 0x0012, NoRepeat = 0x4000;

    uint _threadId;
    readonly ManualResetEventSlim _ready = new();

    /// <summary>False when another program already holds this combination.</summary>
    public bool Registered { get; private set; }

    public GlobalHotkey(uint modifiers, uint key, Action pressed)
    {
        if (!OperatingSystem.IsWindows()) return;
        var thread = new Thread(() =>
        {
            _threadId = GetCurrentThreadId();
            Registered = RegisterHotKey(IntPtr.Zero, Id, modifiers | NoRepeat, key);
            _ready.Set();
            if (!Registered) return;
            try { while (GetMessage(out var m, IntPtr.Zero, 0, 0) > 0) if (m.Message == WmHotkey) pressed(); }
            finally { UnregisterHotKey(IntPtr.Zero, Id); }
        }) { IsBackground = true, Name = "BibleStudy hotkey" };
        thread.Start();
        _ready.Wait(2000);
    }

    public void Dispose()
    {
        if (OperatingSystem.IsWindows() && Registered && _threadId != 0) PostThreadMessage(_threadId, WmQuit, UIntPtr.Zero, IntPtr.Zero);
        Registered = false;
    }
}
