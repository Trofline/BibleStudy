using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using BibleStudy.Core.Models;
using BibleStudy.Core.Storage;

namespace BibleStudy.App;

/// <summary>
/// The "Merkliste": a plain list of verses to jump back to later. No notes, colors, folders or tags – those belong to the notes and highlights.
/// </summary>
public partial class MainWindow
{
    const int BookmarksTab = 25;

    /// <summary>Verses of the open chapter that are on the Merkliste (they get a star next to their number).</summary>
    HashSet<int> _chapterBookmarks = [];

    void LoadChapterBookmarks() => _chapterBookmarks = _vault.GetBookmarkedVerses(_curBook, _curChapter);

    void ToggleBookmark()
    {
        if (_selected is not int v) { StatusText.Text = L("Erst einen Vers anklicken."); return; }
        var verse = new VerseRef(_curBook, _curChapter, v);
        StatusText.Text = _vault.ToggleBookmark(verse, VerseTextOf(v))
            ? L("{0} gemerkt – zu finden unter „Notizen → Merkliste“.", Label(verse))
            : L("{0} nicht mehr gemerkt.", Label(verse));
        RefreshBookmarks();
        _ = ShowChapterAsync(_selected);   // the star next to the verse number
    }

    void RefreshBookmarks()
    {
        if (_vault is null) return;
        var list = _vault.GetBookmarks();
        BookmarkPanel.Children.Clear();
        var intro = new TextBlock { Text = L("Deine gemerkten Verse: einfach eine Liste, um später zurückzuspringen – ohne Notizen. Merken mit dem Knopf „Merken“ oben oder Strg+D; ein Klick auf den Eintrag springt zur Stelle."), TextWrapping = TextWrapping.Wrap };
        intro.Classes.Add("caption");
        BookmarkPanel.Children.Add(intro);
        if (list.Count == 0)
        {
            var empty = new TextBlock { Text = L("Noch nichts gemerkt. Klicke im Text einen Vers an und dann oben auf „Merken“."), TextWrapping = TextWrapping.Wrap, Margin = new(0, 6, 0, 0) };
            empty.Classes.Add("caption");
            BookmarkPanel.Children.Add(empty);
            return;
        }
        foreach (var b in list)
        {
            var bm = b;
            var title = new TextBlock { Text = Label(bm.Ref), FontWeight = FontWeight.SemiBold };
            var text = new TextBlock { Text = Shorten(bm.Text, 140), TextWrapping = TextWrapping.Wrap, FontSize = 12, MaxHeight = 36, TextTrimming = TextTrimming.CharacterEllipsis };
            text.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("BsMuted"));
            var remove = new Button { Content = "✕", Classes = { "tool" }, Width = 26, Height = 26, FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
            ToolTip.SetTip(remove, L("Aus der Merkliste entfernen"));
            remove.Click += (_, _) => { _vault.RemoveBookmark(bm.Id); RefreshBookmarks(); if (bm.Ref.Book == _curBook && bm.Ref.Chapter == _curChapter) _ = ShowChapterAsync(_selected); };
            var grid = new Grid { ColumnDefinitions = new("*,Auto") };
            grid.Children.Add(new StackPanel { Spacing = 2, Children = { title, text } });
            Grid.SetColumn(remove, 1); grid.Children.Add(remove);
            var card = new Border { Child = grid, Padding = new(12, 8), CornerRadius = new(10), BorderThickness = new(1), Margin = new(0, 6, 0, 0), Cursor = new Cursor(StandardCursorType.Hand), Classes = { "tile" } };
            card.Bind(Border.BorderBrushProperty, this.GetResourceObservable("BsBorder"));
            card.PointerPressed += (_, e) => { if (e.GetCurrentPoint(card).Properties.IsLeftButtonPressed) _ = GoToAsync(bm.Ref.Book, bm.Ref.Chapter, bm.Ref.Verse); };
            BookmarkPanel.Children.Add(card);
        }
    }
}
