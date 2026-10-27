using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace BibleStudy.App;

/// <summary>
/// Topic search: a topic word is widened with related words (German synonyms from openthesaurus.de, CC BY-SA 4.0) and the whole
/// text is searched for all of them by their base form. The user decides which related words count.
/// </summary>
public partial class MainWindow
{
    void InitTopics() => TopicBtn.Click += (_, _) => ShowTopicFlyout();

    void ShowTopicFlyout()
    {
        var fly = new Flyout { Placement = PlacementMode.BottomEdgeAlignedLeft };
        var panel = new StackPanel { Spacing = 8, Width = 360 };
        fly.Content = panel;
        var topic = new TextBox { Watermark = L("Thema, z. B. Vergebung, Mut, Trost …"), Text = SearchBox.Text };
        var chips = new WrapPanel();
        var info = new TextBlock { Classes = { "caption" }, TextWrapping = TextWrapping.Wrap };
        var go = new Button { Content = L("Stellen zum Thema suchen"), Classes = { "primary" }, HorizontalAlignment = HorizontalAlignment.Stretch, IsEnabled = false };
        var selected = new List<string>(); var all = new List<string>();

        void Redraw()
        {
            chips.Children.Clear();
            foreach (var w in all)
            {
                var word = w;
                chips.Children.Add(FilterChip(word, selected.Contains(word), null, () => { if (!selected.Remove(word)) selected.Add(word); Redraw(); }));
            }
            go.IsEnabled = selected.Count > 0;
        }

        async Task Find()
        {
            var t = topic.Text?.Trim() ?? "";
            if (t.Length < 2) { info.Text = L("Gib ein Thema ein."); return; }
            info.Text = L("Suche verwandte Wörter …");
            all.Clear(); selected.Clear();
            all.Add(t); selected.Add(t);
            var syn = await SynonymsAsync(t);
            foreach (var s in syn.Where(s => !s.Contains(' ')).Take(16)) all.Add(s);
            foreach (var s in all.Skip(1).Take(5)) selected.Add(s);
            info.Text = syn.Count == 0
                ? "Keine verwandten Wörter gefunden (offline oder Wort unbekannt) – gesucht wird nur das Thema."
                : "Tippe auf Wörter, um sie ein- oder auszuschließen. Gesucht wird nach der Grundform, also auch „vergebe“, „vergab“ …  Quelle der Wörter: openthesaurus.de (CC BY-SA 4.0).";
            Redraw();
        }
        topic.KeyDown += async (_, e) => { if (e.Key == Key.Enter) await Find(); };
        var findBtn = new Button { Content = L("Verwandte Wörter finden"), HorizontalAlignment = HorizontalAlignment.Stretch };
        findBtn.Click += async (_, _) => await Find();
        go.Click += async (_, _) =>
        {
            SearchBox.Text = string.Join(" / ", selected.Select(w => w.Contains(' ') ? $"\"{w}\"" : $"L={w}"));
            fly.Hide();
            await SearchAsync();
        };
        panel.Children.Add(new TextBlock { Text = L("THEMENSUCHE"), Classes = { "eyebrow" } });
        panel.Children.Add(topic); panel.Children.Add(findBtn); panel.Children.Add(chips); panel.Children.Add(info); panel.Children.Add(go);
        fly.ShowAt(TopicBtn);
        Avalonia.Threading.Dispatcher.UIThread.Post(() => topic.Focus());
    }
}
