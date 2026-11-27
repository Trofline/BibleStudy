using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace BibleStudy.App;

/// <summary>
/// The status bar while studying together: a small level meter for the own microphone (green while the others hear you; a click mutes it for a
/// moment, then it is red), a camera sign (a click makes the picture black for a moment: the others see a black picture, the camera light goes
/// off) and how long the room has been running. Switching microphone and camera really off stays in the panel "Zusammen".
/// </summary>
public partial class MainWindow
{
    bool _micMuted, _camPaused;      // switched off for a moment in the status bar (the call and the camera stay "on" in the panel)
    DateTime? _roomSince;            // since when the room runs (UTC): the earliest start anyone in the room knows of
    DispatcherTimer? _footTimer;
    Button? _micChip, _camChip, _roomChip;
    readonly Border[] _bars = new Border[4];
    readonly double[] _barNow = new double[4];
    Ellipse? _camDot;
    Icon? _camIcon;
    TextBlock? _roomText;
    DateTime _lastBlack;
    Button? _callBtn, _camBtn;       // the switches in the panel, kept in step with the status bar

    static readonly IBrush LiveBrush = new SolidColorBrush(Color.Parse("#30A46C"));
    static readonly IBrush MutedBrush = new SolidColorBrush(Color.Parse("#E5484D"));
    static readonly Geometry CamShape = Geometry.Parse("M3 7h11v10H3z M14 10.5l6-3.5v10l-6-3.5");
    static readonly Geometry CamOffShape = Geometry.Parse("M3 7h11v10H3z M14 10.5l6-3.5v10l-6-3.5 M2 4L21 21");
    static readonly double[] BarWeight = [0.55, 1, 0.8, 0.45];
    const double BarMin = 3, BarMax = 14;

    /// <summary>A black picture (JPEG): sent instead of the camera while it is paused, so the others still see the tile, only black.</summary>
    static readonly byte[] BlackPicture = Convert.FromBase64String(
        "/9j/4AAQSkZJRgABAQAAAQABAAD/2wBDABsSFBcUERsXFhceHBsgKEIrKCUlKFE6PTBCYFVlZF9VXVtqeJmBanGQc1tdhbWGkJ6jq62rZ4C8ybqmx5moq6T/wAALCAAYACABAREA/8QAHwAAAQUBAQEBAQEAAAAAAAAAAAECAwQFBgcICQoL/8QAtRAAAgEDAwIEAwUFBAQAAAF9AQIDAAQRBRIhMUEGE1FhByJxFDKBkaEII0KxwRVS0fAkM2JyggkKFhcYGRolJicoKSo0NTY3ODk6Q0RFRkdISUpTVFVWV1hZWmNkZWZnaGlqc3R1dnd4eXqDhIWGh4iJipKTlJWWl5iZmqKjpKWmp6ipqrKztLW2t7i5usLDxMXGx8jJytLT1NXW19jZ2uHi4+Tl5ufo6erx8vP09fb3+Pn6/9oACAEBAAA/AOZooooooooooor/2Q==");

    void InitCollabFooter()
    {
        var bars = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, VerticalAlignment = VerticalAlignment.Center, Height = BarMax };
        for (var i = 0; i < _bars.Length; i++)
        {
            _bars[i] = new Border { Width = 3, Height = BarMin, CornerRadius = new(1.5), Background = LiveBrush, VerticalAlignment = VerticalAlignment.Center };
            _barNow[i] = BarMin;
            bars.Children.Add(_bars[i]);
        }
        _micChip = Chip(bars);
        _micChip.Click += (_, _) => ToggleMicMute();

        _camIcon = new Icon { Data = CamShape, IconSize = 15, VerticalAlignment = VerticalAlignment.Center };
        TextElement.SetForeground(_camIcon, LiveBrush);
        _camDot = new Ellipse { Width = 6, Height = 6, Fill = LiveBrush, VerticalAlignment = VerticalAlignment.Center };
        _camChip = Chip(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, Children = { _camIcon, _camDot } });
        _camChip.Click += async (_, _) => await ToggleCamPauseAsync();

        _roomText = new TextBlock { Classes = { "caption" }, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        var roomDot = new Ellipse { Width = 6, Height = 6, Fill = LiveBrush, VerticalAlignment = VerticalAlignment.Center };
        _roomChip = Chip(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { roomDot, _roomText } });
        ToolTip.SetTip(_roomChip, L("So lange läuft der Raum schon. Ende-zu-Ende-verschlüsselt. Klicken: Zusammen studieren öffnen."));
        _roomChip.Click += (_, _) => OpenCollabPanel();

        CollabChips.Children.Add(_micChip);
        CollabChips.Children.Add(_camChip);
        CollabChips.Children.Add(_roomChip);
        _footTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
        _footTimer.Tick += (_, _) => TickCollabFooter();
        _footTimer.Start();
    }

    static Button Chip(Control content) => new()
    {
        Classes = { "ghost" }, Padding = new(7, 0), MinHeight = 0, Height = 22, VerticalAlignment = VerticalAlignment.Center, Content = content,
    };

    void OpenCollabPanel()
    {
        SetPanelOpen(true); SideTabs.SelectedIndex = CollabTab;
    }

    void TickCollabFooter()
    {
        if (_micChip is null || _camChip is null || _roomChip is null) return;
        var open = CollabOpen;
        if (!open) { _roomSince = null; _micMuted = false; _camPaused = false; CollabChips.IsVisible = false; return; }
        _roomSince ??= DateTime.UtcNow;
        CollabChips.IsVisible = true;

        // the room
        var up = DateTime.UtcNow - _roomSince.Value;
        _roomText!.Text = up.TotalHours >= 1 ? $"{(int)up.TotalHours}:{up.Minutes:00}:{up.Seconds:00}" : $"{up.Minutes}:{up.Seconds:00}";

        // the microphone: the bars follow the own voice while the others hear it
        _micChip.IsVisible = _callMic || _micMuted || _pttHeld;
        if (_micChip.IsVisible)
        {
            var live = (_callMic || _pttHeld) && _mic is not null;
            var level = live ? Math.Min(1, Math.Sqrt(_myLevel * 5)) : 0;   // a quiet voice is about 0.03, a loud one 0.3
            for (var i = 0; i < _bars.Length; i++)
            {
                var target = BarMin + (BarMax - BarMin) * level * BarWeight[i] * (0.65 + 0.35 * Random.Shared.NextDouble());
                _barNow[i] += (target - _barNow[i]) * (target > _barNow[i] ? 0.7 : 0.35);   // up fast, down gently
                _bars[i].Height = Math.Round(_barNow[i], 1);
                _bars[i].Background = _micMuted ? MutedBrush : LiveBrush;
            }
            ToolTip.SetTip(_micChip, _micMuted ? L("Mikrofon stumm. Klicken: wieder einschalten.")
                : _callMic ? L("Mikrofon an: Die anderen hören dich. Klicken: kurz stummschalten.") : L("Die anderen hören dich, solange du den Knopf hältst."));
        }

        // the camera: a slowly glowing dot while it sends
        _camChip.IsVisible = _camOn || _camPaused;
        if (_camChip.IsVisible)
        {
            _camIcon!.Data = _camPaused ? CamOffShape : CamShape;
            TextElement.SetForeground(_camIcon, _camPaused ? MutedBrush : LiveBrush);
            _camDot!.IsVisible = !_camPaused;
            _camDot.Opacity = 0.35 + 0.65 * (0.5 + 0.5 * Math.Sin(DateTime.UtcNow.TimeOfDay.TotalSeconds * Math.PI));
            ToolTip.SetTip(_camChip, _camPaused ? L("Kamera pausiert: Die anderen sehen ein schwarzes Bild. Klicken: Bild wieder zeigen.")
                : L("Kamera an. Klicken: Bild kurz schwarz machen."));
            if (_camPaused && (DateTime.UtcNow - _lastBlack).TotalSeconds >= 1) SendBlackPicture();
        }
    }

    // ---- muting for a moment -------------------------------------------------------------------------------------------------

    /// <summary>The call microphone off for a moment (it is closed, so nothing is heard at all), or back on.</summary>
    void ToggleMicMute()
    {
        if (_micMuted) { _micMuted = false; _callMic = true; }
        else if (_callMic) { _callMic = false; _micMuted = true; }
        else return;
        UpdateMic();
        RefreshMediaButtons();
    }

    /// <summary>The camera black for a moment: it is closed and the others get a black picture each second, so their tile stays; or back on.</summary>
    async Task ToggleCamPauseAsync()
    {
        if (_camPaused)
        {
            _camPaused = false;
            await SetCameraAsync(true);
        }
        else if (_camOn)
        {
            _camPaused = true;
            _camera?.Dispose(); _camera = null; _camOn = false;
            SendBlackPicture();
        }
        RefreshMediaButtons();
    }

    void SendBlackPicture()
    {
        _lastBlack = DateTime.UtcNow;
        CollabSend(new { t = "vf", d = Convert.ToBase64String(BlackPicture) });
        ShowFrame("me", L("Du"), PeopleInkOf(null), BlackPicture);
    }

    /// <summary>The panel's microphone switch: on, or (also when muted) really off.</summary>
    void PanelCallClick()
    {
        if (_micMuted) _micMuted = false;
        else _callMic = !_callMic;
        UpdateMic();
        RefreshMediaButtons();
    }

    /// <summary>The panel's camera switch: on, or (also when paused) really off.</summary>
    async Task PanelCameraClickAsync()
    {
        if (_camPaused) { _camPaused = false; CollabSend(new { t = "vf", off = true }); RemoveTile("me"); }
        else await SetCameraAsync(!_camOn);
        RefreshMediaButtons();
    }

    void RefreshMediaButtons()
    {
        if (_callBtn is { } call) { call.Content = _callMic || _micMuted ? L("Mikrofon aus") : L("Mikrofon an (Gespräch)"); call.Classes.Set("primary", _callMic || _micMuted); }
        if (_camBtn is { } cam) { cam.Content = _camOn || _camPaused ? L("Kamera aus") : L("Kamera an"); cam.Classes.Set("primary", _camOn || _camPaused); }
        UpdateSpeaking();
    }

    // ---- how long the room runs ---------------------------------------------------------------------------------------------

    /// <summary>Seconds since the room started, sent with the hello: someone who comes later then shows the same time.</summary>
    int RoomUpSeconds => (int)Math.Max(0, (DateTime.UtcNow - (_roomSince ??= DateTime.UtcNow)).TotalSeconds);

    /// <summary>Another person said how long the room runs: the earliest start counts (at most a week back, a wrong clock does no harm).</summary>
    void TakeRoomUp(int seconds)
    {
        if (seconds <= 0 || seconds > 7 * 24 * 3600) return;
        var since = DateTime.UtcNow - TimeSpan.FromSeconds(seconds);
        if (_roomSince is null || since < _roomSince) _roomSince = since;
    }

    // ---- the note on encryption -----------------------------------------------------------------------------------------------

    static readonly Geometry LockShape = Geometry.Parse("M6 11h12v9H6z M8.5 11V8a3.5 3.5 0 0 1 7 0v3");

    /// <summary>A short line with a lock: both ways (room code and same network) seal everything end to end (AES-GCM, keys made by ECDH on the
    /// devices; with a room code also mixed with the code), the relay only passes sealed bytes on and keeps nothing.</summary>
    Control EncryptionNote(string text)
    {
        var icon = new Icon { Data = LockShape, IconSize = 14, VerticalAlignment = VerticalAlignment.Top, Margin = new(0, 1, 0, 0) };
        TextElement.SetForeground(icon, LiveBrush);
        var line = new DockPanel { HorizontalAlignment = HorizontalAlignment.Left };
        DockPanel.SetDock(icon, Dock.Left);
        line.Children.Add(icon);
        line.Children.Add(new TextBlock { Text = text, Classes = { "caption" }, TextWrapping = TextWrapping.Wrap, Margin = new(6, 0, 0, 0) });
        return line;
    }

    Control RoomCodeEncryptionNote() => EncryptionNote(L("Ende-zu-Ende-verschlüsselt: Nur wer den Raum-Code hat, kann mitlesen. Der Server reicht nur verschlüsselte Daten weiter, kann sie nicht lesen und speichert nichts."));
}
