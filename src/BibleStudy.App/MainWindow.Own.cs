using Avalonia.Controls;
using Avalonia.Layout;
using BibleStudy.Core.Models;
using BibleStudy.Core.Providers;
using BibleStudy.Core.Storage;

namespace BibleStudy.App;

/// <summary>Everything of your own (notes, highlights, documents, prayers, to-dos, own cross references) in one list: for the search box and for "where did I write about this verse".</summary>
public partial class MainWindow
{
    static string OwnKindLabel(OwnKind kind) => kind switch
    {
        OwnKind.Document => L("Dokument"), OwnKind.Note => L("Notiz"), OwnKind.Mark => L("Markierung"),
        OwnKind.Prayer => L("Gebet"), OwnKind.Todo => L("Aufgabe"), _ => L("Querverweis"),
    };

    string OwnTitle(OwnHit h) => h.Title.Length > 0 ? h.Title : h.Ref is { } r ? Label(r) : OwnKindLabel(h.Kind);

    /// <summary>Opens the thing: a document in the editor, a prayer in the prayer list, everything else at its verse.</summary>
    Task OpenOwn(OwnHit h)
    {
        switch (h.Kind)
        {
            case OwnKind.Document:
                if (_vault.GetDocuments().FirstOrDefault(d => d.Id == h.Id) is { } doc) { OpenWork(NavGroups.First(g => g.Name == "Dokumente"), KnowledgeTab); OpenDocument(doc); }
                return Task.CompletedTask;
            case OwnKind.Prayer:
                OpenWork(NavGroups.First(g => g.Name == "Planen"), PrayerTab);
                return Task.CompletedTask;
            default:
                return h.Ref is { } r ? GoToAsync(r.Book, r.Chapter, r.Verse) : Task.CompletedTask;
        }
    }

    /// <summary>One row in the verse overview: kind, title, and the start of the text; a click opens it.</summary>
    Control OwnRow(OwnHit h)
    {
        var head = new Button { Classes = { "ghost" }, Padding = new(0), HorizontalAlignment = HorizontalAlignment.Left, Content = $"{OwnKindLabel(h.Kind)} · {OwnTitle(h)}", FontWeight = Avalonia.Media.FontWeight.SemiBold, Foreground = Res("BsAccent") };
        head.Click += async (_, _) => await OpenOwn(h);
        var panel = new StackPanel { Children = { head } };
        if (h.Snippet.Length > 0) panel.Children.Add(new TextBlock { Text = Shorten(h.Snippet, 160), TextWrapping = Avalonia.Media.TextWrapping.Wrap, Opacity = 0.85 });
        return panel;
    }

    /// <summary>Opens the log of unexpected errors (errors.log) in the default text editor.</summary>
    void ShowErrorLog()
    {
        if (!File.Exists(ErrorLog.Path)) { StatusText.Text = L("Es wurde noch kein Fehler aufgezeichnet."); return; }
        try
        {
            if (OperatingSystem.IsWindows()) System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("notepad.exe", $"\"{ErrorLog.Path}\"") { UseShellExecute = true });
            else OpenUrl(ErrorLog.Path);
            StatusText.Text = L("Fehlerprotokoll: {0}", ErrorLog.Path);
        }
        catch (Exception ex) { StatusText.Text = L("Das Fehlerprotokoll ließ sich nicht öffnen: {0}", ex.Message); }
    }

    /// <summary>The short meaning from the STEPBible lexicon under a word of the original text (Greek with the entry of Abbott-Smith); null when there is none or offline.</summary>
    async Task<Control?> StepCardAsync(string id)
    {
        if (!_settings.ShowStepLexicon) return null;
        StepEntry? e;
        try { e = await _step.LookupAsync(id).WaitAsync(TimeSpan.FromSeconds(20)); }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException or IOException or OperationCanceledException) { return null; }
        if (e is null || e.Gloss.Length == 0) return null;
        var panel = new StackPanel { Spacing = 3 };
        var head = new TextBlock { Text = L("STEPBible-Kurzlexikon"), Classes = { "eyebrow" } };
        panel.Children.Add(head);
        panel.Children.Add(new TextBlock { Text = $"{e.Word}  ·  {e.Translit}  ·  {e.Gloss}", FontWeight = Avalonia.Media.FontWeight.SemiBold, TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        if (e.Meaning.Length > 0) panel.Children.Add(new TextBlock { Text = Shorten(e.Meaning, 900), TextWrapping = Avalonia.Media.TextWrapping.Wrap, FontSize = 12.5, Opacity = 0.92 });
        var card = new Border { Child = panel, Padding = new(12), CornerRadius = new(10), BorderThickness = new(1) };
        card.Bind(Border.BorderBrushProperty, this.GetResourceObservable("BsBorder"));
        return card;
    }
}
