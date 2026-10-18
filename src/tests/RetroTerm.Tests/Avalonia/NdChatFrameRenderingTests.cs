using System.Text;
using Avalonia.Headless.XUnit;
using RetroTerm.Core.Terminal.Emulators.TDV;
using Xunit;

namespace RetroTerm.Tests.Avalonia;

/// <summary>
/// The box frame an ND screen-handling program draws, rendered through the real pipeline.
/// </summary>
/// <remarks>
/// <para><b>Where this came from</b></para>
/// A live D100 session on 28 August 2026, running the machine's own NDCHAT 2.1. The program
/// frames its whole screen: a top rule, a bottom rule, and a vertical bar down column 0 and
/// column 79 of every row in between. Every one of those characters arrives as SS2 followed by
/// one byte from G2 - the trace shows ESC N, then the byte, over and over.
///
/// On screen the two horizontal rules drew and the two vertical bars did not. PED's small
/// interior box, in the same session and the same emulator, drew all four sides. The difference
/// between the two is WHERE the vertical sits: PED's are at interior columns, NDCHAT's are at
/// the first and last column of the screen.
///
/// <para><b>What this test does</b></para>
/// It rebuilds that frame from the bytes and asks the renderer, cell by cell, whether there is
/// ink. A text dump cannot answer the question: the TDV renderer picks a glyph from the RAW
/// character plus the cell's font number, so a cell holding "j" with font 2 reads as the letter j
/// in a screen dump whatever it draws. Only the pixels know.
/// </remarks>
[Collection("Avalonia")]
public class NdChatFrameRenderingTests
{
    /// <summary>
    /// G2 line-drawing bytes, as NDCHAT sends them.
    /// </summary>
    private const byte TopLeft = 0x67;      // 'g'
    private const byte TopRight = 0x69;     // 'i'
    private const byte BottomLeft = 0x61;   // 'a'
    private const byte BottomRight = 0x63;  // 'c'
    private const byte Horizontal = 0x60;   // '`'
    private const byte Vertical = 0x6A;     // 'j'

    /// <summary>
    /// Sends one G2 character the way the host does - a single shift before every byte.
    /// </summary>
    /// <param name="emulator">
    /// The emulator to feed.
    /// </param>
    /// <param name="g2Byte">
    /// The character to take from G2.
    /// </param>
    private static void SendG2(TDV2200Emulator emulator, byte g2Byte)
        => emulator.ProcessData(new byte[] { 0x1B, 0x4E, g2Byte });

    /// <summary>
    /// Draws NDCHAT's full-screen frame: rules top and bottom, bars down both edges.
    /// </summary>
    /// <param name="emulator">
    /// The emulator to draw on.
    /// </param>
    /// <param name="columns">
    /// Screen width in cells.
    /// </param>
    /// <param name="rows">
    /// Screen height in cells.
    /// </param>
    private static void DrawFullScreenFrame(TDV2200Emulator emulator, int columns, int rows)
    {
        emulator.ProcessData(Encoding.ASCII.GetBytes("\x1b[1;1H"));
        SendG2(emulator, TopLeft);
        for (int c = 1; c < columns - 1; c++)
        {
            SendG2(emulator, Horizontal);
        }
        SendG2(emulator, TopRight);

        for (int r = 2; r < rows; r++)
        {
            emulator.ProcessData(Encoding.ASCII.GetBytes("\x1b[" + r + ";1H"));
            SendG2(emulator, Vertical);
            emulator.ProcessData(Encoding.ASCII.GetBytes("\x1b[" + r + ";" + columns + "H"));
            SendG2(emulator, Vertical);
        }

        emulator.ProcessData(Encoding.ASCII.GetBytes("\x1b[" + rows + ";1H"));
        SendG2(emulator, BottomLeft);
        for (int c = 1; c < columns - 1; c++)
        {
            SendG2(emulator, Horizontal);
        }
        SendG2(emulator, BottomRight);
    }

    [AvaloniaFact]
    public void EveryCellOfTheFrameCarriesFontTwo()
    {
        var emulator = new TDV2200Emulator(80, 25);
        DrawFullScreenFrame(emulator, 80, 25);

        // The buffer half of the question, checked first so a pixel failure below cannot be
        // blamed on the parser.
        Assert.Equal(2, emulator.Buffer.GetCell(0, 0).FontNumber);
        Assert.Equal(2, emulator.Buffer.GetCell(0, 79).FontNumber);
        Assert.Equal(2, emulator.Buffer.GetCell(5, 0).FontNumber);
        Assert.Equal(2, emulator.Buffer.GetCell(5, 79).FontNumber);
    }

    [AvaloniaFact]
    public void TheVerticalBarsDrawAtBothScreenEdges()
    {
        var emulator = new TDV2200Emulator(80, 25);
        DrawFullScreenFrame(emulator, 80, 25);

        using var shot = RenderedScreenshot.Capture(emulator, "ndchat_full_screen_frame");

        // The horizontals are the control: they were seen to draw on the real screen, so if these
        // fail the reproduction itself is wrong and nothing below means anything.
        Assert.True(shot.CellHasInk(0, 10), "the top rule should have ink at column 10");
        Assert.True(shot.CellHasInk(24, 10), "the bottom rule should have ink at column 10");

        // The verticals, which did NOT appear on the real screen.
        Assert.True(shot.CellHasInk(5, 0), "the left bar should have ink at row 5, column 0");
        Assert.True(shot.CellHasInk(5, 79), "the right bar should have ink at row 5, column 79");
    }
}
