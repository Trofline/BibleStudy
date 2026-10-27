using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using BibleStudy.Core.Models;
using BibleStudy.Core.Providers;

namespace BibleStudy.App;

public partial class MainWindow : Window
{
    readonly IBibleProvider _provider;
    IReadOnlyList<TranslationInfo> _all = [];
    IReadOnlyList<BookInfo> _books = [];
    bool _loading;

    public MainWindow()
    {
        InitializeComponent();
        var cache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BibleStudy", "cache");
        _provider = new HelloAoProvider(new HttpClient(), cache);

        LanguageBox.SelectionChanged += (_, _) => FillTranslations();
        TranslationBox.SelectionChanged += async (_, _) => await LoadBooksAsync();
        BookBox.SelectionChanged += (_, _) => { if (BookBox.SelectedItem is BookInfo b) ChapterBox.Maximum = b.NumberOfChapters; _ = ShowChapterAsync(); };
        ChapterBox.ValueChanged += async (_, _) => await ShowChapterAsync();
        PrevBtn.Click += (_, _) => ChapterBox.Value -= 1;
        NextBtn.Click += (_, _) => ChapterBox.Value += 1;
        Opened += async (_, _) => await InitAsync();
    }

    async Task InitAsync()
    {
        try
        {
            StatusText.Text = "Lade Übersetzungen …";
            _all = await _provider.GetTranslationsAsync();
            var langs = _all.Select(t => t.LanguageName).Where(l => l != "").Distinct().Order().ToList();
            LanguageBox.ItemsSource = langs;
            LanguageBox.SelectedItem = langs.Contains("English") ? "English" : langs.FirstOrDefault();
        }
        catch (Exception ex) { StatusText.Text = "Fehler: " + ex.Message; }
    }

    void FillTranslations()
    {
        var list = _all.Where(t => t.LanguageName == (string?)LanguageBox.SelectedItem).OrderBy(t => t.Name).ToList();
        TranslationBox.ItemsSource = list;
        TranslationBox.SelectedItem = list.FirstOrDefault(t => t.Id == "eng_kjv") ?? list.FirstOrDefault();
    }

    async Task LoadBooksAsync()
    {
        if (TranslationBox.SelectedItem is not TranslationInfo t) return;
        try
        {
            _loading = true;
            var prev = (BookBox.SelectedItem as BookInfo)?.Id;
            _books = await _provider.GetBooksAsync(t.Id);
            BookBox.ItemsSource = _books;
            BookBox.SelectedItem = _books.FirstOrDefault(b => b.Id == prev) ?? _books.FirstOrDefault(b => b.Id == "JHN") ?? _books.FirstOrDefault();
            FlowDirection = t.TextDirection == "rtl" ? Avalonia.Media.FlowDirection.RightToLeft : Avalonia.Media.FlowDirection.LeftToRight;
        }
        catch (Exception ex) { StatusText.Text = "Fehler: " + ex.Message; }
        finally { _loading = false; }
        await ShowChapterAsync();
    }

    async Task ShowChapterAsync()
    {
        if (_loading || TranslationBox.SelectedItem is not TranslationInfo t || BookBox.SelectedItem is not BookInfo b) return;
        try
        {
            var ch = await _provider.GetChapterAsync(t.Id, b.Id, (int)(ChapterBox.Value ?? 1));
            ReaderText.Inlines!.Clear();
            ReaderText.Inlines.Add(new Run($"{b.CommonName} {ch.Number}\n") { FontSize = 28, FontWeight = FontWeight.SemiBold });
            foreach (var v in ch.Verses)
            {
                ReaderText.Inlines.Add(new Run(v.Number + " ") { FontSize = 12, FontWeight = FontWeight.Bold, BaselineAlignment = BaselineAlignment.Top, Foreground = Brushes.SteelBlue });
                foreach (var s in v.Segments)
                    ReaderText.Inlines.Add(new Run(s.Text + " ") { Foreground = s.WordsOfJesus ? Brushes.IndianRed : null });
            }
            var l = t.License;
            StatusText.Text = $"{t.Name} · {(l.PublicDomain ? "Gemeinfrei" : "Lizenz: " + t.LicenseUrl)} · Namensnennung: {l.Attribution} · Quelle: {_provider.Name}";
        }
        catch (Exception ex) { StatusText.Text = "Kapitel nicht verfügbar: " + ex.Message; }
    }
}
