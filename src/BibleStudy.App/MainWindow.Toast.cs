using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace BibleStudy.App;

/// <summary>
/// The small "Markierung entfernt. [Rückgängig]" bar at the bottom of the reader: after something was removed, one click takes it back
/// (as Ctrl+Z does). It goes away by itself after a few seconds.
/// </summary>
public partial class MainWindow
{
    Border? _undoToast;
    TextBlock? _undoToastText;
    Button? _undoToastBtn;
    DispatcherTimer? _undoToastTimer;
    Func<Task>? _undoToastAction;

    void ShowUndoToast(string text, Func<Task> undo)
    {
        if (_undoToast is null)
        {
            _undoToastText = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, MaxWidth = 460 };
            _undoToastBtn = new Button { Content = L("Rückgängig"), Classes = { "primary" }, Margin = new(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            var close = new Button { Content = "✕", Classes = { "ghost" }, Margin = new(4, 0, 0, 0), Padding = new(8, 4), VerticalAlignment = VerticalAlignment.Center };
            ToolTip.SetTip(close, L("Schließen"));
            _undoToastBtn.Click += async (_, _) => { var a = _undoToastAction; HideUndoToast(); if (a is not null) await a(); };
            close.Click += (_, _) => HideUndoToast();
            _undoToast = new Border
            {
                CornerRadius = new(10), Padding = new(14, 6, 6, 6), Margin = new(0, 0, 0, 22), BorderThickness = new(1), ZIndex = 60,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom,
                BoxShadow = BoxShadows.Parse("0 4 18 0 #33000000"),
                Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { _undoToastText, _undoToastBtn, close } },
            };
            _undoToast.Bind(Border.BackgroundProperty, this.GetResourceObservable("BsSurface2"));
            _undoToast.Bind(Border.BorderBrushProperty, this.GetResourceObservable("BsAccent"));
            Grid.SetRowSpan(_undoToast, 2);
        }
        _undoToastText!.Text = text;
        _undoToastAction = undo;
        if (_undoToast.Parent is null) ReaderHost.Children.Add(_undoToast);
        _undoToastTimer?.Stop();
        _undoToastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        _undoToastTimer.Tick += (_, _) => HideUndoToast();
        _undoToastTimer.Start();
    }

    void HideUndoToast()
    {
        _undoToastTimer?.Stop();
        _undoToastAction = null;
        if (_undoToast?.Parent is not null) ReaderHost.Children.Remove(_undoToast);
    }
}
