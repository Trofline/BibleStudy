using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace BibleStudy.App;

/// <summary>
/// Screen readers: buttons that only show an icon get their tooltip as the spoken name, fields their hint text. Done for what exists after
/// the start and again when the window's content changes a little later (panels are built when first opened).
/// </summary>
public partial class MainWindow
{
    void InitAutomation()
    {
        AutomationProperties.SetName(this, "BibleStudy");
        foreach (var delay in new[] { 2, 8, 30 })
            DispatcherTimer.RunOnce(() => NameControls(this), TimeSpan.FromSeconds(delay));
        // panels that are filled later (settings, side tabs): the next time a tab is chosen
        SideTabs.SelectionChanged += (_, e) => { if (e.Source == SideTabs) DispatcherTimer.RunOnce(() => NameControls(SidePanel), TimeSpan.FromMilliseconds(400)); };
    }

    static void NameControls(Visual root)
    {
        foreach (var v in root.GetVisualDescendants())
        {
            if (v is not Control c || !string.IsNullOrEmpty(AutomationProperties.GetName(c))) continue;
            switch (c)
            {
                case Button or ToggleButton when !ContainsText(c):
                    if (ToolTip.GetTip(c) is string tip && tip.Length > 0) AutomationProperties.SetName(c, tip);
                    break;
                case TextBox box when !string.IsNullOrEmpty(box.Watermark):
                    AutomationProperties.SetName(c, box.Watermark);
                    break;
                case ComboBox combo when ToolTip.GetTip(combo) is string comboTip && comboTip.Length > 0:
                    AutomationProperties.SetName(c, comboTip);
                    break;
            }
        }
    }

    /// <summary>True when the button shows a text of its own (then that text is its name already).</summary>
    static bool ContainsText(Control button) =>
        button is ContentControl { Content: string s } && s.Length > 0 || button.GetVisualDescendants().OfType<TextBlock>().Any(t => !string.IsNullOrWhiteSpace(t.Text));
}
