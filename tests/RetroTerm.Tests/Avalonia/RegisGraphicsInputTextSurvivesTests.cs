using System.Text;
using Avalonia.Headless.XUnit;
using RetroTerm.Core.Configuration;
using RetroTerm.Core.Terminal.Emulators;
using Xunit;
using Xunit.Abstractions;

namespace RetroTerm.Tests.Avalonia;

/// <summary>
/// Whether the text on the screen survives a ReGIS drawing that enters graphics input mode.
/// </summary>
/// <remarks>
/// <para><b>Why this exists</b></para>
/// Ronny ran case M6.5 by hand on 25 August 2026, pressed the key that starts graphics input mode,
/// and reported <b>the screen went blank</b>. Reading the text buffer over the wire at the same
/// moment showed the text was all still there - round one had finished and round two had started.
/// So the characters are in the buffer and something on top of them is hiding them.
/// <para><b>What a buffer read cannot settle</b></para>
/// That is exactly the gap this file closes. Every other test of graphics input mode looks at the
/// emulator state or at the graphics plane. None of them renders the whole control and asks the
/// only question the operator actually asked: <b>can you still read the text?</b>
/// <para><b>The stream is the real one</b></para>
/// The bytes below are what the test server's graphics-input entry sends, copied rather than
/// invented, so a pass here means that menu entry is usable and a failure here is the thing Ronny
/// saw.
/// </remarks>
[Collection("Avalonia")]
public class RegisGraphicsInputTextSurvivesTests
{
    /// <summary>
    /// The escape character, built from its code so no raw control byte lands in this file.
    /// </summary>
    private const char Escape = (char)0x1B;

    private readonly ITestOutputHelper _output;

    public RegisGraphicsInputTextSurvivesTests(ITestOutputHelper output)
    {
        _output = output;
    }

    /// <summary>
    /// The ReGIS the test server sends for one round of graphics input.
    /// </summary>
    /// <param name="cursorStyle">
    /// The shape to select, as the argument to the S command.
    /// </param>
    /// <returns>
    /// The whole payload including the introducer and the terminator.
    /// </returns>
    private static string GraphicsInputRound(int cursorStyle)
    {
        var regis = new StringBuilder();
        regis.Append(Escape).Append("Pp");
        regis.Append("S(E)");
        regis.Append("W(I3)");
        regis.Append("P[100,100]");
        regis.Append("V[500,100][500,400][100,400][100,100]");
        regis.Append("P[100,100]V[500,400]");
        regis.Append("P[500,100]V[100,400]");
        regis.Append("P[300,250]C[+120]");
        regis.Append("S(C(I").Append(cursorStyle).Append("))");
        regis.Append("P[400,240]");
        regis.Append("R(I0)R(P(I))");
        regis.Append(Escape).Append("\\");
        return regis.ToString();
    }

    /// <summary>
    /// A VT340 with a line of text already on the screen.
    /// </summary>
    /// <returns>
    /// The emulator.
    /// </returns>
    private static TerminalEmulatorBase NewVt340WithText()
    {
        var emulator = EmulatorFactory.CreateEmulator("VT340", 80, 24, 100);

        // Hide the cursor so a blinking block cannot be mistaken for glyph ink in row zero.
        emulator.ProcessData(Encoding.ASCII.GetBytes(Escape + "[?25l"));
        emulator.ProcessData(Encoding.ASCII.GetBytes("ROUND 1 OF 4: CROSSHAIR\r\n"));
        return emulator;
    }

    [AvaloniaFact]
    public void TheTextIsReadableBeforeTheDrawingArrives()
    {
        // The control case. Without this, a failure below could just as easily mean the text was
        // never drawn in the first place, and the drawing would be blamed for nothing.
        var emulator = NewVt340WithText();

        var shot = RenderedScreenshot.Capture(emulator, "regis-input-text-before");

        Assert.True(shot.CellHasInk(0, 0), "the text was not drawn even before any ReGIS arrived");
        _output.WriteLine("before: " + shot.SavedPath);
    }

    [AvaloniaFact]
    public void TheTextIsStillReadableAfterGraphicsInputModeStarts()
    {
        // The operator's own question. The drawing occupies the top-left of an 800x480 plane and the
        // text sits in the top-left of the screen, so they overlap - but a vector drawing is lines
        // on transparency, and lines cannot hide a whole screen of characters.
        var emulator = NewVt340WithText();

        emulator.ProcessData(Encoding.ASCII.GetBytes(GraphicsInputRound(0)));

        var shot = RenderedScreenshot.Capture(emulator, "regis-input-text-after");
        _output.WriteLine("after: " + shot.SavedPath);

        int inkBefore = 0;
        int inkAfter = 0;
        for (int col = 0; col < 23; col++)
        {
            if (shot.CellHasInk(0, col)) inkAfter++;
        }

        // Row zero holds "ROUND 1 OF 4: CROSSHAIR" - 23 cells, four of them spaces or a colon that
        // may or may not clear the ink threshold. Counting rather than testing one cell means a
        // single unlucky glyph cannot decide the result.
        inkBefore = 15;
        Assert.True(inkAfter >= inkBefore,
            "the text went missing when graphics input mode started: only " + inkAfter +
            " of 23 cells in row zero still have ink. This is what Ronny saw as a blank screen.");
    }
}
