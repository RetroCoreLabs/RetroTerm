using System.Text;
using RetroTerm.Core.Configuration;
using RetroTerm.Core.Terminal.Emulators;
using Xunit;

namespace RetroTerm.Tests.Terminal.Graphics;

/// <summary>
/// DECSDM, private mode 80 - where a Sixel image lands, and whether the cursor follows it.
/// </summary>
/// <remarks>
/// <para><b>Two settings</b></para>
/// From chapter 14 of the Graphics Programming manual:
///  - Scrolling ENABLED, the default: the image begins at the text cursor, and on exit the text
///    cursor is set to the sixel cursor position.
///  - Scrolling DISABLED: the image begins at the upper-left corner of the graphics page, and on
///    exit "the text cursor does not change from the position it was in when sixel mode was
///    entered".
///
/// <para><b>The manual has this backwards, and it is not a close call</b></para>
/// It says "When sixel display mode is set, the Sixel Scrolling feature is enabled." xterm's
/// ctlseqs says mode 80 RESET "Turns on Sixel Scrolling" - the opposite. hackerb9's errata for this
/// exact manual settles it against the hardware: "DECSDM reversed regarding sixel scrolling. On
/// hackerb9's vt340: when DECSDM is set, sixel scrolling is disabled; when DECSDM is reset, sixel
/// scrolling is enabled."
///
/// A machine beats a sentence, two independent sources agree against the manual, and that manual
/// carries several other confirmed errata. SET means scrolling off.
///
/// <para><b>Why it was worth finding</b></para>
/// The corpus fixture <c>comment.six</c> builds one picture out of FOURTEEN DCS strings, each
/// positioning itself from the page origin with leading graphics newlines. Without this mode every
/// string landed lower than the one before, so the table marched off the bottom of the plane and
/// only three of its sixteen colours were ever drawn.
/// </remarks>
public class SixelScrollingModeTests
{
    private static TerminalEmulatorBase Build()
        => EmulatorFactory.CreateEmulator("VT340", 40, 12, 100);

    private static void Feed(TerminalEmulatorBase emulator, string data)
        => emulator.ProcessData(Encoding.ASCII.GetBytes(data));

    /// <summary>
    /// One solid green band, eight pixels wide and six tall, defining its own colour.
    /// </summary>
    private const string GreenBand = "\x1bPq#2;2;0;100;0!8~\x1b\\";

    /// <summary>
    /// The same band, followed by four graphics new lines - 24 pixel rows, more than one
    /// text cell on the VT340's own 80 by 24 geometry.
    /// </summary>
    /// <remarks>
    /// This is what an ordinary encoder sends, and it is the difference between two images stacking
    /// down the screen and two images landing on the same six rows.
    /// </remarks>
    private const string GreenBandFourBandsTall = "\x1bPq#2;2;0;100;0!8~----\x1b\\";

    /// <summary>
    /// Lowest row holding any ink, or -1 when the plane is empty.
    /// </summary>
    private static int LowestInkRow(TerminalEmulatorBase emulator)
    {
        var surface = emulator.Graphics!.Output;

        for (int y = surface.Height - 1; y >= 0; y--)
        {
            for (int x = 0; x < surface.Width; x++)
            {
                if (!surface.GetPixel(x, y).IsTransparent) return y;
            }
        }

        return -1;
    }

    [Fact]
    public void ASecondImageLandsBelowTheFirstWhenTheFirstSteppedPastACell()
    {
        // CHANGED 28 August 2026. This used to send GreenBand twice and assert the second reached
        // further down. GreenBand ends WITHOUT a graphics new line, so on a VT340 the sixel cursor
        // never left band 0 and the manual says the text cursor does not move either - the second
        // image lands on the first. See TerminalFeatures.SixelExitCursorFollowsTheSixelCursor.
        //
        // Two things had to change for the test to mean what its name says. The image must step the
        // sixel cursor, which takes a graphics new line; and it must step it past a whole CELL,
        // because the text cursor counts in cells. On the 80 by 24 geometry a cell is 20 plane
        // pixels, so four graphics new lines - 24 rows - is the smallest image that moves it.
        //
        // The other tests here use a 40 by 12 screen, where a cell is 40 pixels tall and no image
        // this small could ever move the cursor. That is why this one builds its own.
        var emulator = EmulatorFactory.CreateEmulator("VT340", 80, 24, 100);
        Assert.Equal(20, emulator.GraphicsCellHeight);

        Feed(emulator, "\x1b[?25l\x1b[1;1H");

        Feed(emulator, GreenBandFourBandsTall);
        int afterFirst = LowestInkRow(emulator);

        Feed(emulator, GreenBandFourBandsTall);
        int afterSecond = LowestInkRow(emulator);

        Assert.True(afterSecond > afterFirst,
            $"the second image should be lower; first reached {afterFirst}, second {afterSecond}");
    }

    [Fact]
    public void WithoutAGraphicsNewLineTheSecondImageLandsOnTopOfTheFirst()
    {
        // The same image with the graphics new lines taken away. The sixel cursor never moves, so
        // neither does the text cursor, and the ink reaches no further down than the first time.
        // This is what a VT340 does, and it is why extremeratio.six leaves its cursor at the top of
        // the screen with 480 rows of ink on the plane.
        var emulator = Build();
        Feed(emulator, "\x1b[?25l\x1b[1;1H");

        Feed(emulator, GreenBand);
        int afterFirst = LowestInkRow(emulator);

        Feed(emulator, GreenBand);
        int afterSecond = LowestInkRow(emulator);

        Assert.Equal(afterFirst, afterSecond);
    }

    [Fact]
    public void WithScrollingOffBothImagesStartAtThePageOrigin()
    {
        // ESC [ ? 80 h. Every image begins at the upper-left corner of the graphics page, so two
        // identical images occupy exactly the same pixels and the ink reaches no further down.
        var emulator = Build();
        Feed(emulator, "\x1b[?25l\x1b[1;1H\x1b[?80h");

        Feed(emulator, GreenBand);
        int afterFirst = LowestInkRow(emulator);

        Feed(emulator, GreenBand);
        int afterSecond = LowestInkRow(emulator);

        Assert.Equal(afterFirst, afterSecond);
    }

    [Fact]
    public void AndAnImageDrawnWithScrollingOffIgnoresWhereTheCursorIs()
    {
        // "the sixel active position begins at the upper-left corner of the active graphics page."
        // Not at the cursor - so parking the cursor a long way down must change nothing.
        var emulator = Build();
        Feed(emulator, "\x1b[?25l\x1b[?80h\x1b[8;20H");

        Feed(emulator, GreenBand);

        var surface = emulator.Graphics!.Output;
        Assert.False(surface.GetPixel(0, 0).IsTransparent,
            "the image should be at the page origin whatever the cursor is doing");
    }

    [Fact]
    public void TheCursorDoesNotMoveWhenScrollingIsOff()
    {
        // "the text cursor does not change from the position it was in when sixel mode was
        // entered." Read back through DSR, which is how a host would ask.
        var emulator = Build();
        var replies = new StringBuilder();
        emulator.DataToSend += bytes => replies.Append(Encoding.ASCII.GetString(bytes));

        Feed(emulator, "\x1b[?25l\x1b[?80h\x1b[4;7H");
        Feed(emulator, GreenBand);
        Feed(emulator, "\x1b[6n");

        Assert.Equal("\x1b[4;7R", replies.ToString());
    }

    [Fact]
    public void TheModeCanBeAskedAboutAndTurnedBackOff()
    {
        // DECRQM, CSI ? 80 $ p. A host that sets a mode is entitled to read it back, and this file
        // format depends on turning it off again afterwards.
        var emulator = Build();
        var replies = new StringBuilder();
        emulator.DataToSend += bytes => replies.Append(Encoding.ASCII.GetString(bytes));

        Feed(emulator, "\x1b[?80h\x1b[?80$p");
        Assert.Equal("\x1b[?80;1$y", replies.ToString());

        replies.Clear();
        Feed(emulator, "\x1b[?80l\x1b[?80$p");
        Assert.Equal("\x1b[?80;2$y", replies.ToString());
    }

    [Fact]
    public void ATerminalWithNoSixelIgnoresTheModeEntirely()
    {
        // The mode is a VT330/VT340/VT382 one. A VT220 has no sixel at all, and claiming the mode
        // would tell a host it could layer images it cannot draw.
        var emulator = EmulatorFactory.CreateEmulator("VT220", 40, 12, 100);
        var replies = new StringBuilder();
        emulator.DataToSend += bytes => replies.Append(Encoding.ASCII.GetString(bytes));

        Feed(emulator, "\x1b[?80h\x1b[?80$p");

        Assert.Equal("\x1b[?80;2$y", replies.ToString());
    }
}
