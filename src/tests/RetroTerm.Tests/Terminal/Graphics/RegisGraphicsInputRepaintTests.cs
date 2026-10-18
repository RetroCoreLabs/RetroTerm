using System.Text;
using RetroTerm.Core.Configuration;
using RetroTerm.Core.Terminal.Emulators;
using Xunit;

namespace RetroTerm.Tests.Terminal.Graphics;

/// <summary>
/// The graphics input cursor must ask for a repaint, not just update the composite.
/// </summary>
/// <remarks>
/// <para><b>The defect, found on Ronny's own screen 29 August 2026</b></para>
/// He was given a live ReGIS graphics input session to drive and reported a BLANK screen and that
/// "no fucking keys does anything". Both were true from where he sat, and neither was what was
/// happening: the render of that same session showed the box, the diagonals, the circle and the
/// cursor, and his keystrokes had in fact answered three of the four rounds.
///
/// <c>RedrawRegisInputCursor</c> called <c>compositor.Composite()</c> and nothing else. Every other
/// composite site in this file pairs that call with <see cref="TerminalEmulatorBase.Invalidated"/>;
/// this one did not, so the picture was rebuilt in memory and nobody was ever told to paint it.
///
/// <para><b>Why it was invisible until a person looked at the real window</b></para>
/// One-shot graphics input SUSPENDS the host. That is the whole point of the mode - nothing more
/// arrives until the operator answers - so after the mode starts there is no other traffic to
/// invalidate the canvas either. The window freezes on the last frame it painted, which is the
/// heading printed a moment before the mode began, and every arrow press afterwards moves a cursor
/// nobody repaints.
///
/// The 27 August pass judged this case entirely through the SCREENSHOT command, which publishes the
/// graphics and draws the control itself - so it produced a correct picture from a session whose
/// real window was frozen. That is the second time an instrument has been mistaken for the thing it
/// measures, and it is why this test asserts the EVENT rather than the pixels.
/// </remarks>
public class RegisGraphicsInputRepaintTests
{
    /// <summary>
    /// The escape character, built from its code so no raw control byte lands in this file.
    /// </summary>
    private const char Escape = (char)0x1B;

    /// <summary>
    /// The string terminator's backslash, built from its code for the same reason.
    /// </summary>
    private const char Backslash = (char)0x5C;

    /// <summary>
    /// A drawing, so the terminal really has a compositor to work with.
    /// </summary>
    private const string ABox = "W(I2)P[200,120]V[600,120]V[600,360]V[200,360]V[200,120]";

    /// <summary>
    /// Builds a VT340 and runs some ReGIS on it.
    /// </summary>
    /// <param name="commands">
    /// The ReGIS to run.
    /// </param>
    /// <returns>
    /// The terminal.
    /// </returns>
    private static TerminalEmulatorBase Draw(string commands)
    {
        var emulator = EmulatorFactory.CreateEmulator("VT340", 80, 24, 100);
        emulator.ProcessData(Encoding.ASCII.GetBytes(Escape + "P1p" + commands + Escape + Backslash));
        return emulator;
    }

    [Fact]
    public void RaisingTheCursorAsksForARepaint()
    {
        // Entering the mode draws the cursor. A composite nobody paints is a picture nobody sees.
        var emulator = Draw(ABox + "P[400,240]");

        int repaints = 0;
        emulator.Invalidated += () => repaints++;

        emulator.ProcessData(Encoding.ASCII.GetBytes(Escape + "P1p" + "R(I0)" + Escape + Backslash));

        Assert.True(repaints > 0,
            "entering graphics input mode drew the cursor and asked for no repaint, so the window "
            + "keeps whatever it last painted");
    }

    [Fact]
    public void MovingTheCursorAsksForARepaint()
    {
        // The one that made it look like the keyboard was dead. Each arrow press moves the cursor
        // and rebuilds the composite; without a repaint the operator sees nothing move and
        // reasonably concludes the program has hung.
        var emulator = Draw(ABox + "P[400,240]R(I0)");

        int repaints = 0;
        emulator.Invalidated += () => repaints++;

        emulator.MoveRegisInputCursor(10, 0);

        Assert.True(repaints > 0,
            "moving the graphics input cursor asked for no repaint, so it does not appear to move");
    }

    [Fact]
    public void LoweringTheCursorAsksForARepaint()
    {
        // Answering ends the mode and clears the cursor's plane. Without a repaint the crosshair
        // stays on screen after it is gone from the composite - the mirror of the first case.
        var emulator = Draw(ABox + "P[400,240]R(I0)R(P(I))");

        int repaints = 0;
        emulator.Invalidated += () => repaints++;

        Assert.True(emulator.SendRegisInputReport("A"),
            "the report was refused, so the mode was not armed and this test measures nothing");

        Assert.True(repaints > 0,
            "leaving graphics input mode cleared the cursor and asked for no repaint, so it stays "
            + "on screen");
    }
}
