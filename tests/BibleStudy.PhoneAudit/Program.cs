using System.Collections;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using BibleStudy.App;
using BibleStudy.Core.Models;

// The phone layout on a PC: phone sizes (iPhone 14/15, iPhone SE, a small Android), the app filled with data, every screen scrolled page by
// page, the bottom sheets, the pickers and menus, small tap targets, and a finger (raw touch input) in the explorer and the reader.
// Run: dotnet run -c Release --project tests/BibleStudy.PhoneAudit -- <folder for the pictures>. AUDIT_TOUCH_ONLY=1: only the finger tests at 390.
// Limit: Avalonia.Headless raises no Holding gesture, so "hold to open the menu" can only be checked on a device.
Environment.SetEnvironmentVariable("BIBLESTUDY_PHONE", "1");
var bin = AppContext.BaseDirectory;
File.WriteAllText(Path.Combine(bin, "portable.flag"), "");
var data = Path.Combine(bin, "Data");
var shots = args.Length > 0 ? args[0] : Path.Combine(bin, "phone-shots");
Directory.CreateDirectory(shots);
var touchOnly = Environment.GetEnvironmentVariable("AUDIT_TOUCH_ONLY") == "1";
var sizes = touchOnly ? new[] { (390, 844) } : new[] { (390, 844), (375, 667), (360, 740) };

AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).WithInterFont().SetupWithoutStarting();
MainWindow.NoShell = true;
const BindingFlags All = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static;
void Pump(int n = 6) { for (var i = 0; i < n; i++) { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs(); } }
void Settle(int ms) { var until = DateTime.Now.AddMilliseconds(ms); while (DateTime.Now < until) { Pump(2); Thread.Sleep(30); } }
var report = new List<string>();
var openPopups = new List<Popup>();
Popup.IsOpenProperty.Changed.AddClassHandler<Popup>((p, e) => { if (p.IsOpen) { if (!openPopups.Contains(p)) openPopups.Add(p); } else openPopups.Remove(p); });

foreach (var (w, h) in sizes)
{
    try { if (Directory.Exists(data)) Directory.Delete(data, true); } catch (IOException) { report.Add("  ! data folder still in use: the data of the size before stays"); }
    var win = new MainWindow { MinWidth = 0, MinHeight = 0, Width = w, Height = h };
    win.Show();
    Pump(20);
    var startWait = DateTime.Now;
    while ((DateTime.Now - startWait).TotalSeconds < 40)
    {
        Settle(100);
        var st = win.FindControl<TextBlock>("StatusText")?.Text ?? "";
        if ((DateTime.Now - startWait).TotalSeconds > 4 && !st.StartsWith("Lade")) break;
    }
    Settle(2500);

    object? Get(string name) => typeof(MainWindow).GetField(name, All)?.GetValue(win) ?? typeof(MainWindow).GetProperty(name, All)?.GetValue(win);
    object? Invoke(object target, string name, params object?[] a)
    {
        var m = target.GetType().GetMethods(All).First(m => m.Name == name && m.GetParameters().Length == a.Length);
        var r = m.Invoke(target, a);
        if (r is Task t) { var until = DateTime.Now.AddSeconds(25); while (!t.IsCompleted && DateTime.Now < until) Settle(50); }
        return r;
    }
    object? Call(string name, params object?[] a)
    {
        try { return Invoke(win, name, a); }
        catch (Exception e) { report.Add($"  ! {name}: {(e.InnerException ?? e).GetType().Name}: {(e.InnerException ?? e).Message}"); return null; }
    }
    void Quiet(string name, params object?[] a) { try { Invoke(win, name, a); } catch { } }
    void CloseAll()
    {
        foreach (var p in openPopups.ToList()) p.IsOpen = false;
        Quiet("ShowSheet", null, Array.Empty<Control>());
        Pump(4);
    }
    var n = 0;
    void Shot(string file, bool scanToo = true)
    {
        using (var f = win.CaptureRenderedFrame()) f?.Save(Path.Combine(shots, file + ".png"));
        report.Add($"== {file}");
        if (scanToo) foreach (var line in Scan(win, w, h)) report.Add("  " + line);
        var pi = 0;
        foreach (var p in openPopups.Where(p => p.IsOpen).ToList())
        {
            if ((typeof(Popup).GetProperty("Host", All)?.GetValue(p) as TopLevel ?? (p.Child is { } pc ? TopLevel.GetTopLevel(pc) : null)) is { } tl && tl != win)
            {
                pi++;
                using (var f = tl.CaptureRenderedFrame()) f?.Save(Path.Combine(shots, $"{file}_popup{pi}.png"));
                var cw = p.Child?.Bounds.Width ?? 0; var ch = p.Child?.Bounds.Height ?? 0;
                report.Add($"  POPUP{pi} {p.Child?.GetType().Name} {cw:0}x{ch:0} (screen {w}x{h})" + (cw > w ? "  WIDER THAN SCREEN" : "") + (ch > h ? "  TALLER THAN SCREEN" : ""));
                foreach (var line in Scan(tl, w, h, popup: true)) report.Add("    " + line);
            }
        }
    }
    // the screen, then page by page down its main scrolling area
    void Screen(string name, int settle = 700)
    {
        Settle(settle);
        var file = $"{w}x{h}_{n++:00}_{name}";
        Shot(file);
        var sv = win.GetVisualDescendants().OfType<ScrollViewer>()
            .Where(s => s.IsEffectivelyVisible && s.Viewport.Height > 150 && s.Extent.Height - s.Viewport.Height > 60 && !s.GetVisualAncestors().OfType<Popup>().Any())
            .OrderByDescending(s => s.Viewport.Width * s.Viewport.Height).FirstOrDefault();
        if (sv is null) return;
        var page = 1;
        report.Add($"  (scrolls: content {sv.Extent.Height:0} high, view {sv.Viewport.Height:0})");
        while (page < 8 && sv.Offset.Y < sv.Extent.Height - sv.Viewport.Height - 2)
        {
            sv.Offset = new Vector(sv.Offset.X, Math.Min(sv.Extent.Height - sv.Viewport.Height, sv.Offset.Y + sv.Viewport.Height * 0.85));
            Settle(250);
            Shot($"{file}_s{page++}");
        }
        sv.Offset = new Vector(sv.Offset.X, 0);
        Pump(4);
    }

    // ---- the app filled with data, like after a few weeks of use --------------------------------------------------------
    try
    {
        var vault = Get("_vault")!;
        var book = (string)Get("_curBook")!;
        var chapter = (int)Get("_curChapter")!;
        var trans = (win.FindControl<ComboBox>("TranslationBox")?.SelectedItem as TranslationInfo)?.Id ?? "";
        string[] colors = ["#F5C542", "#5B8DEF", "#3FB27F", "#E0607E", "#8E6CE0"];
        for (var v = 1; v <= 14; v++)
        {
            var r = new VerseRef(book, chapter, v);
            if (v % 2 == 1) Invoke(vault, "SetNote", r, $"Notiz zu Vers {v}: Das Wort war am Anfang – ein ziemlich langer Gedanke, der über mehrere Zeilen geht und auf Joh 3,16 und 1. Mose 1,1 verweist.");
            Invoke(vault, "AddMark", trans, r, 0, 12, "Im Anfang war", colors[v % colors.Length], v % 3 == 0 ? "Wichtig für die Predigt" : "", null, (IEnumerable<string>)(v % 2 == 0 ? new[] { "Predigt", "Christus" } : new[] { "Licht" }), 0);
            if (v % 3 == 0) Invoke(vault, "ToggleBookmark", r, "Im Anfang war das Wort, und das Wort war bei Gott, und das Wort war Gott.");
        }
        foreach (var f in new[] { "Predigtreihe Johannes", "Hauskreis", "Wortstudien Griechisch" }) Invoke(vault, "AddFolder", f);
        foreach (var (title, tag) in new[] { ("Das Wort wurde Fleisch – Predigt zu Johannes 1,1-18", "Predigt"), ("Logos: Wortstudie", "Wortstudie"), ("Johannes der Täufer", "Personenstudie"), ("Gebetsabend 12. März", "Gebet"), ("Fragen aus dem Hauskreis", "Frage"), ("Licht und Finsternis im Johannesevangelium – Themenstudie", "Themenstudie") })
            Invoke(vault, "AddDocument", title, $"# {title}\n\nText mit Joh 1,1 und [[Logos: Wortstudie]]. " + string.Concat(Enumerable.Repeat("Ein Absatz zum Ausprobieren. ", 20)), null, false, (IEnumerable<string>)new[] { tag }, null);
        for (var i = 1; i <= 6; i++) Invoke(vault, "AddTodo", $"To-do {i}: Römer {i + 2} vergleichen und Notizen für den Hauskreis machen", i % 2 == 0 ? new VerseRef(book, chapter, i) : null, null, i % 3 == 0 ? DateOnly.FromDateTime(DateTime.Today.AddDays(i)) : null);
        for (var i = 1; i <= 5; i++) Invoke(vault, "AddPrayer", $"Gebetsanliegen {i}: für die Familie, die Gemeinde und die Menschen im Hauskreis, die gerade krank sind", i % 2 == 0 ? new VerseRef(book, chapter, i) : null);
        Invoke(vault, "AddToQueue", (IEnumerable<(VerseRef, string)>)Enumerable.Range(1, 10).Select(v => (new VerseRef(book, chapter, v), $"Johannes {chapter},{v}")).ToList());
        foreach (var m in new[] { "RefreshLists", "RefreshBookmarks", "RefreshTodos", "RefreshDocs", "RefreshQueue", "RefreshCollection", "RefreshNotesTree", "RefreshExplorer", "RefreshVaultUi" }) Quiet(m);
        Quiet("ShowChapterAsync", null, false);
        var sb = win.FindControl<TextBox>("SearchBox");
        if (sb is not null) { sb.Text = "Licht"; Call("SearchAsync"); }
        Settle(1500);
    }
    catch (Exception e) { report.Add($"  ! seeding: {(e.InnerException ?? e).Message}"); }

    var pTitle = Get("_pTitle") as Control;
    var pTrans = Get("_pTrans") as Control;

    if (!touchOnly)
    {
        Call("ShowHome"); Screen("start");
        Call("ShowReader"); Screen("reader");
        Call("SelectVerse", 3); Screen("reader_verse_selected");
        Call("ShowVerseMenu", 3, pTitle!, null); Screen("verse_menu"); CloseAll();
        Call("ShowToolsSheet"); Screen("sheet_Werkzeuge"); CloseAll();
        Call("ShowMoreSheet"); Screen("sheet_Mehr"); CloseAll();
        Call("ShowFontBar"); Screen("sheet_Schrift"); CloseAll();
        Call("ShowBookPicker", pTitle, pTitle); Screen("picker_Buch"); CloseAll();
        Call("ShowChapterPicker", pTitle); Screen("picker_Kapitel"); CloseAll();
        Call("ShowTranslationPicker", pTrans); Screen("picker_Übersetzung"); CloseAll();
        Call("ShowLanguagePicker"); Screen("picker_Sprache"); CloseAll();

        // every tab of every area, a verse selected, with data
        var groups = (Array)typeof(MainWindow).GetField("NavGroups", All)!.GetValue(null)!;
        var done = new HashSet<int>();
        foreach (var g in groups)
        {
            var gName = (string)g.GetType().GetProperty("Name")!.GetValue(g)!;
            foreach (var t in (Array)g.GetType().GetProperty("Tabs")!.GetValue(g)!)
            {
                var index = (int)t.GetType().GetProperty("Index")!.GetValue(t)!;
                var label = (string)t.GetType().GetProperty("Label")!.GetValue(t)!;
                if (!done.Add(index)) continue;
                CloseAll();
                Call("ShowReader"); Quiet("SelectVerse", 3);
                Call("RevealSideTab", index);
                Screen($"{Safe(gName)}_{Safe(label)}", 1500);
            }
        }
        // the areas of the "More" sheet that are no tab group (graph, map, timeline …): pressed like a person would
        if (win.FindControl<Panel>("Rail") is { } rail)
            foreach (var b in rail.Children.OfType<Button>().Where(b => b.Tag is "adv").ToList())
            {
                var label = b.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).FirstOrDefault(s => !string.IsNullOrWhiteSpace(s)) ?? "adv";
                CloseAll(); Call("ShowReader");
                b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Screen($"Bereich_{Safe(label)}", 2500);
            }
        // the settings: every area there is
        var settingsTab = (int)(typeof(MainWindow).GetField("SettingsTab", All)?.GetValue(null) ?? -1);
        if (settingsTab >= 0)
            for (var area = 0; area < 8; area++)
            {
                CloseAll(); Call("ShowReader"); Call("RevealSideTab", settingsTab); Call("ShowSettingsArea", (int?)area);
                Screen($"settings_{area}");
            }
        // a document in the editor
        CloseAll(); Call("ShowReader");
        var knowledge = (int)typeof(MainWindow).GetField("KnowledgeTab", All)!.GetValue(null)!;
        Call("RevealSideTab", knowledge);
        var doc = new BibleStudy.Core.Storage.Document(0, "Ein ziemlich langer Titel für ein Dokument zum Ausprobieren", "Text mit Joh 3,16\n\n" + string.Concat(Enumerable.Repeat("Ein Absatz zum Ausprobieren, damit der Editor voll ist. ", 40)), null, false, DateTime.Now, []);
        Call("OpenDocument", doc); Screen("doc_editor");
        CloseAll(); Call("ShowReader"); Call("RevealSideTab", (int)typeof(MainWindow).GetField("ExplorerTab", All)!.GetValue(null)!); Call("SetExPick", true, null); Screen("explorer_pick");
        Call("SetExPick", false, null);
        CloseAll();
    }

    // ---- a finger: raw touch input as the phone sends it (down, moves, up), measured against what the app does ------------------
    try
    {
        var impl = typeof(TopLevel).GetProperty("PlatformImpl", All)!.GetValue(win)!;
        var input = (Action<RawInputEventArgs>)impl.GetType().GetProperty("Input", All)!.GetValue(impl)!;
        var touch = (TouchDevice)Activator.CreateInstance(typeof(TouchDevice), nonPublic: true)!;
        var ctor = typeof(RawTouchEventArgs).GetConstructors(All).First(c => c.GetParameters().Length == 7 && c.GetParameters()[4].ParameterType == typeof(Point));
        object root = win is IInputRoot ? win : (typeof(TopLevel).GetProperties(All).FirstOrDefault(pp => typeof(IInputRoot).IsAssignableFrom(pp.PropertyType))?.GetValue(win) ?? win);
        long id = 1;
        void T(RawPointerEventType type, Point p)
        {
            input((RawInputEventArgs)ctor.Invoke([touch, (ulong)Environment.TickCount64, root, type, p, RawInputModifiers.None, id]));
            Pump(3);
        }
        void Tap(Point p) { T(RawPointerEventType.TouchBegin, p); Settle(80); T(RawPointerEventType.TouchEnd, p); Settle(300); id++; }
        void Drag(Point from, double dy, int holdAfterMs)
        {
            T(RawPointerEventType.TouchBegin, from);
            for (var i = 1; i <= 10; i++) { T(RawPointerEventType.TouchUpdate, new Point(from.X, from.Y + dy * i / 10)); Settle(16); }
            Settle(holdAfterMs);   // the finger rests after the scroll
            T(RawPointerEventType.TouchEnd, new Point(from.X, from.Y + dy)); Settle(300); id++;
        }
        int Picked() => ((IList)Get("_exSel")!).Count;
        bool Picking() => (bool)Get("_exPick")!;
        bool MenuOpen() => openPopups.Any(p => p.IsOpen) || win.GetVisualDescendants().OfType<ContextMenu>().Any(c => c.IsOpen);
        List<Point> Rows()
        {
            var list = (Panel)Get("_exList")!;
            return list.Children.Where(c => c.IsEffectivelyVisible && c.Bounds.Height > 20 && c.TranslatePoint(default, win) is { } p && p.Y > 60 && p.Y + c.Bounds.Height < h - 80)
                .Select(c => c.TranslatePoint(new Point(c.Bounds.Width / 2, c.Bounds.Height / 2), win)!.Value).ToList();
        }
        void Check(string what, bool ok, string detail) => report.Add($"  TOUCH {(ok ? "PASS" : "FAIL")}  {what}  ({detail})");
        void OpenExplorer() { CloseAll(); Call("ShowReader"); Call("RevealSideTab", (int)typeof(MainWindow).GetField("ExplorerTab", All)!.GetValue(null)!); Settle(1200); }

        report.Add($"== {w}x{h}_touch");
        OpenExplorer();
        var rows = Rows();
        report.Add($"  (explorer rows on screen: {rows.Count})");
        if (rows.Count >= 3)
        {
            // 1. scrolling with a finger that rests on a row at the end: no menu, no choosing
            Drag(rows[^1], -(rows[^1].Y - rows[0].Y), 1000);
            Check("Explorer: wischen und danach 1 s halten öffnet kein Menü", !MenuOpen(), MenuOpen() ? "Menü ist offen" : "kein Menü");
            Check("Explorer: wischen startet keinen Auswahlmodus", !Picking() && Picked() <= 1, $"Auswahlmodus={Picking()}, gewählt={Picked()}");
            CloseAll();
            // 2. choosing: each tap ticks exactly one row; a scroll ticks nothing
            OpenExplorer(); Call("SetExPick", true, null); Settle(400);
            foreach (var s in win.GetVisualDescendants().OfType<ScrollViewer>().Where(s => s.IsEffectivelyVisible)) s.Offset = default;
            Settle(300);
            var list0 = (Panel)Get("_exList")!;
            var tickable = list0.Children.Where(c => c.IsEffectivelyVisible && c.Bounds.Height > 20 && c.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "✓") && !c.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text?.Contains("(0)") == true) && c.TranslatePoint(default, win) is { } p && p.Y > 60 && p.Y + c.Bounds.Height < h - 200).ToList();
            Point Mid(Control c) => c.TranslatePoint(new Point(c.Bounds.Width * 0.6, c.Bounds.Height / 2), win)!.Value;
            var before = Picked();
            if (tickable.Count >= 2)
            {
                var a = tickable[0]; var b = tickable[1];
                Tap(Mid(a));
                Check("Auswahlmodus: 1 Tippen hakt genau 1 Eintrag an", Picked() == before + 1, $"vorher {before}, nachher {Picked()}");
                Tap(Mid(b));
                Check("Auswahlmodus: 2. Tippen hakt genau 1 weiteren an", Picked() == before + 2, $"jetzt {Picked()}");
                Tap(Mid(a));
                Check("Auswahlmodus: nochmal Tippen auf A hakt A wieder ab", Picked() == before + 1, $"jetzt {Picked()}");
            }
            else Check("Auswahlmodus: anhakbare Einträge gefunden", false, $"{tickable.Count}");
            rows = Rows();
            var beforeScroll = Picked();
            Drag(rows[^1], -(rows[^1].Y - rows[0].Y), 0);
            Check("Auswahlmodus: wischen hakt nichts an/ab", Picked() == beforeScroll, $"vorher {beforeScroll}, nachher {Picked()}");
            Call("SetExPick", false, null); CloseAll();
            // 3. a tap (not choosing) opens the row and chooses nothing
            OpenExplorer(); rows = Rows();
            Tap(rows[0]);
            Check("Explorer: Tippen (ohne Auswahlmodus) startet keinen Auswahlmodus", !Picking(), $"Auswahlmodus={Picking()}");
            CloseAll();
        }
        else Check("Explorer: genug Einträge für den Test", false, $"{rows.Count} Zeilen sichtbar");

        // the reader: a tap selects one verse, a scroll selects none
        CloseAll(); Call("ShowReader"); Settle(800);
        var verses = win.GetVisualDescendants().OfType<SelectableTextBlock>().Where(t => t.IsEffectivelyVisible && t.TranslatePoint(default, win) is { } p && p.Y > 80 && p.Y < h - 120 && p.X >= 0)
            .Select(t => t.TranslatePoint(new Point(Math.Min(60, t.Bounds.Width / 2), t.Bounds.Height / 2), win)!.Value).ToList();
        if (verses.Count >= 3)
        {
            Quiet("SelectVerse", 0);
            var sel0 = Get("_selected");
            Drag(verses[^1], -(verses[^1].Y - verses[0].Y), 1000);
            var sel1 = Get("_selected");
            Check("Lesen: wischen + halten wählt keinen Vers", Equals(sel0, sel1) && !MenuOpen(), $"vorher {sel0 ?? "-"}, nachher {sel1 ?? "-"}, Menü {MenuOpen()}");
            CloseAll();
            Tap(verses[1]);
            Check("Lesen: Tippen wählt einen Vers", Get("_selected") is int, $"gewählt {Get("_selected") ?? "-"}");
            CloseAll();
        }
        else Check("Lesen: Verse gefunden", false, $"{verses.Count}");
    }
    catch (Exception e) { report.Add($"  ! touch: {(e.InnerException ?? e).GetType().Name}: {(e.InnerException ?? e).Message}"); }

    win.Close();
    Pump(10);
}
File.WriteAllLines(Path.Combine(shots, "report.txt"), report);
Console.WriteLine($"{report.Count} lines, {report.Count(l => l.Contains("TOUCH FAIL"))} touch failures");
return report.Any(l => l.Contains("TOUCH FAIL")) ? 1 : 0;

static string Safe(string s) => new string(s.Where(char.IsLetterOrDigit).ToArray());

/// <summary>What reaches out of the screen sideways, text cut off without "…", and tap targets smaller than 32 px.</summary>
static IEnumerable<string> Scan(TopLevel win, double width, double height, bool popup = false)
{
    var seen = 0; var small = 0;
    foreach (var v in win.GetVisualDescendants().OfType<Control>())
    {
        if (!v.IsEffectivelyVisible || v.Bounds.Width <= 0 || v.Bounds.Height <= 0) continue;
        if (v.TranslatePoint(default, win) is not { } tl) continue;
        if (tl.Y > win.Bounds.Height || tl.Y + v.Bounds.Height < 0) continue;
        if (!popup && v.GetVisualAncestors().OfType<Popup>().Any()) continue;
        var sideways = v.GetVisualAncestors().Any(a => a is ScrollViewer { HorizontalScrollBarVisibility: not ScrollBarVisibility.Disabled } sv && sv.Extent.Width > sv.Viewport.Width + 1
            || a is ScrollContentPresenter { CanHorizontallyScroll: true });
        var right = tl.X + v.Bounds.Width;
        string What() => v.GetType().Name + (v.Name is { Length: > 0 } nm ? "#" + nm : "") + (v is TextBlock tb ? " \"" + Cut(tb.Text ?? "") + "\"" : v is ContentControl { Content: string c } ? " \"" + Cut(c) + "\"" : v is ContentControl { Content: Control cc } && cc.GetVisualDescendants().Prepend(cc).OfType<TextBlock>().FirstOrDefault()?.Text is { Length: > 0 } inner ? " \"" + Cut(inner) + "\"" : "");
        if (!sideways && (right > width + 1.5 || tl.X < -1.5) && v is TextBlock or Button or TextBox or ComboBox or Border { Child: not null } or Image)
        {
            if (seen++ < 60) yield return $"OUT  {What()}  x={tl.X:0}..{right:0} (screen {width:0})";
        }
        else if (v is TextBlock t && t.TextWrapping == TextWrapping.NoWrap && t.TextTrimming == TextTrimming.None && !sideways && t.Text is { Length: > 0 }
                 && t.DesiredSize.Width > t.Bounds.Width + 2)
        {
            if (seen++ < 60) yield return $"CUT  {What()}  needs {t.DesiredSize.Width:0}, has {t.Bounds.Width:0}";
        }
        if (v is Button or ToggleButton or CheckBox or ComboBox or TextBox && tl.Y >= 0 && tl.Y + v.Bounds.Height <= height && (v.Bounds.Height < 32 || v.Bounds.Width < 32) && v.IsHitTestVisible
            && !v.GetVisualAncestors().OfType<ScrollBar>().Any() && v is not RepeatButton)
        {
            if (small++ < 25) yield return $"SMALL {What()}  {v.Bounds.Width:0}x{v.Bounds.Height:0} at y={tl.Y:0}";
        }
    }
    static string Cut(string s) => s.Length > 40 ? s[..40] + "…" : s;
}
