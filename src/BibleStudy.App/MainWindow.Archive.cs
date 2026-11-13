using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using BibleStudy.Core;
using BibleStudy.Core.Models;
using BibleStudy.Core.Search;
using BibleStudy.Core.Storage;

namespace BibleStudy.App;

/// <summary>
/// The archive of the Bible studies: which passage was read on which evening, with notes and the questions that came up. A live
/// Bible study can put itself in at the end; above a chapter that was read in a Bible study, the reader says when.
/// </summary>
public partial class MainWindow
{
    const int ArchiveTab = 33;
    string _archiveFilter = "";
    long _archiveEditing;

    string PassageLabel(StudyPassage p) =>
        p.From <= 0 ? $"{BookName(p.Book)} {p.Chapter}" : $"{BookName(p.Book)} {p.Chapter},{p.From}" + (p.To > p.From ? "–" + p.To : "");

    string PassagesLabel(IEnumerable<StudyPassage> passages) => string.Join("; ", passages.Select(PassageLabel));

    /// <summary>"Joh 10,1-18; Ps 23" → the passages; null when something in it is no Bible passage.</summary>
    static List<StudyPassage>? ParsePassages(string text)
    {
        if (text.Trim().Length == 0) return [];
        if (PassageParser.Parse(text, out _) is not { Count: > 0 } parsed) return null;
        return parsed.Select(p => new StudyPassage(p.Book, p.Chapter, p.FromVerse ?? 0, p.FromVerse is null ? 0 : p.ToVerse ?? p.FromVerse ?? 0)).ToList();
    }

    /// <summary>"Read in the Bible study on 12 Nov 2026" above a chapter; null when it never was.</summary>
    Control? StudiedHint(string book, int chapter)
    {
        List<StudySession> studies;
        try { studies = _vault.StudiesOf(book, chapter); } catch (Microsoft.Data.Sqlite.SqliteException) { return null; }
        if (studies.Count == 0) return null;
        var last = studies[0];
        var text = studies.Count == 1 ? L("In der Bibelstunde am {0:d}: {1}", last.Day.ToDateTime(TimeOnly.MinValue), last.Title)
            : L("In der Bibelstunde am {0:d}: {1} (und {2}× davor)", last.Day.ToDateTime(TimeOnly.MinValue), last.Title, studies.Count - 1);
        var b = new Button { Content = new TextBlock { Text = "◷ " + text, TextWrapping = TextWrapping.Wrap }, Classes = { "tool" }, Width = double.NaN, HorizontalAlignment = HorizontalAlignment.Left, Padding = new(8, 2), Margin = new(4, -8, 0, 12) };
        ToolTip.SetTip(b, L("Öffnet das Archiv der Bibelstunden"));
        b.Click += (_, _) => { _archiveFilter = ""; OpenWork(NavGroups.First(g => g.Name == "Material"), ArchiveTab); };
        return b;
    }

    void BuildArchiveUi()
    {
        ArchivePanel.Children.Clear();
        ArchivePanel.Children.Add(new TextBlock { Text = L("ARCHIV DER BIBELSTUNDEN"), Classes = { "eyebrow" } });
        ArchivePanel.Children.Add(new TextBlock { Text = L("Welche Stelle wann dran war, mit Notizen und den Fragen des Abends. Über einem Kapitel, das schon dran war, steht im Lesefenster, wann."), Classes = { "caption" }, TextWrapping = TextWrapping.Wrap });

        ArchivePanel.Children.Add(StudyForm(_archiveEditing > 0 ? _vault.GetStudy(_archiveEditing) : null));

        var all = _vault.GetStudies();
        if (all.Count == 0)
        {
            ArchivePanel.Children.Add(new TextBlock { Text = L("Noch keine Bibelstunde festgehalten. Trage oben die erste ein, oder beende eine Live-Bibelstunde mit „im Archiv festhalten“."), Classes = { "caption" }, TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 0) });
            return;
        }
        var search = new TextBox { PlaceholderText = L("Im Archiv suchen (Titel, Stelle, Notiz) …"), Text = _archiveFilter, Margin = new(0, 10, 0, 0) };
        var list = new StackPanel { Spacing = 8 };
        void Fill()
        {
            list.Children.Clear();
            var f = _archiveFilter.Trim();
            var shown = all.Where(s => f.Length == 0 || s.Title.Contains(f, StringComparison.CurrentCultureIgnoreCase) || s.Notes.Contains(f, StringComparison.CurrentCultureIgnoreCase)
                || s.Questions.Contains(f, StringComparison.CurrentCultureIgnoreCase) || PassagesLabel(s.Passages).Contains(f, StringComparison.CurrentCultureIgnoreCase)).ToList();
            list.Children.Add(new TextBlock { Text = L("{0} Bibelstunden, die letzte am {1:d}", shown.Count, all[0].Day.ToDateTime(TimeOnly.MinValue)), Classes = { "caption" } });
            foreach (var s in shown) list.Children.Add(StudyCard(s));
        }
        search.TextChanged += (_, _) => { _archiveFilter = search.Text ?? ""; Fill(); };
        ArchivePanel.Children.Add(search);
        ArchivePanel.Children.Add(list);
        Fill();
    }

    /// <summary>The form for a new Bible study, or for changing one; the passage starts as what is open in the reader.</summary>
    Control StudyForm(StudySession? editing)
    {
        var day = new CalendarDatePicker { SelectedDate = editing?.Day.ToDateTime(TimeOnly.MinValue) ?? DateTime.Today, HorizontalAlignment = HorizontalAlignment.Left };
        var title = new TextBox { PlaceholderText = L("Thema oder Titel, z. B. Der gute Hirte"), Text = editing?.Title ?? "" };
        var here = _curBook.Length > 0 ? new StudyPassage(_curBook, _curChapter, _selVerses.Count > 1 ? _selVerses.Min() : 0, _selVerses.Count > 1 ? _selVerses.Max() : 0) : null;
        var passages = new TextBox { PlaceholderText = L("Bibelstellen, z. B. Joh 10,1-18; Ps 23"), Text = editing is not null ? PassagesLabel(editing.Passages) : here is not null ? PassageLabel(here) : "" };
        var notes = new TextBox { PlaceholderText = L("Notizen: Wie war es, was kam heraus, was nehmen wir mit?"), Text = editing?.Notes ?? "", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 60 };
        var questions = new TextBox { PlaceholderText = L("Fragen, die aufkamen (eine pro Zeile)"), Text = editing?.Questions ?? "", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 48 };
        var info = new TextBlock { Classes = { "caption" }, TextWrapping = TextWrapping.Wrap };
        var save = new Button { Content = editing is null ? L("Bibelstunde festhalten") : L("Änderungen speichern"), Classes = { "primary" } };
        save.Click += (_, _) =>
        {
            if (ParsePassages(passages.Text ?? "") is not { } ps) { info.Text = L("„{0}“ ist keine Bibelstelle.", passages.Text ?? ""); return; }
            var t = (title.Text ?? "").Trim();
            if (t.Length == 0) t = ps.Count > 0 ? PassageLabel(ps[0]) : "";
            if (t.Length == 0) { info.Text = L("Gib einen Titel oder eine Bibelstelle an."); return; }
            var d = DateOnly.FromDateTime(day.SelectedDate ?? DateTime.Today);
            if (editing is null) _vault.AddStudy(d, t, ps, notes.Text ?? "", questions.Text ?? "");
            else _vault.UpdateStudy(editing with { Day = d, Title = t, Passages = ps, Notes = notes.Text ?? "", Questions = questions.Text ?? "" });
            _archiveEditing = 0;
            BuildArchiveUi();
            if (_curBook.Length > 0) _ = ShowChapterAsync(null, true);
        };
        var buttons = new WrapPanel { ItemSpacing = 6, Children = { save } };
        if (editing is not null)
        {
            var cancel = new Button { Content = L("Abbrechen") };
            cancel.Click += (_, _) => { _archiveEditing = 0; BuildArchiveUi(); };
            buttons.Children.Add(cancel);
        }
        return new Border
        {
            Classes = { "card" },
            Child = new StackPanel
            {
                Spacing = 6,
                Children =
                {
                    new TextBlock { Text = editing is null ? L("Neue Bibelstunde") : L("Bibelstunde ändern"), FontWeight = FontWeight.SemiBold },
                    day, title, passages, notes, questions, buttons, info,
                },
            },
        };
    }

    Border StudyCard(StudySession s)
    {
        var panel = new StackPanel { Spacing = 4 };
        panel.Children.Add(new TextBlock { Text = s.Day.ToDateTime(TimeOnly.MinValue).ToString("D", Loc.Culture), Classes = { "caption" } });
        panel.Children.Add(new TextBlock { Text = s.Title, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap });
        if (s.Passages.Count > 0)
        {
            var chips = new WrapPanel();
            foreach (var p in s.Passages)
                chips.Children.Add(FilterChip(PassageLabel(p), false, null, async () => await GoToAsync(p.Book, p.Chapter, Math.Max(1, p.From)), L("Springt zur Stelle")));
            panel.Children.Add(chips);
        }
        if (s.Notes.Trim().Length > 0) panel.Children.Add(new SelectableTextBlock { Text = s.Notes.Trim(), TextWrapping = TextWrapping.Wrap });
        if (s.Questions.Trim().Length > 0)
            panel.Children.Add(new Expander { Header = L("Fragen des Abends"), HorizontalAlignment = HorizontalAlignment.Stretch, Content = new SelectableTextBlock { Text = s.Questions.Trim(), TextWrapping = TextWrapping.Wrap } });
        var edit = new Button { Content = L("Ändern") };
        edit.Click += (_, _) => { _archiveEditing = s.Id; BuildArchiveUi(); };
        var del = new Button { Content = L("Löschen"), Classes = { "tool" }, Width = double.NaN, Padding = new(10, 0) };
        del.Click += (_, _) =>
        {
            var yes = new Button { Content = L("Wirklich löschen"), Classes = { "primary" } };
            var fly = new Flyout { Content = new StackPanel { Spacing = 6, Children = { new TextBlock { Text = L("Diese Bibelstunde aus dem Archiv löschen?") }, yes } } };
            yes.Click += (_, _) => { _vault.DeleteStudy(s.Id); fly.Hide(); BuildArchiveUi(); };
            fly.ShowAt(del);
        };
        panel.Children.Add(new WrapPanel { ItemSpacing = 6, Margin = new(0, 4, 0, 0), Children = { edit, del } });
        return new Border { Classes = { "card" }, Child = panel };
    }
}
