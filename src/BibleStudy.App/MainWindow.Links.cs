using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using BibleStudy.Core.Models;
using BibleStudy.Core.Search;

namespace BibleStudy.App;

/// <summary>
/// Links to Bible passages: "biblestudy://open/JHN.3.16" opens the program at that place, the web address
/// (https://trofline.github.io/BibleStudy/v/?JHN.3.16) forwards to it. A text shared from another app ("Share → BibleStudy")
/// goes the same way: its passages are found and opened, several go into the reading list.
/// </summary>
public partial class MainWindow
{
    /// <summary>Called once the program has started: a link that started it (or came in meanwhile) is opened as soon as the books are there.</summary>
    void OpenLinkFromStart()
    {
        var tries = 0;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        timer.Tick += (_, _) =>
        {
            if (!App.HasPendingLink) { timer.Stop(); return; }
            if (_books.Count == 0 && ++tries < 60) return;   // the Bible is still loading (at most half a minute)
            timer.Stop();
            if (App.TakePendingLink() is { } text) _ = OpenLinkTextAsync(text);
        };
        timer.Start();
    }

    /// <summary>A link or a shared text: one passage is opened, several go into the reading list (the first one is opened too).</summary>
    internal async Task OpenLinkTextAsync(string text)
    {
#if !MOBILE
        RestoreFromTray();
#endif
        if (_books.Count == 0) { App.KeepLink(text); OpenLinkFromStart(); return; }   // not ready yet: again when the books are there
        var passages = BibleLinks.IsLink(text) ? BibleLinks.Parse(text) ?? [] : BibleLinks.FromSharedText(text);
        if (passages.Count == 0) { StatusText.Text = L("Keine Bibelstelle erkannt."); return; }
        var first = passages[0];
        if (_books.All(b => b.Id != first.Book)) { StatusText.Text = L("Buch in dieser Übersetzung nicht vorhanden."); return; }
        await GoToAsync(first.Book, first.Chapter, first.FromVerse);
        // a run of verses in one chapter is shown chosen, as if it had been picked by hand
        if (passages.Count == 1 && first.FromVerse is int from && first.ToVerse is int to && to > from && first.ToChapter is null && _curBook == first.Book && _curChapter == first.Chapter)
        {
            _selVerses.Clear();
            for (var v = from; v <= to; v++) _selVerses.Add(v);
            Paint();
        }
        if (passages.Count == 1) { StatusText.Text = L("Geöffnet: {0}", LinkLabel(first)); return; }
        var (verses, missing) = await ExpandPassagesAsync(passages);
        if (verses.Count > 0) AddToQueue(verses);
        StatusText.Text = L("{0} Stellen in die Leseliste aufgenommen.", passages.Count) + (missing.Count > 0 ? " " + L("Nicht in dieser Übersetzung: {0}", string.Join(", ", missing.Distinct().Take(5))) : "");
    }

    string LinkLabel(PassageRef p)
    {
        var s = $"{BookName(p.Book)} {p.Chapter}";
        if (p.FromVerse is int from)
        {
            s += ":" + from;
            if (p.ToChapter is int c2 && c2 != p.Chapter && p.ToVerse is int v2) s += $"–{c2}:{v2}";
            else if (p.ToVerse is int to && to > from) s += "–" + to;
        }
        else if (p.ToChapter is int last && last > p.Chapter) s += "–" + last;
        return s;
    }

    /// <summary>The chosen verses (or the verse that was clicked) as passages, one per run of verses.</summary>
    List<PassageRef> ChosenPassages()
    {
        var verses = _selVerses.Count > 0 ? _selVerses.ToHashSet() : _selected is int one ? [one] : [];
        return BibleStudy.Core.Storage.MarkLogic.Runs(verses).Select(x => BibleLinks.Passage(_curBook, _curChapter, x.From, x.To)).ToList();
    }

    /// <summary>
    /// Copies a link to the chosen verses. The web address is the one that works everywhere: messengers and e-mail make it clickable, and the page
    /// opens BibleStudy (or tells where to get it). The passage stands in front of it, so the message can be read without clicking.
    /// </summary>
    async Task CopyLinkAsync()
    {
        var passages = ChosenPassages();
        if (passages.Count == 0 || TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard) { StatusText.Text = L("Erst einen Vers anklicken."); return; }
        var label = string.Join("; ", passages.Select(LinkLabel));
        await clipboard.SetTextAsync($"{label}\n{BibleLinks.WebLink(passages)}");
        StatusText.Text = L("Link zu {0} kopiert. Wer BibleStudy hat, landet mit einem Tipp direkt an dieser Stelle.", label);
    }
}
