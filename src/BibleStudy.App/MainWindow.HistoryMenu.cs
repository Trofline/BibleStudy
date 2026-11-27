using Avalonia.Controls;

namespace BibleStudy.App;

/// <summary>
/// The back button with the clock (top left, as on the phone): a click goes back to where you were before, a right click lists the
/// places visited last, newest first, and jumps straight to one of them. The skipped places go to "forward", as with going back step by step.
/// </summary>
public partial class MainWindow
{
    const int HistoryMenuSize = 12;

    void InitHistoryMenu()
    {
        BackBtn.ContextRequested += (_, e) =>
        {
            e.Handled = true;
            RememberPlace();
            var menu = new MenuFlyout();
            menu.Items.Add(MenuAction(L("Zuletzt gelesen"), () => { }, false));
            // the last entry is where the reader is now
            for (int i = _history.Count - 2, n = 0; i >= 0 && n < HistoryMenuSize; i--, n++)
            {
                var steps = _history.Count - 1 - i;
                var p = _history[i];
                menu.Items.Add(MenuAction(PlaceLabel(p), () => _ = GoBackStepsAsync(steps)));
            }
            if (menu.Items.Count == 1) menu.Items.Add(MenuAction(L("Noch keine anderen Stellen besucht."), () => { }, false));
            menu.ShowAt(BackBtn);
        };
    }

    string PlaceLabel(Place p) => $"{BookName(p.Book)} {p.Chapter}" + (p.Verse is { } v ? $",{v}" : "");

    /// <summary>Goes back several places at once (from the list of the back button).</summary>
    async Task GoBackStepsAsync(int steps)
    {
        if (steps < 1 || steps >= _history.Count) return;
        if (Home.IsVisible || GraphPane.IsVisible || FamilyPane.IsVisible || TimelinePane.IsVisible || MapPane.IsVisible || KnowledgePane.IsVisible) ShowReader();
        RememberPlace();
        for (var s = 0; s < steps; s++)
        {
            _forward.Add(_history[^1]);
            _history.RemoveAt(_history.Count - 1);
        }
        var target = _history[^1];
        _history.RemoveAt(_history.Count - 1); // GoToAsync records it again
        await ShowPlaceAsync(target);
    }
}
