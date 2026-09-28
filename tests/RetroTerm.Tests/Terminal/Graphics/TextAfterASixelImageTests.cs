using System;
using System.IO;
using System.Text;
using RetroTerm.Core.Configuration;
using RetroTerm.Core.Terminal.Emulators;
using Xunit;

namespace RetroTerm.Tests.Terminal.Graphics;

/// <summary>
/// Text that arrives after a Sixel image reaches the right cell, even when the image covers it.
/// </summary>
/// <remarks>
/// <para><b>Where this came from</b></para>
/// Ronny judged the M6.1a sheets on 1 September 2026 and reported that our half of
/// <c>extremeratio</c> was missing the line of text the hardware capture shows along the top.
///
/// <para><b>What was measured, and it splits in two</b></para>
/// The EMULATOR is right, which is what this file pins. <c>extremeratio.six</c> ends with a plain
/// text line after its Sixel terminator, the image leaves the cursor at row 0 column 0 exactly as
/// the fixture's own note says a VT340 does, and the text lands in row 0 of the buffer.
/// <para>
/// The RENDERER is where it goes missing, and that part is NOT fixed here.
/// <c>TerminalRenderer.Render</c> blits the text cache and then calls
/// <c>DrawGraphicsPlanes</c>, so the graphics plane is painted over the text unconditionally -
/// whatever arrived last. A real VT340 has one bitmap for both, so the later write wins per pixel
/// and the text shows on top of the image. Making ours behave that way needs a decision about how
/// much of the shared-bitmap model to adopt, which is a standing Phase 4 question in
/// <c>docs\PLAN.md</c> rather than something to change underneath it.
/// </para>
///
/// <para><b>One thing the sheet cannot settle either way</b></para>
/// The hardware capture's line reads "This text should be at the top line of the screen." while
/// the fixture sends "* &lt;- A VT340 leaves the text cursor at the top of the screen." They are
/// different strings, so the two halves of that sheet were never going to read identically even
/// with the renderer fixed. Recorded so it is not reported as a defect a second time.
/// </remarks>
public class TextAfterASixelImageTests
{
    private static string CorpusFolder => Path.GetFullPath(Path.Combine(
        Path.GetDirectoryName(typeof(TextAfterASixelImageTests).Assembly.Location) ?? "",
        "..", "..", "..", "Conformance", "vt340test"));

    private static string FixturePath => Path.Combine(CorpusFolder, "extremeratio.six");

    /// <summary>
    /// Reads one whole row of the buffer as text.
    /// </summary>
    /// <param name="emulator">
    /// The emulator to read from.
    /// </param>
    /// <param name="row">
    /// The row to read.
    /// </param>
    /// <returns>
    /// The row's characters with trailing blanks removed.
    /// </returns>
    private static string RowText(TerminalEmulatorBase emulator, int row)
    {
        var sb = new StringBuilder();
        for (int col = 0; col < emulator.Buffer.Width; col++)
        {
            char ch = (char)emulator.Buffer.GetCell(row, col).Codepoint;
            sb.Append(ch == '\0' ? ' ' : ch);
        }

        return sb.ToString().TrimEnd();
    }

    [Fact]
    public void TheImageLeavesTheCursorAtTheTopAndTheTrailingTextLandsOnRowZero()
    {
        if (!File.Exists(FixturePath))
        {
            // The corpus is fetched on demand and never committed.
            return;
        }

        var emulator = (TerminalEmulatorBase)EmulatorFactory.CreateEmulator("VT340", 80, 24, 100);
        byte[] all = File.ReadAllBytes(FixturePath);

        // Split at the LAST string terminator, so the cursor can be read after the image and
        // before the trailing text is played.
        int terminator = -1;
        for (int i = 0; i < all.Length - 1; i++)
        {
            if (all[i] == 0x1B && all[i + 1] == (byte)'\\')
            {
                terminator = i + 1;
            }
        }

        Assert.True(terminator > 0, "the fixture must end with a string terminator");

        var upToImage = new byte[terminator + 1];
        Array.Copy(all, upToImage, upToImage.Length);
        emulator.ProcessData(upToImage);

        // The fixture's own note: "A VT340 leaves the text cursor at the top of the screen."
        Assert.Equal(0, emulator.Cursor.Row);
        Assert.Equal(0, emulator.Cursor.Column);

        var trailing = new byte[all.Length - terminator - 1];
        Array.Copy(all, terminator + 1, trailing, 0, trailing.Length);
        Assert.NotEmpty(trailing);

        emulator.ProcessData(trailing);

        // The text is in the buffer, on the top row, whatever the renderer then paints over it.
        Assert.Contains("VT340 leaves the text cursor at the top", RowText(emulator, 0));
    }

    /// <summary>
    /// Every cell the trailing line printed into is marked as sitting on top of the picture, and
    /// the cells the image alone covers are not.
    /// </summary>
    /// <remarks>
    /// This is the flag the renderer reads to punch the plane transparent, so it is the piece
    /// that decides whether the text is actually SEEN. Asserted on the cells rather than on
    /// pixels because it is the emulator's half of the contract; the rendered half is judged by
    /// eye on the M6.1a sheet, where the hardware capture shows the same line over the same
    /// checkerboard.
    /// </remarks>
    [Fact]
    public void TheTrailingTextClaimsItsCellsAndTheImageKeepsTheRest()
    {
        if (!File.Exists(FixturePath))
        {
            return;
        }

        var emulator = (TerminalEmulatorBase)EmulatorFactory.CreateEmulator("VT340", 80, 24, 100);
        emulator.ProcessData(File.ReadAllBytes(FixturePath));

        string top = RowText(emulator, 0);
        Assert.NotEqual(0, top.Length);

        // Every printed cell on the top row is on top of the image, spaces between words included -
        // on the hardware a space paints background and rubs the picture out under itself, which
        // is why the capture's black bar runs unbroken through the line.
        for (int col = 0; col < top.Length; col++)
        {
            Assert.True(emulator.Buffer.GetCell(0, col).TextIsNewerThanGraphics,
                "column " + col + " of the printed line must sit on top of the image");
        }

        // A row the image covers and nothing was printed into stays under the picture. Row 2 is
        // well inside a 480-pixel-tall image on a 24-row screen.
        for (int col = 0; col < 80; col++)
        {
            Assert.False(emulator.Buffer.GetCell(2, col).TextIsNewerThanGraphics,
                "column " + col + " of an untouched row must stay under the image");
        }
    }
}
