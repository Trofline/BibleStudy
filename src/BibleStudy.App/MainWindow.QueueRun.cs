using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using BibleStudy.Core;
using BibleStudy.Core.Storage;

namespace BibleStudy.App;

/// <summary>Walking through the reading list with a small strip at the bottom. Visited verses leave the list only when the walk ends, so one can still go back.</summary>
public partial class MainWindow
{
    QueueRun? _queueRun;

    void WireQueueRun()
    {
        QueueStartBtn.Click += async (_, _) => { if (_queueRun is null) await StartQueueRunAsync(); else EndQueueRun(); };
        QueueBarNextBtn.Click += async (_, _) => { if (_queueRun is { IsLast: true }) EndQueueRun(); else await QueueRunStepAsync(1); };
        QueueBarPrevBtn.Click += async (_, _) => await QueueRunStepAsync(-1);
        QueueBarEndBtn.Click += (_, _) => EndQueueRun();
        QueueBarListBtn.Click += (_, _) => ShowQueueRunList();
    }

    async Task StartQueueRunAsync()
    {
        var items = _vault.GetQueue();
        if (items.Count == 0) { QueueInfo.Text = L("Die Leseliste ist leer."); return; }
        _queueRun = new QueueRun(items.Select(q => q.Id));
        RefreshQueue();
        await ShowQueueRunVerseAsync();
    }

    async Task QueueRunStepAsync(int delta)
    {
        if (_queueRun?.Move(delta) != true) return;
        RefreshQueue();
        await ShowQueueRunVerseAsync();
    }

    async Task QueueRunGoToAsync(long id)
    {
        if (_queueRun?.MoveTo(id) != true) return;
        RefreshQueue();
        await ShowQueueRunVerseAsync();
    }

    async Task ShowQueueRunVerseAsync()
    {
        if (_queueRun is not { } run || _vault.GetQueue().FirstOrDefault(q => q.Id == run.Current) is not { } item) return;
        await GoToAsync(item.Ref.Book, item.Ref.Chapter, item.Ref.Verse);
        if (_verseBoxes.ContainsKey(item.Ref.Verse)) ScrollToVerse(item.Ref.Verse);   // in the same chapter the view would otherwise stay where it is
    }

    /// <summary>Ends the walk and takes the visited verses off the list of the current vault.</summary>
    void EndQueueRun()
    {
        if (_queueRun is not { } run) return;
        _queueRun = null;
        foreach (var id in run.Visited) _vault.RemoveFromQueue(id);
        RefreshQueue();
        StatusText.Text = L("{0} Verse abgehakt · {1} noch auf der Leseliste", run.Visited.Count, _vault.GetQueue().Count);
    }

    void UpdateQueueBar(List<QueueItem> items)
    {
        QueueBar.IsVisible = _queueRun is not null;
        QueueStartBtn.Content = _queueRun is null ? L("Starten") : L("Beenden");
        if (_queueRun is not { } run) return;
        QueueBarCount.Text = L("Leseliste {0}/{1}", run.Position + 1, run.Count);
        QueueBarTitle.Text = items.FirstOrDefault(q => q.Id == run.Current)?.Label ?? "";
        QueueBarPrevBtn.IsEnabled = !run.IsFirst;
        QueueBarNextBtn.Content = run.IsLast ? L("Fertig") : L("Nächster ▶");
    }

    void ShowQueueRunList()
    {
        if (_queueRun is not { } run) return;
        var panel = new StackPanel { Spacing = 2, MinWidth = 220 };
        var fly = new Flyout { Content = new ScrollViewer { MaxHeight = 360, Content = panel }, Placement = PlacementMode.TopEdgeAlignedLeft };
        var number = 0;
        foreach (var item in _vault.GetQueue())
        {
            number++;
            var id = item.Id;
            var row = new Button
            {
                Content = $"{number}. {item.Label}{(run.Visited.Contains(id) ? " ✓" : "")}",
                Classes = { "ghost" }, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left,
                FontWeight = id == run.Current ? FontWeight.Bold : FontWeight.Normal,
            };
            row.Click += async (_, _) => { fly.Hide(); await QueueRunGoToAsync(id); };
            panel.Children.Add(row);
        }
        fly.ShowAt(QueueBarListBtn);
    }
}
