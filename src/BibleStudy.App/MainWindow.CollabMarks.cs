using System.Text.Json;
using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using BibleStudy.Core.Models;
using BibleStudy.Core.Storage;

namespace BibleStudy.App;

/// <summary>
/// Studying together: send the highlights of the open chapter to the other person. They arrive as a card; nothing is saved until the
/// receiver clicks "Take over" (then they become the receiver's own highlights, tagged as shared). Everything received is checked first.
/// </summary>
public partial class MainWindow
{
    const int MaxSharedMarks = 150;

    void ShareChapterMarks()
    {
        var marks = _vault.GetMarks(_curBook, _curChapter).Take(MaxSharedMarks).ToList();
        if (marks.Count == 0) { StatusText.Text = L("In diesem Kapitel gibt es noch keine Markierungen von dir."); return; }
        var items = marks.Select(m => new { v = m.Ref.Verse, c = m.Color, t = Clip(m.Text, 300), n = Clip(m.Note, 300) }).ToList();
        CollabSend(new { t = "marks", book = _curBook, chapter = _curChapter, items });
        AddCollabLog(L("Du hast {0} Markierungen aus {1} gesendet.", marks.Count, $"{BookName(_curBook)} {_curChapter}"), mine: true, system: true);
    }

    void ShowSharedMarks(string book, int chapter, JsonElement items)
    {
        if (_books.All(b => b.Id != book) || chapter < 1 || items.ValueKind != JsonValueKind.Array) return;
        var list = new List<(int Verse, string Color, string Text, string Note)>();
        foreach (var e in items.EnumerateArray().Take(MaxSharedMarks))
        {
            if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty("v", out var v) || !v.TryGetInt32(out var verse) || verse < 1 || verse > 200) continue;
            var color = e.TryGetProperty("c", out var c) && c.GetString() is { } cs && Regex.IsMatch(cs, "^#[0-9A-Fa-f]{6}$") ? cs : _settings.DefaultColor;
            list.Add((verse, color, e.TryGetProperty("t", out var t) ? Clip(t.GetString(), 300) : "", e.TryGetProperty("n", out var n) ? Clip(n.GetString(), 300) : ""));
        }
        if (list.Count == 0) return;
        var who = _peerName.Length > 0 ? _peerName : L("Die andere Person");
        var take = new Button { Content = L("Übernehmen"), Classes = { "tool" }, Width = double.NaN, Padding = new(10, 0), HorizontalAlignment = HorizontalAlignment.Left };
        take.Click += (_, _) =>
        {
            foreach (var (verse, color, text, note) in list)
            {
                var r = new VerseRef(book, chapter, verse);
                _vault.AddMark(Mark.AnyTranslation, r, -1, -1, text, color, (note.Length > 0 ? note + " · " : "") + L("Geteilt von {0}", who), null, AutoTags(r));
            }
            take.IsEnabled = false; take.Content = L("Übernommen ✓");
            LoadChapterMarks(); Paint(); RefreshCollection();
        };
        var dots = new WrapPanel();
        foreach (var (verse, color, _, _) in list.Take(40))
            dots.Children.Add(new Border { Width = 10, Height = 10, CornerRadius = new(5), Margin = new(0, 0, 4, 4), Background = new SolidColorBrush(Color.Parse(color)) });
        AddCollabItem(Card(new StackPanel
        {
            Spacing = 6,
            Children =
            {
                new TextBlock { Text = L("{0} sendet {1} Markierungen aus {2}", who, list.Count, $"{BookName(book)} {chapter}"), FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap },
                dots,
                new TextBlock { Text = string.Join("  ", list.Take(12).Select(m => m.Verse)), Classes = { "caption" }, TextWrapping = TextWrapping.Wrap },
                take,
            },
        }));
    }
}
