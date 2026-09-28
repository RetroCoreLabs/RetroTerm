using System.Text;
using RetroTerm.Core.Configuration;
using RetroTerm.Core.Terminal.Emulators;
using Xunit;

namespace RetroTerm.Tests.Terminal;

/// <summary>
/// DECDLD - the character shapes a host draws and downloads.
///
/// The host sends the SHAPES: for each character, a grid of lit and unlit pixels, which the
/// terminal then draws wherever that character appears. It is how a VT220 showed a currency
/// symbol or a line-drawing corner its ROM had never heard of.
///
/// The encoding is the interesting part. Each character from '?' to '~' carries SIX pixels in a
/// vertical strip - subtract 0x3F and the low bit is the top pixel. Strips run left to right, '/'
/// starts the next band of six rows below, and ';' ends the character. That is the same idea as a
/// sixel image, because the format was designed for hardware that shifted six pixels at a time.
/// </summary>
public class SoftFontTests
{
    private static TerminalEmulatorBase Build(string type = "VT220")
        => EmulatorFactory.CreateEmulator(type, 80, 24, 100);

    private static void Feed(TerminalEmulatorBase emulator, string text)
        => emulator.ProcessData(Encoding.ASCII.GetBytes(text));

    /// <summary>
    /// A DECDLD that defines one character, 8 by 12, starting at the first position.
    /// </summary>
    private static string Download(string glyphs, int startCharacter = 1)
        => "\x1bP1;" + startCharacter + ";1;8;0;0;12;0{ @" + glyphs + "\x1b\\";

    [Fact]
    public void OneStripLightsSixPixelsDownTheLeftEdge()
    {
        // '~' is 0x7E, so 0x7E - 0x3F is 63: all six bits set, one full column.
        var emulator = Build();

        Feed(emulator, Download("~"));

        Assert.True(emulator.SoftFont.TryGetGlyph(0, out var rows));
        for (int row = 0; row < 6; row++)
        {
            Assert.Equal(0x8000, rows[row]);
        }
        Assert.Equal(0, rows[6]);
    }

    [Fact]
    public void TheLowBitIsTheTopPixel()
    {
        // '@' is 0x40, one more than '?', so bit 0 alone - the top pixel of the strip.
        var emulator = Build();

        Feed(emulator, Download("@"));

        Assert.True(emulator.SoftFont.TryGetGlyph(0, out var rows));
        Assert.Equal(0x8000, rows[0]);
        Assert.Equal(0, rows[1]);
    }

    [Fact]
    public void StripsRunLeftToRight()
    {
        // Two strips: the first column full, the second empty, the third full.
        var emulator = Build();

        Feed(emulator, Download("~?~"));

        Assert.True(emulator.SoftFont.TryGetGlyph(0, out var rows));
        // Bit 15 is the leftmost pixel, which is how a ROM glyph is stored too.
        Assert.Equal(0xA000, rows[0]);
    }

    [Fact]
    public void ASlashStartsTheNextBandOfSixRows()
    {
        var emulator = Build();

        Feed(emulator, Download("?/~"));

        Assert.True(emulator.SoftFont.TryGetGlyph(0, out var rows));
        Assert.Equal(0, rows[0]);
        Assert.Equal(0x8000, rows[6]);
        Assert.Equal(0x8000, rows[11]);
    }

    [Fact]
    public void ASemicolonStartsTheNextCharacter()
    {
        var emulator = Build();

        Feed(emulator, Download("~;?~"));

        Assert.True(emulator.SoftFont.TryGetGlyph(0, out var first));
        Assert.True(emulator.SoftFont.TryGetGlyph(1, out var second));
        Assert.Equal(0x8000, first[0]);
        Assert.Equal(0x4000, second[0]);
        Assert.Equal(2, emulator.SoftFont.Count);
    }

    [Fact]
    public void TheDownloadCanStartPartWayIntoTheSet()
    {
        var emulator = Build();

        Feed(emulator, Download("~", startCharacter: 5));

        Assert.False(emulator.SoftFont.TryGetGlyph(0, out _));
        Assert.True(emulator.SoftFont.TryGetGlyph(4, out _));
    }

    [Fact]
    public void TheDesignationIsKept()
    {
        // The name a host uses to select this set later. Stored, not acted on - selecting a
        // downloaded set is a separate sequence this terminal does not implement yet.
        var emulator = Build();

        Feed(emulator, Download("~"));

        Assert.Equal(" @", emulator.SoftFont.Designation);
    }

    [Fact]
    public void TheMatrixSizeComesFromTheSequence()
    {
        var emulator = Build();

        Feed(emulator, Download("~"));

        Assert.Equal(8, emulator.SoftFont.MatrixWidth);
        Assert.Equal(12, emulator.SoftFont.MatrixHeight);
    }

    [Fact]
    public void AStripPastTheMatrixWidthIsDropped()
    {
        // The matrix says 8 wide, so a ninth strip has nowhere to go. Storing it would write past
        // the character the host described.
        var emulator = Build();

        Feed(emulator, Download("~~~~~~~~~"));

        Assert.True(emulator.SoftFont.TryGetGlyph(0, out var rows));
        Assert.Equal(0xFF00, rows[0]);   // eight columns lit, and no ninth
    }

    [Fact]
    public void TheDefaultIsToEraseTheWholeSetFirst()
    {
        // Pe defaults to 0, which clears everything before loading - the same shape as DECUDK,
        // where the short form is the destructive one.
        var emulator = Build();
        Feed(emulator, Download("~;~;~"));
        Assert.Equal(3, emulator.SoftFont.Count);

        Feed(emulator, "\x1bP1;1;0;8;0;0;12;0{ @~\x1b\\");

        Assert.Equal(1, emulator.SoftFont.Count);
    }

    [Fact]
    public void AHardResetForgetsTheDownloadedSet()
    {
        // The shapes came from a host, and a reset leaves the terminal as it was switched on.
        var emulator = Build();
        Feed(emulator, Download("~"));

        emulator.Reset();

        Assert.Equal(0, emulator.SoftFont.Count);
        Assert.Equal("", emulator.SoftFont.Designation);
    }

    [Fact]
    public void ATerminalWithoutTheFeatureIgnoresTheDownload()
    {
        // A VT100 had no downloadable characters at all.
        var emulator = Build("VT100");

        Feed(emulator, Download("~"));

        Assert.Equal(0, emulator.SoftFont.Count);
    }

    [Fact]
    public void SelectingTheSetMarksTheCellsThatComeFromIt()
    {
        // The designator is the name the host gave the set in the DECDLD that loaded it, matched
        // against what was stored rather than against a table - the host chose the name.
        var emulator = Build();
        Feed(emulator, Download("~"));

        Feed(emulator, "\x1b( @");
        Feed(emulator, " ");

        emulator.GetBuffer().TryGetCell(0, 0, out var cell);
        Assert.Equal(TerminalEmulatorBase.SoftFontCharacterSet, cell.CharacterSet);
    }

    [Fact]
    public void AnUnknownDesignatorLeavesTheSetAlone()
    {
        // Designating something this terminal does not have must not quietly fall back to ASCII:
        // that would draw the wrong letters and say nothing about it.
        var emulator = Build();
        Feed(emulator, Download("~"));

        Feed(emulator, "\x1b( B");   // an intermediate, but not this set's name
        Feed(emulator, "X");

        emulator.GetBuffer().TryGetCell(0, 0, out var cell);
        Assert.NotEqual(TerminalEmulatorBase.SoftFontCharacterSet, cell.CharacterSet);
    }

    [Fact]
    public void ADecudkSequenceIsNotMistakenForAFont()
    {
        // Both arrive as DCS and are told apart by the final byte: '|' loads keys, '{' loads
        // shapes.
        var emulator = Build();

        Feed(emulator, "\x1bP1;1|17/6c73\x1b\\");

        Assert.Equal(0, emulator.SoftFont.Count);
        Assert.Equal(1, emulator.UserKeys.Count);
    }

    // ── Pcss changes what Pcn means ─────────────────────────────────────────────────────────

    /// <summary>
    /// A DECDLD that declares itself a 96-character set.
    /// </summary>
    /// <param name="glyphs">
    /// The sixel strips.
    /// </param>
    /// <param name="startCharacter">
    /// Pcn, which in a 96-character set counts from 0 at position 2/0.
    /// </param>
    /// <returns>
    /// The whole control string.
    /// </returns>
    private static string Download96(string glyphs, int startCharacter)
        => "\x1bP1;" + startCharacter + ";1;8;0;0;12;1{ @" + glyphs + "\x1b\\";

    [Fact]
    public void InANinetySixCharacterSetPcnCountsFromZero()
    {
        // "If Pcss = 1 (96-character set): Pcn 0 specifies column 2/row 0." The whole set was
        // being shifted one position left, because the 94-character reading was applied to both.
        var emulator = Build();

        Feed(emulator, Download96("~", startCharacter: 5));

        Assert.True(emulator.SoftFont.TryGetGlyph(5, out _));
        Assert.False(emulator.SoftFont.TryGetGlyph(4, out _));
    }

    [Fact]
    public void AndTheFirstPositionOfAllIsReachable()
    {
        // Pcn 0 in a 96-character set is a real position - 2/0. In a 94-character set it is not.
        var emulator = Build();

        Feed(emulator, Download96("~", startCharacter: 0));

        Assert.True(emulator.SoftFont.TryGetGlyph(0, out _));
    }

    [Fact]
    public void ButANinetyFourCharacterSetHasNoPositionTwoZero()
    {
        // "If Pcss = 0 (94-character set), the terminal ignores any attempt to load characters
        // into the 2/0 or 7/15 table positions." So Pcn 0 loses its glyph, and the NEXT one lands
        // in the first real position rather than the whole string being thrown away.
        var emulator = Build();

        Feed(emulator, Download("~;~", startCharacter: 0));

        Assert.Equal(1, emulator.SoftFont.Count);
        Assert.True(emulator.SoftFont.TryGetGlyph(0, out _));
    }

    [Fact]
    public void AndItStopsAtPositionSevenFourteen()
    {
        // Pcn 94 is 7/14, the last position a 94-character set has. A glyph after it would be
        // 7/15, which that set does not have either.
        var emulator = Build();

        Feed(emulator, Download("~;~", startCharacter: 94));

        Assert.True(emulator.SoftFont.TryGetGlyph(93, out _));
        Assert.Equal(1, emulator.SoftFont.Count);
    }

    [Fact]
    public void AWholeNinetySixCharacterSetFits()
    {
        // The last position of a 96-character set is 7/15, which a 94-character set refuses.
        var emulator = Build();

        Feed(emulator, Download96("~", startCharacter: 95));

        Assert.True(emulator.SoftFont.TryGetGlyph(95, out _));
    }
}
