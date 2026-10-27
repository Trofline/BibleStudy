using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using BibleStudy.Core.Models;
using BibleStudy.Core.Storage;

namespace BibleStudy.App;

/// <summary>Prayer list: requests with date and optional verse; answered prayers are ticked off and kept with a note.</summary>
public partial class MainWindow
{
    const int PrayerTab = 20;
    bool _prayerWithVerse = true;

    static string Ago(DateTime d)
    {
        var days = (int)(DateTime.Now.Date - d.Date).TotalDays;
        return days <= 0 ? "heute" : days == 1 ? "gestern" : days < 31 ? $"vor {days} Tagen" : days < 365 ? $"vor {days / 30} Monaten" : $"vor {days / 365} Jahr(en)";
    }

    void BuildPrayerUi()
    {
        PrayerPanel.Children.Clear();
        PrayerPanel.Children.Add(new TextBlock { Text = "GEBETSLISTE", Classes = { "eyebrow" } });

        var box = new TextBox { Watermark = "Wofür möchtest du beten oder danken?", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 64 };
        var withVerse = new CheckBox { Content = _selected is int v ? $"Mit {BookName(_curBook)} {_curChapter},{v} verbinden" : "Mit dem angeklickten Vers verbinden", IsChecked = _prayerWithVerse && _selected is not null, IsEnabled = _selected is not null };
        withVerse.IsCheckedChanged += (_, _) => _prayerWithVerse = withVerse.IsChecked == true;
        var add = new Button { Content = "Anliegen hinzufügen", Classes = { "primary" }, HorizontalAlignment = HorizontalAlignment.Left };
        void Commit()
        {
            var text = box.Text?.Trim() ?? "";
            if (text.Length == 0) return;
            _vault.AddPrayer(text, withVerse.IsChecked == true && _selected is int sv ? new VerseRef(_curBook, _curChapter, sv) : null);
            BuildPrayerUi();
        }
        add.Click += (_, _) => Commit();
        box.KeyDown += (_, e) => { if (e.Key == Key.Enter && e.KeyModifiers.HasFlag(KeyModifiers.Control)) { e.Handled = true; Commit(); } };
        PrayerPanel.Children.Add(box); PrayerPanel.Children.Add(withVerse); PrayerPanel.Children.Add(add);

        var all = _vault.GetPrayers();
        var open = all.Where(p => p.Answered is null).ToList();
        var done = all.Where(p => p.Answered is not null).ToList();

        PrayerPanel.Children.Add(new TextBlock { Text = $"OFFEN ({open.Count})", Classes = { "eyebrow" }, Margin = new(0, 10, 0, 0) });
        if (open.Count == 0) PrayerPanel.Children.Add(new TextBlock { Text = "Noch keine Anliegen. Schreibe oben das erste auf; Strg+Enter fügt es hinzu.", Classes = { "caption" }, TextWrapping = TextWrapping.Wrap });
        foreach (var p in open) PrayerPanel.Children.Add(PrayerCard(p));

        if (done.Count > 0)
        {
            PrayerPanel.Children.Add(new TextBlock { Text = $"ERHÖRT UND ERLEDIGT ({done.Count})", Classes = { "eyebrow" }, Margin = new(0, 10, 0, 0) });
            foreach (var p in done) PrayerPanel.Children.Add(PrayerCard(p));
        }
    }

    Border PrayerCard(Prayer p)
    {
        var isDone = p.Answered is not null;
        var text = new TextBlock { Text = p.Text, TextWrapping = TextWrapping.Wrap, Opacity = isDone ? 0.7 : 1 };
        var meta = new TextBlock
        {
            Text = isDone ? $"Aufgeschrieben {p.Created:dd.MM.yyyy} · erhört {p.Answered:dd.MM.yyyy}" : $"Aufgeschrieben {p.Created:dd.MM.yyyy} ({Ago(p.Created)})",
            Classes = { "caption" },
        };
        var row = new WrapPanel();
        if (p.Ref is { } r)
        {
            var chip = FilterChip(Label(r), false, null, async () => await GoToAsync(r.Book, r.Chapter, r.Verse), "Springt zur Stelle");
            row.Children.Add(chip);
        }
        var panel = new StackPanel { Spacing = 4, Children = { text, meta, row } };
        if (isDone && p.Note.Length > 0) panel.Children.Insert(2, new TextBlock { Text = "Notiz: " + p.Note, TextWrapping = TextWrapping.Wrap, FontStyle = FontStyle.Italic });

        var actions = new WrapPanel { Margin = new(0, 4, 0, 0) };
        if (!isDone)
        {
            var answered = new Button { Content = "✓ Erhört" };
            answered.Click += (_, _) =>
            {
                var note = new TextBox { Watermark = "Wie wurde das Gebet erhört? (optional)", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 56, Width = 300 };
                var save = new Button { Content = "Als erhört speichern", Classes = { "primary" }, Margin = new(0, 6, 0, 0) };
                var fly = new Flyout { Content = new StackPanel { Children = { new TextBlock { Text = "Gebet erhört", FontWeight = FontWeight.SemiBold, Margin = new(0, 0, 0, 6) }, note, save } } };
                save.Click += (_, _) => { _vault.SetPrayerAnswered(p.Id, true, note.Text?.Trim() ?? ""); fly.Hide(); BuildPrayerUi(); };
                fly.ShowAt(answered);
            };
            actions.Children.Add(answered);
        }
        else
        {
            var reopen = new Button { Content = "Wieder öffnen" };
            reopen.Click += (_, _) => { _vault.SetPrayerAnswered(p.Id, false); BuildPrayerUi(); };
            actions.Children.Add(reopen);
        }
        var del = new Button { Content = "Löschen", Classes = { "tool" }, Width = double.NaN, Padding = new(10, 0) };
        del.Click += (_, _) =>
        {
            var yes = new Button { Content = "Wirklich löschen", Classes = { "primary" } };
            var fly = new Flyout { Content = new StackPanel { Spacing = 6, Children = { new TextBlock { Text = "Dieses Anliegen endgültig löschen?" }, yes } } };
            yes.Click += (_, _) => { _vault.DeletePrayer(p.Id); fly.Hide(); BuildPrayerUi(); };
            fly.ShowAt(del);
        };
        actions.Children.Add(del);
        panel.Children.Add(actions);
        return new Border { Classes = { "card" }, Child = panel };
    }
}
