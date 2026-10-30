using BibleStudy.Core.Providers;
using Xunit;

namespace BibleStudy.Tests;

public class HelloAoTranslationListTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "bs_" + Guid.NewGuid().ToString("N"));

    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    // The list is read from the cache first, so no network is needed.
    async Task<IReadOnlyList<BibleStudy.Core.Models.TranslationInfo>> ReadAsync(string json)
    {
        Directory.CreateDirectory(Path.Combine(_dir, "api"));
        await File.WriteAllTextAsync(Path.Combine(_dir, "api", "available_translations.json"), json);
        return await new HelloAoProvider(new HttpClient(), _dir).GetTranslationsAsync();
    }

    [Fact]
    public async Task TranslationWithoutLicenseBlock_DoesNotBreakTheList()
    {
        // the server dropped the "license" block for some translations: the whole list used to fail with "key not found"
        var list = await ReadAsync("""
            {"translations":[
              {"id":"AAA","name":"A","englishName":"A","shortName":"A","language":"eng","languageEnglishName":"English","textDirection":"ltr","licenseUrl":"u","website":"w","numberOfBooks":66,
               "license":{"attribution":"required","commercialUse":"allowed","derivatives":"allowed","copyleft":false,"publicDomain":true}},
              {"id":"BBB","name":"B","englishName":"B","shortName":"B","language":"eng","languageEnglishName":"English","textDirection":"ltr","licenseUrl":"u","website":"w","numberOfBooks":66,
               "licenseNotice":"Bible Society"},
              {"id":"CCC","name":"C","language":"deu"},
              {"name":"no id"}
            ]}
            """);
        Assert.Equal(["AAA", "BBB", "CCC"], list.Select(t => t.Id).ToArray());
        Assert.True(list[0].License.PublicDomain);
        Assert.Equal("Bible Society", list[1].License.Attribution);
        Assert.False(list[2].License.PublicDomain);
    }
}
