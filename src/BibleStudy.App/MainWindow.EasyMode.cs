using Avalonia.Controls;

namespace BibleStudy.App;

/// <summary>
/// Easy mode: only what is needed for reading, searching and notes (search, verse overview, notes, bookmarks; settings and help stay on top; fewer buttons there).
/// A button in the top bar switches between "Advanced" (everything) and "Easy"; the choice is remembered. Nothing is removed or lost.
/// </summary>
public partial class MainWindow
{
    bool _panelFolded;   // the side panel was folded away because the window got narrow

    static readonly HashSet<int> EasySideTabs = [0, 2, GuideTab, BookmarksTab, 12, 13, HandbookTab];

    void InitEasyMode()
    {
        ModeTopBtn.Click += (_, _) => { _settings.EasyMode = !_settings.EasyMode; SaveSettings(); ApplyEasyMode(); StatusText.Text = _settings.EasyMode ? L("Einfach-Modus: nur das Wichtigste. Ein Klick auf „Einfach“ oben zeigt wieder alles.") : L("Alle Funktionen sind wieder da."); };
        ApplyEasyMode();
    }

    /// <summary>The extra shortcuts in the reading bar only where there is room: in the normal-sized window the bar is full with its own buttons (they were cut off).</summary>
    void UpdateReadQuick()
    {
        var wide = Bounds.Width;
        ReadQuick.IsVisible = !_settings.EasyMode && !CompactLook && wide >= 1600;
        ReadQuickMore.IsVisible = wide >= 1800;
        SidePanel.MaxWidth = Math.Max(300, wide - 460);   // a narrow window keeps room for the text: the panel gives way
        // below about 1000 wide the open panel took half of the window: it folds away by itself and comes back when there is room again
        // (the choice saved in the settings stays the user's)
        if (wide > 0 && wide < 1000 && SidePanel.IsVisible && !_panelFolded) { _panelFolded = true; var keep = _settings.SideOpen; SetPanelOpen(false); _settings.SideOpen = keep; SaveSettings(); }
        else if (wide >= 1100 && _panelFolded) { _panelFolded = false; if (_settings.SideOpen && !SidePanel.IsVisible) SetPanelOpen(true); }
        FitReadBar();
    }

    void ApplyEasyMode()
    {
        var easy = _settings.EasyMode;
        ModeTopText.Text = easy ? L("Einfach") : L("Komplett");
        ToolTip.SetTip(ModeTopBtn, easy ? L("Einfach-Modus ist an. Klick: alle Funktionen zeigen.") : L("Alle Funktionen sind sichtbar. Klick: Einfach-Modus mit dem Wichtigsten."));
        for (var i = 0; i < SideTabs.ItemCount; i++)
            if (SideTabs.Items[i] is TabItem tab) tab.IsVisible = !easy || EasySideTabs.Contains(i);
        foreach (Control c in new Control[] { TodoTopBtn, SplitTopBtn, FeedbackTopBtn, KeysTopBtn, ParallelBtn, StudyBtn }) c.IsVisible = !easy;
        // a group of the side panel stays in the rail when Easy mode keeps one of its tabs (the verse overview, not the Greek; no AI, no studying together)
        foreach (var b in Rail.Children.OfType<Button>()) if (b.Tag is NavGroup g) b.IsVisible = !easy || g.Tabs.Any(t => EasySideTabs.Contains(t.Index));
        // the extra shortcuts that fill the rail and the reading bar are for Advanced only: Easy looks like it always did
        foreach (var c in Rail.Children.OfType<Control>()) if (c.Tag is "adv") c.IsVisible = !easy && !CompactLook;
        UpdateReadQuick();
        // the chosen side tab might be hidden now: go to the first one that is shown
        if (easy && !EasySideTabs.Contains(SideTabs.SelectedIndex)) SideTabs.SelectedIndex = 0;
        SyncNav(); FitTopBar(); FitReadBar();
    }
}
