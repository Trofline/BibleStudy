using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using BibleStudy.App;
using BibleStudy.Core.Storage;

// Drives the REAL main window in a window that is not on the screen: real pointer and key events, its own data folder.
var bin = AppContext.BaseDirectory;
File.WriteAllText(Path.Combine(bin, "portable.flag"), "");
var data = Path.Combine(bin, "Data");
if (Directory.Exists(data)) Directory.Delete(data, true);
var shots = args.Length > 0 ? args[0] : Path.Combine(bin, "shots");
Directory.CreateDirectory(shots);

AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).WithInterFont().SetupWithoutStarting();

Popup? lastPopup = null;
var openPopups = new HashSet<Popup>();
Popup.IsOpenProperty.Changed.AddClassHandler<Popup>((p, e) => { if (e.NewValue is true) { lastPopup = p; openPopups.Add(p); } else openPopups.Remove(p); });
TopLevel? OpenPopup() => lastPopup is { IsOpen: true, Child: { } child } ? TopLevel.GetTopLevel(child) : null;
var fails = 0;
void Check(string name, bool ok, string detail = "") { Console.WriteLine((ok ? "PASS " : "FAIL ") + name + (ok ? "" : "  -> " + detail)); if (!ok) fails++; }

Environment.SetEnvironmentVariable("BIBLESTUDY_BACKUPDIR", Path.Combine(Path.GetTempPath(), "bs_test_backup")); Environment.SetEnvironmentVariable("BIBLESTUDY_EXPORTDIR", Path.Combine(Path.GetTempPath(), "bs_test_export"));
MainWindow.NoShell = true;   // never open Explorer or a browser on the screen of whoever runs this
var win = new MainWindow { Width = 1600, Height = 1000 };
win.Show();
void Pump(int n = 6) { for (var i = 0; i < n; i++) { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs(); } }
Pump(20);
// the start loads the list of translations in the background and then shows the reader: wait for that, as a user has by the time they write
var startWait = DateTime.Now;
while ((DateTime.Now - startWait).TotalSeconds < 40)
{
    Pump(2); Thread.Sleep(100);
    var st = win.FindControl<TextBlock>("StatusText")?.Text ?? "";
    if ((DateTime.Now - startWait).TotalSeconds > 4 && !st.StartsWith("Lade")) break;
}
for (var i = 0; i < 30; i++) { Pump(2); Thread.Sleep(100); }

const BindingFlags All = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static;
T F<T>(string name) where T : class => (T)(typeof(MainWindow).GetField(name, All)?.GetValue(win) ?? typeof(MainWindow).GetProperty(name, All)!.GetValue(win))!;
object? Call(string name, params object?[] a) => typeof(MainWindow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static)!.Invoke(win, a);
T C<T>(string name) where T : Control => win.FindControl<T>(name)!;
void Shot(TopLevel t, string name) { Pump(); using var f = t.CaptureRenderedFrame(); f?.Save(Path.Combine(shots, name + ".png")); }
Point Center(Control c, TopLevel t) => c.TranslatePoint(new Point(c.Bounds.Width / 2, c.Bounds.Height / 2), t)!.Value;
void Click(Control c)
{
    var t = TopLevel.GetTopLevel(c)!; var p = Center(c, t);
    t.MouseMove(p); t.MouseDown(p, MouseButton.Left); Pump(2); t.MouseUp(p, MouseButton.Left); Pump(4);
}
void Press(Key key, RawInputModifiers m)
{
    var t = TopLevel.GetTopLevel(C<TextBox>("DocBodyBox"))!;
    t.KeyPress(key, m, PhysicalKey.None, null); t.KeyRelease(key, m, PhysicalKey.None, null); Pump(3);
}
Button Rib(string tipStart) => C<WrapPanel>("DocRibbon").Children.OfType<Button>().First(b => (ToolTip.GetTip(b) as string ?? "").StartsWith(tipStart));

// opens a question window by a call and waits for it (a notice of the first start is closed before)
Window Dlg(Func<object?> start, out object? task)
{
    foreach (var w0 in win.OwnedWindows.ToList()) w0.Close();
    Settle(300);
    task = start();
    Window? d = null; var t0 = DateTime.Now;
    while (d is null && (DateTime.Now - t0).TotalSeconds < 10) { Pump(2); Thread.Sleep(80); d = win.OwnedWindows.FirstOrDefault(w => w.IsVisible); }
    if (d is null) throw new InvalidOperationException("no window came up");
    return d;
}
void Press2(Button b) { b.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); Settle(500); }
List<Button> Plain(Window d) => d.GetLogicalDescendants().OfType<Button>().Where(b => b is not Avalonia.Controls.Primitives.ToggleButton).ToList();
// a real mouse click on a piece of text
void ClickRun(SelectableTextBlock tb, Avalonia.Controls.Documents.Run target)
{
    var idx = 0;
    foreach (var i in tb.Inlines!) { if (ReferenceEquals(i, target)) break; idx += ((Avalonia.Controls.Documents.Run)i).Text!.Length; }
    var r = tb.TextLayout.HitTestTextPosition(idx);
    // where the glyph is drawn: a raised small sign does not fill its line
    var p = tb.TranslatePoint(new Point(r.X + 2, r.Y + r.Height / 2), win)!.Value;
    for (var y = 1.0; y < r.Height; y += 1)
    {
        var q = tb.TranslatePoint(new Point(r.X + 2, r.Y + y), win)!.Value;
        if (ReferenceEquals(win.InputHitTest(q), tb)) { p = q; break; }
    }
    win.MouseMove(p); win.MouseDown(p, MouseButton.Left); Pump(2); win.MouseUp(p, MouseButton.Left); Pump(6);
}
SelectableTextBlock TbOf(int verse) => (SelectableTextBlock)((System.Collections.IEnumerable)F<object>("_cells")).Cast<object>().Select(c => (Data: c.GetType().GetProperty("Data")!.GetValue(c)!, Tb: c.GetType().GetProperty("Tb")!.GetValue(c)!)).First(c => (int)c.Data.GetType().GetProperty("Number")!.GetValue(c.Data)! == verse).Tb;

void Settle(int ms = 400) { for (var i = 0; i < 6; i++) { Pump(4); Thread.Sleep(ms / 6); } }
void Wait(Func<bool> done, int seconds = 25) { var s = DateTime.Now; while (!done() && (DateTime.Now - s).TotalSeconds < seconds) { Pump(2); Thread.Sleep(80); } Pump(8); Thread.Sleep(100); Pump(8); }
void RunTask(string method, params object?[] a) { var t = (Task)typeof(MainWindow).GetMethods(All).First(m => m.Name == method && m.GetParameters().Length == a.Length).Invoke(win, a)!; Wait(() => t.IsCompleted); }
RunTask("GoToAsync", "JHN", 3, (int?)16);
Settle(800);
var local = F<BibleStudy.Core.Import.LocalBibleStore>("_local");
var vs = Enumerable.Range(1, 12).Select(n => new BibleStudy.Core.Import.ParsedVerse("GEN", 1, n, [new BibleStudy.Core.Models.Segment($"Dies ist der Vers {n} der Testbibel und er hat genug Text, damit er eine Zeile füllt.")])).ToList();
var tid = local.Save(new BibleStudy.Core.Import.ParsedBible("Testbibel mit Überschriften", "TMÜ", "de", vs, [], [("GEN", 1, 1, "Der Anfang der Welt"), ("GEN", 1, 6, "Der zweite Tag"), ("GEN", 1, 9, "Der dritte Tag")]));
RunTask("ReloadTranslationsAsync", tid);
Settle(800);
var tb = C<ComboBox>("TranslationBox");
Console.WriteLine("selected: " + (tb.SelectedItem as BibleStudy.Core.Models.TranslationInfo)?.Id + " wanted " + tid);
if ((tb.SelectedItem as BibleStudy.Core.Models.TranslationInfo)?.Id != tid) tb.SelectedItem = tb.Items.OfType<BibleStudy.Core.Models.TranslationInfo>().First(x => x.Id == tid);
Settle(600);
RunTask("GoToAsync", "GEN", 1, (int?)1);
Settle(900);
var panel = C<StackPanel>("ReaderPanel");
var heads = panel.Children.OfType<Border>().Select(b => b.Child as TextBlock).Where(t => t is not null && t.FontSize == 19).Select(t => t!.Text).ToList();
Console.WriteLine("headings shown: " + string.Join(" | ", heads));
Check("three imported headings shown", heads.Count == 3, heads.Count.ToString());
Check("heading text", heads.Contains("Der zweite Tag"));
// order: heading sits in front of its verse
var order = panel.Children.Select((c, i) => (c, i)).ToList();
var iHead = order.First(x => x.c is Border { Child: TextBlock { Text: "Der zweite Tag" } }).i;
var iVerse6 = order.First(x => x.c == F<System.Collections.IDictionary>("_verseBoxes")[6]).i;
Check("heading in front of verse 6", iHead == iVerse6 - 1, $"{iHead} vs {iVerse6}");
Shot(win, "imported-headings");
// heading modes: own heading before verse 6 collides with the translation's
var v0 = F<BibleStudy.Core.Storage.VaultStore>("_vault");
v0.AddHeading(new BibleStudy.Core.Models.VerseRef("GEN", 1, 6), "Mein Titel", 2);
RunTask("GoToAsync", "GEN", 1, (int?)1); Settle(900);
List<string> Titles() => C<StackPanel>("ReaderPanel").Children.OfType<Border>().Select(b => b.Child is StackPanel sp ? sp.Children.OfType<DockPanel>().SelectMany(d => d.Children.OfType<TextBlock>()).FirstOrDefault() : b.Child as TextBlock).Where(t => t is not null && (t.FontSize == 19 || t.FontSize == 21)).Select(t => t!.Text).ToList();
var hb = C<ComboBox>("HeadingModeBox");
foreach (var (idx, expect) in new[] { (0, 4), (1, 3), (2, 1), (3, 3), (4, 0) })
{
    hb.SelectedIndex = idx; Settle(900);
    var t = Titles();
    Check($"heading mode {idx}: {expect} shown", t.Count == expect, string.Join("|", t));
}
hb.SelectedIndex = 1; Settle(900);
Check("smart: mine instead of translation at verse 6", Titles().Contains("Mein Titel") && !Titles().Contains("Der zweite Tag"));
hb.SelectedIndex = 0; Settle(600);
// keys: digits jump, Page down scrolls
var scroll = C<ScrollViewer>("ReaderScroll");
win.Height = 520; Settle(500); scroll.Offset = new Vector(0, 0); Settle(300);
var t2 = TopLevel.GetTopLevel(win)!;
win.Focus(); Settle(200);
t2.KeyPress(Key.PageDown, RawInputModifiers.None, PhysicalKey.None, null); t2.KeyRelease(Key.PageDown, RawInputModifiers.None, PhysicalKey.None, null); Settle(400);
Console.WriteLine("scroll after PageDown: " + scroll.Offset.Y + " viewport " + scroll.Viewport.Height + " extent " + scroll.Extent.Height);
Check("page down scrolls", scroll.Offset.Y > 100, scroll.Offset.Y.ToString());
t2.KeyPress(Key.PageUp, RawInputModifiers.None, PhysicalKey.None, null); t2.KeyRelease(Key.PageUp, RawInputModifiers.None, PhysicalKey.None, null); Settle(400);
Check("page up scrolls back", scroll.Offset.Y < 5, scroll.Offset.Y.ToString());
t2.KeyPress(Key.D9, RawInputModifiers.None, PhysicalKey.None, null); t2.KeyRelease(Key.D9, RawInputModifiers.None, PhysicalKey.None, null); Settle(700);
Console.WriteLine("after typing 9: status=" + C<TextBlock>("StatusText").Text + " offset=" + scroll.Offset.Y);
Check("digit jumps to verse 9", (C<TextBlock>("StatusText").Text ?? "").Contains("9") && scroll.Offset.Y > 50, scroll.Offset.Y.ToString());
Thread.Sleep(1500);
t2.KeyPress(Key.D1, RawInputModifiers.None, PhysicalKey.None, null); t2.KeyRelease(Key.D1, RawInputModifiers.None, PhysicalKey.None, null); Settle(200);
t2.KeyPress(Key.D1, RawInputModifiers.None, PhysicalKey.None, null); t2.KeyRelease(Key.D1, RawInputModifiers.None, PhysicalKey.None, null); Settle(600);
Check("two digits: verse 11", (C<TextBlock>("StatusText").Text ?? "").Contains("11"), C<TextBlock>("StatusText").Text ?? "");
Thread.Sleep(1200);
t2.KeyPress(Key.D1, RawInputModifiers.None, PhysicalKey.None, null); t2.KeyRelease(Key.D1, RawInputModifiers.None, PhysicalKey.None, null); Settle(100);
Thread.Sleep(1200);
t2.KeyPress(Key.D3, RawInputModifiers.None, PhysicalKey.None, null); t2.KeyRelease(Key.D3, RawInputModifiers.None, PhysicalKey.None, null); Settle(300);
Check("after the pause a new number starts", (C<TextBlock>("StatusText").Text ?? "").EndsWith(" 3"), C<TextBlock>("StatusText").Text ?? "");
Thread.Sleep(1200);
// walking through the reading list
var qVault = F<BibleStudy.Core.Storage.VaultStore>("_vault");
var qBook = (string)typeof(MainWindow).GetField("_curBook", All)!.GetValue(win)!; var qCh = (int)typeof(MainWindow).GetField("_curChapter", All)!.GetValue(win)!;
qVault.AddToQueue(new[] { 2, 5, 9 }.Select(v => (new BibleStudy.Core.Models.VerseRef(qBook, qCh, v), $"Probe {v}")));
Call("RefreshQueue"); Settle(200);
Check("no bar before the start", !C<Border>("QueueBar").IsVisible);
Press2(C<Button>("QueueStartBtn")); Settle(500);
int Selected() => (int)typeof(MainWindow).GetField("_selected", All)!.GetValue(win)!;
Check("start opens the bar at the first verse", C<Border>("QueueBar").IsVisible && C<TextBlock>("QueueBarCount").Text!.Contains("1/3") && Selected() == 2, C<TextBlock>("QueueBarCount").Text ?? "");
var barTop = C<Border>("QueueBar").TranslatePoint(new Point(0, 0), win)!.Value.Y; var readerBottom = scroll.TranslatePoint(new Point(0, scroll.Bounds.Height), win)!.Value.Y;
Check("the bar covers no text", readerBottom <= barTop + 0.5, $"{readerBottom} / {barTop}");
Check("the end button shows its whole word", C<Button>("QueueBarEndBtn").Bounds.Width >= 60, C<Button>("QueueBarEndBtn").Bounds.Width.ToString());
Shot(win, "queue-bar");
Click(C<Button>("QueueBarNextBtn")); Settle(500);
Check("next goes on (real click)", Selected() == 5 && C<TextBlock>("QueueBarCount").Text!.Contains("2/3"), Selected().ToString());
Check("nothing leaves the list during the walk", qVault.GetQueue().Count == 3, qVault.GetQueue().Count.ToString());
Click(C<Button>("QueueBarPrevBtn")); Settle(500);
Check("back returns", Selected() == 2 && C<TextBlock>("QueueBarCount").Text!.Contains("1/3"), Selected().ToString());
Check("the first has no way back", !C<Button>("QueueBarPrevBtn").IsEnabled);
Click(C<Button>("QueueBarEndBtn")); Settle(500);
Check("end takes the visited off, the rest stays", qVault.GetQueue().Select(q => q.Ref.Verse).SequenceEqual(new[] { 9 }), string.Join(",", qVault.GetQueue().Select(q => q.Ref.Verse)));
Check("end hides the bar", !C<Border>("QueueBar").IsVisible);
Press2(C<Button>("QueueStartBtn")); Settle(500);
Check("a single verse offers Done", (C<Button>("QueueBarNextBtn").Content as string) == "Fertig", C<Button>("QueueBarNextBtn").Content?.ToString() ?? "");
Click(C<Button>("QueueBarNextBtn")); Settle(500);
Check("done empties the list and ends", qVault.GetQueue().Count == 0 && !C<Border>("QueueBar").IsVisible);
// a link to several verses chooses all of them
var rangeBook = (string)typeof(MainWindow).GetField("_curBook", All)!.GetValue(win)!; var rangeCh = (int)typeof(MainWindow).GetField("_curChapter", All)!.GetValue(win)!;
var rangeTask = (Task)Call("GoToRangeAsync", new BibleStudy.Core.Models.VerseRef(rangeBook, rangeCh, 3), 6)!;
for (var i = 0; i < 40 && !rangeTask.IsCompleted; i++) { Pump(2); Thread.Sleep(50); }
Settle(300);
var chosen = ((HashSet<int>)typeof(MainWindow).GetField("_selVerses", All)!.GetValue(win)!).Order().ToList();
Check("a range link chooses every verse of it", chosen.SequenceEqual(new[] { 3, 4, 5, 6 }), string.Join(",", chosen));
Check("the side panel follows the first verse", Selected() == 3, Selected().ToString());
// the first start: no update question, and the setup counts only when it was finished
var firstSettings = F<AppSettings>("_settings"); var (tourBefore, setupBefore) = (firstSettings.TourPending, firstSettings.SetupDone);
// (only for a moment, without pumping: the program's own timer would otherwise start a setup of its own)
firstSettings.TourPending = true;
var askDuringFirstStart = (bool)Call("UpdateMayAsk", false)!; var askByHand = (bool)Call("UpdateMayAsk", true)!;
firstSettings.TourPending = false;
Check("no update question by itself during the first start", !askDuringFirstStart);
Check("looking for updates by hand still works", askByHand);
Check("updates are offered again afterwards", (bool)Call("UpdateMayAsk", false)! == true);
var setupWin = Dlg(() => { firstSettings.SetupDone = false; return Call("RunSetupWizardAsync"); }, out _);
Check("the open setup does not count as done", !firstSettings.SetupDone);
Press2(Plain(setupWin).First(b => (b.Content as string) == "Überspringen"));
Check("the closed setup counts as done", firstSettings.SetupDone);
(firstSettings.TourPending, firstSettings.SetupDone) = (tourBefore, setupBefore);
// typing in a text box must not jump
var box = C<TextBox>("SearchBox"); box.Focus(); Settle(200); var offBefore = scroll.Offset.Y;
t2.KeyPress(Key.D2, RawInputModifiers.None, PhysicalKey.None, null); t2.KeyRelease(Key.D2, RawInputModifiers.None, PhysicalKey.None, null); Settle(300);
Check("no jump while typing in a box", Math.Abs(scroll.Offset.Y - offBefore) < 1, $"{offBefore} -> {scroll.Offset.Y}");
// vault rename
var vault = typeof(MainWindow).GetField("_vault", All)!.GetValue(win)!; var vname = (string)vault.GetType().GetProperty("Name")!.GetValue(vault)!;
{ var rt = (Task)typeof(MainWindow).GetMethod("RenameVaultAsync", All)!.Invoke(win, [vname, "Umbenannt Test"])!; Wait(() => rt.IsCompleted); Console.WriteLine("rename task: " + rt.Status + " " + rt.Exception?.GetBaseException()); }
Settle(800);
vault = typeof(MainWindow).GetField("_vault", All)!.GetValue(win)!;
Console.WriteLine("VaultInfo: " + C<TextBlock>("VaultInfo").Text);
Check("vault renamed and reopened", (string)vault.GetType().GetProperty("Name")!.GetValue(vault)! == "Umbenannt Test");
Check("top bar shows the new name", C<TextBlock>("VaultTopText").Text == "Umbenannt Test", C<TextBlock>("VaultTopText").Text ?? "");
// ===== more checks of what was reported as unverified =====
var vNow = F<BibleStudy.Core.Storage.VaultStore>("_vault");
var settings = F<AppSettings>("_settings");
// A) sub-mark color change recolors existing sub-marks
vNow.AddMark(tid, new BibleStudy.Core.Models.VerseRef("GEN", 1, 2), -1, -1, "", "#FFD54F", "", null, []);
var subOld = (string)Call("SubMarkColor")!;
var subId = vNow.AddMark(tid, new BibleStudy.Core.Models.VerseRef("GEN", 1, 2), 5, 8, "ist", subOld, "", null, []);
Call("ChangeSubMarkColor", "#E53935"); Settle(600);
var recolored = vNow.GetMarks().First(m => m.Id == subId).Color;
Check("sub-mark recolored after color change", string.Equals(recolored, "#E53935", StringComparison.OrdinalIgnoreCase), recolored);
Check("whole-verse mark keeps its color", vNow.GetMarks().Any(m => m.WholeVerse && m.Color == "#FFD54F"));
// B) export -> import round trip: name and sub-mark color come back
var zipPath = Path.Combine(Path.GetTempPath(), "bs_roundtrip.zip"); File.Delete(zipPath);
Call("ExportToPath", zipPath);
Console.WriteLine("BackupInfo: " + C<TextBlock>("BackupInfo").Text);
Check("export zip written", File.Exists(zipPath));
using (var z = System.IO.Compression.ZipFile.OpenRead(zipPath))
{
    Check("zip has name.txt", z.GetEntry("name.txt") is not null);
    using var r = new StreamReader(z.GetEntry("name.txt")!.Open()); var n = r.ReadToEnd();
    Check("name.txt holds the vault name", n == "Umbenannt Test", n);
}
settings.SubMarkColor = "#00AA00"; // something else, the import must bring the exported color back
RunTask("ImportFromPathAsync", zipPath, false); Settle(900);
var imported = F<BibleStudy.Core.Storage.VaultStore>("_vault");
var impName = imported.Name; Console.WriteLine("imported as: " + impName);
Check("import keeps the exported name", impName.StartsWith("Umbenannt Test"), impName);
Check("import brings the sub-mark color back", string.Equals(F<AppSettings>("_settings").SubMarkColor, "#E53935", StringComparison.OrdinalIgnoreCase), F<AppSettings>("_settings").SubMarkColor);
Check("imported vault holds the sub-mark in its color", imported.GetMarks().Any(m => !m.WholeVerse && string.Equals(m.Color, "#E53935", StringComparison.OrdinalIgnoreCase)));
// B2) export all vaults -> import as guest and normally
RunTask("SwitchVaultAsync", "Zweit Test", false); Settle(600);
var allZip = Path.Combine(Path.GetTempPath(), "bs_all.zip"); File.Delete(allZip);
Call("ExportAllToPath", allZip);
Console.WriteLine("BackupInfo: " + C<TextBlock>("BackupInfo").Text);
var vaultsBefore = Directory.GetDirectories(Path.Combine(data, "vaults")).Length;
using (var z = System.IO.Compression.ZipFile.OpenRead(allZip))
{
    var dbs = z.Entries.Where(e => e.FullName.StartsWith("vaults/") && e.FullName.EndsWith("/vault.db")).Select(e => e.FullName).ToList();
    Console.WriteLine("bundle: " + string.Join(", ", dbs));
    Check("bundle holds all vaults", dbs.Count == vaultsBefore && dbs.Any(d => d.Contains("Zweit Test")) && dbs.Any(d => d.Contains("Umbenannt Test")), string.Join(",", dbs));
    Check("bundle holds no Bible texts", !z.Entries.Any(e => e.FullName.Contains("local.db") || e.FullName.Contains("translations")));
}
RunTask("ImportFromPathAsync", allZip, true); Settle(900);
var afterGuest = Directory.GetDirectories(Path.Combine(data, "vaults")).Select(Path.GetFileName).ToList();
Check("guest import added the vaults as guests", afterGuest.Count == vaultsBefore * 2 && afterGuest.Count(n => n!.Contains("(Gast)") || n.Contains("(guest)")) >= vaultsBefore, string.Join(",", afterGuest));
Check("guest vaults carry the flag", Directory.GetDirectories(Path.Combine(data, "vaults")).Count(d => File.Exists(Path.Combine(d, "gast.flag"))) == vaultsBefore);
RunTask("ImportFromPathAsync", allZip, false); Settle(900);
Check("normal import added copies", Directory.GetDirectories(Path.Combine(data, "vaults")).Length == vaultsBefore * 3, Directory.GetDirectories(Path.Combine(data, "vaults")).Length.ToString());
Console.WriteLine("BackupInfo: " + C<TextBlock>("BackupInfo").Text);
// B3) a look before the import; work done as a guest goes home
var look = ((string Text, bool IsGuestWork, bool SameName))Call("ImportPreview", allZip)!;
Console.WriteLine("preview: " + look.Text.Replace("\n", " | "));
Check("preview names the vaults", look.Text.Contains("Zweit Test") && look.Text.Contains("Umbenannt Test"), look.Text);
Check("nothing changed in the guests yet", ((List<string>)Call("ChangedGuestVaults")!).Count == 0, string.Join(",", (List<string>)Call("ChangedGuestVaults")!));
var vaultsDir = Path.Combine(data, "vaults");
var guestName = Directory.GetDirectories(vaultsDir).Where(d => File.Exists(Path.Combine(d, "gast.flag"))).Select(d => Path.GetFileName(d)!).First(n => n.StartsWith("Zweit Test"));
((VaultStore)Call("StoreOf", guestName)!).SetNote(new BibleStudy.Core.Models.VerseRef("GEN", 1, 7), "Vom Gast geschrieben");
var changedGuests = (List<string>)Call("ChangedGuestVaults")!;
Check("the changed guest vault is noticed", changedGuests.SequenceEqual(new[] { guestName }), string.Join(",", changedGuests));
var workZip = Path.Combine(Path.GetTempPath(), "bs_guestwork.zip"); File.Delete(workZip);
Call("SaveGuestWorkTo", workZip, changedGuests);
using (var z = System.IO.Compression.ZipFile.OpenRead(workZip))
    Check("guest work has its home name", z.GetEntry("vaults/Zweit Test/vault.db") is not null && z.GetEntry("guest-work.txt") is not null, string.Join(",", z.Entries.Select(e => e.FullName)));
Check("preview knows guest work", (((string Text, bool IsGuestWork, bool SameName))Call("ImportPreview", workZip)!).IsGuestWork);
RunTask("MergeGuestWorkAsync", workZip); Settle(900);
Console.WriteLine("BackupInfo: " + C<TextBlock>("BackupInfo").Text);
Check("guest note arrived at home", ((VaultStore)Call("StoreOf", "Zweit Test")!).GetNotes().Any(n => n.Text == "Vom Gast geschrieben"));
Check("merge made a backup in the test folder only", Directory.GetFiles(BibleStudy.Core.AppPaths.BackupDir, "*Einmischen*").Length + Directory.GetFiles(BibleStudy.Core.AppPaths.BackupDir, "*merging*").Length > 0);
// B4) both sides differ, and something was deleted as a guest: the review, then undo
{
    var home = (VaultStore)Call("StoreOf", "Zweit Test")!;
    var round = Path.Combine(Path.GetTempPath(), "bs_round2.zip"); File.Delete(round);
    Call("ExportAllToPath", round);
    var before = Directory.GetDirectories(vaultsDir).Select(Path.GetFileName).ToHashSet();
    RunTask("ImportFromPathAsync", round, true); Settle(900);
    var g2 = Directory.GetDirectories(vaultsDir).Select(Path.GetFileName).Where(n => !before.Contains(n)).First(n => File.ReadAllText(Path.Combine(vaultsDir, n!, "gast.origin")) == "Zweit Test")!;
    var gs = (VaultStore)Call("StoreOf", g2)!;
    gs.SetNote(new BibleStudy.Core.Models.VerseRef("GEN", 1, 7), "");
    gs.SetNote(new BibleStudy.Core.Models.VerseRef("GEN", 1, 8), "Gastfassung");
    home.SetNote(new BibleStudy.Core.Models.VerseRef("GEN", 1, 8), "Heimfassung");
    var w2 = Path.Combine(Path.GetTempPath(), "bs_work2.zip"); File.Delete(w2);
    Call("SaveGuestWorkTo", w2, new List<string> { g2 });
    RunTask("MergeGuestWorkAsync", w2); Settle(900);
    Check("undo is offered after a merge", C<Button>("MergeUndoBtn").IsVisible);
    var review = Dlg(() => Call("ReviewMergeAsync"), out var rt);
    var combo = review.GetLogicalDescendants().OfType<ComboBox>().Single(); combo.SelectedIndex = 1;
    var del = review.GetLogicalDescendants().OfType<CheckBox>().Single();
    Check("what was deleted as a guest is listed, not ticked", del.IsChecked == false);
    del.IsChecked = true; Pump(4);
    Press2(Plain(review)[0]);
    await (Task)rt!;
    var notes = home.GetNotes();
    Check("their text was chosen", notes.Single(n => n.Ref.Verse == 8).Text == "Gastfassung", notes.Single(n => n.Ref.Verse == 8).Text);
    Check("the deletion was applied", notes.All(n => n.Ref.Verse != 7));
    RunTask("UndoMergeCoreAsync"); Settle(900);
    notes = ((VaultStore)Call("StoreOf", "Zweit Test")!).GetNotes();
    Check("undo brings the old state back", notes.Single(n => n.Ref.Verse == 8).Text == "Heimfassung" && notes.Any(n => n.Ref.Verse == 7));
    Check("undo is gone after use", !C<Button>("MergeUndoBtn").IsVisible);
    // a normal export can be mixed in too
    Check("same name is noticed", (((string Text, bool IsGuestWork, bool SameName))Call("ImportPreview", round)!).SameName);
    RunTask("MergeGuestWorkAsync", zipPath); Settle(900);
    Check("a file of one vault can be mixed in", (C<TextBlock>("BackupInfo").Text ?? "").StartsWith("Eingemischt"), C<TextBlock>("BackupInfo").Text ?? "");
}
// C) import check report: faulty USFM, then button
var bsl = "\\";
var uf = Path.Combine(Path.GetTempPath(), "bs_faulty.usfm");
var ul = new List<string> { bsl + "id GEN", bsl + "c 1" };
for (var i = 1; i <= 14; i++) ul.Add($"{bsl}v {i} Der H sprach , und G{1000 + i} NA/G2{i} TR es ward gin g en.");
File.WriteAllLines(uf, ul);
RunTask("ImportBibleFromAsync", uf, "de"); Settle(900);
Console.WriteLine("ImportBibleInfo: " + C<TextBlock>("ImportBibleInfo").Text);
var cbtn = C<Button>("ImportCheckBtn");
Check("check button visible after import", cbtn.IsVisible);
Check("info mentions suspicious places", (C<TextBlock>("ImportBibleInfo").Text ?? "").Contains("auffällige") || (C<TextBlock>("ImportBibleInfo").Text ?? "").Contains("suspicious"), C<TextBlock>("ImportBibleInfo").Text ?? "");
var expectFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "BibleStudy", "Export", "bs_faulty-import-check.md");
cbtn.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); Settle(500);
var reportFiles = Directory.GetFiles(Path.Combine(Path.GetTempPath(), "bs_test_export"), "*import-check.md");
Check("report file saved by the button", reportFiles.Length > 0, string.Join(",", reportFiles));
if (reportFiles.Length > 0) { var md = File.ReadAllText(reportFiles.OrderByDescending(File.GetLastWriteTime).First()); Check("report lists leftover-marker and USFM task", md.Contains("leftover-marker") && md.Contains("USFM")); foreach (var f in reportFiles) File.Delete(f); }
Shot(win, "import-check");
// D) marks follow the translation (no Strong numbers: position guess)
var local2 = F<BibleStudy.Core.Import.LocalBibleStore>("_local");
var vsB = Enumerable.Range(1, 12).Select(n => new BibleStudy.Core.Import.ParsedVerse("GEN", 1, n, [new BibleStudy.Core.Models.Segment($"Das ist der Vers {n} der anderen Bibel und er besitzt genug Text, damit er eine Zeile füllt.")])).ToList();
var tidB = local2.Save(new BibleStudy.Core.Import.ParsedBible("Andere Testbibel", "ATB", "de", vsB, []));
RunTask("ReloadTranslationsAsync", tidB); Settle(800);
var vFinal = F<BibleStudy.Core.Storage.VaultStore>("_vault");
var textA = $"Dies ist der Vers 3 der Testbibel und er hat genug Text, damit er eine Zeile füllt.";
var sIdx = textA.IndexOf("Testbibel");
vFinal.AddMark(tid, new BibleStudy.Core.Models.VerseRef("GEN", 1, 3), sIdx, sIdx + 9, "Testbibel", "#43A047", "", null, []);
settings.ShareMarksAcrossTranslations = true;
var tbx = C<ComboBox>("TranslationBox");
tbx.SelectedItem = tbx.Items.OfType<BibleStudy.Core.Models.TranslationInfo>().First(x => x.Id == tidB); Settle(700);
RunTask("GoToAsync", "GEN", 1, (int?)1); Settle(1200);
var boxes = F<System.Collections.IDictionary>("_verseBoxes");
IEnumerable<Avalonia.Controls.Documents.Run> RunsOf(Control c) => c.GetVisualDescendants().OfType<TextBlock>().SelectMany(t => t.Inlines ?? []).OfType<Avalonia.Controls.Documents.Run>();
var coloredB = RunsOf((Control)boxes[3]!).Where(r => r.Background is not null).Select(r => r.Text).ToList();
Console.WriteLine("colored runs in B verse 3: " + string.Join("|", coloredB));
Check("mark from translation A shows in translation B", coloredB.Count > 0, "none colored");
Check("a guessed place is underlined", RunsOf((Control)boxes[3]!).Any(r => r.Background is not null && r.TextDecorations is not null));
// the place is corrected in the real window: first and last word clicked
{
    foreach (var w0 in win.OwnedWindows.ToList()) w0.Close(); Settle(400);   // the notice about the data sources of the first start
    var guessed = vFinal.GetMarks("GEN", 1).First(m => m.Text == "Testbibel" && m.TranslationId == tid);
    var cell = ((System.Collections.IEnumerable)F<object>("_cells")).Cast<object>().First(c => { var dd = c.GetType().GetProperty("Data")!.GetValue(c)!; return (int)dd.GetType().GetProperty("Number")!.GetValue(dd)! == 3; });
    var cellData = cell.GetType().GetProperty("Data")!.GetValue(cell)!;
    var vData = (BibleStudy.Core.Models.Verse)cellData.GetType().GetProperty("V")!.GetValue(cellData)!;
    var correcting = (Task)typeof(MainWindow).GetMethod("CorrectPlaceAsync", All)!.Invoke(win, [guessed, cellData, BibleStudy.Core.Storage.MarkLogic.RawText(vData.Segments), 0, 5])!;
    Window? fix = null; var fw = DateTime.Now;
    while (fix is null && (DateTime.Now - fw).TotalSeconds < 10) { Pump(2); Thread.Sleep(80); fix = win.OwnedWindows.FirstOrDefault(w => w.IsVisible); }
    Check("the correction window opens", fix is not null);
    var chips = fix!.GetLogicalDescendants().OfType<Avalonia.Controls.Primitives.ToggleButton>().ToList();
    foreach (var k in new[] { 1, 2 }) chips[k].RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
    Pump(4);
    fix.GetLogicalDescendants().OfType<Button>().First(b => b is not Avalonia.Controls.Primitives.ToggleButton).RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
    Settle(900);
    var fixedRuns = RunsOf((Control)F<System.Collections.IDictionary>("_verseBoxes")[3]!).Where(r => r.Background is not null).ToList();
    Console.WriteLine("after correction: " + string.Join("|", fixedRuns.Select(r => r.Text)));
    Check("the corrected place is used and no longer a guess", fixedRuns.Count == 1 && fixedRuns[0].Text == "ist der" && fixedRuns[0].TextDecorations is null, string.Join("|", fixedRuns.Select(r => r.Text)));
}
Shot(win, "marks-follow");
tbx.SelectedItem = tbx.Items.OfType<BibleStudy.Core.Models.TranslationInfo>().First(x => x.Id == tid); Settle(700);
RunTask("GoToAsync", "GEN", 1, (int?)1); Settle(1000);
var coloredA = RunsOf((Control)F<System.Collections.IDictionary>("_verseBoxes")[3]!).Where(r => r.Background is not null).Select(r => r.Text).ToList();
Console.WriteLine("colored runs in A verse 3: " + string.Join("|", coloredA));
Check("mark still shows in the translation it was made in", coloredA.Count > 0);
// choosing what goes along, with a real window
{
    var d = Dlg(() => Call("ChooseVaultsAsync"), out var t);
    var boxes2 = d.GetLogicalDescendants().OfType<CheckBox>().ToList();
    var bibleBox = boxes2.First(b => b.Content as string == "bs_faulty");
    Check("bibles are not ticked at first", bibleBox.IsChecked == false);
    bibleBox.IsChecked = true; boxes2[0].IsChecked = false; Pump(4);
    Press2(Plain(d)[0]);
    var boxed = t!.GetType().GetProperty("Result")!.GetValue(t)!;
    var (cv, cb) = ((List<string>, List<string>))boxed;
    Check("the ticks count", !cv.Contains((string)boxes2[0].Content!) && cb.Count == 1, string.Join(",", cv) + " / " + string.Join(",", cb));
    var withB = Path.Combine(Path.GetTempPath(), "bs_bible.zip"); File.Delete(withB);
    Call("WriteVaultsZip", withB, new List<(string, string)> { ("Zweit Test", "Zweit Test") }, false, cb);
    using (var z = System.IO.Compression.ZipFile.OpenRead(withB)) Check("the bible is in the file", z.GetEntry("bibles.db") is not null);
    var pv = ((string Text, bool IsGuestWork, bool SameName))Call("ImportPreview", withB)!;
    Check("preview names the bible", pv.Text.Contains("bs_faulty"), pv.Text);
}
// the answer of an AI: shown first, one verse unticked keeps its old text
{
    Check("the AI button shows after a faulty import", C<Button>("ImportAiBtn").IsVisible);
    var ans = "GEN 1,1: Der H sprach, und G1001 NA/G21 TR es ward gingen.\nGEN 1,2: Der H sprach, und G1002 NA/G22 TR es ward gingen.";
    var d = Dlg(() => Call("ApplyAiAnswerAsync", ans), out var t);
    var ticks = d.GetLogicalDescendants().OfType<CheckBox>().ToList();
    Check("each verse has its tick", ticks.Count == 2);
    ticks[1].IsChecked = false; Pump(4);
    Press2(Plain(d)[0]);
    Settle(900);
    var localS = F<BibleStudy.Core.Import.LocalBibleStore>("_local");
    var fid = (await localS.GetTranslationsAsync()).First(x => x.Name == "bs_faulty").Id;
    var after = (await localS.GetChapterAsync(fid, "GEN", 1)).Verses;
    Check("the ticked verse is corrected", after[0].Text.Contains("ward gingen"), after[0].Text);
    Check("the unticked verse keeps its text", after[1].Text.Contains("gin g en"), after[1].Text);
}
// replacing a Bible: the changes are shown first, and cancelling leaves it alone
{
    var faulty = (await F<BibleStudy.Core.Import.LocalBibleStore>("_local").GetTranslationsAsync()).First(t => t.Name == "bs_faulty");
    var newer = new BibleStudy.Core.Import.ParsedBible("bs_faulty", "x", "de", [new BibleStudy.Core.Import.ParsedVerse("GEN", 1, 1, [new BibleStudy.Core.Models.Segment("Ganz neuer Text")])], []);
    var confirm = (Task<BibleStudy.Core.Import.ParsedBible?>)typeof(MainWindow).GetMethod("ConfirmReplaceAsync", All)!.Invoke(win, [faulty.Id, newer])!;
    Window? cw = null; var ct = DateTime.Now;
    while (cw is null && (DateTime.Now - ct).TotalSeconds < 10) { Pump(2); Thread.Sleep(80); cw = win.OwnedWindows.FirstOrDefault(w => w.IsVisible); }
    Check("replace window opens", cw is not null);
    var shown = string.Join(" ", cw!.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text));
    Console.WriteLine("replace window: " + shown[..Math.Min(160, shown.Length)]);
    Check("replace window shows the new text", shown.Contains("Ganz neuer Text"));
    Press2(Plain(cw)[^1]);
    Check("cancel means no", confirm.IsCompleted && confirm.Result is null);
}
tbx.SelectedItem = tbx.Items.OfType<BibleStudy.Core.Models.TranslationInfo>().First(x => x.Id == tid); Settle(700);   // the correction above switched to the corrected Bible
// a sub-mark from a translation this computer does not have: found by its stored words
vFinal.AddMark("weg_bibel", new BibleStudy.Core.Models.VerseRef("GEN", 1, 4), sIdx, sIdx + 9, "Testbibel", "#43A047", "", null, []);
vFinal.AddMark("weg_bibel", new BibleStudy.Core.Models.VerseRef("GEN", 1, 5), sIdx, sIdx + 10, "Gibtsnicht", "#43A047", "", null, []);
RunTask("GoToAsync", "GEN", 1, (int?)1); Settle(1000);
var gone4 = RunsOf((Control)F<System.Collections.IDictionary>("_verseBoxes")[4]!).Where(r => r.Background is not null).Select(r => r.Text).ToList();
var gone5 = RunsOf((Control)F<System.Collections.IDictionary>("_verseBoxes")[5]!).Where(r => r.Background is not null).Select(r => r.Text).ToList();
Console.WriteLine("missing-translation runs: " + string.Join("|", gone4) + " / " + string.Join("|", gone5));
Check("mark of a missing translation shows by its words", gone4.Any(t => t!.Contains("Testbibel")), string.Join("|", gone4));
Check("mark whose words are not in the verse stays away", !gone5.Any(t => t!.Contains("Gibtsnicht")) && gone5.Count == 0, string.Join("|", gone5));
Check("a sign says that a mark cannot be shown here", RunsOf((Control)F<System.Collections.IDictionary>("_verseBoxes")[5]!).Any(r => r.Text == "° "));
Check("no sign where the mark is shown", !RunsOf((Control)F<System.Collections.IDictionary>("_verseBoxes")[4]!).Any(r => r.Text == "° "));
// real mouse clicks: the sign shows why, a guessed place opens the correction
{
    var signRun = TbOf(5).Inlines!.OfType<Avalonia.Controls.Documents.Run>().First(r => r.Text == "° ");
    foreach (var stale in openPopups.ToList()) stale.IsOpen = false;   // a window left open by an earlier step would take the click
    Settle(200);
    ClickRun(TbOf(5), signRun); Settle(300);
    var shownPop = OpenPopup()?.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList() ?? [];
    Check("a click on the sign says which mark", shownPop.Any(t => t is not null && t.Contains("Gibtsnicht")), string.Join("|", shownPop));
    if (lastPopup is { } lp) lp.IsOpen = false;
    Settle(200);
    tbx.SelectedItem = tbx.Items.OfType<BibleStudy.Core.Models.TranslationInfo>().First(x => x.Id == tidB); Settle(700);
    vFinal.AddMark(tid, new BibleStudy.Core.Models.VerseRef("GEN", 1, 2), sIdx, sIdx + 9, "Testbibel", "#43A047", "", null, []);
    RunTask("GoToAsync", "GEN", 1, (int?)1); Settle(1200);
    var dashed = TbOf(2).Inlines!.OfType<Avalonia.Controls.Documents.Run>().First(r => r.Background is not null && r.TextDecorations is not null);
    foreach (var w0 in win.OwnedWindows.ToList()) w0.Close(); Settle(300);
    ClickRun(TbOf(2), dashed);
    Window? cor = null; var t1 = DateTime.Now;
    while (cor is null && (DateTime.Now - t1).TotalSeconds < 6) { Pump(2); Thread.Sleep(80); cor = win.OwnedWindows.FirstOrDefault(w => w.IsVisible); }
    Check("a click on a guessed place opens the correction", cor is not null && cor.GetLogicalDescendants().OfType<Avalonia.Controls.Primitives.ToggleButton>().Any());
    if (cor is not null) Press2(Plain(cor)[^1]);
    tbx.SelectedItem = tbx.Items.OfType<BibleStudy.Core.Models.TranslationInfo>().First(x => x.Id == tid); Settle(700);
}
// ===== once-only question: read the imported Bibles again =====
{
    var bs3 = "\\";
    var src = Path.Combine(Path.GetTempPath(), "AlteTestBibel.usfm");
    List<string> Usfm(string firstVerse) { var l = new List<string> { bs3 + "id GEN", bs3 + "c 1", bs3 + "v 1 " + firstVerse }; for (var i = 2; i <= 14; i++) l.Add($"{bs3}v {i} Dies ist Vers {i} der Testbibel."); return l; }
    File.WriteAllLines(src, Usfm("Im Anfang schuf Gott die wurd e Himmel."));
    RunTask("ImportBibleFromAsync", src, "de"); Settle(600);
    var lstore = F<BibleStudy.Core.Import.LocalBibleStore>("_local");
    var tr = lstore.GetTranslationsAsync().Result.First(t => t.Name == "AlteTestBibel");
    Check("new import is current (no question)", !lstore.GetOutdated().Any(o => o.Id == tr.Id));
    // make it look like an import of the old code: no version, no remembered file
    using (var con = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=" + Path.Combine(data, "local.db"))) { con.Open(); using var cm = con.CreateCommand(); cm.CommandText = "UPDATE translations SET importer=NULL, source=NULL WHERE id=$i"; cm.Parameters.AddWithValue("$i", tr.Id); cm.ExecuteNonQuery(); }
    Check("old import is found", lstore.GetOutdated().Any(o => o.Id == tr.Id));
    // a mark in the first verse on the word "Gott"
    var vv = F<BibleStudy.Core.Storage.VaultStore>("_vault");
    var verse1 = lstore.GetChapterAsync(tr.Id, "GEN", 1).Result.Verses.First(v => v.Number == 1);
    var raw1 = BibleStudy.Core.Storage.MarkLogic.RawText(verse1.Segments); var gAt = raw1.IndexOf("Gott");
    var markId = vv.AddMark(tr.Id, new BibleStudy.Core.Models.VerseRef("GEN", 1, 1), gAt, gAt + 4, "Gott", "#43A047", "", null, []);
    var hAt = raw1.IndexOf("Himmel");
    var markId2 = vv.AddMark(tr.Id, new BibleStudy.Core.Models.VerseRef("GEN", 1, 1), hAt, hAt + 6, "Himmel", "#E53935", "", null, []);
    // the better file: the broken word is whole now, so everything after it moves by one letter
    File.WriteAllLines(src, Usfm("Im Anfang schuf Gott die wurde Himmel."));
    // the question: first "Nein", then the second warning appears
    using (var con = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=" + Path.Combine(data, "local.db"))) { con.Open(); using var cm = con.CreateCommand(); cm.CommandText = "UPDATE translations SET source=$s WHERE id=$i"; cm.Parameters.AddWithValue("$s", src); cm.Parameters.AddWithValue("$i", tr.Id); cm.ExecuteNonQuery(); }
    var settingsObj = F<AppSettings>("_settings"); settingsObj.ImportRefreshAsked = 0;
    foreach (var w0 in win.OwnedWindows.ToList()) w0.Close(); Settle(400);   // the notice about the data sources of the first start
    var ask = (Task)typeof(MainWindow).GetMethod("OfferReimportAsync", All)!.Invoke(win, null)!;
    Window? dlg = null; var wt = DateTime.Now;
    while (dlg is null && (DateTime.Now - wt).TotalSeconds < 15) { Pump(2); Thread.Sleep(80); dlg = win.OwnedWindows.FirstOrDefault(); }
    Check("first question window appears", dlg is not null);
    string TitleOf(Window w) => (w.Content as StackPanel)?.Children.OfType<TextBlock>().FirstOrDefault()?.Text ?? "";
    Console.WriteLine("dialog 1: " + (dlg is null ? "" : TitleOf(dlg)));
    Button Btn(Window w, string text) => w.GetVisualDescendants().OfType<Button>().First(b => (b.Content as string) == text);
    Check("question mentions the Bible", dlg is not null && (dlg.Content as StackPanel)!.Children.OfType<TextBlock>().Any(t => (t.Text ?? "").Contains("AlteTestBibel")));
    Shot(dlg!, "reimport-question");
    Btn(dlg!, "Nein").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); Settle(500);
    Window? dlg2 = null; wt = DateTime.Now;
    while (dlg2 is null && (DateTime.Now - wt).TotalSeconds < 10) { Pump(2); Thread.Sleep(80); dlg2 = win.OwnedWindows.FirstOrDefault(w => w != dlg && w.IsVisible); }
    Console.WriteLine("dialog 2: " + (dlg2 is null ? "" : TitleOf(dlg2)));
    Check("a second warning follows on No", dlg2 is not null && TitleOf(dlg2).Contains("Wirklich"));
    Shot(dlg2!, "reimport-sure");
    // set the file so that no file picker is needed, then confirm "Doch neu einlesen"
    Btn(dlg2!, "Doch neu einlesen").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
    Wait(() => ask.IsCompleted, 40); Settle(600);
    Console.WriteLine("import info: " + C<TextBlock>("ImportBibleInfo").Text);
    Check("no old import left", !lstore.GetOutdated().Any(o => o.Id == tr.Id));
    var verse1b = lstore.GetChapterAsync(tr.Id, "GEN", 1).Result.Verses.First(v => v.Number == 1);
    var raw1b = BibleStudy.Core.Storage.MarkLogic.RawText(verse1b.Segments);
    Check("text is the better one", raw1b.Contains("wurde Himmel") && !raw1b.Contains("wurd e"), raw1b);
    var moved = vv.GetMarks().First(m => m.Id == markId);
    Check("the mark still covers its word", raw1b.Substring(moved.Start, moved.End - moved.Start) == "Gott", raw1b.Substring(Math.Clamp(moved.Start, 0, raw1b.Length - 1), Math.Min(4, raw1b.Length - Math.Clamp(moved.Start, 0, raw1b.Length - 1))));
    var moved2 = vv.GetMarks().First(m => m.Id == markId2);
    Check("a mark behind the corrected word moved with its text", moved2.Start != hAt && raw1b.Substring(moved2.Start, moved2.End - moved2.Start) == "Himmel", $"{hAt} -> {moved2.Start}");
    Check("asked flag set", F<AppSettings>("_settings").ImportRefreshAsked >= BibleStudy.Core.Import.LocalBibleStore.CurrentImporter);
    var madeBackups = Directory.GetFiles(BibleStudy.Core.AppPaths.BackupDir, "*vor dem Neu-Einlesen*");
    Check("backup was made", madeBackups.Length > 0);
    foreach (var f in madeBackups) File.Delete(f);   // the test's own backup does not stay in the real backup folder
}
// closing with changed guest work: the question comes, and "cancel" keeps the program open
{
    foreach (var w in win.OwnedWindows.ToList()) w.Close();
    Settle(300);
    win.Close(); Settle(300);
    Window? q = null; var qt = DateTime.Now;
    while (q is null && (DateTime.Now - qt).TotalSeconds < 10) { Pump(2); Thread.Sleep(80); q = win.OwnedWindows.FirstOrDefault(w => w.IsVisible); }
    Check("closing asks about the guest work", q is not null);
    if (q is not null)
    {
        var buttons = q.GetLogicalDescendants().OfType<Button>().ToList();
        Check("three answers", buttons.Count == 3);
        buttons[^1].RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); Settle(400);
        Check("cancel keeps the program open", win.IsVisible);
    }
}
Console.WriteLine(fails == 0 ? "ALL OK" : $"{fails} FAILED");
Environment.Exit(fails == 0 ? 0 : 1);
Environment.Exit(fails);
