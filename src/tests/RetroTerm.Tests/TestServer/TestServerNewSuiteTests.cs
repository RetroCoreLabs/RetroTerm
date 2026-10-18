using System.Collections.Generic;
using System.Text;
using RetroTerm.Core.Terminal.Graphics;
using RetroTerm.TestServer.App;
using Xunit;

namespace RetroTerm.Tests.TestServer;

/// <summary>
/// The DEC, xterm and graphics suites added to the test server, checked against the real decoders.
/// </summary>
/// <remarks>
/// A manual test suite that emits a malformed stream is worse than no suite at all: the person
/// running it sees a blank screen and reports a defect in the emulator. So the parts of those
/// suites that BUILD bytes are checked here against the same decoders the emulator uses.
///
/// The Tektronix case is not hypothetical. The first version of TekPoint used the 12-bit 4014
/// encoding, which is what most descriptions of a Tektronix terminal give, while this program
/// decodes the 10-bit 4010 form - five bits of each axis per byte. Every coordinate would have been
/// divided by four and the whole picture drawn into one corner.
/// </remarks>
public class TestServerNewSuiteTests
{
    /// <summary>
    /// Feeds bytes into a decoder one at a time, the way a connection delivers them.
    /// </summary>
    private static void Feed(TektronixVectorDecoder decoder, string bytes)
    {
        for (int i = 0; i < bytes.Length; i++)
        {
            decoder.Consume((byte)bytes[i]);
        }
    }

    [Fact]
    public void ATektronixPointComesBackOutWhereItWentIn()
    {
        // The round trip is the whole point: whatever TekPoint encodes, the decoder in the
        // emulator must report the same coordinate.
        var decoder = new TektronixVectorDecoder();
        var points = new List<(int X, int Y)>();
        decoder.PointReady += (x, y) => points.Add((x, y));

        Feed(decoder, "\x1d");                       // GS - into graph mode
        Feed(decoder, TestServerApp.TekPoint(970, 730));

        Assert.Single(points);
        Assert.Equal(970, points[0].X);
        Assert.Equal(730, points[0].Y);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(31, 31)]         // the low five bits exactly full
    [InlineData(32, 32)]         // the first value that needs a high byte
    [InlineData(511, 300)]
    [InlineData(1023, 1023)]     // the top of the 10-bit space
    public void EveryCornerOfTheCoordinateSpaceSurvivesTheRoundTrip(int x, int y)
    {
        var decoder = new TektronixVectorDecoder();
        var points = new List<(int X, int Y)>();
        decoder.PointReady += (px, py) => points.Add((px, py));

        Feed(decoder, "\x1d");
        Feed(decoder, TestServerApp.TekPoint(x, y));

        Assert.Single(points);
        Assert.Equal(x, points[0].X);
        Assert.Equal(y, points[0].Y);
    }

    [Fact]
    public void ATektronixCoordinateIsFourBytesWithTheRightTags()
    {
        // The tags are what tell the decoder which byte is which. Getting one wrong does not fail
        // loudly - it silently reads the next coordinate as part of this one.
        string point = TestServerApp.TekPoint(100, 200);

        Assert.Equal(4, point.Length);
        Assert.Equal(0x20, point[0] & 0xE0);      // high Y
        Assert.Equal(0x60, point[1] & 0xE0);      // low Y
        Assert.Equal(0x20, point[2] & 0xE0);      // high X
        Assert.Equal(0x40, point[3] & 0xE0);      // low X, and this ends the coordinate
    }

    [Fact]
    public void TheThreeColourBarsDecodeToRedGreenAndBlueInThatOrder()
    {
        // This is the test that makes the manual Sixel pass mean something. If the bars did not
        // come out red, green, blue from left to right, a person looking at the screen could not
        // tell a channel swap in the renderer from a mistake in the test data.
        var surface = new InMemoryGraphicsSurface(160, 40);
        var decoder = new SixelDecoder();

        var image = new StringBuilder();
        image.Append("#0;2;100;0;0");
        image.Append("#1;2;0;100;0");
        image.Append("#2;2;0;0;100");
        image.Append(TestServerApp.SixelRun(0, 40));
        image.Append(TestServerApp.SixelRun(1, 40));
        image.Append(TestServerApp.SixelRun(2, 40));

        decoder.Decode(image.ToString(), surface);

        var left = surface.GetPixel(10, 2);
        var middle = surface.GetPixel(50, 2);
        var right = surface.GetPixel(90, 2);

        Assert.False(left.IsTransparent, "the left bar should be painted");
        Assert.False(middle.IsTransparent, "the middle bar should be painted");
        Assert.False(right.IsTransparent, "the right bar should be painted");

        // Red on the left: more red than blue. Blue on the right: more blue than red.
        Assert.True(left.R > left.B, $"left bar should be red, was R{left.R} G{left.G} B{left.B}");
        Assert.True(middle.G > middle.R && middle.G > middle.B,
            $"middle bar should be green, was R{middle.R} G{middle.G} B{middle.B}");
        Assert.True(right.B > right.R, $"right bar should be blue, was R{right.R} G{right.G} B{right.B}");
    }

    [Fact]
    public void ASixelRunPaintsExactlyAsManyColumnsAsItWasAskedFor()
    {
        // The repeat introducer is what keeps these images short. An off-by-one here would make
        // every bar in the manual test a different width than the one beside it.
        var surface = new InMemoryGraphicsSurface(64, 12);
        var decoder = new SixelDecoder();

        decoder.Decode("#1;2;100;100;100" + TestServerApp.SixelRun(1, 20), surface);

        Assert.False(surface.GetPixel(19, 0).IsTransparent, "column 20 should be painted");
        Assert.True(surface.GetPixel(20, 0).IsTransparent, "column 21 should NOT be painted");
    }

    [Theory]
    [InlineData("\x1b[<0;15;7M", "left press at row 7, column 15")]
    [InlineData("\x1b[<0;15;7m", "left release at row 7, column 15")]
    [InlineData("\x1b[<2;3;4M", "right press at row 4, column 3")]
    [InlineData("\x1b[<64;10;10M", "wheel up press at row 10, column 10")]
    [InlineData("\x1b[<65;10;10M", "wheel down press at row 10, column 10")]
    [InlineData("\x1b[<4;5;6M", "left press at row 6, column 5 +Shift")]
    public void AMouseReportIsDescribedInWords(string report, string expected)
    {
        // The interactive mouse test prints this line beside every event. If the decoding is wrong
        // the person running the test is being told a confident lie about where they clicked.
        Assert.Equal(expected, TestServerApp.DescribeMouseReport(report));
    }

    [Fact]
    public void SomethingThatIsNotAnSgrReportSaysSoRatherThanInventingNumbers()
    {
        Assert.Equal("(not an SGR report)", TestServerApp.DescribeMouseReport("\x1b[A"));
    }

    [Fact]
    public void EveryEntryOnTheThreeNewMenusIsListed()
    {
        // A menu entry with no key, or a key with no entry, is a dead end a person finds by
        // pressing it. Cheap to pin.
        var app = new TestServerApp();

        var dec = new StringBuilder();
        app.WriteDECTestsMenu(dec);
        string decText = dec.ToString();
        string[] decKeys = { "1.", "2.", "3.", "4.", "5.", "6.", "7.", "8.", "9.", "A." };
        for (int i = 0; i < decKeys.Length; i++)
        {
            Assert.Contains(decKeys[i], decText);
        }

        var xterm = new StringBuilder();
        app.WriteXtermTestsMenu(xterm);
        string xtermText = xterm.ToString();
        for (int i = 1; i <= 8; i++)
        {
            Assert.Contains($"{i}.", xtermText);
        }

        var graphics = new StringBuilder();
        app.WriteGraphicsTestsMenu(graphics);
        string graphicsText = graphics.ToString();
        for (int i = 1; i <= 7; i++)
        {
            Assert.Contains($"{i}.", graphicsText);
        }
    }

    [Fact]
    public void AReplyIsShownAsHexSoAWrongLengthIsVisible()
    {
        Assert.Equal("1B 5B 63", TestServerApp.ToHex("\x1b[c"));
    }
}
