using BibleStudy.Core;
using Xunit;

namespace BibleStudy.Tests;

public class OllamaTests
{
    [Fact]
    public void PiecesOfTheStreamedAnswerAreRead()
    {
        Assert.Equal("Hal", OllamaClient.ParseLine("{\"message\":{\"role\":\"assistant\",\"content\":\"Hal\"},\"done\":false}", out var done));
        Assert.False(done);
        OllamaClient.ParseLine("{\"message\":{\"content\":\"\"},\"done\":true}", out done);
        Assert.True(done);
        Assert.Null(OllamaClient.ParseLine("kein json", out _));
        Assert.Throws<InvalidOperationException>(() => OllamaClient.ParseLine("{\"error\":\"model not found\"}", out _));
    }
}
