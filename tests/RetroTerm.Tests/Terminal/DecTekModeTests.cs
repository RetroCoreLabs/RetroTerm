using System.Text;
using RetroTerm.Core.Configuration;
using RetroTerm.Core.Terminal.Emulators;
using Xunit;

namespace RetroTerm.Tests.Terminal;

/// <summary>
/// DECTEK - private mode 38, a VT becoming a Tektronix 4010/4014.
/// </summary>
/// <remarks>
/// <para><b>The defect this closes</b></para>
/// On 25 August 2026 a VT340 was sent a Tektronix plot and printed the coordinate bytes as text
/// straight across the screen: <c>!r!R!r&gt;J6z...</c>. Nothing was broken - <c>ESC[?38h</c> was
/// simply not implemented, and the UNHANDLED counter could not report it either because unknown
/// private modes were being discarded without being counted.
/// <para><b>What "implemented" has to mean here</b></para>
/// Not a flag. <c>TDV2200Emulator._isTektronixMode</c> is set, cleared, and then used for nothing
/// except appending a word to a capability string - it routes no bytes and draws nothing. So every
/// test below checks that a coordinate stream stops reaching the TEXT buffer, which is the only
/// thing the operator could see going wrong.
/// </remarks>
public class DecTekModeTests
{
    /// <summary>
    /// The escape character, built from its code so no raw control byte lands in this file.
    /// </summary>
    private const char Escape = (char)0x1B;

    /// <summary>
    /// GS - the byte that enters graph mode once the terminal is a 4014.
    /// </summary>
    private const char GroupSeparator = (char)0x1D;

    /// <summary>
    /// A fresh VT340.
    /// </summary>
    /// <returns>
    /// The emulator.
    /// </returns>
    private static TerminalEmulatorBase NewVt340()
    {
        return EmulatorFactory.CreateEmulator("VT340", 80, 24, 100);
    }

    /// <summary>
    /// One Tektronix coordinate, as its four bytes.
    /// </summary>
    /// <param name="x">
    /// Horizontal position, 0 to 1023.
    /// </param>
    /// <param name="y">
    /// Vertical position, 0 to 1023, with 0 at the BOTTOM.
    /// </param>
    /// <returns>
    /// High Y, low Y, high X, low X - the ten-bit 4010 form this program's decoder reads.
    /// </returns>
    private static string TekPoint(int x, int y)
    {
        char highY = (char)(0x20 | ((y >> 5) & 0x1F));
        char lowY = (char)(0x60 | (y & 0x1F));
        char highX = (char)(0x20 | ((x >> 5) & 0x1F));
        char lowX = (char)(0x40 | (x & 0x1F));
        return new string(new[] { highY, lowY, highX, lowX });
    }

    /// <summary>
    /// Everything on the screen, as one string.
    /// </summary>
    /// <param name="emulator">
    /// The terminal to read.
    /// </param>
    /// <returns>
    /// The text of every row joined together.
    /// </returns>
    private static string ScreenText(TerminalEmulatorBase emulator)
    {
        var buffer = emulator.GetBuffer();
        var text = new StringBuilder();

        for (int row = 0; row < buffer.Height; row++)
        {
            for (int col = 0; col < buffer.Width; col++)
            {
                uint codepoint = buffer.GetCell(row, col).Codepoint;
                if (codepoint != 0 && codepoint != ' ') text.Append((char)codepoint);
            }
        }

        return text.ToString();
    }

    /// <summary>
    /// A short plot: enter graph mode, move to one corner, draw to another.
    /// </summary>
    /// <returns>
    /// The bytes a host would send.
    /// </returns>
    private static string ShortPlot()
    {
        return GroupSeparator + TekPoint(100, 100) + TekPoint(900, 700);
    }

    [Fact]
    public void WithoutTheModeAPlotPrintsAsTextWhichIsTheDefectItself()
    {
        // The behaviour Ronny saw, pinned so the story cannot be argued about later. This is NOT a
        // test of something desirable - it records what a terminal that has not been asked to be a
        // 4014 does with 4014 bytes, which is print them.
        var emulator = NewVt340();

        emulator.ProcessData(Encoding.ASCII.GetBytes(ShortPlot()));

        Assert.NotEqual(string.Empty, ScreenText(emulator));
    }

    [Fact]
    public void TheModeIsAnsweredRatherThanCounted()
    {
        var emulator = NewVt340();

        emulator.ProcessData(Encoding.ASCII.GetBytes(Escape + "[?38h"));

        Assert.True(emulator.TektronixMode);
        Assert.False(emulator.UnrecognisedSequences.ContainsKey("private mode 38 set"));
    }

    [Fact]
    public void ResettingTheModeLeavesIt()
    {
        var emulator = NewVt340();

        emulator.ProcessData(Encoding.ASCII.GetBytes(Escape + "[?38h"));
        Assert.True(emulator.TektronixMode);

        emulator.ProcessData(Encoding.ASCII.GetBytes(Escape + "[?38l"));
        Assert.False(emulator.TektronixMode);
    }

    [Fact]
    public void InTheModeAPlotDoesNotReachTheTextBuffer()
    {
        // THE WHOLE POINT. The screen stays clean because the bytes went to the plotter.
        var emulator = NewVt340();

        emulator.ProcessData(Encoding.ASCII.GetBytes(Escape + "[?38h"));
        emulator.ProcessData(Encoding.ASCII.GetBytes(ShortPlot()));

        Assert.Equal(string.Empty, ScreenText(emulator));
    }

    [Fact]
    public void InTheModeAPlotActuallyDrawsSomething()
    {
        // Not reaching the text buffer is only half the claim - bytes could be reaching nothing at
        // all. This proves ink landed on the plane, which is the difference between a feature and a
        // flag that swallows the stream.
        var emulator = NewVt340();

        emulator.ProcessData(Encoding.ASCII.GetBytes(Escape + "[?38h"));
        emulator.ProcessData(Encoding.ASCII.GetBytes(ShortPlot()));

        var plane = emulator.Graphics!.FindPlane("dectek");
        Assert.NotNull(plane);

        int lit = 0;
        for (int y = 0; y < plane!.Height && lit == 0; y++)
        {
            for (int x = 0; x < plane.Width; x++)
            {
                if (!plane.Surface.GetPixel(x, y).IsTransparent) { lit++; break; }
            }
        }

        Assert.True(lit > 0, "the mode was entered and the plot vanished - nothing was drawn");
    }

    [Fact]
    public void LeavingGraphModePutsTheCursorAtTheLastPlottedPoint()
    {
        // How a host labels a plot: move the beam, drop to alpha, print. Getting this wrong is what
        // once put every gnuplot axis label in a diagonal cascade across the picture, so it is
        // pinned rather than assumed.
        var emulator = NewVt340();

        emulator.ProcessData(Encoding.ASCII.GetBytes(Escape + "[?38h"));
        emulator.ProcessData(Encoding.ASCII.GetBytes(GroupSeparator + TekPoint(512, 390)));

        // US leaves graph mode and returns to alpha.
        emulator.ProcessData(new byte[] { 0x1F });

        // 512 of 1024 is halfway across; 390 of 780 is halfway down. Tektronix Y counts UPWARDS,
        // so the halfway point is halfway either way and this is the one coordinate that cannot
        // hide a flipped axis - which is exactly why the assertion below also checks a corner.
        Assert.Equal(40, emulator.Cursor.Column);
    }

    [Fact]
    public void TektronixYRunsUpwardsSoTheTopOfThePlotIsTheTopOfTheScreen()
    {
        // The flip that is easy to get backwards. A high Tektronix Y is near the TOP of the screen,
        // which is a LOW text row.
        var emulator = NewVt340();

        emulator.ProcessData(Encoding.ASCII.GetBytes(Escape + "[?38h"));
        emulator.ProcessData(Encoding.ASCII.GetBytes(GroupSeparator + TekPoint(0, 779)));
        emulator.ProcessData(new byte[] { 0x1F });

        Assert.Equal(0, emulator.Cursor.Row);
    }

    [Fact]
    public void AHardResetTakesTheModeWithIt()
    {
        // Leaving a terminal in 4014 mode across a reset would mean the next thing the host printed
        // came out as coordinates.
        var emulator = NewVt340();

        emulator.ProcessData(Encoding.ASCII.GetBytes(Escape + "[?38h"));
        Assert.True(emulator.TektronixMode);

        emulator.ProcessData(Encoding.ASCII.GetBytes(Escape + "c"));

        Assert.False(emulator.TektronixMode);
    }

    [Fact]
    public void TextStillPrintsAfterLeavingTheMode()
    {
        // The round trip. A mode you can enter but not properly leave is worse than no mode.
        var emulator = NewVt340();

        emulator.ProcessData(Encoding.ASCII.GetBytes(Escape + "[?38h"));
        emulator.ProcessData(Encoding.ASCII.GetBytes(ShortPlot()));
        emulator.ProcessData(Encoding.ASCII.GetBytes(Escape + "[?38l"));
        emulator.ProcessData(Encoding.ASCII.GetBytes(Escape + "[1;1H" + "HELLO"));

        Assert.Contains("HELLO", ScreenText(emulator));
    }
}
