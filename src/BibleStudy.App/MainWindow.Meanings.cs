namespace BibleStudy.App;

/// <summary>Shows the English word data (glosses, dictionary entries) in the program's language when the translation is cached; asks for it otherwise.</summary>
public partial class MainWindow
{
    string Mean(string english) => _settings.TranslateMeanings ? _meanings.Get(english) : english;

    /// <summary>Translates what is missing (one quiet request per batch), then lets the caller redraw.</summary>
    async Task WarmMeaningsAsync(IEnumerable<string> english, Action apply)
    {
        if (!_settings.TranslateMeanings || !_meanings.Available) return;
        var list = english.ToList();
        if (!list.Any(_meanings.IsMissing)) return;
        StatusText.Text = L("Übersetze Wortbedeutungen …");
        try
        {
            var translated = await _meanings.WarmAsync(list, string.IsNullOrWhiteSpace(_settings.AiModel) ? null : _settings.AiModel);
            if (translated) apply();
        }
        finally { StatusText.Text = ""; }
    }
}
