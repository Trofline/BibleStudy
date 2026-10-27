using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace BibleStudy.App;

public partial class MainWindow
{
    void InitLicenses() => LicensesBtn.Click += (_, _) =>
    {
        var list = new StackPanel { Spacing = 10, Margin = new(20) };
        list.Children.Add(new TextBlock { Text = L("Quellen und Lizenzen"), FontSize = 20, FontWeight = FontWeight.SemiBold });
        list.Children.Add(new TextBlock { Text = Licenses.Own, TextWrapping = TextWrapping.Wrap, Opacity = 0.85 });
        foreach (var (name, use, license) in Licenses.Sources)
            list.Children.Add(new Border
            {
                Classes = { "card" },
                Child = new StackPanel { Spacing = 2, Children = { new TextBlock { Text = name, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap }, new TextBlock { Text = use, Classes = { "caption" } }, new TextBlock { Text = L("Lizenz: ") + license, TextWrapping = TextWrapping.Wrap } } },
            });
        new Window { Title = L("BibleStudy – Quellen und Lizenzen"), Width = 640, Height = 720, Background = Res("BsSurface"), Icon = Icon, Content = new ScrollViewer { Content = list }, WindowStartupLocation = WindowStartupLocation.CenterOwner }.Show(this);
    };
}
