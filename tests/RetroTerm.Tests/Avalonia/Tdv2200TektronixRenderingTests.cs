using System.Text;
using Avalonia.Headless.XUnit;
using RetroTerm.Core.Terminal.Emulators.TDV;
using RetroTerm.Core.Terminal.Graphics;
using Xunit;

namespace RetroTerm.Tests.Avalonia;

/// <summary>
/// The Norsk Data machine as a Tektronix 4014: TDV2200 plus vector graphics, through the real
/// renderer.
/// </summary>
/// <remarks>
/// <para><b>Why this file exists</b></para>
/// The 4014 features were all tested against <c>Tek4014Emulator</c>, the standalone terminal, and
/// the TDV2200 was tested for vectors and for the ND <c>ESC "</c> protocol. Nothing drove the 4014
/// FEATURE set through a TDV2200 - and the two are not the same machine. The TDV has its own
/// Tektronix mode and answers <c>ESC ENQ</c> with the seven-byte ND form rather than a real 4014's
/// five.
///
/// That gap hid a defect. The ND analysis lists under "Compatible (standard Tek 4014)":
/// "Line types: Dotted (ESC a), dot-dashed (ESC b), short-dashed (ESC c), long-dashed (ESC d),
/// normal (ESC `)". <c>Tek4014Emulator</c> implemented all five; the TDV2200 implemented none, so a
/// host asking an ND machine for a dashed line got a solid one. Nothing could catch it, because
/// nothing drove that combination.
///
/// <para><b>The ESC c clash</b></para>
/// On a 4014 <c>ESC c</c> is the short-dashed line type; on the text terminal it is the reset that
/// TDVEmulatorBase handles and that ND software uses. Both have to work on one machine, so graph
/// mode decides which applies. DERIVED - the analysis lists the line types and says nothing about
/// the clash. Ronny's call, 2026-08-18.
/// </remarks>
[Collection("Avalonia")]
public class Tdv2200TektronixRenderingTests
{
    private const byte Gs = 0x1D;
    private const byte Us = 0x1F;
    private const byte Esc = 0x1B;

    private static void Feed(TDV2200Emulator emulator, params byte[] data)
        => emulator.ProcessData(data);

    private static void Feed(TDV2200Emulator emulator, string text)
        => emulator.ProcessData(Encoding.ASCII.GetBytes(text));

    /// <summary>
    /// The four bytes a Tektronix host sends for one point.
    /// </summary>
    /// <param name="x">
    /// Horizontal, 0 to 1023.
    /// </param>
    /// <param name="y">
    /// Vertical, 0 to 779, measured upwards as the hardware does.
    /// </param>
    /// <returns>
    /// High Y, low Y, high X, low X.
    /// </returns>
    private static byte[] Point(int x, int y) => new byte[]
    {
        (byte)(0x20 | ((y >> 5) & 0x1F)),
        (byte)(0x60 | (y & 0x1F)),
        (byte)(0x20 | ((x >> 5) & 0x1F)),
        (byte)(0x40 | (x & 0x1F)),
    };

    /// <summary>
    /// Builds a TDV2200 with the cursor hidden, so nothing but the drawing puts ink down.
    /// </summary>
    /// <remarks>
    /// ND mode 17 makes the drawing plane visible. Without it the vectors land on the plane and the
    /// screen stays blank - which is a real behaviour, not a quirk of the test: a host turns the
    /// graphics display on before it plots.
    ///
    /// Both sequences are built from BYTES rather than string literals, for the same reason the
    /// line type letters are - see the remark on the helper below.
    /// </remarks>
    private static TDV2200Emulator Terminal()
    {
        var emulator = new TDV2200Emulator(80, 24);

        // ESC [ ? 2 5 l - hide the cursor, which is ink like any other.
        Feed(emulator, new byte[] { Esc, (byte)'[', (byte)'?', (byte)'2', (byte)'5', (byte)'l' });

        // ESC " 1 7 h - ND mode 17, which makes the drawing plane visible.
        Feed(emulator, new byte[] { Esc, (byte)'"', (byte)'1', (byte)'7', (byte)'h' });

        return emulator;
    }

    /// <summary>
    /// Draws one long horizontal vector and counts the lit pixels along it.
    /// </summary>
    /// <param name="lineType">
    /// The line type letter to send after ESC while in graph mode, or null for whatever is current.
    /// </param>
    /// <remarks>
    /// The letter is passed as a CHARACTER and the escape built from bytes, deliberately. Writing
    /// it as the string "\x1ba" does NOT give escape-then-a: C# reads up to four hex digits after
    /// \x, so that literal is the single character U+01BA and the emulator never sees an escape at
    /// all. Every line type letter except the backtick is a hex digit, so this bites four times out
    /// of five, and the one test that used a byte array passed while the rest reported solid lines.
    /// </remarks>
    /// <returns>
    /// How many pixels of the vector carry ink.
    /// </returns>
    private static int LitPixelsAlongAVector(char? lineType)
    {
        var emulator = Terminal();

        Feed(emulator, Gs);
        Feed(emulator, Point(100, 400));

        if (lineType != null) Feed(emulator, new byte[] { Esc, (byte)lineType.Value });

        Feed(emulator, Point(900, 400));
        Feed(emulator, Us);

        var plane = emulator.Graphics!.FindPlane(NorskDataGraphicsModule.DrawingPlaneId);
        var surface = plane!.Surface;

        // The vector was drawn at y=400 measured upwards, so it lands on one row of the surface.
        // Which row does not matter - the whole plane is scanned and the busiest row counted.
        int best = 0;
        for (int y = 0; y < surface.Height; y++)
        {
            int lit = 0;
            for (int x = 0; x < surface.Width; x++)
            {
                if (!surface.GetPixel(x, y).IsTransparent) lit++;
            }

            if (lit > best) best = lit;
        }

        return best;
    }

    [AvaloniaFact]
    public void ASolidVectorFillsItsWholeLength()
    {
        // The baseline every dashed line is measured against.
        Assert.Equal(801, LitPixelsAlongAVector(null));
    }

    [AvaloniaFact]
    public void EscapeADrawsADottedLine()
    {
        // "Dotted (ESC a)". Half the pixels, because the mask alternates.
        int lit = LitPixelsAlongAVector('a');

        Assert.True(lit < 801, $"a dotted line must be shorter than a solid one - got {lit}");
        Assert.True(lit > 0, "a dotted line still draws something");
    }

    [AvaloniaFact]
    public void EachLineTypeUsesLessInkThanSolid()
    {
        // All four patterned types, in one place. Before the TDV2200 knew about them every one of
        // these came back as 801 - a solid line - which is the defect this file was written for.
        char[] lineTypes = { 'a', 'b', 'c', 'd' };

        for (int i = 0; i < lineTypes.Length; i++)
        {
            int lit = LitPixelsAlongAVector(lineTypes[i]);
            Assert.True(lit < 801,
                $"line type {i} drew {lit} pixels of 801 - it is still solid");
        }
    }

    [AvaloniaFact]
    public void EscapeBacktickPutsTheLineBackToSolid()
    {
        // "normal (ESC `)". A host that dashes one curve and wants the next one solid sends this.
        var emulator = Terminal();

        Feed(emulator, Gs);
        Feed(emulator, Point(100, 400));
        Feed(emulator, new byte[] { Esc, (byte)'a' });
        Assert.Equal(LinePattern.Dotted, emulator.GraphicsModule!.Pattern);

        Feed(emulator, new byte[] { Esc, (byte)'`' });
        Assert.Equal(LinePattern.Solid, emulator.GraphicsModule!.Pattern);
    }

    [AvaloniaFact]
    public void EscapeCIsTheResetWhenTheTerminalIsNotPlotting()
    {
        // The clash, from the text side. Outside graph mode ESC c must still reach the terminal
        // reset that ND software uses - it must NOT be swallowed as a line type.
        var emulator = Terminal();
        emulator.ResetWasCalled = false;

        Feed(emulator, new byte[] { Esc, (byte)'c' });

        Assert.True(emulator.ResetWasCalled, "ESC c outside graph mode is the terminal reset");
    }

    [AvaloniaFact]
    public void EscapeCIsTheLineTypeWhilePlotting()
    {
        // And the same escape from the graphics side, in the same test file so the pair cannot
        // drift apart.
        var emulator = Terminal();
        emulator.ResetWasCalled = false;

        Feed(emulator, Gs);
        Feed(emulator, Point(100, 400));
        Feed(emulator, new byte[] { Esc, (byte)'c' });

        Assert.Equal(LinePattern.ShortDash, emulator.GraphicsModule!.Pattern);
        Assert.False(emulator.ResetWasCalled, "ESC c in graph mode must not reset the terminal");
    }

    [AvaloniaFact]
    public void APlottedPictureReachesTheScreenThroughTheRealRenderer()
    {
        // The whole path, not just the plane: bytes to pixels through TerminalRenderer. A box in
        // dot-dash, which is what an ND plotting program draws its border with.
        var emulator = Terminal();

        Feed(emulator, Gs);
        Feed(emulator, Point(200, 200));
        Feed(emulator, new byte[] { Esc, (byte)'b' });
        Feed(emulator, Point(800, 200));
        Feed(emulator, Point(800, 600));
        Feed(emulator, Point(200, 600));
        Feed(emulator, Point(200, 200));
        Feed(emulator, Us);

        using var shot = RenderedScreenshot.Capture(emulator, "tdv2200-tek-dashed-box", saveZoom: 1);

        Assert.True(shot.HasAnyPixelDifferentFrom(shot.PixelAt(2, 2), 30),
            "the dashed box never reached the screen");
    }

    [AvaloniaFact]
    public void TextStillPrintsAfterAPlot()
    {
        // Leaving graph mode has to hand the terminal back. A label after a plot is how every
        // Tektronix program annotates its axes.
        var emulator = Terminal();

        Feed(emulator, Gs);
        Feed(emulator, Point(300, 300));
        Feed(emulator, Point(700, 300));
        Feed(emulator, Us);
        Feed(emulator, "SIN");

        using var shot = RenderedScreenshot.Capture(emulator, "tdv2200-tek-label", saveZoom: 1);

        // The cursor is parked at the last plotted point when graph mode ends, so the label starts
        // there rather than wherever the previous one finished.
        bool foundS = false;
        for (int row = 0; row < 24 && !foundS; row++)
        {
            for (int col = 0; col < 80; col++)
            {
                if (emulator.Buffer[row, col].Codepoint == 'S') { foundS = true; break; }
            }
        }

        Assert.True(foundS, "the label after the plot never reached the buffer");
    }

    [AvaloniaFact]
    public void TheLineTypeSurvivesLeavingAndReenteringGraphMode()
    {
        // A plotting program sets its style once and draws many curves, dropping to alpha between
        // them to print labels. Resetting the style on every US would silently solidify the lot.
        var emulator = Terminal();

        Feed(emulator, Gs);
        Feed(emulator, Point(100, 400));
        Feed(emulator, new byte[] { Esc, (byte)'d' });
        Feed(emulator, Us);
        Feed(emulator, Gs);

        Assert.Equal(LinePattern.LongDash, emulator.GraphicsModule!.Pattern);
    }
}
