using System.Runtime.InteropServices;
using Avalonia.Controls;

namespace BibleStudy.App;

/// <summary>
/// Windows treats a window that covers the whole screen as a full-screen program: an automatically hidden taskbar then no longer comes up
/// when the mouse reaches the edge. With the setting on, the maximized window leaves one line free at the edge where the taskbar sits.
/// The size is changed in WM_WINDOWPOSCHANGING, the last step before the window gets it: in WM_GETMINMAXINFO the program's own window code
/// set the full screen again afterwards, so the first version of this setting had no effect.
/// </summary>
public partial class MainWindow
{
    const uint WM_WINDOWPOSCHANGING = 0x0046;
    const uint WM_NCCALCSIZE = 0x0083;
    const uint ABM_GETSTATE = 0x4, ABM_GETAUTOHIDEBAREX = 0xB, ABS_AUTOHIDE = 0x1;
    const uint MONITOR_DEFAULTTONEAREST = 2;
    const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2;

    [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] struct MONITORINFO { public int Size; public RECT Monitor, Work; public uint Flags; }
    [StructLayout(LayoutKind.Sequential)] struct APPBARDATA { public int Size; public IntPtr Wnd; public uint CallbackMessage, Edge; public RECT Rect; public IntPtr Param; }
    [StructLayout(LayoutKind.Sequential)] struct WINDOWPOS { public IntPtr Hwnd, After; public int X, Y, Cx, Cy; public uint Flags; }

    [DllImport("shell32.dll")] static extern UIntPtr SHAppBarMessage(uint msg, ref APPBARDATA data);
    [DllImport("user32.dll")] static extern IntPtr MonitorFromRect(ref RECT rect, uint flags);
    [DllImport("user32.dll")] static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);
    [DllImport("user32.dll")] static extern bool IsZoomed(IntPtr wnd);

    void InitTaskbar()
    {
        TaskbarRevealCheck.IsChecked = _settings.TaskbarReveal;
        TaskbarRevealCheck.IsVisible = OperatingSystem.IsWindows();
        TaskbarRevealCheck.IsCheckedChanged += (_, _) =>
        {
            _settings.TaskbarReveal = TaskbarRevealCheck.IsChecked == true; SaveSettings();
            // a maximized window takes the new size only when it is maximized again
            if (WindowState == WindowState.Maximized) { WindowState = WindowState.Normal; WindowState = WindowState.Maximized; }
        };
#if !ANDROID
        if (OperatingSystem.IsWindows()) Win32Properties.AddWndProcHookCallback(this, TaskbarHook);
#endif
    }

    IntPtr TaskbarHook(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_NCCALCSIZE && wParam != IntPtr.Zero && lParam != IntPtr.Zero && _settings.TaskbarReveal && IsZoomed(hwnd))
        {
            // the window now ends a line before the screen edge, so it has no frame hanging over the edges: all of it is client area
            // (otherwise the program takes off a frame width on every side and a border shows around the maximized window)
            var proposed = Marshal.PtrToStructure<RECT>(lParam);
            var mi = new MONITORINFO { Size = Marshal.SizeOf<MONITORINFO>() };
            if (GetMonitorInfo(MonitorFromRect(ref proposed, MONITOR_DEFAULTTONEAREST), ref mi) && HiddenTaskbarEdge(mi.Monitor) is not null
                && proposed.Left >= mi.Monitor.Left && proposed.Top >= mi.Monitor.Top && proposed.Right <= mi.Monitor.Right && proposed.Bottom <= mi.Monitor.Bottom)
            { handled = true; return IntPtr.Zero; }
            return IntPtr.Zero;
        }
        if (msg != WM_WINDOWPOSCHANGING || !_settings.TaskbarReveal || lParam == IntPtr.Zero) return IntPtr.Zero;
        try
        {
            var pos = Marshal.PtrToStructure<WINDOWPOS>(lParam);
            if ((pos.Flags & SWP_NOSIZE) != 0 || !IsZoomed(hwnd)) return IntPtr.Zero;
            var r = new RECT { Left = pos.X, Top = pos.Y, Right = pos.X + pos.Cx, Bottom = pos.Y + pos.Cy };
            var info = new MONITORINFO { Size = Marshal.SizeOf<MONITORINFO>() };
            if (!GetMonitorInfo(MonitorFromRect(ref r, MONITOR_DEFAULTTONEAREST), ref info)) return IntPtr.Zero;
            if (HiddenTaskbarEdge(info.Monitor) is not uint edge) return IntPtr.Zero;   // no hidden taskbar on this screen: nothing to do
            var (x, y, w, h) = TaskbarGap(info.Monitor.Left, info.Monitor.Top, info.Monitor.Right - info.Monitor.Left, info.Monitor.Bottom - info.Monitor.Top, edge);
            pos.X = x; pos.Y = y; pos.Cx = w; pos.Cy = h;
            pos.Flags &= ~SWP_NOMOVE;
            Marshal.StructureToPtr(pos, lParam, false);
            handled = true;   // the window code of the program would set the full screen again otherwise
        }
        catch (Exception ex) { ErrorLog.Write(ex); }
        return IntPtr.Zero;
    }

    /// <summary>The edge of an automatically hidden taskbar on this screen (0 left, 1 top, 2 right, 3 bottom), or null when there is none.</summary>
    static uint? HiddenTaskbarEdge(RECT monitor)
    {
        var state = new APPBARDATA { Size = Marshal.SizeOf<APPBARDATA>() };
        if (((uint)SHAppBarMessage(ABM_GETSTATE, ref state) & ABS_AUTOHIDE) == 0) return null;
        for (uint edge = 0; edge < 4; edge++)
        {
            // asks per screen and edge: with several screens each may have its own taskbar
            var q = new APPBARDATA { Size = Marshal.SizeOf<APPBARDATA>(), Edge = edge, Rect = monitor };
            if (SHAppBarMessage(ABM_GETAUTOHIDEBAREX, ref q) != UIntPtr.Zero) return edge;
        }
        return null;
    }

    /// <summary>The maximized window: the screen less one line at the edge of the hidden taskbar (0 left, 1 top, 2 right, 3 bottom).</summary>
    public static (int X, int Y, int W, int H) TaskbarGap(int x, int y, int w, int h, uint edge) => edge switch
    {
        0 => (x + 1, y, w - 1, h),
        1 => (x, y + 1, w, h - 1),
        2 => (x, y, w - 1, h),
        _ => (x, y, w, h - 1),
    };
}
