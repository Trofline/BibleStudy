using Avalonia.Input.Platform;
using Avalonia.Threading;
using BibleStudy.Core.Models;
using BibleStudy.Core.Search;

namespace BibleStudy.App;

/// <summary>
/// Bible passages from outside: a link "biblestudy://Joh3,16" (WhatsApp, e-mail, Obsidian, a web page) and, on Android, text shared with the app
/// or chosen with "BibleStudy" in the menu of a selection. The passage opens in the reader; several become a list of verses; a word is searched.
/// </summary>
public partial class MainWindow
{
    int _passageTries;
    bool _linksStarted;

    /// <summary>The program has started: a link that started it is opened as soon as the books are there; a desktop program also takes the links of later clicks.</summary>
    void StartLinks()
    {
#if !MOBILE
        if (!_linksStarted) { LinkInbox.RegisterScheme(); LinkInbox.Listen(App.OfferPassage); }
#endif
        _linksStarted = true;
        DispatcherTimer.RunOnce(OpenPendingPassage, TimeSpan.FromMilliseconds(300));
    }

    /// <summary>Opens what a link or a shared text named, once the books of the translation are loaded (right after the start they are not yet).</summary>
    internal void OpenPendingPassage()
    {
        if (_books.Count == 0)
        {
            if (++_passageTries < 120) DispatcherTimer.RunOnce(OpenPendingPassage, TimeSpan.FromMilliseconds(500));
            return;
        }
        _passageTries = 0;
        if (App.TakePendingPassage() is not { } text) return;
#if !MOBILE
        RestoreFromTray();
#endif
        _ = OpenPassageTextAsync(text);
    }

    async Task OpenPassageTextAsync(string text)
    {
        try
        {
            var isLink = VerseLink.IsVerseLink(text);
            var passage = isLink ? VerseLink.Passage(text) : text.Trim();
            if (string.IsNullOrWhiteSpace(passage)) { StatusText.Text = L("Dieser Link nennt keine Bibelstelle."); return; }
            if (PassageParser.Parse(passage, out var single) is { } direct) { await OpenPassagesAsync(direct, single, passage); return; }
            // a shared text: the passages in it ("Lies mal Joh 3,16 und Röm 8,28")
            var found = PassageParser.Extract(passage, out _);
            if (found.Count > 0) { await OpenPassagesAsync(found, found.Count == 1, string.Join("; ", found.Select(Written))); return; }
            if (passage.Length <= 80) { await QuickJumpAsync(passage); return; }   // a word or a phrase is searched
            StatusText.Text = L("Im geteilten Text wurde keine Bibelstelle gefunden.");
        }
        catch (Exception ex) { ErrorLog.Write(ex); StatusText.Text = L("Fehler: ") + ex.Message; }
    }

    async Task OpenPassagesAsync(IReadOnlyList<PassageRef> passages, bool single, string written)
    {
        if (single)
        {
            var p = passages[0];
            await GoToAsync(p.Book, p.Chapter, p.FromVerse);
            return;
        }
        if (TranslationBox.SelectedItem is not TranslationInfo t) return;
        RevealSideTab(0);
        SearchBox.Text = written;
        await ShowPassageListAsync(t, passages);
    }

    /// <summary>A passage as it can be typed into the search again ("JHN 3,16-18"): the id of the book is read in every language.</summary>
    static string Written(PassageRef p) =>
        $"{p.Book} {p.Chapter}" + (p.FromVerse is int f ? "," + f + (p.ToChapter is int c2 ? $"-{c2},{p.ToVerse}" : p.ToVerse is int t && t != f ? "-" + t : "") : p.ToChapter is int ce ? "-" + ce : "");

    /// <summary>The chosen verses as a link that opens them in BibleStudy on any device (biblestudy://Joh3,16-18).</summary>
    async Task CopyLinkAsync()
    {
        var verses = _selVerses.Count > 0 ? _selVerses.ToHashSet() : _selected is int one ? [one] : [];
        if (verses.Count == 0 || Avalonia.Controls.TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard) { StatusText.Text = L("Erst einen Vers anklicken."); return; }
        var run = BibleStudy.Core.Storage.MarkLogic.Runs(verses).First();
        var url = VerseLink.ToUrl(_curBook, _curChapter, run.From, run.To);
        await clipboard.SetTextAsync(url);
        StatusText.Text = L("Link kopiert: {0}", url);
    }
}
