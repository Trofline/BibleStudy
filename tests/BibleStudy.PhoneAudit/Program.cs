using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using BibleStudy.App;

// The phone layout on a PC: every screen at the size of real phones (iPhone 14 / 15: 390 x 844, a small Android: 360 x 740),
// a picture of each, and a list of what reaches out of the screen or is cut off.
Environment.SetEnvironmentVariable("BIBLESTUDY_PHONE", "1");   // before anything asks AppPlatform.IsMobile
var bin = AppContext.BaseDirectory;
File.WriteAllText(Path.Combine(bin, "portable.flag"), "");
var data = Path.Combine(bin, "Data");
if (Directory.Exists(data)) Directory.Delete(data, true);
var shots = args.Length > 0 ? args[0] : Path.Combine(bin, "phone-shots");
Directory.CreateDirectory(shots);
var sizes = new[] { (390, 844), (360, 740) };

AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).WithInterFont().SetupWithoutStarting();
MainWindow.NoShell = true;
const BindingFlags All = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static;
void Pump(int n = 6) { for (var i = 0; i < n; i++) { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs(); } }
void Settle(int ms) { var until = DateTime.Now.AddMilliseconds(ms); while (DateTime.Now < until) { Pump(2); Thread.Sleep(30); } }
var report = new List<string>();

foreach (var (w, h) in sizes)
{
    var win = new MainWindow { MinWidth = 0, MinHeight = 0, Width = w, Height = h };   // the window of a desktop is at least 850 wide
    win.Show();
    Pump(20);
    var startWait = DateTime.Now;
    while ((DateTime.Now - startWait).TotalSeconds < 40)
    {
        Settle(100);
        var st = win.FindControl<TextBlock>("StatusText")?.Text ?? "";
        if ((DateTime.Now - startWait).TotalSeconds > 4 && !st.StartsWith("Lade")) break;
    }
    Settle(2000);
    object? Call(string name, params object?[] a)
    {
        try { return typeof(MainWindow).GetMethods(All).First(m => m.Name == name && m.GetParameters().Length == a.Length).Invoke(win, a); }
        catch (Exception e) { report.Add($"  ! {name}: {e.InnerException?.Message ?? e.Message}"); return null; }
    }
    void Screen(string name)
    {
        Settle(400);
        var file = $"{w}x{h}_{name}";
        using (var f = win.CaptureRenderedFrame()) f?.Save(Path.Combine(shots, file + ".png"));
        report.Add($"== {file}");
        foreach (var line in Scan(win, w)) report.Add("  " + line);
    }

    Call("ShowHome"); Screen("00_start");
    Call("ShowReader"); Screen("01_reader");
    Call("SetPanelOpen", true); Screen("02_panel");
    // every tab of every work area and of the side panel
    var groups = (Array)typeof(MainWindow).GetField("NavGroups", All)!.GetValue(null)!;
    var done = new HashSet<int>();
    var n = 3;
    foreach (var g in groups)
    {
        var gName = (string)g.GetType().GetProperty("Name")!.GetValue(g)!;
        foreach (var t in (Array)g.GetType().GetProperty("Tabs")!.GetValue(g)!)
        {
            var index = (int)t.GetType().GetProperty("Index")!.GetValue(t)!;
            var label = (string)t.GetType().GetProperty("Label")!.GetValue(t)!;
            if (!done.Add(index)) continue;
            Call("ShowReader");
            Call("RevealSideTab", index);
            Screen($"{n++:00}_{Safe(gName)}_{Safe(label)}");
        }
    }
    // the settings: every area
    var settingsTab = (int)(typeof(MainWindow).GetField("SettingsTab", All)?.GetValue(null) ?? -1);
    if (settingsTab >= 0)
        for (var area = 0; area < 8; area++)
        {
            Call("ShowReader"); Call("RevealSideTab", settingsTab); Call("ShowSettingsArea", (int?)area);
            Screen($"{n++:00}_settings_{area}");
        }
    // a new document in the editor
    Call("ShowReader");
    var knowledge = (int)typeof(MainWindow).GetField("KnowledgeTab", All)!.GetValue(null)!;
    Call("RevealSideTab", knowledge);
    var doc = new BibleStudy.Core.Storage.Document(0, "Ein ziemlich langer Titel für ein Dokument zum Ausprobieren", "Text mit Joh 3,16", null, false, DateTime.Now, []);
    Call("OpenDocument", doc); Screen($"{n++:00}_doc_editor");
    // the explorer while choosing
    Call("ShowReader"); Call("RevealSideTab", (int)typeof(MainWindow).GetField("ExplorerTab", All)!.GetValue(null)!); Call("SetExPick", true, null); Screen($"{n++:00}_explorer_pick");
    Call("SetExPick", false, null);
    win.Close();
    Pump(10);
}
File.WriteAllLines(Path.Combine(shots, "report.txt"), report);
Console.WriteLine(string.Join(Environment.NewLine, report));
return 0;

static string Safe(string s) => new string(s.Where(char.IsLetterOrDigit).ToArray());

/// <summary>Everything that can be seen and reaches out of the screen sideways (not inside something that scrolls sideways), and text that is cut off without "…".</summary>
static IEnumerable<string> Scan(Window win, double width)
{
    var seen = 0;
    foreach (var v in win.GetVisualDescendants().OfType<Control>())
    {
        if (!v.IsEffectivelyVisible || v.Bounds.Width <= 0 || v.Bounds.Height <= 0) continue;
        if (v.TranslatePoint(default, win) is not { } tl) continue;
        if (tl.Y > win.Bounds.Height || tl.Y + v.Bounds.Height < 0) continue;   // below or above the screen (scrolled away)
        var sideways = v.GetVisualAncestors().Any(a => a is ScrollViewer { HorizontalScrollBarVisibility: not ScrollBarVisibility.Disabled } sv && sv.Extent.Width > sv.Viewport.Width + 1
            || a is ScrollContentPresenter { CanHorizontallyScroll: true });
        if (v.GetVisualAncestors().OfType<Popup>().Any()) continue;
        var right = tl.X + v.Bounds.Width;
        string What() => v.GetType().Name + (v.Name is { Length: > 0 } nm ? "#" + nm : "") + (v is TextBlock tb ? " \"" + Cut(tb.Text ?? "") + "\"" : v is ContentControl { Content: string c } ? " \"" + Cut(c) + "\"" : "");
        if (!sideways && (right > width + 1.5 || tl.X < -1.5) && v is TextBlock or Button or TextBox or ComboBox or Border { Child: not null } or Image)
        {
            if (seen++ < 60) yield return $"OUT  {What()}  x={tl.X:0}..{right:0} (screen {width:0})";
        }
        else if (v is TextBlock t && t.TextWrapping == TextWrapping.NoWrap && t.TextTrimming == TextTrimming.None && !sideways && t.Text is { Length: > 0 }
                 && t.DesiredSize.Width > t.Bounds.Width + 2)
        {
            if (seen++ < 60) yield return $"CUT  {What()}  needs {t.DesiredSize.Width:0}, has {t.Bounds.Width:0}";
        }
    }
    static string Cut(string s) => s.Length > 40 ? s[..40] + "…" : s;
}
