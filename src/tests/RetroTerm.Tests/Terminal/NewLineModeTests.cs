using System.Text;
using RetroTerm.Core.Configuration;
using RetroTerm.Core.Terminal.Emulators;
using Xunit;

namespace RetroTerm.Tests.Terminal;

/// <summary>
/// LNM - Line Feed/New Line Mode, and which direction it actually points.
/// </summary>
/// <remarks>
/// The VT420 Programmer Reference, chapter 11: "If LNM is set, the cursor moves to the first column
/// on the next line when the terminal receives an LF, FF, or VT character. When you press Return,
/// the terminal sends both a carriage return (CR) and line feed (LF)."
///
/// So the mode adds a CARRIAGE RETURN TO A RECEIVED LINE FEED, and adds a LINE FEED TO WHAT THE
/// RETURN KEY SENDS. It says nothing about a received carriage return, and ours made one feed a
/// line - so every CR LF pair, which is what a tty sends for every newline, advanced two rows.
/// </remarks>
public class NewLineModeTests
{
    private static readonly string Esc = ((char)0x1B).ToString();

    private static TerminalEmulatorBase Build()
        => EmulatorFactory.CreateEmulator("XTERM", 10, 6, 100);

    private static void Feed(TerminalEmulatorBase emulator, string text)
        => emulator.ProcessData(Encoding.ASCII.GetBytes(text));

    private static string Row(TerminalEmulatorBase emulator, int row)
    {
        var text = new StringBuilder(emulator.Width);
        for (int col = 0; col < emulator.Width; col++)
        {
            emulator.GetBuffer().TryGetCell(row, col, out var cell);
            text.Append(cell.Codepoint == 0 ? '.' : (char)cell.Codepoint);
        }
        return text.ToString().TrimEnd('.');
    }

    [Fact]
    public void ACarriageReturnNeverFeedsALineHoweverTheModeIsSet()
    {
        // THE defect. A carriage return returns the carriage: same row, first column.
        var emulator = Build();
        Feed(emulator, Esc + "[20h");        // LNM set
        Feed(emulator, "abc\rX");

        Assert.Equal("Xbc", Row(emulator, 0));
        Assert.Equal(0, emulator.GetCursor().Row);
    }

    [Fact]
    public void SoACarriageReturnAndLineFeedTogetherAdvanceOneRowNotTwo()
    {
        // What a tty sends for every newline. Two rows per line is the shape the whole corpus
        // fixture failed in.
        var emulator = Build();
        Feed(emulator, Esc + "[20h");
        Feed(emulator, "a\r\nb\r\nc");

        Assert.Equal("a", Row(emulator, 0));
        Assert.Equal("b", Row(emulator, 1));
        Assert.Equal("c", Row(emulator, 2));
    }

    [Fact]
    public void WithTheModeSetALineFeedAlsoReturnsTheCarriage()
    {
        // The half of the mode that is real: "the cursor moves to the first column on the next
        // line when the terminal receives an LF, FF, or VT character".
        var emulator = Build();
        Feed(emulator, Esc + "[20h");
        Feed(emulator, "abc\nX");

        Assert.Equal("abc", Row(emulator, 0));
        Assert.Equal("X", Row(emulator, 1));
    }

    [Fact]
    public void AndSoDoFormFeedAndVerticalTab()
    {
        // The manual names all three: LF, FF and VT.
        var emulator = Build();
        Feed(emulator, Esc + "[20h");
        Feed(emulator, "abc" + ((char)0x0B) + "X" + ((char)0x0C) + "Y");

        Assert.Equal("abc", Row(emulator, 0));
        Assert.Equal("X", Row(emulator, 1));
        Assert.Equal("Y", Row(emulator, 2));
    }

    [Fact]
    public void WithTheModeResetALineFeedKeepsTheColumn()
    {
        // "If LNM is reset, the cursor moves to the current column on the next line." Reset is the
        // default, and the manual recommends leaving it that way.
        var emulator = Build();
        Feed(emulator, Esc + "[20l");
        Feed(emulator, "abc\nX");

        Assert.Equal("abc", Row(emulator, 0));
        Assert.Equal("...X", Row(emulator, 1).Replace(' ', '.'));
    }

    [Fact]
    public void ResetIsTheDefault()
    {
        var emulator = Build();
        Feed(emulator, "abc\nX");

        Assert.Equal("...X", Row(emulator, 1).Replace(' ', '.'));
    }
}
