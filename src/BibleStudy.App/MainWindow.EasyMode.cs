using Avalonia.Controls;

namespace BibleStudy.App;

/// <summary>
/// Easy mode: only what is needed for reading, searching and notes (search, notes, verse overview, settings, help; fewer buttons on top).
/// A button in the top bar switches between "Advanced" (everything) and "Easy"; the choice is remembered. Nothing is removed or lost.
/// </summary>
public partial class MainWindow
{
    static readonly HashSet<int> EasySideTabs = [0, 2, 12, 13, HandbookTab];

    void InitEasyMode()
    {
        ModeTopBtn.Click += (_, _) => { _settings.EasyMode = !_settings.EasyMode; SaveSettings(); ApplyEasyMode(); StatusText.Text = _settings.EasyMode ? L("Einfach-Modus: nur das Wichtigste. Mit „Easy“ oben wieder zurück zu „Advanced“.") : L("Advanced: alle Funktionen sind wieder da."); };
        ApplyEasyMode();
    }

    /// <summary>The extra shortcuts in the reading bar only where there is room: in the normal-sized window the bar is full with its own buttons (they were cut off).</summary>
    void UpdateReadQuick()
    {
        var wide = Bounds.Width;
        ReadQuick.IsVisible = !_settings.EasyMode && !CompactLook && wide >= 1600;
        ReadQuickMore.IsVisible = wide >= 1800;
        SidePanel.MaxWidth = Math.Max(300, wide - 460);   // a narrow window keeps room for the text: the panel gives way
        FitReadBar();
    }

    void ApplyEasyMode()
    {
        var easy = _settings.EasyMode;
        ModeTopText.Text = easy ? L("Easy") : L("Advanced");
        ToolTip.SetTip(ModeTopBtn, easy ? L("Einfach-Modus ist an. Klick: alle Funktionen zeigen (Advanced).") : L("Alle Funktionen sind sichtbar. Klick: aufgeräumte Oberfläche (Easy)."));
        for (var i = 0; i < SideTabs.ItemCount; i++)
            if (SideTabs.Items[i] is TabItem tab) tab.IsVisible = !easy || EasySideTabs.Contains(i);
        foreach (Control c in new Control[] { TodoTopBtn, FeedbackTopBtn, KeysTopBtn, ParallelBtn, GraphBtn, StudyBtn }) c.IsVisible = !easy;
        foreach (var b in Rail.Children.OfType<Button>()) if (b.Tag is NavGroup { Name: var gn } && gn == K("Vers")) b.IsVisible = !easy;
        // the extra shortcuts that fill the rail and the reading bar are for Advanced only: Easy looks like it always did
        foreach (var c in Rail.Children.OfType<Control>()) if (c.Tag is "adv") c.IsVisible = !easy && !CompactLook;
        UpdateReadQuick();
        // the chosen side tab might be hidden now: go to the first one that is shown
        if (easy && !EasySideTabs.Contains(SideTabs.SelectedIndex)) SideTabs.SelectedIndex = 0;
        FitTopBar(); FitReadBar();
    }
}
