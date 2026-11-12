using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using BibleStudy.Core.Models;
using BibleStudy.Core.Providers;

namespace BibleStudy.App;

/// <summary>
/// Finding the commentaries. They sat only behind "Books" → "Commentaries" and came after a restart, so someone who had turned them
/// on could look for them in vain. Now they come at once, the verse overview shows the note on the clicked verse, a right click on a
/// verse offers "Commentary on this verse", and Ctrl+K knows "Commentaries".
/// </summary>
public partial class MainWindow
{
    /// <summary>The setting in "Appearance": the commentaries come and go at once (the "Books" tab is built anew with or without them).</summary>
    void ApplyCommentariesSetting(bool on)
    {
        CommentariesOn = on;
        NavGroups = MakeNavGroups();
        if (_windows.TryGetValue("group:Bücher", out var books)) { if (books.Win is not null) books.Win.Close(); else Restore(books); }
        _shownCommentary = null;
        if (on) ShowUndoToast(L("Kommentare sind an. Du findest sie in der Leiste unter „Bücher“, in der Vers-Übersicht und mit Rechtsklick auf einen Vers."), () => { OpenCommentary(null); return Task.CompletedTask; }, L("Kommentare öffnen"));
        else StatusText.Text = L("Kommentare sind aus.");
    }

    /// <summary>Opens the commentaries at the current chapter; with a verse that verse is chosen first, so its note is marked.</summary>
    void OpenCommentary(VerseRef? r)
    {
        if (!CommentariesOn) return;
        if (r is { } v && v.Book == _curBook && v.Chapter == _curChapter) SelectVerse(v.Verse);
        if (NavGroups.FirstOrDefault(g => g.Name == "Bücher") is { } books) OpenWork(books, CommentaryTab);
    }

    /// <summary>The commentary chosen last (or the first one), loading the list once.</summary>
    async Task<CommentaryInfo?> CurrentCommentaryAsync()
    {
        if (_commentaries.Count == 0) _commentaries = await _hello.GetCommentariesAsync();
        return _commentaries.FirstOrDefault(c => c.Id == _settings.Commentary) ?? _commentaries.FirstOrDefault();
    }

    /// <summary>The part of the verse overview with the note of the chosen commentary on this verse.</summary>
    async Task FillGuideCommentaryAsync(VerseRef r, int version, StackPanel into)
    {
        try
        {
            var c = await CurrentCommentaryAsync();
            if (c is null || version != _guideVersion) return;
            var chapter = await _hello.GetCommentaryAsync(c.Id, r.Book, r.Chapter);
            if (version != _guideVersion) return;
            into.Children.Clear();
            var open = new Button { Content = L("Ganzen Kommentar öffnen"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new(0, 4, 0, 0) };
            open.Click += (_, _) => OpenCommentary(r);
            if (chapter is null || chapter.Notes.Count == 0)
            {
                into.Children.Add(Muted(L("{0} hat zu {1} {2} keine Anmerkungen.", c.Name, BookName(r.Book), r.Chapter)));
                into.Children.Add(open);
                return;
            }
            var last = r.Book == _curBook && r.Chapter == _curChapter && _verseBoxes.Count > 0 ? _verseBoxes.Keys.Max() : 0;
            var ranges = CommentaryRanges.Of(c.Id, chapter.Notes, r.Chapter, last);
            var i = ranges.FindLastIndex(x => x.From <= r.Verse && r.Verse <= x.To);
            if (i < 0)
            {
                into.Children.Add(Muted(L("Zu Vers {0} hat {1} keine eigene Anmerkung.", r.Verse, c.Name)));
                into.Children.Add(open);
                return;
            }
            var text = chapter.Notes[i].Text.Trim();
            // in a long section the part about this verse comes first, when the note names it
            var paras = text.Split('\n').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
            var at = ranges[i].From != ranges[i].To ? paras.FindIndex(p => CommentaryRanges.Mentions(p, r.Chapter, r.Verse, c.Id == "john-calvin")) : -1;
            var shown = string.Join("\n\n", (at > 0 ? paras.Skip(at) : paras).Take(3));
            if (shown.Length > 900) shown = shown[..900].TrimEnd() + " …";
            into.Children.Add(new TextBlock { Text = c.Name + " · " + CommentaryRanges.Label(ranges[i].From, ranges[i].To), Classes = { "caption" }, FontWeight = FontWeight.SemiBold });
            into.Children.Add(new SelectableTextBlock { Text = shown, TextWrapping = TextWrapping.Wrap, LineHeight = 21 });
            into.Children.Add(open);
        }
        catch (Exception ex)
        {
            if (version != _guideVersion) return;
            into.Children.Clear();
            into.Children.Add(Muted(L("Kommentar nicht verfügbar: ") + ex.Message));
        }
    }
}
