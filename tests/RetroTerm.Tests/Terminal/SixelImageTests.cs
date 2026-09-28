using System.Text;
using RetroTerm.Core.Configuration;
using RetroTerm.Core.Terminal.Emulators;
using RetroTerm.Core.Terminal.Profiles;
using Xunit;

namespace RetroTerm.Tests.Terminal;

/// <summary>
/// Sixel images arriving at a terminal: DCS q, painted where the cursor is.
///
/// The decoder is tested on its own against a bare surface. This is the wiring around it - which
/// terminals accept an image, where it lands, what it costs a terminal that never sees one, and
/// what happens to the cursor afterwards.
/// </summary>
public class SixelImageTests
{
    private static TerminalEmulatorBase Build(string type = "VT340")
        => EmulatorFactory.CreateEmulator(type, 20, 6, 100);

    private static void Feed(TerminalEmulatorBase emulator, string text)
        => emulator.ProcessData(Encoding.ASCII.GetBytes(text));

    /// <summary>
    /// A six-pixel column in register 1, which is the smallest real image.
    /// </summary>
    private const string OneColumn = "\x1bPq#1~\x1b\\";

    [Fact]
    public void AnImageReachesTheScreen()
    {
        var emulator = Build();

        Feed(emulator, OneColumn);

        Assert.NotNull(emulator.Graphics);
        Assert.True(emulator.Graphics!.HasAnythingToDraw());
    }

    [Fact]
    public void ATerminalThatNeverSeesOnePaysNothing()
    {
        // The plane is 800 by 480 pixels for an ordinary screen. Every session allocating that so
        // a few can show pictures would be the wrong trade.
        var emulator = Build();

        Feed(emulator, "just text");

        Assert.Null(emulator.Graphics);
    }

    [Fact]
    public void ATerminalWithoutTheFeatureIgnoresTheImage()
    {
        // A VT220 has no Sixel. Accepting one there would be inventing a machine.
        var emulator = Build("VT220");

        Feed(emulator, OneColumn);

        Assert.Null(emulator.Graphics);
    }

    [Fact]
    public void TheImageLandsWhereTheCursorIs()
    {
        var emulator = Build();

        // Third row, fifth column, then an image.
        Feed(emulator, "\x1b[3;5H");
        Feed(emulator, OneColumn);

        emulator.Graphics!.Composite();
        var output = emulator.Graphics.Output;

        int x = 4 * emulator.GraphicsCellWidth;
        int y = 2 * emulator.GraphicsCellHeight;

        Assert.False(output.GetPixel(x, y).IsTransparent);
        Assert.True(output.GetPixel(x - 1, y).IsTransparent, "nothing to the left of it");
        Assert.True(output.GetPixel(x, y - 1).IsTransparent, "nothing above it");
    }

    [Fact]
    public void OnAVt340AnImageWithNoGraphicsNewLineLeavesTheCursorWhereItWas()
    {
        // CHANGED 28 August 2026. This used to assert row 1 - "images stack down the screen instead
        // of landing on top of each other" - which is what every other Sixel terminal does and is
        // NOT what the VT340 manual says. With sixel scrolling on it sets the text cursor to the
        // SIXEL cursor position, and OneColumn contains no graphics new line, so that cursor never
        // left band 0.
        //
        // The old expectation was written from our code. hackerb9's extremeratio.six, captured off
        // real hardware, disproves it outright: 480 rows of ink and the cursor stays at the top.
        // The other behaviour is kept for the profiles that really have it - see the xterm test
        // below and TerminalFeatures.SixelExitCursorFollowsTheSixelCursor.
        var emulator = Build();
        Feed(emulator, "\x1b[1;1H");

        Feed(emulator, OneColumn);

        Assert.Equal(0, emulator.GetCursor().Row);
    }

    [Fact]
    public void EveryProfileThatDoesSixelHasDecidedWhereTheCursorLands()
    {
        // WHY THIS IS A GUARD AND NOT A BEHAVIOUR TEST. The obvious test to write here was "xterm
        // still moves the cursor below the image" - and it passes for the wrong reason, because
        // this program's xterm profile does not claim Sixel at all. It drew nothing, moved nothing,
        // and went green.
        //
        // Only two profiles do Sixel today, VT240 and VT340, and both are DEC terminals reading the
        // same DEC specification, so both follow it. That leaves the profile feature with no second
        // side to test - until somebody adds a Sixel profile modelled on xterm or libsixel, which
        // move the cursor past the pixels instead.
        //
        // So this asserts the thing that actually matters: nobody adds a Sixel profile without
        // deciding which rule it follows. If this fails, the new profile is the answer - not this
        // test.
        string[] doSixel = { "VT240", "VT340" };

        for (int i = 0; i < doSixel.Length; i++)
        {
            var emulator = EmulatorFactory.CreateEmulator(doSixel[i], 80, 24, 100);

            Assert.True(emulator.Profile.Supports(TerminalFeatures.Sixel),
                doSixel[i] + " no longer claims Sixel");
            Assert.True(emulator.Profile.Supports(TerminalFeatures.SixelExitCursorFollowsTheSixelCursor),
                doSixel[i] + " does Sixel but has not said where it leaves the text cursor");
        }
    }

    [Fact]
    public void ATallImageMovesTheCursorFurther()
    {
        // On the ordinary 80 by 24 screen a cell is 20 plane pixels tall, which is the VT340's own
        // cell. The small screen the other tests use derives a much taller one, and then no image
        // this test could reasonably send would cover two rows.
        var emulator = EmulatorFactory.CreateEmulator("VT340", 80, 24, 100);
        Assert.Equal(20, emulator.GraphicsCellHeight);

        Feed(emulator, "\x1b[1;1H");

        // Four graphics new lines. The DCS omits its aspect parameter, so a band is twelve screen
        // rows - 2:1 - and the sixel cursor ends at pixel row 48, which is text row 2.
        //
        // This value has now been 2, then 1, then 2 again. It was 2 for the WRONG reason: the
        // aspect was ignored and the height was counted, and the two errors cancelled. Honouring
        // the sixel cursor made it 1, and honouring the DCS aspect made it 2 again - this time
        // because both rules are right. See SixelExitCursorTests and SixelDcsAspectTests.
        Feed(emulator, "\x1bPq#1~-~-~-~-~\x1b\\");

        Assert.Equal(2, emulator.GetCursor().Row);
    }

    [Fact]
    public void TwoImagesBothStay()
    {
        // The plane is not cleared between images - a host drawing a row of small pictures expects
        // to see all of them.
        var emulator = Build();

        Feed(emulator, "\x1b[1;1H");
        Feed(emulator, OneColumn);
        Feed(emulator, "\x1b[1;10H");
        Feed(emulator, OneColumn);

        emulator.Graphics!.Composite();
        var output = emulator.Graphics.Output;

        Assert.False(output.GetPixel(0, 0).IsTransparent);
        Assert.False(output.GetPixel(9 * emulator.GraphicsCellWidth, 0).IsTransparent);
    }

    [Fact]
    public void TheVt340SaysWhatItCanDo()
    {
        // 63 is the VT300 family; 4 is Sixel. The rest of the list is the extensions that really
        // work. ReGIS was added to it later - 3 - and only once a drawing actually reached the
        // screen, which is why this expectation grew rather than being written that way up front.
        var emulator = Build();
        var replies = new StringBuilder();
        emulator.DataToSend += bytes => replies.Append(Encoding.ASCII.GetString(bytes));

        Feed(emulator, "\x1b[c");

        Assert.Equal("\x1b[?63;1;3;4;6;7;8c", replies.ToString());
    }

    [Fact]
    public void AResetTakesTheImagesAway()
    {
        var emulator = Build();
        Feed(emulator, OneColumn);

        emulator.Reset();
        emulator.Graphics?.Composite();

        Assert.False(emulator.Graphics?.HasAnythingToDraw() ?? false);
    }
}
