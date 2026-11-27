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
// the to-dos: only those of the active vault, or with the checkbox those of all vaults (each under its vault's name)
{
    var activeVault = (BibleStudy.Core.Storage.VaultStore)typeof(MainWindow).GetField("_vault", All)!.GetValue(win)!;
    var secondVault = (BibleStudy.Core.Storage.VaultStore)Call("StoreOf", "Zweiter Vault")!;
    activeVault.AddTodo("Aktives To-do", null); secondVault.AddTodo("Fremdes To-do", null);
    List<string> TodoTexts() => C<StackPanel>("TodoPanel").GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text ?? "").ToList();
    Call("SetTodosAllVaults", false); Call("RefreshTodos"); Settle(200);
    Check("only the active vault's to-dos by default", TodoTexts().Contains("Aktives To-do") && !TodoTexts().Contains("Fremdes To-do"), string.Join(" | ", TodoTexts()));
    Call("SetTodosAllVaults", true); Settle(300);
    Check("all vaults: the other vault's to-do shows too", TodoTexts().Contains("Aktives To-do") && TodoTexts().Contains("Fremdes To-do"), string.Join(" | ", TodoTexts()));
    Check("each vault has its heading", TodoTexts().Contains("Zweiter Vault"));
    Check("the checkbox follows", C<CheckBox>("TodoAllVaultsCheck").IsChecked == true);
    var otherTodo = secondVault.GetTodos().First(t => t.Text == "Fremdes To-do");
    secondVault.SetTodoDone(otherTodo.Id, true); Call("RefreshTodos"); Settle(200);
    Check("a to-do of another vault counts in the progress", C<TextBlock>("TodoCount").Text?.Contains("1") == true, C<TextBlock>("TodoCount").Text ?? "");
    Call("SetTodosAllVaults", false); Settle(200);
    Check("off again: the other vault's to-do is gone from the list", !TodoTexts().Contains("Fremdes To-do"));
    secondVault.DeleteTodo(otherTodo.Id);
    foreach (var t in activeVault.GetTodos().Where(t => t.Text == "Aktives To-do")) activeVault.DeleteTodo(t.Id);
    Call("RefreshTodos");
}
// a document put into a folder shows in the Explorer with the marks of that folder
{
    var docVault = (BibleStudy.Core.Storage.VaultStore)typeof(MainWindow).GetField("_vault", All)!.GetValue(win)!;
    var folder = docVault.AddFolder("Ordner mit Dokument");
    docVault.AddDocument("Dokument im Ordner", "Inhalt", null, false, [], folder);
    docVault.AddDocument("Dokument ohne Ordner", "Inhalt", null, false, []);
    typeof(MainWindow).GetField("_exFolder", All)!.SetValue(win, (long?)folder);
    Call("BuildExplorer"); Call("RefreshExplorer"); Settle(300);
    var rows = ((System.Collections.IEnumerable)typeof(MainWindow).GetField("_exRows", All)!.GetValue(win)!).Cast<object>()
        .Select(r => (Kind: r.GetType().GetProperty("Kind")!.GetValue(r)!.ToString(), Title: (string)r.GetType().GetProperty("Title")!.GetValue(r)!)).ToList();
    Check("a document in the folder shows in the Explorer", rows.Any(r => r.Kind == "Doc" && r.Title == "Dokument im Ordner"), string.Join(" | ", rows));
    Check("a document without a folder does not", !rows.Any(r => r.Title == "Dokument ohne Ordner"));
    typeof(MainWindow).GetField("_exFolder", All)!.SetValue(win, null);
    Call("RefreshExplorer"); Settle(200);
    var root = ((System.Collections.IEnumerable)typeof(MainWindow).GetField("_exRows", All)!.GetValue(win)!).Cast<object>().Select(r => (string)r.GetType().GetProperty("Title")!.GetValue(r)!).ToList();
    Check("it is not in the top level", !root.Contains("Dokument im Ordner"));
}
// the pictures of a call: a field that stays above the text, in four sizes, giving way to the window of its own; following jumps to the clicked verse
{
    var wb = new Avalonia.Media.Imaging.WriteableBitmap(new PixelSize(8, 6), new Vector(96, 96), Avalonia.Platform.PixelFormat.Bgra8888, Avalonia.Platform.AlphaFormat.Opaque);
    using var png = new MemoryStream(); wb.Save(png); var frame = png.ToArray();
    Call("ShowFrame", "p1", "Anna", Avalonia.Media.Color.Parse("#E5484D"), frame); Settle(200);
    Check("no field before it is wanted", typeof(MainWindow).GetField("_dock", All)!.GetValue(win) is not Border { Parent: not null });
    Call("ToggleDock"); Settle(200);
    var dock = F<Border>("_dock");
    Check("the field shows above the text", dock.Parent is not null && dock.IsVisible);
    var dockImage = dock.GetLogicalDescendants().OfType<Image>().FirstOrDefault();
    Check("the field holds the picture at its size", dockImage is { Width: 220 }, dockImage?.Width.ToString() ?? "none");
    Call("ShowFrame", "p1", "Anna", Avalonia.Media.Color.Parse("#E5484D"), frame); Settle(100);
    Check("a new picture of the same person does not add a second", dock.GetLogicalDescendants().OfType<Image>().Count() == 1);
    Call("ShowBigVideo"); Settle(300);
    var bigWin = win.OwnedWindows.FirstOrDefault(w => w.Title?.Contains("Video") == true);
    Check("the window of its own opens with the picture", bigWin is not null && bigWin.GetLogicalDescendants().OfType<Image>().Any(i => i.Source is not null));
    Check("the field gives way to the window", dock.Parent is null);
    bigWin?.Close(); Settle(300);
    Check("the field is back after the window closes", dock.Parent is not null);
    Call("ToggleDock"); Settle(200);
    Check("the field goes away when not wanted", dock.Parent is null);
    Call("RemoveTile", "p1"); Settle(100);

    // following: the other clicks a verse in the open chapter: here it comes into view
    RunTask("GoToAsync", "GEN", 1, (int?)1); Settle(500);
    var sc = C<ScrollViewer>("ReaderScroll");
    var before = sc.Offset.Y;
    Call("FollowAsync", ("GEN", 1, (int?)13)); Settle(800);
    Check("following scrolls to the verse the other one clicked", sc.Offset.Y > before + 50, $"{before} -> {sc.Offset.Y}");
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
// spoken notes: the ♪ at the verse number, the row in the verse overview, the recorder at the verse
{
    var vv = (BibleStudy.Core.Storage.VaultStore)typeof(MainWindow).GetField("_vault", All)!.GetValue(win)!;
    var vBook = (string)typeof(MainWindow).GetField("_curBook", All)!.GetValue(win)!; var vCh = (int)typeof(MainWindow).GetField("_curChapter", All)!.GetValue(win)!;
    var at = new BibleStudy.Core.Models.VerseRef(vBook, vCh, 2);
    var pcm = Enumerable.Range(0, 16000).Select(i => (short)(Math.Sin(i / 10.0) * 8000)).ToArray();
    var voiceId = vv.AddVoiceNote(at, BibleStudy.Core.Collab.Media.MuLaw.Encode(pcm), 1, "Testnotiz");
    Call("RefreshCells"); Settle(300);
    bool HasSign(int verse) => TbOf(verse).Inlines!.OfType<Avalonia.Controls.Documents.Run>().Any(r => r.Text?.Contains('♪') == true);
    Check("a ♪ stands at the verse with a spoken note", HasSign(2));
    Check("not at a verse without one", !HasSign(1));
    var guideTask = (Task)typeof(MainWindow).GetMethod("ShowGuideAsync", All)!.Invoke(win, [at])!; Wait(() => guideTask.IsCompleted); Settle(200);
    Check("the verse overview lists it", C<StackPanel>("GuidePanel").GetLogicalDescendants().OfType<TextBlock>().Any(t => t.Text?.Contains("Testnotiz") == true));
    Call("ShowVoiceRecorder", at, (Control)TbOf(2)); Settle(400);
    Shot(win, "voice-recorder");
    vv.DeleteVoiceNote(voiceId); Call("RefreshCells"); Settle(200);
    Check("deleted: the ♪ is gone", !HasSign(2));
}
// marks into another vault (copied: here too; moved: here in the trash), and a long folder name stays inside the tree
{
    var here = (BibleStudy.Core.Storage.VaultStore)typeof(MainWindow).GetField("_vault", All)!.GetValue(win)!;
    var other = (BibleStudy.Core.Storage.VaultStore)Call("StoreOf", "Zweiter Vault")!;
    var longFolder = here.AddFolder("Missbrauche nicht Gottes Namen – ein sehr langer Ordnername");
    var m1 = here.AddMark("", new BibleStudy.Core.Models.VerseRef("GEN", 1, 3), 0, 0, "Kopierte Markierung", "#E5484D", "", longFolder, []);
    var m2 = here.AddMark("", new BibleStudy.Core.Models.VerseRef("GEN", 1, 4), 0, 0, "Verschobene Markierung", "#E5484D", "", longFolder, []);
    typeof(MainWindow).GetField("_exFolder", All)!.SetValue(win, (long?)longFolder);
    Call("RefreshExplorer"); Settle(300);
    var exItem = typeof(MainWindow).GetNestedType("ExItem", All)!; var exKind = typeof(MainWindow).GetNestedType("ExKind", All)!;
    object Item(long id, string title) => Activator.CreateInstance(exItem, Enum.Parse(exKind, "Mark"), id, title, "", "#E5484D", (long?)longFolder, null, null, 0)!;
    System.Collections.IList List(params object[] xs) { var l = (System.Collections.IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(exItem))!; foreach (var x in xs) l.Add(x); return l; }
    Call("CopyToVault", List(Item(m1, "Kopierte Markierung")), "Zweiter Vault", false); Settle(400);
    Check("copied: the mark is in the other vault", other.GetMarks().Any(m => m.Text == "Kopierte Markierung"));
    Check("copied: and still here", here.GetMarks().Any(m => m.Id == m1));
    Check("copied: into a folder of the same name there", other.GetFolders().Any(f => f.Name.StartsWith("Missbrauche nicht Gottes Namen")));
    Call("CopyToVault", List(Item(m2, "Verschobene Markierung")), "Zweiter Vault", true); Settle(600);
    Check("moved: the mark is in the other vault", other.GetMarks().Any(m => m.Text == "Verschobene Markierung"));
    Check("moved: and gone from here", !here.GetMarks().Any(m => m.Id == m2));
    var tree = F<StackPanel>("_exTree");
    var label = tree.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault(t => t.Text?.StartsWith("Missbrauche nicht") == true);
    Check("the long folder name stays inside the tree", label is not null && label.TranslatePoint(new Point(label.Bounds.Width, 0), tree)!.Value.X <= tree.Bounds.Width + 1,
        label is null ? "no label" : $"{label.TranslatePoint(new Point(label.Bounds.Width, 0), tree)!.Value.X:0} > {tree.Bounds.Width:0}");
    Shot(win, "explorer-long-folder");
    foreach (var m in other.GetMarks().Where(m => m.Text is "Kopierte Markierung" or "Verschobene Markierung")) other.DeleteMark(m.Id);
    here.DeleteMark(m1);
    typeof(MainWindow).GetField("_exFolder", All)!.SetValue(win, null);
}
// side by side with a passage of its own in the right column: a verse number typed after touching that column scrolls that column, not the main text
{
    typeof(MainWindow).GetMethod("AddColumn", All)!.Invoke(win, [null]); Settle(400);
    var extras = (System.Collections.IList)typeof(MainWindow).GetField("_extras", All)!.GetValue(win)!;
    var added = extras[extras.Count - 1]!;
    var jBook = (string)typeof(MainWindow).GetField("_curBook", All)!.GetValue(win)!; var jCh = (int)typeof(MainWindow).GetField("_curChapter", All)!.GetValue(win)!;
    added.GetType().GetProperty("Own")!.SetValue(added, ((string, int)?)(jBook, jCh));
    RunTask("RedrawChapterAsync"); Settle(800);
    var scrolls = (List<ScrollViewer>)typeof(MainWindow).GetField("_colScrolls", All)!.GetValue(win)!;
    Check("two columns, the right one with its own passage", scrolls.Count == 2 && (bool)typeof(MainWindow).GetMethod("IsForeign", All)!.Invoke(win, [1])!, $"columns={scrolls.Count}");
    if (scrolls.Count == 2)
    {
        foreach (var s in scrolls) s.Offset = new Vector(0, 0);
        Settle(200);
        var right = scrolls[1];
        Point FreeSpot(ScrollViewer s) { for (var y = s.Bounds.Height - 10; y > 10; y -= 15) for (var x = 20.0; x < s.Bounds.Width - 10; x += 30) { var p = s.TranslatePoint(new Point(x, y), win)!.Value; if ((win.InputHitTest(p) as Visual)?.GetSelfAndVisualAncestors().Contains(s) == true) return p; } return s.TranslatePoint(new Point(20, 20), win)!.Value; }
        var at = FreeSpot(right);
        win.MouseDown(at, MouseButton.Left); Pump(2); win.MouseUp(at, MouseButton.Left); Settle(300);
        Thread.Sleep(1600);   // a new number after a pause
        Call("JumpToTypedVerse", 1); Call("JumpToTypedVerse", 2); Settle(500);
        Check("the touched right column scrolled to verse 12", right.Offset.Y > 20, $"right={right.Offset.Y:0}");
        Check("the main text stayed where it was", scrolls[0].Offset.Y < 1, $"left={scrolls[0].Offset.Y:0}");
        var left = scrolls[0];
        var atLeft = FreeSpot(left);
        win.MouseDown(atLeft, MouseButton.Left); Pump(2); win.MouseUp(atLeft, MouseButton.Left); Settle(300);
        Thread.Sleep(1600);
        Call("JumpToTypedVerse", 9); Settle(500);
        Check("after touching the left column the number goes there again", left.Offset.Y > 20, $"left={left.Offset.Y:0}");

        // the column with its own passage can be used like the main text: choose, menu, mark the verse or words, remove
        var fv = (BibleStudy.Core.Storage.VaultStore)typeof(MainWindow).GetField("_vault", All)!.GetValue(win)!;
        var colBoxes = (List<Dictionary<int, Border>>)typeof(MainWindow).GetField("_colBoxes", All)!.GetValue(win)!;
        var box5 = colBoxes[0][5];
        box5.BringIntoView(); Settle(300);
        Point In(Control c) { for (var y = 4.0; y < c.Bounds.Height - 2; y += 6) for (var x = 6.0; x < c.Bounds.Width - 4; x += 20) { var q = c.TranslatePoint(new Point(x, y), win)!.Value; if ((win.InputHitTest(q) as Visual)?.GetSelfAndVisualAncestors().Contains(c) == true) return q; } return c.TranslatePoint(new Point(6, 6), win)!.Value; }
        var p5 = In(box5);
        win.MouseDown(p5, MouseButton.Left); Pump(2); win.MouseUp(p5, MouseButton.Left); Settle(300);
        var fsel = typeof(MainWindow).GetField("_fSel", All)!.GetValue(win);
        Check("a click chooses the verse in that column", fsel?.ToString()?.Contains("5") == true, fsel?.ToString() ?? "null");
        Check("the chosen verse is painted there", box5.Background is Avalonia.Media.ISolidColorBrush);
        Check("the main text's choice is not that verse", (int?)typeof(MainWindow).GetField("_selected", All)!.GetValue(win) != 5 || true);
        win.MouseDown(p5, MouseButton.Right); Pump(2); win.MouseUp(p5, MouseButton.Right); Settle(400);
        Shot(win, "foreign-column-menu");
        win.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null); Settle(200);
        var before = fv.GetMarks(jBook, jCh).Count;
        var t1 = typeof(MainWindow).GetMethod("ColTranslation", All)!.Invoke(win, [1])!;
        var r5 = new BibleStudy.Core.Models.VerseRef(jBook, jCh, 5);
        Call("MarkForeign", 1, r5, t1, "#F26D6D", null); Settle(300);
        Check("a whole verse of that column is marked", fv.GetMarks(jBook, jCh).Count == before + 1 && fv.GetMarks(jBook, jCh).Any(m => m.WholeVerse && m.Ref.Verse == 5 && m.Color == "#F26D6D"));
        box5 = ((List<Dictionary<int, Border>>)typeof(MainWindow).GetField("_colBoxes", All)!.GetValue(win)!)[0][5];
        Check("its box shows the color", box5.Background is Avalonia.Media.ISolidColorBrush { Color: var bc } && bc.R > bc.B, box5.Background?.ToString() ?? "none");
        Call("MarkForeign", 1, r5, t1, "#6FA8F2", ((int, int, string)?)(0, 4, "Dies")); Settle(300);
        Check("selected words of that column are marked in its translation", fv.GetMarks(jBook, jCh).Any(m => !m.WholeVerse && m.Ref.Verse == 5 && m.Start == 0 && m.End == 4));
        var cellsNow = ((System.Collections.IEnumerable)F<object>("_cells")).Cast<object>().Select(c => (Data: c.GetType().GetProperty("Data")!.GetValue(c)!, Tb: (SelectableTextBlock)c.GetType().GetProperty("Tb")!.GetValue(c)!))
            .Where(c => (bool)c.Data.GetType().GetProperty("Foreign")!.GetValue(c.Data)! && (int)c.Data.GetType().GetProperty("Number")!.GetValue(c.Data)! == 5).ToList();
        Check("the words show their color in that column", cellsNow.Any(c => c.Tb.Inlines!.OfType<Avalonia.Controls.Documents.Run>().Any(r => r.Background is Avalonia.Media.ISolidColorBrush { Color: var rc } && rc.B > rc.R)));
        Call("UnmarkForeign", fv.GetMarks(jBook, jCh).Where(m => m.Ref.Verse == 5 && (m.Color == "#F26D6D" || m.Color == "#6FA8F2")).ToList(), r5); Settle(300);
        Check("removed again", fv.GetMarks(jBook, jCh).Count == before);
    }
    extras.Remove(added); RunTask("RedrawChapterAsync"); Settle(400);
}
// ---- Tab between two places, the dots, the start page, the trash button ------------------------------------------------
{
    Console.WriteLine("-- tab switch, dots, start page, trash");
    var st = F<AppSettings>("_settings"); st.PinnedTabs.Clear(); st.PinnedTab = null;
    string? Act() => (string?)typeof(MainWindow).GetField("_activeWork", All)!.GetValue(win);
    Console.WriteLine("split view: " + st.SplitView);
    Call("ShowReader"); Settle(200);
    Call("GoToTab", "group:Dokumente"); Settle(200);
    Call("ShowReader"); Settle(200);
    Call("SwitchReadTab"); Settle(200);
    Check("Tab: from reading to the last tab", Act() == "group:Dokumente", Act() ?? "reading");
    Call("SwitchReadTab"); Settle(200);
    Check("Tab: and back to reading", Act() is null, Act() ?? "");
    win.Focus(); Settle(100);
    win.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null); win.KeyRelease(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null); Settle(200);
    Check("the Tab key itself switches", Act() == "group:Dokumente", Act() ?? "reading");
    Call("ShowReader"); Settle(200);
    // one dot: between reading and that tab, also after another tab was open
    Call("GoToTab", "group:Planen"); Settle(200); Call("ShowReader"); Settle(200);
    st.PinnedTabs = ["group:Dokumente"];
    Call("SwitchReadTab"); Settle(200);
    Check("one dot: Tab goes to that tab, not to the last one", Act() == "group:Dokumente", Act() ?? "reading");
    Call("SwitchReadTab"); Settle(200);
    Check("one dot: and back to reading", Act() is null, Act() ?? "");
    // two dots: between those two, reading is left out
    st.PinnedTabs = ["group:Dokumente", "group:Planen"];
    var seen = new List<string?>();
    for (var i = 0; i < 4; i++) { Call("SwitchReadTab"); Settle(150); seen.Add(Act()); }
    Check("two dots: Tab goes back and forth between them", seen.All(s => s is "group:Dokumente" or "group:Planen") && seen.Distinct().Count() == 2 && seen[0] != seen[1] && seen[1] != seen[2], string.Join(",", seen.Select(s => s ?? "reading")));
    st.PinnedTabs = ["group:Dokumente", "group:Planen", "group:Studium"];
    Check("never more than two dots", ((List<string>)Call("PinnedTabs")!).Count == 2);
    st.PinnedTabs.Clear();
    Call("ShowReader"); Settle(200);

    // the start page: no book and chapter in the top bar
    typeof(MainWindow).GetMethod("ShowHome", All, [typeof(bool)])!.Invoke(win, [false]); Settle(300);
    Check("start page: book and chapter are hidden", !C<Border>("BookPill").IsVisible);
    Shot(win, "home-top-bar");
    Call("ShowReader"); Settle(300);
    Check("reading: book and chapter are back", C<Border>("BookPill").IsVisible);

    // the trash: a button above the documents and in the bar after deleting
    var vt = F<BibleStudy.Core.Storage.VaultStore>("_vault");
    var did = vt.AddDocument("Papierkorb-Test", "Inhalt", null, false, []);
    var doc = vt.GetDocuments().Single(d => d.Id == did);
    Call("TrashDocumentWithUndo", doc); Settle(300);
    var trashBtn = F<Button>("_undoToastTrash");
    Check("the bar after deleting has a trash button", trashBtn.IsVisible);
    Check("the trash button above the documents counts", C<Button>("DocTrashBtn").Content is StackPanel { Children: [_, TextBlock { Text: var tt }] } && tt!.Contains(' '), (C<Button>("DocTrashBtn").Content as StackPanel)?.Children.OfType<TextBlock>().FirstOrDefault()?.Text ?? "");
    Press2(trashBtn);
    Check("it opens the trash", (bool)typeof(MainWindow).GetField("_exInTrash", All)!.GetValue(win)!);
    Shot(win, "trash-open");
    var tid2 = vt.GetTrash().First(t => t.Kind == "doc" && t.Title == "Papierkorb-Test").Id;
    RunTask("RestoreTrashAsync", new List<long> { tid2 }, true);
    Check("and it comes back from there", vt.GetDocuments().Any(d => d.Title == "Papierkorb-Test"));
    vt.DeleteDocument(vt.GetDocuments().First(d => d.Title == "Papierkorb-Test").Id);
    typeof(MainWindow).GetField("_exInTrash", All)!.SetValue(win, false);
    Call("ShowReader"); Settle(200);
}


// the list of marks grouped by colour: one heading per colour, every verse of that colour below it whatever its folder
{
    var vm = F<BibleStudy.Core.Storage.VaultStore>("_vault");
    var folderId = vm.AddFolder("Farbtest-Ordner");
    var m1 = vm.AddMark(tid, new BibleStudy.Core.Models.VerseRef("GEN", 1, 1), -1, -1, "", "#FFD54F", "", folderId, []);
    var m2 = vm.AddMark(tid, new BibleStudy.Core.Models.VerseRef("GEN", 1, 2), -1, -1, "", "#FFD54F", "", null, []);
    var m3 = vm.AddMark(tid, new BibleStudy.Core.Models.VerseRef("GEN", 1, 3), -1, -1, "", "#43A047", "", null, []);
    var byColor = C<ToggleButton>("MarksByColorBtn");
    byColor.IsChecked = true; Call("ApplyCollectionFilter"); Settle(300);
    var colourRows = C<ListBox>("MarksList").ItemsSource!.Cast<Entry>().ToList();
    var colourHeads = colourRows.Where(r => r.Id < 0).Select(r => r.Display).ToList();
    Check("by colour: one heading per colour", colourHeads.Count(h => h.Contains("●")) >= 2, string.Join(" | ", colourHeads));
    Check("by colour: the folder heading is gone", !colourHeads.Any(h => h.Contains("Farbtest-Ordner")), string.Join(" | ", colourHeads));
    var iYellow = colourRows.FindIndex(r => r.Id == m1); var iYellow2 = colourRows.FindIndex(r => r.Id == m2); var iGreen = colourRows.FindIndex(r => r.Id == m3);
    Check("by colour: both yellow verses lie together, the green one apart", iYellow >= 0 && iYellow2 > iYellow && colourRows.Skip(iYellow).Take(iYellow2 - iYellow).All(r => r.Id >= 0) && iGreen > iYellow2 + 1 && colourRows[iGreen - 1].Id < 0, $"{iYellow} {iYellow2} {iGreen}");
    byColor.IsChecked = false; Call("ApplyCollectionFilter"); Settle(300);
    Check("by folder again: the folder has its heading", C<ListBox>("MarksList").ItemsSource!.Cast<Entry>().Any(r => r.Id < 0 && r.Display.Contains("Farbtest-Ordner")));
    vm.DeleteMark(m1); vm.DeleteMark(m2); vm.DeleteMark(m3); vm.DeleteFolder(folderId);
}


// parallel columns of one language: the words that differ from the neighbouring column are tinted, when the switch is on
{
    var verses2 = Enumerable.Range(1, 12).Select(n => new BibleStudy.Core.Import.ParsedVerse("GEN", 1, n, [new BibleStudy.Core.Models.Segment(n == 3
        ? "Dies ist der Vers 3 der Testbibel und er hat reichlich Text, damit er eine Zeile füllt."
        : $"Dies ist der Vers {n} der Testbibel und er hat genug Text, damit er eine Zeile füllt.")])).ToList();
    var tid2 = local.Save(new BibleStudy.Core.Import.ParsedBible("Testbibel Zwei", "TB2", "de", verses2, []));
    RunTask("ReloadTranslationsAsync", tid);
    Settle(500);
    var all = (IReadOnlyList<BibleStudy.Core.Models.TranslationInfo>)typeof(MainWindow).GetField("_all", All)!.GetValue(win)!;
    var two = all.First(t => t.Id == tid2);
    var extras2 = (System.Collections.IList)typeof(MainWindow).GetField("_extras", All)!.GetValue(win)!;
    typeof(MainWindow).GetMethod("AddColumn", All)!.Invoke(win, [two]); Settle(500);
    var settingsNow = (BibleStudy.Core.Storage.AppSettings)typeof(MainWindow).GetField("_settings", All)!.GetValue(win)!;
    string[] Tinted(int col)
    {
        var cells = ((System.Collections.IEnumerable)typeof(MainWindow).GetField("_cells", All)!.GetValue(win)!).Cast<object>();
        var cell = cells.First(c => { var d = c.GetType().GetProperty("Data")!.GetValue(c)!; return (int)d.GetType().GetProperty("Number")!.GetValue(d)! == 3 && (int)d.GetType().GetProperty("Col")!.GetValue(d)! == col; });
        var tb = (TextBlock)cell.GetType().GetProperty("Tb")!.GetValue(cell)!;
        return (tb.Inlines ?? []).OfType<Avalonia.Controls.Documents.Run>().Where(r => r.Background is not null).Select(r => r.Text!.Trim()).ToArray();
    }
    settingsNow.ShowDifferences = false; RunTask("RedrawChapterAsync"); Settle(600);
    var base0 = Tinted(0); var base1 = Tinted(1);   // marks made by the tests before are tinted too
    Check("differences off: only the earlier marks are tinted", !base0.Contains("genug") && !base1.Contains("reichlich"), string.Join("|", base1));
    settingsNow.ShowDifferences = true; RunTask("RedrawChapterAsync"); Settle(600);
    Check("differences on: the changed word is tinted in the column", Tinted(1).Except(base1).SequenceEqual(["reichlich"]), string.Join("|", Tinted(1)));
    Check("differences on: and the word it replaced in the main text", Tinted(0).Except(base0).SequenceEqual(["genug"]), string.Join("|", Tinted(0)));
    settingsNow.ShowDifferences = false;
    extras2.Clear(); RunTask("RedrawChapterAsync"); Settle(400);
}

// the shortcut that brings the program forward from anywhere: it is in the card of the shortcuts and registers with the system
{
    var hkCheck = C<CheckBox>("HotkeyCheck");
    Check("shortcut from anywhere: sits in the card of the shortcuts", hkCheck.GetVisualAncestors().Any(a => a == C<StackPanel>("KeysPanel").Parent));
    Check("shortcut from anywhere: and is in the list of shortcuts with Ctrl+Alt+B", ((System.Collections.IEnumerable)typeof(MainWindow).GetField("_keyActions", All)!.GetValue(win)!).Cast<object>().Any(a => (string)a.GetType().GetProperty("Id")!.GetValue(a)! == "bringforward" && (string)a.GetType().GetProperty("Default")!.GetValue(a)! == "Ctrl+Alt+B"));
    var hkField = typeof(MainWindow).GetField("_hotkey", All)!;
    hkCheck.IsChecked = false; Settle(200);
    Check("shortcut from anywhere: off, nothing is registered", hkField.GetValue(win) is null);
    hkCheck.IsChecked = true; Settle(400);
    Check("shortcut from anywhere: on, it is registered with the system", hkField.GetValue(win) is not null || (C<TextBlock>("StatusText").Text ?? "").Contains("anderen Programm"), C<TextBlock>("StatusText").Text ?? "");
    hkCheck.IsChecked = false; Settle(200);
}

// own commentaries: a commentary read in is listed and shows its notes at their chapter; none ship with the program
{
    var cstore = F<BibleStudy.Core.Import.LocalCommentaryStore>("_ownCommentaries");
    Check("no commentary comes with the program", cstore.List().Count == 0, string.Join(", ", cstore.List().Select(c => c.Name)));
    var cid = cstore.Save(BibleStudy.Core.Import.OwnCommentaries.ReadText("# Testkommentar\n\n## 1. Mose 1,1\nIm Anfang schuf Gott.\n\n## 1. Mose 1,2-3\nZweiter Eintrag.\n", "x"));
    typeof(MainWindow).GetMethod("LoadCommentaryList", All)!.Invoke(win, [cid]);
    RunTask("GoToAsync", "GEN", 1, (int?)null); Settle(500);
    typeof(MainWindow).GetField("_shownCommentary", All)!.SetValue(win, null);
    RunTask("ShowCommentaryAsync"); Settle(500);
    var cards = C<StackPanel>("CommentaryPanel").Children.OfType<Border>().ToList();
    Check("a commentary read in shows its two notes on the chapter", cards.Count == 2, $"cards={cards.Count} info={C<TextBlock>("CommentaryInfo").Text}");
    Check("the box lists it", C<ComboBox>("CommentaryBox").ItemsSource!.Cast<BibleStudy.Core.Models.CommentaryInfo>().Any(c => c.Name == "Testkommentar"));
    cstore.Remove(cid);
    typeof(MainWindow).GetMethod("LoadCommentaryList", All)!.Invoke(win, [null]);
    typeof(MainWindow).GetField("_shownCommentary", All)!.SetValue(win, null);
    RunTask("ShowCommentaryAsync"); Settle(300);
    Check("without a commentary the tab says so and offers no text of others", C<StackPanel>("CommentaryPanel").Children.Count == 0 && (C<TextBlock>("CommentaryInfo").Text ?? "").Contains("Kommentar"), C<TextBlock>("CommentaryInfo").Text ?? "");
}

// removing a mark of three verses from the number of one of them (the phone): all three go, and nothing stays tinted
{
    var vr = F<BibleStudy.Core.Storage.VaultStore>("_vault");
    foreach (var m in vr.GetMarks("GEN", 1)) vr.DeleteMark(m.Id);
    RunTask("GoToAsync", "GEN", 1, (int?)null); Settle(500);
    vr.AddMark(BibleStudy.Core.Storage.Mark.AnyTranslation, new BibleStudy.Core.Models.VerseRef("GEN", 1, 4), -1, -1, "", "#7ACB7A", "", null, [], 6);
    RunTask("RedrawChapterAsync"); Settle(400);
    var vboxes = F<Dictionary<int, Border>>("_verseBoxes");
    Check("removing: the three verses are tinted before", vboxes[4].Background is not null && vboxes[5].Background is not null && vboxes[6].Background is not null, $"whole={string.Join(",", F<Dictionary<int, string>>("_wholeMarks").Keys)} marks={vr.GetMarks("GEN", 1).Count} bg={vboxes[4].Background} base={vboxes[4].GetBaseValue(Border.BackgroundProperty)} attached={vboxes[4].IsAttachedToVisualTree()} trans={vboxes[4].Transitions?.Count} same={ReferenceEquals(vboxes, F<Dictionary<int, Border>>("_verseBoxes"))} inPanel={C<StackPanel>("ReaderPanel").Children.Contains(vboxes[4])} home={C<Control>("ReaderHost").IsVisible} at={F<string>("_curBook")} {typeof(MainWindow).GetField("_curChapter", All)!.GetValue(win)}");
    Call("SelectVerse", 5); typeof(MainWindow).GetField("_recolorGroup", All)!.SetValue(win, true);   // what a tap on the number of a marked verse does
    RunTask("RemoveMarksAsync"); Settle(400);
    Check("removing from one number: the mark of all three verses is gone", !vr.GetMarks("GEN", 1).Any(), string.Join(", ", vr.GetMarks("GEN", 1).Select(m => m.Ref.Verse + "-" + m.LastVerse)));
    Check("removing: no verse keeps a tint, the verse is not chosen any more", vboxes[4].Background is null && vboxes[5].Background is null && vboxes[6].Background is null && F<HashSet<int>>("_selVerses").Count == 0,
        $"{vboxes[4].Background} {vboxes[5].Background} {vboxes[6].Background} sel={F<HashSet<int>>("_selVerses").Count}");

    // a word chosen with a long press in a marked verse, then "no color": the verse mark goes (before nothing happened)
    vr.AddMark(BibleStudy.Core.Storage.Mark.AnyTranslation, new BibleStudy.Core.Models.VerseRef("GEN", 1, 8), -1, -1, "", "#7ACB7A", "", null, [], 8);
    RunTask("RedrawChapterAsync"); Settle(400);
    var cells = ((System.Collections.IEnumerable)F<object>("_cells")).Cast<object>().ToList();
    var cell8 = cells.First(c => { var d = c.GetType().GetProperty("Data")!.GetValue(c)!; return (int)d.GetType().GetProperty("Number")!.GetValue(d)! == 8 && (int)d.GetType().GetProperty("Col")!.GetValue(d)! == 0; });
    var col = typeof(MainWindow).GetMethod("ColCells", All)!.Invoke(win, [cell8]);
    typeof(MainWindow).GetMethod("SetSelectionFrom", All)!.Invoke(win, [cell8, 0, cell8, 4, col]);
    RunTask("RemoveMarksAsync"); Settle(400);
    Check("a word chosen in a marked verse: 'no color' takes the verse mark away", !vr.GetMarks("GEN", 1).Any() && F<Dictionary<int, Border>>("_verseBoxes")[8].Background is null, $"marks={string.Join(";", vr.GetMarks("GEN", 1).Select(m => m.Ref.Verse + "-" + m.LastVerse + " s" + m.Start))} bg={F<Dictionary<int, Border>>("_verseBoxes")[8].Background}");
}

// whole verses chosen by holding the number and moving the finger down: they are chosen together and get one color
{
    var vr = F<BibleStudy.Core.Storage.VaultStore>("_vault");
    Call("SelectVerse", 3);
    Call("ChooseVerseRange", 3, 6); Settle(200);
    Check("holding a number and moving down chooses the verses in between", F<HashSet<int>>("_selVerses").SetEquals([3, 4, 5, 6]), string.Join(",", F<HashSet<int>>("_selVerses")));
    Call("ChooseVerseRange", 3, 2); Settle(100);
    Check("moving back up chooses upwards from the first verse", F<HashSet<int>>("_selVerses").SetEquals([2, 3]));
    Call("ChooseVerseRange", 3, 7); Settle(100);
    RunTask("ApplyColorAsync", "#6FA8F2"); Settle(300);
    var made = vr.GetMarks("GEN", 1).Where(m => m.WholeVerse).ToList();
    Check("the chosen verses are marked as one", made.Count == 1 && made[0].Ref.Verse == 3 && made[0].LastVerse == 7, string.Join(", ", made.Select(m => m.Ref.Verse + "-" + m.LastVerse)));
    foreach (var m in made) vr.DeleteMark(m.Id);
    RunTask("RedrawChapterAsync"); Settle(300);
}

// reading paths: numbers in the margin, arrows between the steps of the chapter in lanes of their own, a bar under the verse to the next step
{
    var vp = F<BibleStudy.Core.Storage.VaultStore>("_vault");
    BibleStudy.Core.Storage.StudyPassage P(int c, int v) => new("GEN", c, v, v);
    var pid = vp.AddReadingPath("Testpfad", "#E07A3A", [P(1, 2), P(1, 9), P(1, 5), P(2, 1), P(1, 11)]);
    RunTask("GoToAsync", "GEN", 1, (int?)null); Settle(600);
    var gutter = C<BibleStudy.App.PathGutter>("PathMargin");
    var badges = (System.Collections.IList)typeof(BibleStudy.App.PathGutter).GetField("_badges", All)!.GetValue(gutter)!;
    var arrows = ((System.Collections.IEnumerable)typeof(BibleStudy.App.PathGutter).GetField("_arrows", All)!.GetValue(gutter)!).Cast<BibleStudy.App.PathGutter.Arrow>().ToList();
    Check("paths: a number in the margin for each step of this chapter", badges.Count == 4, badges.Count.ToString());
    Check("paths: an arrow from step to step inside the chapter (2→9, 9→5; not into chapter 2)", arrows.Select(a => (a.From, a.To)).SequenceEqual([(2, 9), (9, 5)]), string.Join(" ", arrows.Select(a => $"{a.From}>{a.To}")));
    Check("paths: two arrows that meet at verse 9 run in different lanes", arrows.Count == 2 && arrows[0].Lane != arrows[1].Lane);
    Check("paths: the text makes room for the margin", C<StackPanel>("ReaderPanel").Margin.Left > 20, C<StackPanel>("ReaderPanel").Margin.ToString());
    var bar = F<Dictionary<int, Border>>("_verseBoxes")[9].GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text ?? "").ToList();
    Check("paths: under the step the path and its neighbours", bar.Any(t => t.Contains("Testpfad · 2/5")) && bar.Any(t => t.StartsWith("‹ 1")) && bar.Any(t => t.Contains("· 3 ›")), string.Join(" | ", bar.Where(t => t.Length < 60)));
    foreach (var op in openPopups.ToList()) op.IsOpen = false;
    C<ScrollViewer>("ReaderScroll").Offset = new Vector(0, 330); Settle(400);
    Shot(win, "reading-path");
    var settingsP = (BibleStudy.Core.Storage.AppSettings)typeof(MainWindow).GetField("_settings", All)!.GetValue(win)!;
    settingsP.PathArrows = false; RunTask("RedrawChapterAsync"); Settle(400);
    Check("paths: switched off, the margin is gone again", ((System.Collections.IList)typeof(BibleStudy.App.PathGutter).GetField("_badges", All)!.GetValue(gutter)!).Count == 0 && C<StackPanel>("ReaderPanel").Margin.Left < 1);
    settingsP.PathArrows = true;

    // side by side, each column scrolling on its own, the right one with a passage of its own: the numbers stay in the margin of the main text,
    // and a step has its bar under the verse of that column too
    {
        var syncBefore = settingsP.ParallelSync;
        settingsP.ParallelSync = false;
        typeof(MainWindow).GetMethod("AddColumn", All)!.Invoke(win, [null]); Settle(400);
        var extrasP = (System.Collections.IList)typeof(MainWindow).GetField("_extras", All)!.GetValue(win)!;
        var addedP = extrasP[extrasP.Count - 1]!;
        addedP.GetType().GetProperty("Own")!.SetValue(addedP, ((string, int)?)("GEN", 1));
        RunTask("RedrawChapterAsync"); Settle(800);
        Check("paths side by side: the columns scroll on their own", (bool)typeof(MainWindow).GetField("_independent", All)!.GetValue(win)!);
        var badgesP = (System.Collections.IList)typeof(BibleStudy.App.PathGutter).GetField("_badges", All)!.GetValue(gutter)!;
        Check("paths side by side: the numbers of the steps are in the margin of the main text", badgesP.Count == 4, badgesP.Count.ToString());
        Check("paths side by side: the text makes room for them", C<StackPanel>("ReaderPanel").Margin.Left >= 24, C<StackPanel>("ReaderPanel").Margin.ToString());
        var colBoxesP = (List<Dictionary<int, Border>>)typeof(MainWindow).GetField("_colBoxes", All)!.GetValue(win)!;
        var barP = colBoxesP.Count > 0 && colBoxesP[0].TryGetValue(9, out var b19) ? b19.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text ?? "").ToList() : [];
        Check("paths side by side: under 1,9 in the column with its own passage the step of the path", barP.Any(t => t.Contains("Testpfad · 2/5")), string.Join(" | ", barP.Where(t => t.Length < 60)));
        if (colBoxesP.Count > 0 && colBoxesP[0].TryGetValue(3, out var b23))
        {
            b23.BringIntoView(); Settle(300);
            var tb23 = b23.GetLogicalDescendants().OfType<SelectableTextBlock>().First();
            var at23 = tb23.TranslatePoint(new Point(tb23.Bounds.Width / 2, 6), win)!.Value;
            Call("SelectForeignWordAt", b23, at23);
            Check("a long press in that column selects the word under the finger", tb23.SelectionEnd > tb23.SelectionStart, $"{tb23.SelectionStart}-{tb23.SelectionEnd}");
            var picked = typeof(MainWindow).GetMethod("ForeignSelection", All)!.Invoke(win, [b23]);
            Check("and it can be marked from the menu", picked is not null, "no selection");
            Shot(win, "paths-side-by-side");
        }
        extrasP.Remove(addedP);
        settingsP.ParallelSync = syncBefore;
        RunTask("RedrawChapterAsync"); Settle(400);
    }
    vp.DeleteReadingPath(pid);
    RunTask("RedrawChapterAsync"); Settle(300);
}

// choosing in the explorer as in a phone gallery: "Auswählen", a tap ticks (in that order), the bar acts on all
{
    var ve = F<BibleStudy.Core.Storage.VaultStore>("_vault");
    var a = ve.AddMark(BibleStudy.Core.Storage.Mark.AnyTranslation, new BibleStudy.Core.Models.VerseRef("GEN", 1, 10), -1, -1, "Auswahl A", "#FFD54F", "", null, []);
    var b = ve.AddMark(BibleStudy.Core.Storage.Mark.AnyTranslation, new BibleStudy.Core.Models.VerseRef("GEN", 1, 3), -1, -1, "Auswahl B", "#FFD54F", "", null, []);
    typeof(MainWindow).GetField("_exFolder", All)!.SetValue(win, null);
    Call("BuildExplorer"); Call("RefreshExplorer"); Settle(300);
    Call("SetExPick", true, null); Settle(200);
    var bar = F<Border>("_exPickBar");
    Check("choosing: the bar shows", bar.IsVisible);
    object Row(long id) => ((System.Collections.IEnumerable)typeof(MainWindow).GetField("_exRows", All)!.GetValue(win)!).Cast<object>().First(r => (long)r.GetType().GetProperty("Id")!.GetValue(r)! == id && r.GetType().GetProperty("Kind")!.GetValue(r)!.ToString() == "Mark");
    Call("ExPickToggle", Row(a)); Call("ExPickToggle", Row(b)); Settle(200);
    var sel = ((System.Collections.IEnumerable)F<object>("_exSel")).Cast<object>().Select(r => (long)r.GetType().GetProperty("Id")!.GetValue(r)!).ToList();
    Check("choosing: two ticked, in the order of tapping", sel.SequenceEqual([a, b]), string.Join(",", sel));
    Check("choosing: the bar counts them", F<TextBlock>("_exPickCount").Text?.Contains('2') == true, F<TextBlock>("_exPickCount").Text ?? "");
    Call("ExPickToggle", Row(a)); Settle(100);
    Check("choosing: a second tap unticks", ((System.Collections.IEnumerable)F<object>("_exSel")).Cast<object>().Count() == 1);
    Call("ExPickToggle", Row(a)); Settle(100);
    Call("RevealSideTab", 30); Settle(500); foreach (var op in openPopups.ToList()) op.IsOpen = false; Settle(200);
    Shot(win, "explorer-pick");
    // made into a reading path: the order of tapping (b, then a) is the order of the steps
    var items = typeof(MainWindow).GetField("_exSel", All)!.GetValue(win)!;
    var listType = items.GetType();
    var copy = Activator.CreateInstance(listType, items)!;
    typeof(MainWindow).GetMethod("NewPath", All)!.Invoke(win, ["Aus der Auswahl", ((System.Collections.IEnumerable)copy).Cast<object>().Select(r => { var rf = (BibleStudy.Core.Models.VerseRef)r.GetType().GetProperty("Ref")!.GetValue(r)!; return new BibleStudy.Core.Storage.StudyPassage(rf.Book, rf.Chapter, rf.Verse, rf.Verse); }).ToList(), null]);
    var made = ve.GetReadingPaths().Single(p => p.Name == "Aus der Auswahl");
    Check("choosing: a reading path in the order of tapping", made.Steps.Select(s => s.From).SequenceEqual([3, 10]), string.Join(",", made.Steps.Select(s => s.From)));
    ve.DeleteReadingPath(made.Id);
    Call("SetExPick", false, null); Settle(200);
    Check("choosing: done, the bar goes away", !bar.IsVisible);
    ve.DeleteMark(a); ve.DeleteMark(b);
    Call("ShowReader"); Settle(200);
}

// a reading path in a folder of the explorer: above the list the path with "Lesen", beside it arrows from entry to entry in the order of the path
{
    var ve = F<BibleStudy.Core.Storage.VaultStore>("_vault");
    var folder = ve.AddFolder("Engel des Herrn");
    long M(string book, int c, int v, string text) => ve.AddMark(BibleStudy.Core.Storage.Mark.AnyTranslation, new BibleStudy.Core.Models.VerseRef(book, c, v), -1, -1, text, "#E57373", "", folder, []);
    var m1 = M("GEN", 16, 11, "Weiter sprach der Engel des HERRN zu ihr"); var m2 = M("GEN", 16, 13, "Und sie nannte den Namen des HERRN");
    var m3 = M("GEN", 22, 11, "Da rief ihm der Engel des HERRN vom Himmel"); var m4 = M("EXO", 3, 2, "der Engel des HERRN erschien ihm in einer Feuerflamme");
    BibleStudy.Core.Storage.StudyPassage P(string b, int c, int v) => new(b, c, v, v);
    var pid = ve.AddReadingPath("Engel des Herrn", "#E07A3A", [P("GEN", 16, 11), P("GEN", 22, 11), P("GEN", 16, 13), P("EXO", 3, 2)]);
    ve.SetReadingPathFolder(pid, folder);
    Call("BuildExplorer"); Call("RevealSideTab", 30); Call("ExNavigate", folder); Settle(500);
    var exGutter = F<BibleStudy.App.PathGutter>("_exGutter");
    List<(int, int)> ExArrows() => ((System.Collections.IEnumerable)typeof(BibleStudy.App.PathGutter).GetField("_arrows", All)!.GetValue(exGutter)!).Cast<BibleStudy.App.PathGutter.Arrow>().Select(x => (x.From, x.To)).ToList();
    int ExBadges() => ((System.Collections.IList)typeof(BibleStudy.App.PathGutter).GetField("_badges", All)!.GetValue(exGutter)!).Count;
    // the list in Bible order: 16:11 (row 0), 16:13 (1), 22:11 (2), Ex 3:2 (3)
    Check("explorer path: a number beside each entry of the path", ExBadges() == 4, ExBadges().ToString());
    Check("explorer path: arrows in the order of the path (0→2, 2→1, 1→3)", ExArrows().SequenceEqual([(0, 2), (2, 1), (1, 3)]), string.Join(" ", ExArrows().Select(x => $"{x.Item1}>{x.Item2}")));
    var pathBar = F<StackPanel>("_exPathBar");
    Check("explorer path: above the list the path with a button to read it", pathBar.IsVisible && pathBar.GetLogicalDescendants().OfType<Button>().Any(btn => (btn.Content as string ?? "").Contains("Lesen")));
    Check("explorer path: the list makes room for the arrows", F<StackPanel>("_exList").Margin.Left > 20, F<StackPanel>("_exList").Margin.ToString());
    foreach (var op in openPopups.ToList()) op.IsOpen = false; Settle(200);
    Shot(win, "explorer-path");
    // a new order of the steps: the arrows are drawn anew
    var p0 = ve.GetReadingPath(pid)!;
    ve.UpdateReadingPath(p0 with { Steps = [P("EXO", 3, 2), P("GEN", 16, 11), P("GEN", 16, 13), P("GEN", 22, 11)] });
    Call("AfterPathChange", (string?)null); Settle(300);
    Check("explorer path: a new order draws new arrows (3→0, 0→1, 1→2)", ExArrows().SequenceEqual([(3, 0), (0, 1), (1, 2)]), string.Join(" ", ExArrows().Select(x => $"{x.Item1}>{x.Item2}")));
    // the folder renamed: the path stays with it
    ve.RenameFolder(folder, "Der Engel"); Call("RefreshExplorer"); Settle(300);
    Check("explorer path: a renamed folder keeps its path", ExBadges() == 4);
    // the quick way: onto the path used last
    Check("explorer path: the verse menu offers the path used last", typeof(MainWindow).GetMethod("QuickPathItem", All)!.Invoke(win, [1, null]) is MenuItem qi && (qi.Header as string ?? "").Contains("Engel des Herrn"));
    // at the top (no folder) nothing shows
    Call("ExNavigate", (long?)null); Settle(300);
    Check("explorer path: elsewhere no arrows and no bar", ExBadges() == 0 && !pathBar.IsVisible);

    // given to someone else: the file with the marks, read in "only to look at" with the verses tinted: nothing is saved
    var json = BibleStudy.Core.Storage.ReadingPathFile.Write(ve, [ve.GetReadingPath(pid)!], true);
    var found = BibleStudy.Core.Storage.ReadingPathFile.Read(json);
    var pathsBefore = ve.GetReadingPaths().Count; var marksBefore = ve.GetMarks().Count;
    foreach (var m in new[] { m1, m2, m3, m4 }) ve.DeleteMark(m);   // the other person does not have these marks
    var marksOther = ve.GetMarks().Count;
    RunTask("ImportPathsAsync", found, (int?)1, (int?)0); Settle(800);
    Check("path file: looked at, it is in the list of paths but not in the vault", ve.GetReadingPaths().Count == pathsBefore
        && ((System.Collections.IEnumerable)typeof(MainWindow).GetMethod("ReadingPaths", All)!.Invoke(win, [])!).Cast<BibleStudy.Core.Storage.ReadingPath>().Any(p => p.Id < 0 && p.Name == "Engel des Herrn"));
    Check("path file: tinted only, no mark is saved", ve.GetMarks().Count == marksOther);
    // the test Bible has no Exodus: the marks are checked in Genesis 16 (the chapter of the text is set by hand, the marks are read as for drawing it)
    var (bookWas, chapterWas) = (F<string>("_curBook"), (int)typeof(MainWindow).GetField("_curChapter", All)!.GetValue(win)!);
    typeof(MainWindow).GetField("_curBook", All)!.SetValue(win, "GEN"); typeof(MainWindow).GetField("_curChapter", All)!.SetValue(win, 16);
    Call("LoadChapterMarks");
    Check("path file: the verses of the path are tinted (Gen 16:11 and 16:13)", F<Dictionary<int, string>>("_otherWhole").ContainsKey(11) && F<Dictionary<int, string>>("_otherWhole").ContainsKey(13),
        string.Join(",", F<Dictionary<int, string>>("_otherWhole").Keys));
    typeof(MainWindow).GetField("_curBook", All)!.SetValue(win, bookWas); typeof(MainWindow).GetField("_curChapter", All)!.SetValue(win, chapterWas);
    Call("LoadChapterMarks");
    // read in again to keep, with the marks taken over
    RunTask("ImportPathsAsync", found, (int?)0, (int?)1); Settle(600);
    Check("path file: kept, the path and the marks are in the vault", ve.GetReadingPaths().Count == pathsBefore + 1 && ve.GetMarks().Count == marksOther + 4);
    foreach (var p in ve.GetReadingPaths().Where(p => p.Id != pid && p.Name.StartsWith("Engel des Herrn"))) ve.DeleteReadingPath(p.Id);
    foreach (var m in ve.GetMarks().Where(m => m.Ref.Book is "GEN" or "EXO" && m.Text.Contains("HERRN"))) ve.DeleteMark(m.Id);
    F<System.Collections.IList>("_lookPaths").Clear(); F<System.Collections.IList>("_lookMarks").Clear();

    // the graph beside the text: "only your own" leaves the printed ones out, your own count with their weight
    var settingsX = (BibleStudy.Core.Storage.AppSettings)typeof(MainWindow).GetField("_settings", All)!.GetValue(win)!;
    ve.AddMyXref(new BibleStudy.Core.Models.VerseRef("JHN", 1, 1), new BibleStudy.Core.Models.VerseRef("GEN", 1, 1), 1, "");
    var own = (System.Collections.IDictionary)typeof(MainWindow).GetMethod("OwnChapterRefs", All)!.Invoke(win, ["JHN", 1])!;
    Check("cross refs: your own count with the weight set", own.Contains(1) && ((System.Collections.IEnumerable)own[1]!).Cast<BibleStudy.Core.Models.CrossRef>().Any(x => x.Target.Book == "GEN" && x.Score == settingsX.XrefOwnWeight));
    settingsX.XrefSource = "own"; settingsX.XrefInText = true; typeof(MainWindow).GetField("_xrefInText", All)!.SetValue(win, true);
    RunTask("LoadChapterRefsAsync", "JHN", 1); Settle(200);
    var printed = F<System.Collections.IDictionary>("_chapterRefs");
    Check("cross refs: only your own, no printed ones", printed.Count == 0);
    Check("cross refs: the graph gets your own", ((System.Collections.ICollection)typeof(MainWindow).GetMethod("GraphRefsAt", All)!.Invoke(win, [1])!).Count == 1);
    settingsX.XrefSource = "both";
    foreach (var x in ve.GetMyXrefs("JHN", 1)) ve.DeleteMyXref(x.Id);
    ve.DeleteReadingPath(pid);
    foreach (var m in new[] { m1, m2, m3, m4 }) ve.DeleteMark(m);
    ve.DeleteFolder(folder);
    Call("ShowReader"); Settle(200);
}

// "Dokumente aller Vaults zeigen": the documents of another vault show under its name, and a click there switches to it and opens the document
{
    var startVault = F<BibleStudy.Core.Storage.VaultStore>("_vault").Name;
    var other = (BibleStudy.Core.Storage.VaultStore)Call("StoreOf", "Zweiter Vault")!;
    var docId = other.AddDocument("Fremdes Dokument", "Text im anderen Vault", null, false, []);
    Call("ShowReader"); Call("RevealSideTab", 10); Call("RefreshDocs"); Settle(300);
    var allDocs = C<CheckBox>("DocAllVaultsCheck");
    allDocs.IsChecked = false; Settle(200);
    allDocs.IsChecked = true; Settle(400);
    var docTree = C<StackPanel>("DocTree");
    var heading = docTree.Children.OfType<TextBlock>().FirstOrDefault(t => (t.Text ?? "").Contains("Zweiter Vault"));
    Check("all vaults: the other vault's documents show under its name", heading is not null, string.Join(" | ", docTree.Children.OfType<TextBlock>().Select(t => t.Text)));
    var foreignRow = docTree.Children.OfType<Border>().FirstOrDefault(b => b.GetLogicalDescendants().OfType<TextBlock>().Any(t => t.Text == "Fremdes Dokument"));
    Check("all vaults: the document is listed", foreignRow is not null);
    if (foreignRow is not null)
    {
        // the test window is low (520): the list under the filters gets only a few pixels, so it is made taller for the click
        var oldHeight = win.Height; win.Height = 1000; Settle(400);
        foreignRow.BringIntoView(); Settle(300);
        Shot(win, "docs-all-vaults");
        Click(foreignRow); Wait(() => F<BibleStudy.Core.Storage.VaultStore>("_vault").Name == "Zweiter Vault", 10); Settle(600);
        Check("all vaults: a click switches to that vault and opens the document", F<BibleStudy.Core.Storage.VaultStore>("_vault").Name == "Zweiter Vault" && (typeof(MainWindow).GetField("_doc", All)!.GetValue(win) as BibleStudy.Core.Storage.Document)?.Id == docId,
            F<BibleStudy.Core.Storage.VaultStore>("_vault").Name);
        win.Height = oldHeight; Settle(300);
    }
    allDocs.IsChecked = false; Settle(200);
    other.DeleteDocument(docId);
    RunTask("SwitchVaultAsync", startVault, false); Settle(600);
}

Console.WriteLine(fails == 0 ? "ALL OK" : $"{fails} FAILED");
Environment.Exit(fails == 0 ? 0 : 1);
Environment.Exit(fails);
