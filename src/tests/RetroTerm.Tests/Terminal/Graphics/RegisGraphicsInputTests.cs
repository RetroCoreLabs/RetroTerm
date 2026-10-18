using RetroTerm.Core.Terminal.Graphics;
using Xunit;

namespace RetroTerm.Tests.Terminal.Graphics;

/// <summary>
/// ReGIS graphics input mode - <c>R(I0)</c>, <c>R(I1)</c> and <c>R(P(I))</c> from chapter 10.
/// </summary>
/// <remarks>
/// <para><b>Keys only, by decision</b></para>
/// A locator device does not drive this here. The manual specifies the arrow-key half completely -
/// one pixel per press, ten with shift, wrapping at the screen edge - and leaves the question of what
/// wins when a program is ALSO asking for xterm mouse reports unanswered. Decided with Ronny on
/// 2026-08-20: the mouse keeps selecting text, the arrow keys drive the graphics input cursor.
///
/// <para><b>Where the quotations come from</b></para>
/// Chapter 10 "Graphics Input Modes" and "Report Position Interactive", and chapter 15 "ReGIS Locator
/// Reports", both in <c>spec\DEC\EK-VT3XX-GP-002_VT330_VT340_Graphics_Programming_May88.pdf</c>.
/// </remarks>
public class RegisGraphicsInputTests
{
    /// <summary>
    /// The escape character, built from its code so no raw control byte lands in this file.
    /// </summary>
    private const char Escape = (char)0x1B;

    /// <summary>
    /// Builds a decoder over a surface the size of a real ReGIS screen.
    /// </summary>
    /// <param name="surface">
    /// The surface the decoder drew on, for a caller that wants to keep using it.
    /// </param>
    /// <returns>
    /// A fresh decoder.
    /// </returns>
    private static RegisDecoder NewDecoder(out InMemoryGraphicsSurface surface)
    {
        surface = new InMemoryGraphicsSurface(RegisDecoder.DefaultWidth, RegisDecoder.DefaultHeight);
        return new RegisDecoder();
    }

    /// <summary>
    /// Plays a command string and returns whatever the decoder owes the host.
    /// </summary>
    /// <param name="decoder">
    /// The decoder to drive.
    /// </param>
    /// <param name="surface">
    /// Where any pixels go.
    /// </param>
    /// <param name="commands">
    /// The ReGIS to play.
    /// </param>
    /// <returns>
    /// The reports, with their trailing carriage returns.
    /// </returns>
    private static string Play(RegisDecoder decoder, InMemoryGraphicsSurface surface, string commands)
    {
        decoder.Decode(commands, surface);
        return decoder.HasReports ? decoder.TakeReports() : string.Empty;
    }

    [Fact]
    public void ADecoderStartsWithNoGraphicsInput()
    {
        var decoder = NewDecoder(out var surface);
        Play(decoder, surface, "P[10,10]");

        Assert.Equal(RegisGraphicsInputMode.Off, decoder.InputMode);
        Assert.False(decoder.IsHostDataSuspended);
    }

    [Fact]
    public void OneShotModeIsEnteredByInputModeZero()
    {
        var decoder = NewDecoder(out var surface);
        Play(decoder, surface, "R(I0)");

        Assert.Equal(RegisGraphicsInputMode.OneShot, decoder.InputMode);
    }

    [Fact]
    public void MultipleModeIsEnteredByInputModeOne()
    {
        var decoder = NewDecoder(out var surface);
        Play(decoder, surface, "R(I1)");

        Assert.Equal(RegisGraphicsInputMode.Multiple, decoder.InputMode);
    }

    [Fact]
    public void EnteringAnInputModeReturnsABareCarriageReturn()
    {
        // "NOTE: When the terminal receives R(I), it returns a carriage return (CR). Applications
        // can use the CR for synchronization."
        var decoder = NewDecoder(out var surface);

        Assert.Equal("\r", Play(decoder, surface, "R(I0)"));
    }

    [Fact]
    public void TheInputCursorStartsAtTheDrawingPoint()
    {
        // "The graphics cursor (input or output) indicates the active screen location. This location
        // is either the screen origin [0,0] or the point most recently moved or drawn to."
        var decoder = NewDecoder(out var surface);
        Play(decoder, surface, "P[123,45]R(I0)");

        Assert.Equal(123, decoder.InputCursorX);
        Assert.Equal(45, decoder.InputCursorY);
    }

    [Fact]
    public void OnlyOneShotModeSuspendsTheHostStream()
    {
        // One-shot "suspends processing of new data from the application"; multiple mode
        // "immediately processes characters it receives from the host, instead of buffering them as
        // in one-shot mode".
        var oneShot = NewDecoder(out var a);
        Play(oneShot, a, "R(I0)");
        Assert.True(oneShot.IsHostDataSuspended);

        var multiple = NewDecoder(out var b);
        Play(multiple, b, "R(I1)");
        Assert.False(multiple.IsHostDataSuspended);
    }

    [Fact]
    public void AnArrowKeyMovesTheCursorOnePixel()
    {
        // "arrow key - The cursor moves one pixel in the direction of the arrow."
        var decoder = NewDecoder(out var surface);
        Play(decoder, surface, "P[100,100]R(I0)");

        decoder.MoveInputCursor(RegisDecoder.InputCursorStep, 0);
        Assert.Equal(101, decoder.InputCursorX);
        Assert.Equal(100, decoder.InputCursorY);

        decoder.MoveInputCursor(0, -RegisDecoder.InputCursorStep);
        Assert.Equal(101, decoder.InputCursorX);
        Assert.Equal(99, decoder.InputCursorY);
    }

    [Fact]
    public void AShiftedArrowKeyMovesTheCursorTenPixels()
    {
        // "Shift-arrow key - The cursor moves 10 pixels in the direction of the arrow."
        var decoder = NewDecoder(out var surface);
        Play(decoder, surface, "P[100,100]R(I0)");

        decoder.MoveInputCursor(0, RegisDecoder.InputCursorShiftStep);

        Assert.Equal(110, decoder.InputCursorY);
        Assert.Equal(10, RegisDecoder.InputCursorShiftStep);
    }

    [Fact]
    public void TheCursorWrapsPastTheRightEdge()
    {
        // "If you move the cursor past a screen boundary, the cursor wraps to the other side of the
        // screen." Clamping instead would let a held arrow key park in a corner and stop.
        var decoder = NewDecoder(out var surface);
        Play(decoder, surface, "P[799,10]R(I0)");

        decoder.MoveInputCursor(1, 0);

        Assert.Equal(0, decoder.InputCursorX);
    }

    [Fact]
    public void TheCursorWrapsPastTheLeftEdge()
    {
        // The other direction, which is where a plain remainder gets it wrong: C# gives -1 for
        // -1 % 800, not 799.
        var decoder = NewDecoder(out var surface);
        Play(decoder, surface, "P[0,10]R(I0)");

        decoder.MoveInputCursor(-1, 0);

        Assert.Equal(RegisDecoder.DefaultWidth - 1, decoder.InputCursorX);
    }

    [Fact]
    public void TheCursorWrapsPastTheTopEdge()
    {
        var decoder = NewDecoder(out var surface);
        Play(decoder, surface, "P[10,0]R(I0)");

        decoder.MoveInputCursor(0, -RegisDecoder.InputCursorShiftStep);

        Assert.Equal(RegisDecoder.DefaultHeight - 10, decoder.InputCursorY);
    }

    [Fact]
    public void MovingTheInputCursorDoesNotMoveTheDrawingPoint()
    {
        // The two are different cursors. If arrow keys dragged the drawing point, the next vector
        // the host sent would start somewhere the host never asked for.
        var decoder = NewDecoder(out var surface);
        Play(decoder, surface, "P[100,100]R(I0)");

        decoder.MoveInputCursor(50, 50);

        Assert.Equal(100, decoder.CurrentX);
        Assert.Equal(100, decoder.CurrentY);
        Assert.Equal(150, decoder.InputCursorX);
    }

    [Fact]
    public void TheArrowKeysDoNotMoveTheCursorInMultipleMode()
    {
        // "You cannot use the four arrow keys to position the input cursor as you can in ReGIS
        // one-shot graphics input mode. If you press an arrow key in multiple mode, the terminal
        // sends that key's escape sequence to the host."
        var decoder = NewDecoder(out var surface);
        Play(decoder, surface, "P[100,100]R(I1)");

        decoder.MoveInputCursor(25, 25);

        Assert.Equal(100, decoder.InputCursorX);
        Assert.Equal(100, decoder.InputCursorY);
    }

    [Fact]
    public void OneShotSendsNothingUntilTheApplicationAsks()
    {
        // "In one-shot mode, the terminal cannot send a position report until the application sends
        // a request to the terminal." A keystroke before the request is just a keystroke.
        var decoder = NewDecoder(out var surface);
        Play(decoder, surface, "P[10,20]R(I0)");

        Assert.False(decoder.SendInputReport("A"));
        Assert.False(decoder.HasReports);
        Assert.Equal(RegisGraphicsInputMode.OneShot, decoder.InputMode);
    }

    [Fact]
    public void TheInteractiveRequestSendsNothingByItselfInOneShotMode()
    {
        // "After sending the report position interactive command, the application does not receive a
        // report until you press an active key or locator button."
        var decoder = NewDecoder(out var surface);
        Play(decoder, surface, "P[10,20]R(I0)");

        Assert.Equal(string.Empty, Play(decoder, surface, "R(P(I))"));
    }

    [Fact]
    public void AKeystrokeAnswersTheRequestWithItsCodeAndThePosition()
    {
        // "Locator reports begin with the code(s) of the active non-arrow key or locator button
        // pressed. Following this code is the current position of the input cursor... The report ends
        // with the carriage return character (CR)." The manual's own worked example is A[102,200]CR.
        var decoder = NewDecoder(out var surface);
        Play(decoder, surface, "P[102,200]R(I0)R(P(I))");

        Assert.True(decoder.SendInputReport("A"));

        Assert.Equal("A[102,200]\r", decoder.TakeReports());
    }

    [Fact]
    public void SendingTheReportLeavesOneShotMode()
    {
        // "The input cursor disappears from the screen, and the terminal exits one-shot mode."
        var decoder = NewDecoder(out var surface);
        Play(decoder, surface, "P[10,20]R(I0)R(P(I))");

        decoder.SendInputReport("A");

        Assert.Equal(RegisGraphicsInputMode.Off, decoder.InputMode);
        Assert.False(decoder.IsHostDataSuspended);
    }

    [Fact]
    public void OnlyOneReportComesOutOfOneShotMode()
    {
        // The whole point of the name. A second keypress after the mode has ended reports nothing.
        var decoder = NewDecoder(out var surface);
        Play(decoder, surface, "P[10,20]R(I0)R(P(I))");

        Assert.True(decoder.SendInputReport("A"));
        decoder.TakeReports();

        Assert.False(decoder.SendInputReport("B"));
        Assert.False(decoder.HasReports);
    }

    [Fact]
    public void TheReportCarriesWhereTheCursorWasMovedTo()
    {
        // Ties the two halves together: the arrow keys must reach the coordinates the host is told.
        var decoder = NewDecoder(out var surface);
        Play(decoder, surface, "P[100,100]R(I0)R(P(I))");

        decoder.MoveInputCursor(RegisDecoder.InputCursorShiftStep, -RegisDecoder.InputCursorStep);
        decoder.SendInputReport("Z");

        Assert.Equal("Z[110,99]\r", decoder.TakeReports());
    }

    [Fact]
    public void MultipleModeAnswersTheRequestImmediately()
    {
        // "When the terminal receives R(P(I)) in multiple mode, it immediately returns a position
        // report to the application." Chapter 15 prints this exact case as CSI 240 ~ [100,100] CR:
        // "The null button sequence indicates this report is the result of an application request,
        // not a locator button transition."
        var decoder = NewDecoder(out var surface);
        Play(decoder, surface, "P[100,100]R(I1)");

        Assert.Equal(Escape + "[240~[100,100]\r", Play(decoder, surface, "R(P(I))"));
    }

    [Fact]
    public void MultipleModeStaysInMultipleModeAfterReporting()
    {
        // "The terminal remains in multiple mode."
        var decoder = NewDecoder(out var surface);
        Play(decoder, surface, "R(I1)R(P(I))");

        Assert.Equal(RegisGraphicsInputMode.Multiple, decoder.InputMode);
    }

    [Fact]
    public void MultipleModeCanReportAsOftenAsItIsAsked()
    {
        // "You can continue to send reports to the application without exiting multiple mode."
        var decoder = NewDecoder(out var surface);
        Play(decoder, surface, "P[5,6]R(I1)");

        string first = Play(decoder, surface, "R(P(I))");
        string second = Play(decoder, surface, "R(P(I))");

        Assert.Equal(first, second);
        Assert.Equal(Escape + "[240~[5,6]\r", second);
    }

    [Fact]
    public void InputModeZeroLeavesMultipleModeForOneShot()
    {
        // "The terminal stays in multiple mode until the application sends the R(I0) option. This
        // option makes the terminal exit multiple mode and enter one-shot mode."
        var decoder = NewDecoder(out var surface);
        Play(decoder, surface, "R(I1)");

        Play(decoder, surface, "R(I0)");

        Assert.Equal(RegisGraphicsInputMode.OneShot, decoder.InputMode);
    }

    [Fact]
    public void APlainPositionReportStillReportsTheDrawingPoint()
    {
        // R(P) and R(P(I)) share a letter and mean different things. The plain form must keep
        // answering with the DRAWING point, not the input cursor, and must not wait for a key.
        var decoder = NewDecoder(out var surface);
        Play(decoder, surface, "P[70,80]R(I0)");
        decoder.MoveInputCursor(5, 5);

        Assert.Equal("[70,80]\r", Play(decoder, surface, "R(P)"));
    }

    [Fact]
    public void CancellingLeavesInputModeWithoutAReport()
    {
        // OURS, not the manual's: a real VT340 has no way out of one-shot except answering. Here a
        // suspended session would look dead, so ESC cancels. Nothing goes to the host, so an
        // application counting its requests sees one unanswered - which is what happened.
        var decoder = NewDecoder(out var surface);
        Play(decoder, surface, "R(I0)R(P(I))");

        decoder.CancelInputMode();

        Assert.Equal(RegisGraphicsInputMode.Off, decoder.InputMode);
        Assert.False(decoder.IsHostDataSuspended);
        Assert.False(decoder.HasReports);
    }

    [Fact]
    public void ACancelledRequestDoesNotSurviveIntoTheNextInputMode()
    {
        // A request belongs to the mode it was made in. If it leaked, the first keypress of the NEXT
        // session would answer a question the host had given up on.
        var decoder = NewDecoder(out var surface);
        Play(decoder, surface, "R(I0)R(P(I))");
        decoder.CancelInputMode();

        Play(decoder, surface, "R(I0)");

        Assert.False(decoder.SendInputReport("A"));
    }

    [Fact]
    public void TheCursorWrapsAtTheSurfaceEdgeNotAFixedNumber()
    {
        // A host can change the addressing, and the cursor has to wrap at the edge the operator can
        // actually see. Pinning 800 here instead would be right by accident on the default screen.
        var small = new InMemoryGraphicsSurface(100, 60);
        var decoder = new RegisDecoder();
        decoder.Decode("P[99,10]R(I0)", small);

        decoder.MoveInputCursor(1, 0);

        Assert.Equal(0, decoder.InputCursorX);
    }
}
