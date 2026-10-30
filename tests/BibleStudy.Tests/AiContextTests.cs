using BibleStudy.Core.Storage;
using Xunit;

namespace BibleStudy.Tests;

public class AiContextTests : IDisposable
{
    readonly string _file = Path.Combine(Path.GetTempPath(), "bs_" + Guid.NewGuid().ToString("N") + ".json");
    public void Dispose() { try { File.Delete(_file); } catch (IOException) { } }

    [Fact]
    public void NewInstallation_StartsWithTheBibleOnlyInstruction()
    {
        var s = AppSettings.Load(_file);   // no file: the defaults
        Assert.StartsWith("Du legst biblische Texte ausschließlich aus der Bibel selbst aus", s.AiContext);
        Assert.DoesNotContain("adventistisch", s.AiContext);
    }

    [Fact]
    public void OldDefault_IsReplaced_ButAChangedTextStays()
    {
        File.WriteAllText(_file, """{"AiContext":"Standpunkt: adventistisch; die Bibel allein ist Maßstab (keine kirchliche oder päpstliche Autorität)."}""");
        Assert.Equal(AppSettings.DefaultAiContext, AppSettings.Load(_file).AiContext);
        File.WriteAllText(_file, """{"AiContext":"Mein eigener Text"}""");
        Assert.Equal("Mein eigener Text", AppSettings.Load(_file).AiContext);
        File.WriteAllText(_file, """{"AiContext":""}""");
        Assert.Equal("", AppSettings.Load(_file).AiContext);   // emptied on purpose: stays empty
    }
}
