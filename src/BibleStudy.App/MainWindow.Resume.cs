using Avalonia;
using Avalonia.Threading;
using BibleStudy.Core;
using BibleStudy.Core.Storage;

namespace BibleStudy.App;

/// <summary>
/// The restart after a quick update is meant to feel like a blink: no closing animation, no start window, no slide-in, and the reader is where it was
/// (chapter, verse, scroll position, way back). Window place and open tabs come from what closing saves anyway, also when those settings are off.
/// </summary>
public partial class MainWindow
{
    ResumeState? _resume;   // from the program that just updated itself; until the first chapter is on screen
    bool _resuming;         // this start continues that program

    /// <summary>Notes where the reader is, for the version that starts next.</summary>
    void CaptureResume()
    {
        RememberPlace();
        _settings.Resume = new ResumeState
        {
            Book = _curBook, Chapter = _curChapter, Verse = _selected, OffsetX = ReaderScroll.Offset.X, OffsetY = ReaderScroll.Offset.Y, Home = Home.IsVisible,
            History = _history.Select(ToResume).ToList(), Forward = _forward.Select(ToResume).ToList(),
        };
        SaveSettings();
    }

    static ResumePlace ToResume(Place p) => new() { Book = p.Book, Chapter = p.Chapter, Verse = p.Verse, OffsetX = p.Offset?.X, OffsetY = p.Offset?.Y };

    static Place FromResume(ResumePlace r) => new(r.Book, r.Chapter, r.Verse) { Offset = r.OffsetY is { } y ? new Vector(r.OffsetX ?? 0, y) : null };

    /// <summary>Taken once: a later start is a normal start again, even if this one does not get far.</summary>
    void TakeResume()
    {
        _resume = _settings.Resume;
        _resuming = _resume is not null;
        if (_settings.Resume is null) return;
        _settings.Resume = null;
        SaveSettings();
    }

    /// <summary>The first chapter is drawn: back to the verse and the scroll position, and say quietly what happened.</summary>
    void ApplyResume()
    {
        if (_resume is not { } r) return;
        _resume = null;
        if (r.Book == _curBook && r.Chapter == _curChapter)
        {
            if (r.Verse is int verse && _verseBoxes.ContainsKey(verse)) SelectVerse(verse);
            var offset = new Vector(r.OffsetX, r.OffsetY);
            // once the page has its height, and again a moment later: fonts and cross references can still change it
            Dispatcher.UIThread.Post(() => ReaderScroll.Offset = offset, DispatcherPriority.Background);
            DispatcherTimer.RunOnce(() => ReaderScroll.Offset = offset, TimeSpan.FromMilliseconds(200));
            _history.Clear(); _history.AddRange(r.History.Select(FromResume));
            _forward.Clear(); _forward.AddRange(r.Forward.Select(FromResume));
            UpdateHistoryButtons();
        }
        // instead of the window with the patch notes, which would break the blink
        _settings.LastSeenVersion = AppPaths.Version; SaveSettings();
        ShowUndoToast(L("Aktualisiert auf Version {0}.", AppPaths.Version), () => { _ = ShowPatchNotesAsync(); return Task.CompletedTask; }, L("Was ist neu?"));
    }
}
