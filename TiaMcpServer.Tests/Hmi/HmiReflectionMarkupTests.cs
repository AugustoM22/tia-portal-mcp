using TiaMcpServer.OpennessWorker.Openness.Hmi;
using Xunit;

namespace TiaMcpServer.Tests.Hmi;

public class HmiReflectionMarkupTests
{
    [Theory]
    [InlineData("Start", "<body><p>Start</p></body>")]
    [InlineData("A&B <x>", "<body><p>A&amp;B &lt;x&gt;</p></body>")]
    [InlineData("", "<body><p></p></body>")]
    [InlineData("<body><p>Pronto</p></body>", "<body><p>Pronto</p></body>")]
    [InlineData("  <BODY><p>x</p></BODY>", "  <BODY><p>x</p></BODY>")]
    public void PlainText_IsWrapped_MarkupPassesThrough(string input, string expected)
        => Assert.Equal(expected, HmiReflection.Markup(input));
}
