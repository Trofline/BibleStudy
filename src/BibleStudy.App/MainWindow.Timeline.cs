namespace BibleStudy.App;

/// <summary>Timeline of the Bible with the places of the chosen event on the map.</summary>
public partial class MainWindow
{
    void InitFamily()
    {
        FamilyPane.CloseRequested += ShowReader;
        FamilyPane.PersonProvider = id => _hello.GetPersonAsync(id);
        FamilyPane.PeopleProvider = async () => _lexIndex ??= await _hello.GetLexiconIndexAsync();
        FamilyBtn.Click += async (_, _) =>
        {
            Home.IsVisible = false; ReaderHost.IsVisible = false; GraphPane.IsVisible = false; KnowledgePane.IsVisible = false; MapPane.IsVisible = false; TimelinePane.IsVisible = false; FamilyPane.IsVisible = true;
            await FamilyPane.OpenAsync();
        };
    }

    void InitTimeline()
    {
        TimelinePane.CloseRequested += ShowReader;
        TimelinePane.EventsProvider = () => _hello.GetTimelineAsync();
        TimelinePane.DetailProvider = id => _hello.GetEventAsync(id);
        TimelinePane.PlaceProvider = id => _hello.GetPlaceAsync(id);
        TimelinePane.MapProvider = async () => _worldMap ??= await BibleStudy.Core.Models.WorldMap.LoadAsync(_http, Path.Combine(_root, "cache"));
        TimelinePane.BookName = BookName;
        TimelinePane.OpenVerse += async (book, chapter, verse) => await GoToAsync(book, chapter, verse);
        TimelineBtn.Click += async (_, _) =>
        {
            Home.IsVisible = false; ReaderHost.IsVisible = false; GraphPane.IsVisible = false; KnowledgePane.IsVisible = false; MapPane.IsVisible = false; FamilyPane.IsVisible = false; TimelinePane.IsVisible = true;
            await TimelinePane.OpenAsync();
        };
    }
}
