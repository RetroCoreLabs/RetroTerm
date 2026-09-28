using System.IO;
using System.Text;
using RetroTerm.Core.Configuration;
using RetroTerm.Core.Terminal.Emulators;
using Xunit;

namespace RetroTerm.Tests.Terminal.Graphics;

/// <summary>
/// Where the text cursor is left standing when a Sixel image ends.
/// </summary>
/// <remarks>
/// <para><b>What the manual says, quoted</b></para>
/// VT330/VT340 Programmer Reference, on sixel scrolling enabled: "when sixel mode is exited, the
/// text cursor is set to the current sixel cursor position". The SIXEL cursor - which moves only
/// when the image sends a graphics new line, <c>-</c>. Painting pixels never moves it, and neither
/// does <c>$</c>, which returns to the left margin inside the SAME band.
///
/// <para><b>What we did before 28 August 2026</b></para>
/// We counted pixel rows instead - <c>ceil(Height / GraphicsCellHeight)</c> line feeds. That gives
/// the same answer whenever every band ends with a <c>-</c> and one screen row is one pixel row,
/// which covers very nearly every image ever sent. That is exactly why it went unnoticed: the two
/// rules coincide at a 1:1 aspect ratio, so no ordinary fixture could tell them apart.
///
/// <para><b>The fixture that tells them apart</b></para>
/// <c>extremeratio.six</c>, from hackerb9's vt340test, declares an 80:1 aspect ratio and contains
/// NO graphics new line at all - it paints 480 screen rows out of a single band. Its height is
/// therefore 480 while its sixel cursor never moved from 0. The file's own closing comment reads
/// "A VT340 leaves the text cursor at the top of the screen"; we drove it 24 rows to the bottom.
///
/// <para><b>Red before green</b></para>
/// Restore the old line - <c>int rows = (_sixelDecoder.Height + GraphicsCellHeight - 1) /
/// GraphicsCellHeight;</c> - in <c>TerminalEmulatorBase</c> and
/// <c>TheCursorDoesNotMoveWhenTheImageNeverSendsAGraphicsNewLine</c> fails: the picture drives
/// the cursor 24 rows down instead of leaving it alone. Confirmed by doing it.
/// </remarks>
public class SixelExitCursorTests
{
    /// <summary>
    /// The escape character, built from its code so no raw control byte lands in this file.
    /// </summary>
    private const char Escape = (char)0x1B;

    /// <summary>
    /// A VT340 with the cursor hidden, so the only ink is the image.
    /// </summary>
    /// <returns>
    /// The emulator.
    /// </returns>
    private static TerminalEmulatorBase NewVt340()
    {
        var emulator = EmulatorFactory.CreateEmulator("VT340", 80, 24, 100);
        emulator.ProcessData(Encoding.ASCII.GetBytes(Escape + "[?25l"));
        return emulator;
    }

    /// <summary>
    /// The vt340test fixture directory, found from the assembly rather than the working directory.
    /// </summary>
    private static string FixtureDirectory => Path.GetFullPath(Path.Combine(
        Path.GetDirectoryName(typeof(SixelExitCursorTests).Assembly.Location) ?? "",
        "..", "..", "..", "Conformance", "vt340test"));

    [Fact]
    public void TheCursorDoesNotMoveWhenTheImageNeverSendsAGraphicsNewLine()
    {
        // The whole point of the fixture. Zero "-" means the sixel cursor never left band 0, so
        // the text cursor must not move either - however tall the pixels turned out.
        //
        // THE ROW IS MEASURED ACROSS THE PICTURE, not against zero. The fixture is a shell script
        // with real newlines between its title, comment and URL blocks, and those newlines move
        // the cursor exactly as they should. Asserting row 0 would be asserting something about
        // the file's formatting rather than about the image.
        string path = Path.Combine(FixtureDirectory, "extremeratio.six");
        Assert.True(File.Exists(path), "could not find " + path);

        string text = File.ReadAllText(path);

        // Guard the premise rather than trusting it. If somebody ever edits the fixture to contain
        // a graphics new line, this test would silently start proving nothing.
        Assert.DoesNotContain("-", SixelPayloadOf(text));

        int split = text.IndexOf(Escape.ToString() + "P9;0;0q", System.StringComparison.Ordinal);
        Assert.True(split > 0, "the fixture no longer opens its picture with ESC P 9;0;0 q");

        var emulator = NewVt340();
        emulator.ProcessData(Encoding.ASCII.GetBytes(text.Substring(0, split)));
        int before = emulator.Cursor.Row;

        // ONLY the picture block, terminator included. What follows it in the file is one more
        // line of English - "A VT340 leaves the text cursor at the top of the screen" - and
        // printing that would move the cursor by a row that has nothing to do with the image.
        int end = text.IndexOf(Escape.ToString() + "\\", split, System.StringComparison.Ordinal);
        Assert.True(end > split, "the fixture picture is not terminated");
        end += 2;

        emulator.ProcessData(Encoding.ASCII.GetBytes(text.Substring(split, end - split)));

        // Counting pixel rows gave 24 line feeds here, which parked the cursor on the last line of
        // the screen. The fixture says a real VT340 leaves it where it was.
        Assert.Equal(before, emulator.Cursor.Row);
    }

    [Fact]
    public void EachGraphicsNewLineMovesTheCursorByItsOwnBandAndNotByThePixelsDrawn()
    {
        // Four graphics new lines. The DCS omits its aspect parameter, which the specification
        // says is 0, which is 2:1 - so a band is TWELVE screen rows, not six, and four of them
        // put the sixel cursor at pixel row 48. On a 20-pixel cell that is text row 2.
        //
        // This said 1 until the DCS aspect was honoured on 28 August 2026. See SixelDcsAspectTests.
        var emulator = NewVt340();

        emulator.ProcessData(Encoding.ASCII.GetBytes(
            Escape + "Pq" + "#1~~~~" + "----" + Escape + "\\"));

        Assert.Equal(2, emulator.Cursor.Row);
    }

    [Fact]
    public void ADollarDoesNotMoveTheCursorBecauseItStaysInTheSameBand()
    {
        // "$" is a graphics CARRIAGE RETURN. It returns to the left margin without dropping a band,
        // which is how a multi-coloured image repaints the same six rows. It must not count.
        var emulator = NewVt340();

        emulator.ProcessData(Encoding.ASCII.GetBytes(
            Escape + "Pq" + "#1~~~~" + "$$$$$$$$" + Escape + "\\"));

        Assert.Equal(0, emulator.Cursor.Row);
    }

    [Fact]
    public void ScrollingDisabledStillLeavesTheCursorExactlyWhereItWas()
    {
        // The other half of the manual's rule, and the one that was already right. Pinned here so a
        // change to the exit path cannot quietly take it with it.
        var emulator = NewVt340();

        emulator.ProcessData(Encoding.ASCII.GetBytes(Escape + "[?80h"));
        int before = emulator.Cursor.Row;

        emulator.ProcessData(Encoding.ASCII.GetBytes(
            Escape + "Pq" + "#1~~~~" + "--------" + Escape + "\\"));

        Assert.Equal(before, emulator.Cursor.Row);
    }

    /// <summary>
    /// The Sixel payload of a vt340test fixture - what lies between the picture's own DCS
    /// introducer and the string terminator that closes it.
    /// </summary>
    /// <param name="text">
    /// The whole fixture file.
    /// </param>
    /// <returns>
    /// Just the sixel data.
    /// </returns>
    /// <remarks>
    /// The fixture is not bare sixel. It opens with several DCS blocks carrying a title, a
    /// comment and a URL, and it ends with a line of English AFTER the terminator - "A VT340
    /// leaves the text cursor at the top of the screen". All of that is prose and any hyphen in
    /// it would be read as a graphics new line by a guard that scanned the whole file, which is
    /// exactly what happened when this test was first written.
    ///
    /// The picture itself is the block introduced by <c>ESC P 9;0;0 q</c>.
    /// </remarks>
    private static string SixelPayloadOf(string text)
    {
        int start = text.IndexOf("9;0;0q", System.StringComparison.Ordinal);
        Assert.True(start >= 0, "the fixture no longer opens its picture with 9;0;0q");
        start += "9;0;0q".Length;

        int end = text.IndexOf(Escape.ToString() + "\\", start, System.StringComparison.Ordinal);
        Assert.True(end > start, "the fixture picture is not terminated");

        return text.Substring(start, end - start);
    }
}
