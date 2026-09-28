using System.Text;
using RetroTerm.Core.Terminal.Emulators.Tektronix;
using RetroTerm.Core.Terminal.Graphics;
using Xunit;

namespace RetroTerm.Tests.Terminal.Graphics;

/// <summary>
/// The 4014's four character sizes.
/// </summary>
/// <remarks>
/// <para><b>The numbers are the manual's</b></para>
/// The "4010/4014 Mode" chapter of
/// <c>spec\DEC\EK-VT3XX-GP-002_VT330_VT340_Graphics_Programming_May88.pdf</c>, aligned mode:
///  - <c>ESC 8</c> - 35 lines of 74 characters, the default.
///  - <c>ESC 9</c> - 38 lines of 81 characters.
///  - <c>ESC :</c> - 58 lines of 121 characters.
///  - <c>ESC ;</c> - 64 lines of 133 characters.
///
/// The tube does not change size. The cell does, so more characters fit on the same glass.
///
/// <para><b>Why it mattered</b></para>
/// The corpus rendering tests had to build their terminal at 74 by 35 by hand, because a stream can
/// ask for its own geometry and was being ignored.
/// </remarks>
public class TektronixCharacterSizeTests
{
    /// <summary>
    /// ESC as its own string, and never written inline before a hex digit.
    /// </summary>
    /// <remarks>
    /// C# hex escapes are VARIABLE length - one to four digits - so a backslash-x-1-b written immediately before the character 8 is NOT ESC followed by
    /// the character 8, it is the single character U+01B8. Every sequence on this page ends in a
    /// character that can be a hex digit, so the whole file concatenates instead. This cost a
    /// debugging session: the test failed, the emulator was blamed, and the emulator was right.
    /// </remarks>
    private const string Esc = "\u001b";

    private static Tek4014Emulator Build() => new Tek4014Emulator();

    /// <summary>
    /// One 4014 coordinate, in the four bytes the tube expects.
    /// </summary>
    /// <remarks>
    /// High Y, low Y, high X, low X - five bits each, tagged by their top bits so the decoder can
    /// tell them apart in any order a host chooses to omit them. Writing this out rather than
    /// hand-picking characters: a wrong byte here reads as a plausible coordinate somewhere else
    /// on the screen, which is a slow thing to debug.
    /// </remarks>
    /// <param name="x">
    /// Horizontal position, 0 to 1023.
    /// </param>
    /// <param name="y">
    /// Vertical position, 0 to 779.
    /// </param>
    /// <returns>
    /// The four bytes as a string.
    /// </returns>
    private static string Point(int x, int y)
    {
        char highY = (char)(0x20 | ((y >> 5) & 0x1F));
        char lowY = (char)(0x60 | (y & 0x1F));
        char highX = (char)(0x20 | ((x >> 5) & 0x1F));
        char lowX = (char)(0x40 | (x & 0x1F));

        return new string(new[] { highY, lowY, highX, lowX });
    }

    private static void Feed(Tek4014Emulator emulator, string data)
        => emulator.ProcessData(Encoding.ASCII.GetBytes(data));

    [Fact]
    public void ATerminalStartsAtThirtyFiveLinesOfSeventyFour()
    {
        var emulator = Build();

        Assert.Equal(74, emulator.Width);
        Assert.Equal(35, emulator.Height);
        Assert.Equal(Tek4014Emulator.CharacterSize.Size1, emulator.TextSize);
    }

    [Theory]
    [InlineData("8", 74, 35)]
    [InlineData("9", 81, 38)]
    [InlineData(":", 121, 58)]
    [InlineData(";", 133, 64)]
    public void EachSequenceSelectsTheGridTheManualGivesIt(string final, int columns, int rows)
    {
        var emulator = Build();

        Feed(emulator, "\x1b" + final);

        Assert.Equal(columns, emulator.Width);
        Assert.Equal(rows, emulator.Height);
    }

    [Theory]
    [InlineData("0", 133, 64)]
    [InlineData("1", 74, 35)]
    [InlineData("2", 74, 35)]
    [InlineData("3", 74, 35)]
    public void TheFourDigitalDiscouragesStillWork(string final, int columns, int rows)
    {
        // "Digital does not recommend using ESC 0, ESC 1, ESC 2, and ESC 3. These sequences are not
        // standard Tektronix sequences." Discouraged is not the same as absent, and the manual
        // prints what each one does - so a host that sends one gets what it asked for.
        var emulator = Build();
        Feed(emulator, "\x1b;");

        Feed(emulator, "\x1b" + final);

        Assert.Equal(columns, emulator.Width);
        Assert.Equal(rows, emulator.Height);
    }

    [Fact]
    public void GoingBackToTheDefaultRestoresTheOriginalGrid()
    {
        var emulator = Build();
        Feed(emulator, Esc + ";");
        Assert.Equal(133, emulator.Width);

        // Esc + "8", NOT "\x1b8" - see the note on Esc. That literal is one character, U+01B8.
        Feed(emulator, Esc + "8");

        Assert.Equal(74, emulator.Width);
        Assert.Equal(35, emulator.Height);
    }

    [Fact]
    public void TextWrittenAfterwardsFitsTheNewWidth()
    {
        // The point of the smallest cell: 133 columns of text on a tube that held 74.
        var emulator = Build();

        Feed(emulator, "\x1b;");
        Feed(emulator, new string('X', 133));

        var buffer = emulator.GetBuffer();
        buffer.TryGetCell(0, 132, out var last);

        Assert.Equal((uint)'X', last.Codepoint);
    }

    [Fact]
    public void AnEraseStillClearsWhateverSizeIsInForce()
    {
        // ESC FF is the one erase a storage tube has, and changing the cell must not take it away.
        var emulator = Build();
        Feed(emulator, "\x1b:");
        Feed(emulator, "HELLO");

        Feed(emulator, "\x1b\x0c");

        var buffer = emulator.GetBuffer();
        buffer.TryGetCell(0, 0, out var cell);

        Assert.True(cell.Codepoint == 0 || cell.Codepoint == ' ');
        Assert.Equal(121, emulator.Width);
    }

    // ─────────────────────────────────────────────────────────────
    // Select Vector Patterns - ESC ` through ESC g
    //
    // The manual lists eight sequences and five distinct patterns; e, f and g are solid again,
    // which is the table as printed rather than a mistake in reading it.
    // ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("`", LinePattern.Solid)]
    [InlineData("a", LinePattern.Dotted)]
    [InlineData("b", LinePattern.DotDash)]
    [InlineData("c", LinePattern.ShortDash)]
    [InlineData("d", LinePattern.LongDash)]
    [InlineData("e", LinePattern.Solid)]
    [InlineData("f", LinePattern.Solid)]
    [InlineData("g", LinePattern.Solid)]
    public void EachSequenceSelectsThePatternTheManualNames(string final, LinePattern expected)
    {
        var emulator = Build();

        Feed(emulator, Esc + final);

        Assert.Equal(expected, emulator.Plotter.Pattern);
    }

    [Fact]
    public void ADottedVectorLeavesGapsOnTheScreen()
    {
        // End to end: the escape reaches the plotter, the plotter reaches the surface, and the
        // surface leaves holes. GS moves to the first point, the second point draws.
        var emulator = Build();
        Feed(emulator, Esc + "a");

        // GS, then two points: a long horizontal vector across the tube. The first point moves,
        // the second draws.
        Feed(emulator, ((char)0x1D) + Point(100, 400) + Point(900, 400));

        var surface = emulator.Graphics!.Output;
        int lit = 0;
        int blank = 0;

        for (int y = 0; y < surface.Height; y++)
        {
            for (int x = 0; x < surface.Width; x++)
            {
                if (surface.GetPixel(x, y).IsTransparent) blank++; else lit++;
            }
        }

        Assert.True(lit > 0, "the dotted vector drew nothing at all");
        Assert.True(blank > 0, "a dotted vector must leave gaps");
    }

    [Fact]
    public void AnEraseputsThePatternBackToSolid()
    {
        // "Selecting alpha mode ... resets the pattern register and intensity."
        var emulator = Build();
        Feed(emulator, Esc + "d");
        Assert.Equal(LinePattern.LongDash, emulator.Plotter.Pattern);

        Feed(emulator, Esc + "\f");

        Assert.Equal(LinePattern.Solid, emulator.Plotter.Pattern);
    }
}
