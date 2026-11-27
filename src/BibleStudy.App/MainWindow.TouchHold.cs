using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace BibleStudy.App;

/// <summary>
/// The menu of a long press (what a right click is on a computer) comes after <see cref="TouchTap.HoldMs"/> instead of Avalonia's 300 ms.
/// Avalonia opens the menu of a row the moment a finger rests for 300 ms: a thumb laid down to scroll, or an older hand that is not quick,
/// opened menus all the time. Its request is held back here; when the finger still rests on the same spot after <see cref="TouchTap.HoldMs"/>,
/// the same request is made again and the menu opens. A text field keeps its own long press (choose a word, paste).
/// </summary>
public partial class MainWindow
{
    IPointer? _holdPointer;
    PointerPressedEventArgs? _holdPress;
    Point _holdStart;
    Avalonia.Threading.DispatcherTimer? _holdTimer;
    bool _holdRaising;

    void InitTouchHold()
    {
        AddHandler(PointerPressedEvent, (_, e) =>
        {
            if (e.Pointer.Type != PointerType.Touch || InTextBox(e.Source)) return;
            StopHold();
            _holdPointer = e.Pointer; _holdPress = e; _holdStart = e.GetPosition(this);
            var timer = _holdTimer = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(TouchTap.HoldMs) };
            timer.Tick += (_, _) =>
            {
                var press = _holdPress;
                StopHold();
                if (press?.Source is not Interactive source || source is Visual { IsEffectivelyVisible: false }) return;
                _holdRaising = true;
                try { source.RaiseEvent(new ContextRequestedEventArgs(press)); }
                catch (Exception ex) { ErrorLog.Write(ex); }
                finally { _holdRaising = false; }
            };
            timer.Start();
        }, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerMovedEvent, (_, e) =>
        {
            if (e.Pointer != _holdPointer) return;
            var p = e.GetPosition(this);
            if (Math.Abs(p.X - _holdStart.X) > 10 || Math.Abs(p.Y - _holdStart.Y) > 10) StopHold();   // a scroll or a drag, no long press
        }, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerReleasedEvent, (_, e) => { if (e.Pointer == _holdPointer) StopHold(); }, RoutingStrategies.Tunnel, handledEventsToo: true);
        // Avalonia's own request after 300 ms (only while a finger is down, and not the one made above): held back
        AddHandler(ContextRequestedEvent, (_, e) =>
        {
            if (_holdRaising || _holdPointer is null || InTextBox(e.Source)) return;
            e.Handled = true;
        }, RoutingStrategies.Tunnel);
    }

    void StopHold()
    {
        _holdTimer?.Stop(); _holdTimer = null;
        _holdPointer = null; _holdPress = null;
    }

    /// <summary>True inside a text field: its long press chooses a word and shows copy and paste, as on every phone.</summary>
    static bool InTextBox(object? source) =>
        source is Visual v && (v is TextBox || v.GetVisualAncestors().Any(a => a is TextBox));
}
