using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace BibleStudy.App;

/// <summary>
/// A touch is a scroll as often as it is a tap: acting when the finger goes down selects things the user only wanted to scroll past.
/// For a finger the action therefore waits until it lifts, on the spot where it went down; if it moved (a scroll) or stayed long (a long press,
/// which has its own meaning) nothing happens. A mouse and a pen act at the press, as before.
/// </summary>
static class TouchTap
{
    const double Slop = 9;          // how far a finger may wander and still be a tap (device-independent pixels)
    const long LongMs = 650;        // longer than this is no tap

    /// <summary>
    /// How long a finger rests before a long press counts (the verse menu, the menu of a row). Avalonia waits only 300 ms: an older hand that
    /// is not quick, or a thumb laid down to scroll, opened a menu all the time. iOS and Android wait about 500 ms; a little more is calmer.
    /// </summary>
    public const int HoldMs = 800;

    /// <summary>Like <c>PointerPressed +=</c>, but for a finger only after it lifted without having moved.</summary>
    public static void OnPress(this InputElement control, EventHandler<PointerPressedEventArgs> handler)
    {
        control.PointerPressed += (sender, e) =>
        {
            if (e.Pointer.Type != PointerType.Touch) { handler(sender, e); return; }
            Defer(control, e, () => handler(sender, e));
        };
    }

    /// <summary>Runs <paramref name="act"/> when the touch of <paramref name="e"/> lifts without a scroll; nothing happens if it moved or was held.</summary>
    public static void Defer(Visual from, PointerPressedEventArgs e, Action act, long maxMs = LongMs)
    {
        if (TopLevel.GetTopLevel(from) is not { } top) { act(); return; }
        var start = e.GetPosition(top);
        var pointer = e.Pointer;
        var begin = Environment.TickCount64;

        void Stop()
        {
            top.RemoveHandler(InputElement.PointerReleasedEvent, Up);
            top.RemoveHandler(InputElement.PointerMovedEvent, Move);
        }
        void Move(object? s, PointerEventArgs a)
        {
            if (a.Pointer != pointer) return;
            var p = a.GetPosition(top);
            if (Math.Abs(p.X - start.X) > Slop || Math.Abs(p.Y - start.Y) > Slop) Stop();
        }
        void Up(object? s, PointerReleasedEventArgs a)
        {
            if (a.Pointer != pointer) return;
            Stop();
            var p = a.GetPosition(top);
            if (Math.Abs(p.X - start.X) > Slop || Math.Abs(p.Y - start.Y) > Slop || Environment.TickCount64 - begin > maxMs) return;
            act();
        }
        top.AddHandler(InputElement.PointerReleasedEvent, Up, RoutingStrategies.Tunnel, handledEventsToo: true);
        top.AddHandler(InputElement.PointerMovedEvent, Move, RoutingStrategies.Tunnel, handledEventsToo: true);
    }
}

/// <summary>The key that does what Ctrl does on Windows: Ctrl there, ⌘ (Meta) on a Mac.</summary>
static class Mods
{
    public static readonly KeyModifiers Ctrl = OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;
}
