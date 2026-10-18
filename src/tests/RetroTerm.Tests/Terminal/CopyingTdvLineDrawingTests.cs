using System.Text;
using RetroTerm.Core.Configuration;
using RetroTerm.Core.Terminal.Buffer;
using RetroTerm.Core.Terminal.Emulators;
using Xunit;

namespace RetroTerm.Tests.Terminal;

/// <summary>
/// Copying a box out of a TDV session gives the box, not the bytes behind it.
/// </summary>
/// <remarks>
/// <para><b>Ronny's report, 2 September 2026</b></para>
/// Copying from a TDV session that had drawn a box pasted as the underlying letters:
/// <c>g``````i</c> along the top, <c>j</c> down the sides, <c>a``````c</c> along the bottom.
///
/// <para><b>Why it happened</b></para>
/// <c>TDVEmulatorBase.ApplyCharacterSetMapping</c> passes the byte through unchanged on purpose,
/// because TDV glyphs come from a bitmap font chosen by <c>FontNumber</c> rather than from a
/// Unicode font. Right for drawing, wrong for every path that reads the screen AS TEXT - copy and
/// scrollback search both go through <c>TerminalCell.GetString</c>, which returned the raw byte.
/// </remarks>
public class CopyingTdvLineDrawingTests
{
    /// <summary>
    /// Puts the emulator into the line-drawing set, writes the bytes, and returns the row as the
    /// clipboard would see it.
    /// </summary>
    /// <param name="bytes">
    /// The characters to send while the graphics set is active.
    /// </param>
    /// <returns>
    /// The text of row 0, up to the number of characters sent.
    /// </returns>
    private static string CopiedRow(string bytes)
    {
        var emulator = (TerminalEmulatorBase)EmulatorFactory.CreateEmulator("TDV2200", 80, 25);

        // LS2, ESC n - the LOCKING shift to G2, which is where the TDV keeps its line drawing.
        // Measured rather than assumed: SO does nothing on a TDV, SS2 (ESC N) shifts one character
        // only, and LS2 is the one that marks every following cell with font number 2 - which is
        // what a host drawing a whole box has to use.
        var data = new byte[bytes.Length + 2];
        data[0] = 0x1B;
        data[1] = (byte)'n';
        for (int i = 0; i < bytes.Length; i++)
        {
            data[i + 2] = (byte)bytes[i];
        }

        emulator.ProcessData(data);

        var sb = new StringBuilder();
        for (int col = 0; col < bytes.Length; col++)
        {
            sb.Append(emulator.Buffer.GetCell(0, col).GetString());
        }

        return sb.ToString();
    }

    [Fact]
    public void TheBoxRonnyCopiedComesBackAsABox()
    {
        // Exactly the characters from his paste: corners, edges and sides.
        Assert.Equal("┌──────┐", CopiedRow("g``````i"));
        Assert.Equal("└──────┘", CopiedRow("a``````c"));
        Assert.Equal("│", CopiedRow("j"));
    }

    [Fact]
    public void EveryOneOfTheElevenBoxGlyphsIsMapped()
    {
        // The whole contiguous run, in byte order, as classified off the real bitmaps.
        Assert.Equal("─└┴┘├┼┤┌┬┐│", CopiedRow("`abcdefghij"));
    }

    [Fact]
    public void OrdinaryTextIsUntouched()
    {
        // The mapping must apply ONLY to the line-drawing set. A letter typed normally is still
        // that letter, even though 'g' and 'j' are box glyphs in the other set.
        var emulator = (TerminalEmulatorBase)EmulatorFactory.CreateEmulator("TDV2200", 80, 25);
        emulator.ProcessData(Encoding.ASCII.GetBytes("gj`a"));

        var sb = new StringBuilder();
        for (int col = 0; col < 4; col++)
        {
            sb.Append(emulator.Buffer.GetCell(0, col).GetString());
        }

        Assert.Equal("gj`a", sb.ToString());
    }

    [Fact]
    public void ShapesThatAreNotBoxDrawingKeepTheirOwnByte()
    {
        // Charset 2 also holds blocks and arrows. There is no obviously right Unicode for those,
        // and a wrong one would look correct while being undetectably wrong - so they are left
        // alone deliberately rather than forced into a shape they do not have.
        Assert.False(TdvLineDrawingCharacters.TryGetDrawnCharacter(
            'A', TdvLineDrawingCharacters.LineDrawingFontNumber, out uint drawn));
        Assert.Equal((uint)'A', drawn);
    }
}
