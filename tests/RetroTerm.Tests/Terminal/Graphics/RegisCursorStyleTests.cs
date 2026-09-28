using System.Text;
using RetroTerm.Core.Configuration;
using RetroTerm.Core.Terminal.Emulators;
using RetroTerm.Core.Terminal.Graphics;
using Xunit;

namespace RetroTerm.Tests.Terminal.Graphics;

/// <summary>
/// <c>S(C...)</c> - graphics cursor control, chapter 2.
/// </summary>
/// <remarks>
/// <para><b>Four shapes, and none of them has a photograph</b></para>
/// No fixture in any corpus turns a graphics input cursor on, so every one of these is read from the
/// manual and judged by eye. Each test carries the sentence it was built from.
///
/// <para><b>What is deliberately not built</b></para>
/// The user-defined cursor - <c>S(C(I[+5,+10]"XO"))</c>, two characters from the loaded set combined
/// into a 16 by 24 shape. It needs the soft font to become a cursor bitmap and nothing asks for it.
/// The syntax is stepped over so the rest of a command still arrives, which has its own test.
/// </remarks>
public class RegisCursorStyleTests
{
    /// <summary>
    /// The escape character, built from its code so no raw control byte lands in this file.
    /// </summary>
    private const char Escape = (char)0x1B;

    /// <summary>
    /// Runs some ReGIS on a fresh decoder.
    /// </summary>
    /// <param name="commands">
    /// The ReGIS to run.
    /// </param>
    /// <returns>
    /// The decoder afterwards.
    /// </returns>
    private static RegisDecoder Play(string commands)
    {
        var surface = new InMemoryGraphicsSurface(RegisDecoder.DefaultWidth, RegisDecoder.DefaultHeight);
        var decoder = new RegisDecoder();
        decoder.Decode(commands, surface);
        return decoder;
    }

    /// <summary>
    /// Builds a VT340 and runs some ReGIS on it.
    /// </summary>
    /// <param name="commands">
    /// The ReGIS to run.
    /// </param>
    /// <returns>
    /// The terminal.
    /// </returns>
    private static TerminalEmulatorBase Terminal(string commands)
    {
        var emulator = EmulatorFactory.CreateEmulator("VT340", 80, 24, 100);
        emulator.ProcessData(Encoding.ASCII.GetBytes(Escape + "P1p" + commands + Escape + "\\"));
        return emulator;
    }

    /// <summary>
    /// The surface the input cursor is drawn on.
    /// </summary>
    /// <param name="emulator">
    /// The terminal.
    /// </param>
    /// <returns>
    /// The cursor plane's surface.
    /// </returns>
    private static IGraphicsSurface CursorPlane(TerminalEmulatorBase emulator)
        => emulator.Graphics!.FindPlane(TerminalEmulatorBase.RegisInputCursorPlaneId)!.Surface;

    [Fact]
    public void TheCrosshairIsTheDefault()
    {
        // "Omitted - Crosshair", and "0 - Crosshair (default)".
        Assert.Equal(RegisCursorStyle.Crosshair, Play("P[10,10]").InputCursorStyle);
    }

    [Fact]
    public void StyleOneIsTheDiamond()
    {
        Assert.Equal(RegisCursorStyle.Diamond, Play("S(C(I1))").InputCursorStyle);
    }

    [Fact]
    public void StylesZeroAndTwoAreBothTheCrosshair()
    {
        // The table really does list the crosshair twice - 0 as "Crosshair (default)" and 2 as
        // "Crosshair". The output cursor table does the same with "0 or 1" for the diamond, so this
        // is the manual's habit rather than a misreading of it.
        Assert.Equal(RegisCursorStyle.Crosshair, Play("S(C(I1))S(C(I0))").InputCursorStyle);
        Assert.Equal(RegisCursorStyle.Crosshair, Play("S(C(I1))S(C(I2))").InputCursorStyle);
    }

    [Fact]
    public void StyleThreeIsTheRubberBandLine()
    {
        Assert.Equal(RegisCursorStyle.RubberBandLine, Play("S(C(I3))").InputCursorStyle);
    }

    [Fact]
    public void StyleFourIsTheRubberBandRectangle()
    {
        Assert.Equal(RegisCursorStyle.RubberBandRectangle, Play("S(C(I4))").InputCursorStyle);
    }

    [Fact]
    public void AnOmittedNumberGoesBackToTheCrosshair()
    {
        // "Omitted - Crosshair". So S(C(I)) after a diamond is a way back, not a no-op.
        Assert.Equal(RegisCursorStyle.Crosshair, Play("S(C(I1))S(C(I))").InputCursorStyle);
    }

    [Fact]
    public void TheOutputCursorFlagIsReadAndDrawsNothing()
    {
        // "0 turns the output cursor off. 1 turns the output cursor on." Nothing here draws the
        // output cursor - it is the mark a real VT340 shows while waiting for the next command - but
        // the flag is remembered rather than dropped.
        Assert.True(Play("S(C1)").OutputCursorVisible);
        Assert.False(Play("S(C1)S(C0)").OutputCursorVisible);
    }

    [Fact]
    public void TheOutputCursorStyleDoesNotChangeTheInputCursor()
    {
        // The two suboptions are easy to confuse, and confusing them would mean a host selecting an
        // output style silently changed the shape the operator is aiming with.
        var decoder = Play("S(C(I1))S(C(H2))");

        Assert.Equal(RegisCursorStyle.Diamond, decoder.InputCursorStyle);
        Assert.Equal(2, decoder.OutputCursorStyleNumber);
    }

    [Fact]
    public void CursorControlIsNotCountedAsUnhandled()
    {
        Assert.False(Play("S(C(I3))").UnhandledCommands.ContainsKey('S'));
    }

    [Fact]
    public void AUserDefinedCursorIsSteppedOverRatherThanMisread()
    {
        // "S(C(I[+5,+10]"XO"))" - the character mask form, which is not built. The danger is not that
        // it does nothing; it is that the 5 gets read as a style number. The style must stay put and
        // the command after it must still run.
        var decoder = Play("S(C(I1))S(C(I[+5,+10]\"XO\"))P[42,43]");

        Assert.Equal(RegisCursorStyle.Diamond, decoder.InputCursorStyle);
        Assert.Equal(42, decoder.CurrentX);
        Assert.Equal(43, decoder.CurrentY);
    }

    [Fact]
    public void TheDiamondIsDrawnAroundTheCursorAndNowhereElse()
    {
        // "This cursor is a 21 x 21 pixel diamond." Ten pixels each way from the centre, so the
        // points sit exactly on the axes through it.
        var emulator = Terminal("P[400,240]S(C(I1))R(I0)");
        var surface = CursorPlane(emulator);

        int reach = TerminalEmulatorBase.RegisDiamondSize / 2;

        // The four points of the diamond.
        Assert.NotEqual(GraphicsColor.Transparent, surface.GetPixel(400, 240 - reach));
        Assert.NotEqual(GraphicsColor.Transparent, surface.GetPixel(400 + reach, 240));
        Assert.NotEqual(GraphicsColor.Transparent, surface.GetPixel(400, 240 + reach));
        Assert.NotEqual(GraphicsColor.Transparent, surface.GetPixel(400 - reach, 240));

        // And, unlike the crosshair, nothing out at the screen edge. This is the assertion that
        // tells the two shapes apart at all.
        Assert.Equal(GraphicsColor.Transparent, surface.GetPixel(0, 240));
        Assert.Equal(GraphicsColor.Transparent, surface.GetPixel(400, 0));
    }

    [Fact]
    public void TheRubberBandLineRunsFromTheDrawingPointToTheCursor()
    {
        // "with its origin fixed at the current drawing (output) position and its endpoint at the
        // current cursor position." Moving the cursor must stretch it, not carry it.
        var emulator = Terminal("P[100,100]S(C(I3))R(I0)");
        emulator.MoveRegisInputCursor(100, 0);

        var surface = CursorPlane(emulator);

        // The fixed end, the moving end, and the middle of what is now a 100-pixel line.
        Assert.NotEqual(GraphicsColor.Transparent, surface.GetPixel(100, 100));
        Assert.NotEqual(GraphicsColor.Transparent, surface.GetPixel(150, 100));
        Assert.NotEqual(GraphicsColor.Transparent, surface.GetPixel(200, 100));

        // Nothing beyond the moving end. A line drawn to the screen edge would pass everything above.
        Assert.Equal(GraphicsColor.Transparent, surface.GetPixel(300, 100));
    }

    [Fact]
    public void TheRubberBandRectangleHasCornersAtBothPoints()
    {
        // "a rectangle, with one corner fixed at the current drawing (output) position and the
        // opposite corner at the current cursor position."
        var emulator = Terminal("P[100,100]S(C(I4))R(I0)");
        emulator.MoveRegisInputCursor(200, 100);

        var surface = CursorPlane(emulator);

        // Along the top edge, down the right edge, and the two far corners.
        Assert.NotEqual(GraphicsColor.Transparent, surface.GetPixel(200, 100));
        Assert.NotEqual(GraphicsColor.Transparent, surface.GetPixel(300, 150));
        Assert.NotEqual(GraphicsColor.Transparent, surface.GetPixel(100, 200));
        Assert.NotEqual(GraphicsColor.Transparent, surface.GetPixel(300, 200));

        // The middle is empty - it is an outline, not a filled box.
        Assert.Equal(GraphicsColor.Transparent, surface.GetPixel(200, 150));

        // Just past the FIXED corner, along the lines its two edges run down. These are what pin the
        // anchor to the drawing point: every assertion above still passed with the anchor shifted 50
        // pixels sideways, because they all sit inside the wrong rectangle as well as the right one.
        Assert.Equal(GraphicsColor.Transparent, surface.GetPixel(80, 100));
        Assert.Equal(GraphicsColor.Transparent, surface.GetPixel(100, 80));
    }

    [Fact]
    public void TheRubberBandRectangleWorksWhenTheCursorIsUpAndLeftOfTheAnchor()
    {
        // The two corners can be in either order on either axis. A rectangle drawn from a width and
        // a height would come out negative here and draw nothing at all.
        var emulator = Terminal("P[300,200]S(C(I4))R(I0)");
        emulator.MoveRegisInputCursor(-200, -100);

        var surface = CursorPlane(emulator);

        Assert.NotEqual(GraphicsColor.Transparent, surface.GetPixel(100, 100));
        Assert.NotEqual(GraphicsColor.Transparent, surface.GetPixel(300, 200));
        Assert.NotEqual(GraphicsColor.Transparent, surface.GetPixel(200, 100));
    }

    [Fact]
    public void ChangingTheStyleWhileTheCursorIsUpRedrawsIt()
    {
        // A host may change its mind mid-session. The old shape has to go, or the two are on the
        // screen at once.
        //
        // MULTIPLE mode, and that is not an arbitrary choice. One-shot "buffers all received
        // characters, until it leaves one-shot mode", so a style change sent afterwards is held with
        // everything else and cannot arrive. Multiple mode is the only one that "lets the terminal
        // perform graphics input and output at the same time" - which is exactly what this is.
        var emulator = Terminal("P[400,240]R(I1)");

        var surface = CursorPlane(emulator);
        Assert.NotEqual(GraphicsColor.Transparent, surface.GetPixel(0, 240));

        emulator.ProcessData(Encoding.ASCII.GetBytes(Escape + "P1pS(C(I1))" + Escape + "\\"));

        var after = CursorPlane(emulator);

        // The crosshair's arm at the screen edge is gone...
        Assert.Equal(GraphicsColor.Transparent, after.GetPixel(0, 240));

        // ...and the diamond is there instead. Without this, a plane that had simply been cleared
        // and left empty would pass.
        Assert.NotEqual(GraphicsColor.Transparent,
            after.GetPixel(400, 240 - TerminalEmulatorBase.RegisDiamondSize / 2));
    }

    [Fact]
    public void AStyleChangeCannotArriveDuringOneShotMode()
    {
        // The other side of the same coin, stated so nobody "fixes" it later. One-shot suspends the
        // host, so a style change sent after it is buffered and the cursor keeps its shape until the
        // mode ends. A host that wants a particular shape must select it BEFORE R(I0).
        var emulator = Terminal("P[400,240]R(I0)");

        emulator.ProcessData(Encoding.ASCII.GetBytes(Escape + "P1pS(C(I1))" + Escape + "\\"));

        // Still the crosshair, still reaching the screen edge.
        Assert.NotEqual(GraphicsColor.Transparent, CursorPlane(emulator).GetPixel(0, 240));

        // And the held command runs once the mode ends, rather than being thrown away.
        emulator.CancelRegisInput();
        Assert.Equal(RegisCursorStyle.Diamond, emulator.Regis!.InputCursorStyle);
    }
}
