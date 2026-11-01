using Avalonia.Controls;
using Avalonia.Media;
using BibleStudy.Core;
using BibleStudy.Core.Models;

namespace BibleStudy.App;

/// <summary>Text criticism: where the Greek editions differ in a New Testament verse (Tyndale House data, CC BY 4.0).</summary>
public partial class MainWindow
{
    TextVariants? _variants;

    async Task ShowVariantsAsync(VerseRef r)
    {
        _variants ??= new TextVariants(_http, Path.Combine(_root, "cache"));
        var body = new StackPanel { Spacing = 8, Margin = new(16) };
        var info = new TextBlock { Text = L("Lade …"), TextWrapping = TextWrapping.Wrap };
        body.Children.Add(info);
        var win = new Window { Title = L("Lesarten") + " · " + Label(r), Width = 560, Height = 520, MinWidth = 340, MinHeight = 260, Background = Res("BsSurface"), Icon = Icon, WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = new ScrollViewer { Content = body } };
        win.Show(this);
        try
        {
            var list = await _variants.GetAsync(r.Book, r.Chapter, r.Verse);
            body.Children.Clear();
            if (list.Count == 0) body.Children.Add(Muted(L("In diesem Vers stimmen die großen griechischen Ausgaben überein (oder die Unterschiede ändern die Übersetzung nicht).")));
            foreach (var v in list)
            {
                var box = new StackPanel { Spacing = 2 };
                box.Children.Add(new TextBlock { Text = v.Greek + (v.English.Length > 0 ? "  –  " + v.English : ""), FontFamily = ScriptureFont, FontSize = 18 });
                box.Children.Add(new TextBlock { Text = L("steht in: {0}", v.Editions.Length > 0 ? v.Editions : "–"), Classes = { "caption" }, TextWrapping = TextWrapping.Wrap });
                if (v.Alternative.Length > 0) box.Children.Add(new TextBlock { Text = L("Andere Lesart: {0}", v.Alternative), TextWrapping = TextWrapping.Wrap, FontSize = 13 });
                var card = new Border { Child = box, Padding = new(10), CornerRadius = new(8), BorderThickness = new(1) };
                card.Bind(Border.BorderBrushProperty, this.GetResourceObservable("BsBorder"));
                body.Children.Add(card);
            }
            body.Children.Add(Muted(TextVariants.EditionsLegend));
            body.Children.Add(Muted(L("Quelle: {0}", TextVariants.Attribution)));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        { info.Text = L("Die Lesarten konnten nicht geladen werden: {0}", ex.Message); }
    }
}
