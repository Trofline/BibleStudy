using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using BibleStudy.Core;
using BibleStudy.Core.Plugins;

namespace BibleStudy.App;

/// <summary>
/// Plugins (see PLUGINS.md): programs that deliver further Bible translations. They are found in the plugin folder, do nothing until the user
/// has allowed them here, and have to be allowed again when their plugin.json changes.
/// </summary>
public partial class MainWindow
{
    PluginManager _plugins = null!;
    PluginBibleProvider _pluginProvider = null!;

    /// <summary>Called early (before the provider is built), because the provider needs the plugin source.</summary>
    void CreatePlugins()
    {
        _plugins = new PluginManager(Path.Combine(_root, "plugins"), () => _settings.AllowedPlugins, PluginEnvironment);
        _pluginProvider = new PluginBibleProvider(_plugins);
    }

    /// <summary>What an allowed plugin gets to reach the program: the address and secret of the local interface (the same one the MCP server uses) and the data folder.</summary>
    IReadOnlyDictionary<string, string> PluginEnvironment() => _control is { Port: > 0 } c
        ? new Dictionary<string, string> { ["BIBLESTUDY_API"] = $"http://127.0.0.1:{c.Port}/call", ["BIBLESTUDY_TOKEN"] = c.Token, ["BIBLESTUDY_DATA"] = _root, ["BIBLESTUDY_VERSION"] = BibleStudy.Core.AppPaths.Version }
        : new Dictionary<string, string> { ["BIBLESTUDY_DATA"] = _root, ["BIBLESTUDY_VERSION"] = BibleStudy.Core.AppPaths.Version };

    /// <summary>Runs a command a plugin brought (from the search box) and shows its answer in the status bar.</summary>
    async Task RunPluginCommandAsync(PluginInfo plugin, PluginCommand command)
    {
        StatusText.Text = L("{0}: {1} …", plugin.Manifest.Name, command.Title);
        try { StatusText.Text = await _plugins.RunCommandAsync(plugin.Manifest.Id, command.Id) is { Length: > 0 } message ? plugin.Manifest.Name + ": " + message : L("{0}: erledigt.", plugin.Manifest.Name); }
        catch (Exception ex) when (ex is PluginException or OperationCanceledException) { StatusText.Text = L("Das Plugin meldet einen Fehler: {0}", ex.Message); }
    }

    void InitPlugins()
    {
        Closed += (_, _) => _plugins.Dispose();
        PluginsFolderBtn.Click += (_, _) =>
        {
            Directory.CreateDirectory(_plugins.Dir);
            OpenUrl(_plugins.Dir);
        };
        PluginsHelpBtn.Click += (_, _) => OpenUrl("https://github.com/Trofline/BibleStudy/blob/main/PLUGINS.md");
        PluginsReloadBtn.Click += async (_, _) => { await ReloadTranslationsAsync(null); BuildPluginList(); };
        BuildPluginList();
    }

    void BuildPluginList()
    {
        PluginsList.Children.Clear();
        var found = _plugins.Discover();
        PluginsInfo.Text = found.Count == 0 ? L("Keine Plugins gefunden. Lege ein Plugin als Ordner mit einer plugin.json in den Plugin-Ordner und klicke auf „Neu einlesen“.") : "";
        foreach (var p in found)
        {
            var plugin = p;
            var m = plugin.Manifest;
            var allowed = _plugins.IsAllowed(plugin);
            var title = new TextBlock { Text = m.Name + (m.Version.Length > 0 ? "  " + m.Version : "") + (m.Author.Length > 0 ? "  ·  " + m.Author : ""), FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap };
            var panel = new StackPanel { Spacing = 4, Children = { title } };
            if (m.Description.Length > 0) panel.Children.Add(new TextBlock { Text = m.Description, TextWrapping = TextWrapping.Wrap });
            var offers = new List<string>();
            if (m.ProvidesBibles) offers.Add(L("liefert Übersetzungen"));
            if (m.Commands.Count > 0) offers.Add(L("bringt Befehle für das Suchfeld: {0}", string.Join(", ", m.Commands.Select(c => c.Title))));
            if (offers.Count > 0) panel.Children.Add(new TextBlock { Text = L("Dieses Plugin") + " " + string.Join("; ", offers) + ".", Classes = { "caption" }, TextWrapping = TextWrapping.Wrap });
            if (plugin.Problem is not null) panel.Children.Add(new TextBlock { Text = plugin.Problem, Classes = { "caption" }, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.IndianRed });
            else
            {
                var allow = new CheckBox { Content = L("Dieses Plugin erlauben"), IsChecked = allowed };
                allow.IsCheckedChanged += async (_, _) => await SetPluginAllowedAsync(plugin, allow.IsChecked == true, allow);
                panel.Children.Add(allow);
                if (_settings.AllowedPlugins.ContainsKey(m.Id) && !allowed)
                    panel.Children.Add(new TextBlock { Text = L("Die plugin.json hat sich seit deiner Erlaubnis geändert. Erlaube das Plugin bitte erneut, wenn du der neuen Fassung vertraust."), Classes = { "caption" }, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DarkOrange });
                if (allowed && _pluginProvider.Problems.TryGetValue(m.Id, out var problem))
                    panel.Children.Add(new TextBlock { Text = L("Das Plugin meldet einen Fehler: {0}", problem), Classes = { "caption" }, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.IndianRed });
            }
            var card = new Border { Child = panel, Padding = new(12), CornerRadius = new(10), BorderThickness = new(1) };
            card.Bind(Border.BorderBrushProperty, this.GetResourceObservable("BsBorder"));
            PluginsList.Children.Add(card);
        }
    }

    bool _pluginBusy;

    async Task SetPluginAllowedAsync(PluginInfo plugin, bool allow, CheckBox box)
    {
        if (_pluginBusy) return;
        _pluginBusy = true;
        try
        {
            var m = plugin.Manifest;
            if (allow)
            {
                var text = L("„{0}“ ist ein Programm und läuft mit deinen Rechten auf deinem Computer. Es wird mit diesem Befehl gestartet:\n\n{1} {2}\n\n", m.Name, m.Command, string.Join(" ", m.Args))
                    + (m.ContentNotice.Length > 0 ? L("Angaben des Plugin-Autors zu den Inhalten:\n{0}\n\n", m.ContentNotice) : L("Der Plugin-Autor macht keine Angaben zu den Inhalten.\n\n"))
                    + L("Ein Plugin kann außerdem die Schnittstelle des Programms benutzen: Es darf dann deine Daten im Programm lesen und ändern (Notizen, Markierungen, Dokumente, Gebete ...) und das Programm steuern, genau wie die KI-Anbindung.\n\n")
                    + L("Das Projekt BibleStudy hat dieses Plugin nicht geprüft und übernimmt keine Verantwortung für das Plugin und seine Inhalte. Erlaube es nur, wenn du seinem Autor vertraust und die Inhalte so nutzen darfst.");
                if (await AskAsync(L("Plugin erlauben?"), text, L("Erlauben"), L("Abbrechen")) != 0)
                {
                    box.IsChecked = false;
                    return;
                }
                _settings.AllowedPlugins[m.Id] = plugin.Hash;
            }
            else
            {
                _settings.AllowedPlugins.Remove(m.Id);
                _plugins.Stop(m.Id);
            }
            SaveSettings();
            await ReloadTranslationsAsync(null);
            BuildPluginList();
        }
        finally { _pluginBusy = false; }
    }
}
