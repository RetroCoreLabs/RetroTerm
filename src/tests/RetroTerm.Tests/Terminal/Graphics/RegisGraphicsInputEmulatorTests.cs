using System.Collections.Generic;
using System.Text;
using RetroTerm.Core.Configuration;
using RetroTerm.Core.Terminal.Buffer;
using RetroTerm.Core.Terminal.Emulators;
using RetroTerm.Core.Terminal.Graphics;
using Xunit;

namespace RetroTerm.Tests.Terminal.Graphics;

/// <summary>
/// ReGIS graphics input mode driven through a whole emulator, rather than the decoder alone.
/// </summary>
/// <remarks>
/// <para><b>What only shows up at this level</b></para>
/// The decoder owns no connection and no screen, so three parts of this feature cannot be seen in
/// <c>RegisGraphicsInputTests</c> at all: the host's data actually being held back, the crosshair
/// being drawn on a plane of its own, and the report reaching the wire.
///
/// <para><b>The suspension is the risky half</b></para>
/// Holding the host's bytes means an ordinary terminal stops printing. If the release ever failed,
/// a session would look dead - so the release is tested from both ends, by answering and by
/// cancelling.
/// </remarks>
public class RegisGraphicsInputEmulatorTests
{
    /// <summary>
    /// The escape character, built from its code so no raw control byte lands in this file.
    /// </summary>
    private const char Escape = (char)0x1B;

    /// <summary>
    /// Wraps ReGIS commands in the device control string that carries them.
    /// </summary>
    /// <param name="commands">
    /// The ReGIS to send.
    /// </param>
    /// <returns>
    /// The whole sequence, introducer and terminator included.
    /// </returns>
    private static string Regis(string commands)
        => Escape + "P1p" + commands + Escape + "\\";

    /// <summary>
    /// Reads one row of the screen as text.
    /// </summary>
    /// <param name="buffer">
    /// The screen to read.
    /// </param>
    /// <param name="row">
    /// Which row.
    /// </param>
    /// <returns>
    /// The row's characters, with trailing blanks removed.
    /// </returns>
    private static string LineText(TerminalBuffer buffer, int row)
    {
        var text = new StringBuilder();
        for (int col = 0; col < buffer.Width; col++)
        {
            uint codepoint = buffer[row, col].Codepoint;
            if (codepoint == 0) break;
            text.Append(char.ConvertFromUtf32((int)codepoint));
        }

        return text.ToString().TrimEnd();
    }

    /// <summary>
    /// Builds a VT340 and collects everything it sends to the host.
    /// </summary>
    /// <param name="sent">
    /// Receives the list the outgoing chunks land in.
    /// </param>
    /// <returns>
    /// The emulator.
    /// </returns>
    private static TerminalEmulatorBase NewTerminal(out List<byte[]> sent)
    {
        var emulator = EmulatorFactory.CreateEmulator("VT340", 80, 24, 100);
        var collected = new List<byte[]>();
        emulator.DataToSend += data => collected.Add(data);
        sent = collected;
        return emulator;
    }

    /// <summary>
    /// Joins everything sent to the host into one string.
    /// </summary>
    /// <param name="sent">
    /// The collected chunks.
    /// </param>
    /// <returns>
    /// The bytes as ASCII.
    /// </returns>
    private static string SentText(List<byte[]> sent)
    {
        var text = new StringBuilder();
        for (int i = 0; i < sent.Count; i++) text.Append(Encoding.ASCII.GetString(sent[i]));
        return text.ToString();
    }

    /// <summary>
    /// Feeds a string to the terminal as host data.
    /// </summary>
    /// <param name="emulator">
    /// The terminal.
    /// </param>
    /// <param name="text">
    /// What the host sent.
    /// </param>
    private static void Feed(TerminalEmulatorBase emulator, string text)
        => emulator.ProcessData(Encoding.ASCII.GetBytes(text));

    [Fact]
    public void OneShotModeHoldsTheHostsTextBack()
    {
        // "In one-shot mode, the terminal suspends processing of new data from the application until
        // ReGIS sends a position report. The terminal buffers any data received from the
        // application in this mode."
        var emulator = NewTerminal(out _);

        Feed(emulator, Regis("P[10,10]R(I0)"));
        Feed(emulator, "HELD");

        Assert.True(emulator.IsRegisInputSuspended);
        Assert.Equal(string.Empty, LineText(emulator.GetBuffer(), 0));
    }

    [Fact]
    public void TheHeldTextArrivesOnceTheReportIsSent()
    {
        // The release, from the answering end. Held bytes are replayed in order, so the screen ends
        // up exactly as it would have without the pause.
        var emulator = NewTerminal(out _);

        Feed(emulator, Regis("P[10,10]R(I0)R(P(I))"));
        Feed(emulator, "HELD");

        emulator.SendRegisInputReport("A");

        Assert.False(emulator.IsRegisInputSuspended);
        Assert.Equal("HELD", LineText(emulator.GetBuffer(), 0));
    }

    [Fact]
    public void CancellingAlsoReleasesTheHeldText()
    {
        // The release, from the other end. Ours rather than the manual's: a real VT340 has no way
        // out of one-shot except answering, and a session that cannot be released looks dead.
        var emulator = NewTerminal(out var sent);

        Feed(emulator, Regis("P[10,10]R(I0)R(P(I))"));
        Feed(emulator, "HELD");

        // Cleared AFTER entering the mode: R(I) legitimately returns its synchronizing carriage
        // return, and that is not what this test is about.
        sent.Clear();

        Assert.True(emulator.CancelRegisInput());

        Assert.False(emulator.IsRegisInputSuspended);
        Assert.Equal("HELD", LineText(emulator.GetBuffer(), 0));

        // Nothing is reported. An application counting its requests sees one go unanswered, which is
        // what actually happened - the operator declined to answer it.
        Assert.Equal(string.Empty, SentText(sent));
    }

    [Fact]
    public void TextAfterTheModeChangeInTheSameChunkIsAlsoHeld()
    {
        // The trap. A host that puts R(I0) and its next line in one write would have had the line
        // parsed anyway, because the parser was already part-way through the chunk. This is the same
        // stop-and-hand-back the printer controller needs, for the same reason.
        var emulator = NewTerminal(out _);

        Feed(emulator, Regis("P[10,10]R(I0)R(P(I))") + "SAMECHUNK");

        Assert.Equal(string.Empty, LineText(emulator.GetBuffer(), 0));

        emulator.SendRegisInputReport("A");

        Assert.Equal("SAMECHUNK", LineText(emulator.GetBuffer(), 0));
    }

    [Fact]
    public void TheRequestMustRideINTheSameCommandStringAsTheModeChange()
    {
        // Chapter 15 is absolute: "In one-shot mode, the terminal suspends processing of received
        // characters and commands. The terminal buffers ALL received characters, until it leaves
        // one-shot mode." So a request sent afterwards is buffered like everything else and is never
        // heard - an application has to send R(I0) and R(P(I)) together, which is how chapter 10
        // presents them.
        //
        // This is the reason the ESC cancel exists. Without it, a host that sent R(I0) on its own
        // could never be answered and could never take it back.
        var emulator = NewTerminal(out _);

        Feed(emulator, Regis("P[10,10]R(I0)"));
        Feed(emulator, Regis("R(P(I))"));

        Assert.False(emulator.SendRegisInputReport("A"));
        Assert.True(emulator.IsRegisInputSuspended);

        Assert.True(emulator.CancelRegisInput());
        Assert.False(emulator.IsRegisInputSuspended);
    }

    [Fact]
    public void MultipleModeDoesNotHoldAnythingBack()
    {
        // "The terminal processes characters and commands as it receives them from the host. This
        // feature lets the terminal perform graphics input and output at the same time."
        var emulator = NewTerminal(out _);

        Feed(emulator, Regis("R(I1)"));
        Feed(emulator, "FLOWING");

        Assert.False(emulator.IsRegisInputSuspended);
        Assert.Equal("FLOWING", LineText(emulator.GetBuffer(), 0));
    }

    [Fact]
    public void TheReportReachesTheHostThroughTheEmulator()
    {
        // The manual's own worked example: "A[102,200]CR - The user pressed the letter 'A' with the
        // cursor at position 102,200."
        var emulator = NewTerminal(out var sent);

        Feed(emulator, Regis("P[102,200]R(I0)R(P(I))"));
        sent.Clear();

        Assert.True(emulator.SendRegisInputReport("A"));

        Assert.Equal("A[102,200]\r", SentText(sent));
    }

    [Fact]
    public void TheSynchronizingCarriageReturnReachesTheHost()
    {
        // "When the terminal receives R(I), it returns a carriage return (CR). Applications can use
        // the CR for synchronization." A host waiting for it hangs if it never arrives.
        var emulator = NewTerminal(out var sent);

        Feed(emulator, Regis("R(I0)"));

        Assert.Equal("\r", SentText(sent));
    }

    [Fact]
    public void AnArrowKeyMovesTheCursorAndIsNotPassedOn()
    {
        // True means the keypress belonged to graphics input. The caller uses that to decide whether
        // the key ALSO goes to the host, which in one-shot mode it must not.
        var emulator = NewTerminal(out var sent);

        Feed(emulator, Regis("P[100,100]R(I0)R(P(I))"));
        sent.Clear();

        Assert.True(emulator.MoveRegisInputCursor(RegisDecoder.InputCursorShiftStep, 0));
        emulator.SendRegisInputReport("A");

        Assert.Equal("A[110,100]\r", SentText(sent));
    }

    [Fact]
    public void AnArrowKeyIsPassedOnInMultipleMode()
    {
        // "You cannot use the four arrow keys to position the input cursor as you can in ReGIS
        // one-shot graphics input mode. If you press an arrow key in multiple mode, the terminal
        // sends that key's escape sequence to the host." False here is what lets it through.
        var emulator = NewTerminal(out _);

        Feed(emulator, Regis("R(I1)"));

        Assert.False(emulator.MoveRegisInputCursor(1, 0));
    }

    [Fact]
    public void AnArrowKeyIsPassedOnWhenNoInputModeIsRunning()
    {
        // The ordinary case, and by far the most common one: nothing about this feature may change
        // what an arrow key does on a terminal that never entered graphics input mode.
        var emulator = NewTerminal(out _);

        Assert.False(emulator.MoveRegisInputCursor(1, 0));
        Assert.False(emulator.CancelRegisInput());
    }

    [Fact]
    public void TheCrosshairIsDrawnOnAPlaneOfItsOwn()
    {
        // It cannot go on the drawing: it moves with every keypress, so it would leave a trail, and
        // erasing it would take the picture underneath with it.
        var emulator = NewTerminal(out _);

        Feed(emulator, Regis("P[100,100]R(I0)"));

        var plane = emulator.Graphics?.FindPlane(TerminalEmulatorBase.RegisInputCursorPlaneId);

        Assert.NotNull(plane);
        Assert.True(plane!.IsVisible);
    }

    [Fact]
    public void TheCrosshairCrossesTheWholeScreenAtTheCursor()
    {
        // "This cursor is a horizontal and a vertical line. The horizontal line is the width of the
        // screen, and the vertical line is the height of the screen. The two lines intersect at the
        // active position."
        var emulator = NewTerminal(out _);

        Feed(emulator, Regis("P[100,50]R(I0)"));

        var surface = emulator.Graphics!.FindPlane(TerminalEmulatorBase.RegisInputCursorPlaneId)!.Surface;

        // Along the horizontal line, far from the intersection at both ends.
        Assert.NotEqual(GraphicsColor.Transparent, surface.GetPixel(0, 50));
        Assert.NotEqual(GraphicsColor.Transparent, surface.GetPixel(surface.Width - 1, 50));

        // And down the vertical one.
        Assert.NotEqual(GraphicsColor.Transparent, surface.GetPixel(100, 0));
        Assert.NotEqual(GraphicsColor.Transparent, surface.GetPixel(100, surface.Height - 1));

        // A point on neither line stays empty, or "the whole screen is lit" would pass this too.
        Assert.Equal(GraphicsColor.Transparent, surface.GetPixel(300, 200));
    }

    [Fact]
    public void TheCrosshairFollowsTheArrowKeys()
    {
        // The old row must go dark and the new one light. Asserting only the new row would pass
        // against an implementation that never cleared, which is exactly the trail defect.
        var emulator = NewTerminal(out _);

        Feed(emulator, Regis("P[100,50]R(I0)"));
        emulator.MoveRegisInputCursor(0, RegisDecoder.InputCursorShiftStep);

        var surface = emulator.Graphics!.FindPlane(TerminalEmulatorBase.RegisInputCursorPlaneId)!.Surface;

        Assert.NotEqual(GraphicsColor.Transparent, surface.GetPixel(0, 60));
        Assert.Equal(GraphicsColor.Transparent, surface.GetPixel(0, 50));
    }

    [Fact]
    public void MovingTheCrosshairAsksForARepaint()
    {
        // Found by reading, not by a failure. An arrow keypress changes no text and receives no host
        // data, so nothing else in the program would ever ask for a repaint - the crosshair moved on
        // its plane and the screen went on showing where it used to be. Every plane assertion above
        // passed the whole time.
        var emulator = NewTerminal(out _);
        Feed(emulator, Regis("P[100,100]R(I0)"));

        int repaints = 0;
        emulator.Invalidated += () => repaints++;

        emulator.MoveRegisInputCursor(RegisDecoder.InputCursorShiftStep, 0);

        Assert.Equal(1, repaints);
    }

    [Fact]
    public void EndingTheModeAsksForARepaint()
    {
        // Same question at the other end: the crosshair has to actually disappear from the screen,
        // not merely from its plane.
        var emulator = NewTerminal(out _);
        Feed(emulator, Regis("P[100,100]R(I0)"));

        int repaints = 0;
        emulator.Invalidated += () => repaints++;

        emulator.CancelRegisInput();

        Assert.True(repaints > 0);
    }

    [Fact]
    public void TheCrosshairGoesAwayWhenTheModeEnds()
    {
        // "The input cursor disappears from the screen, and the terminal exits one-shot mode."
        var emulator = NewTerminal(out _);

        Feed(emulator, Regis("P[100,50]R(I0)R(P(I))"));
        emulator.SendRegisInputReport("A");

        var plane = emulator.Graphics!.FindPlane(TerminalEmulatorBase.RegisInputCursorPlaneId)!;

        Assert.False(plane.IsVisible);
        Assert.Equal(GraphicsColor.Transparent, plane.Surface.GetPixel(0, 50));
    }

    [Fact]
    public void TheCrosshairDoesNotTouchTheHostsDrawing()
    {
        // The drawing must come through the pause unchanged. A crosshair painted onto the ReGIS
        // plane would have written over this line and stayed there after the mode ended.
        var emulator = NewTerminal(out _);

        Feed(emulator, Regis("P[0,50]V[799,50]P[100,200]R(I0)"));
        emulator.MoveRegisInputCursor(0, RegisDecoder.InputCursorShiftStep);
        emulator.CancelRegisInput();

        var drawing = emulator.Graphics!.FindPlane(TerminalEmulatorBase.RegisPlaneId)!.Surface;

        Assert.NotEqual(GraphicsColor.Transparent, drawing.GetPixel(400, 50));
        Assert.Equal(GraphicsColor.Transparent, drawing.GetPixel(400, 210));
    }
}
