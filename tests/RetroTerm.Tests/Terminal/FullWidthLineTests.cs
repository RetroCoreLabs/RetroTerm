using System.Text;
using RetroTerm.Core.Configuration;
using RetroTerm.Core.Terminal.Emulators;
using Xunit;

namespace RetroTerm.Tests.Terminal;

/// <summary>
/// What a line feed does, and what it does NOT do.
///
/// A line feed moves the cursor DOWN AND NOTHING ELSE. It does not return the carriage. Text that
/// looks like it starts at the left margin after a newline does so because something else put the
/// carriage back - the tty driver's ONLCR on a real system, or the terminal's own newline mode.
///
/// This file exists because getting that wrong cost a whole investigation. xterm.js's fixtures are
/// captured through a tty, so their expected screens have every line at column 0; feeding the raw
/// bytes to this emulator produced lines that stepped rightwards, and the emulator looked at fault
/// when it was behaving exactly as a terminal should.
/// </summary>
public class FullWidthLineTests
{
    private const int Columns = 10;

    private static TerminalEmulatorBase Build(string type = "XTERM", int rows = 4)
        => EmulatorFactory.CreateEmulator(type, Columns, rows, 100);

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
        return text.ToString();
    }

    [Fact]
    public void ABareLineFeedDoesNotReturnTheCarriage()
    {
        // THE misreading this file was written for. After ten characters on a ten-column screen
        // the cursor is at the last column; a line feed moves it down and leaves it there.
        var emulator = Build();

        Feed(emulator, "ABCDEFGHIJ\nx");

        Assert.Equal("ABCDEFGHIJ", Row(emulator, 0));
        Assert.Equal(".........x", Row(emulator, 1));
    }

    [Fact]
    public void CarriageReturnAndLineFeedTogetherStartTheNextLineAtTheLeft()
    {
        // What a host actually sends, and what the tty produces from a bare newline.
        var emulator = Build();

        Feed(emulator, "ABCDEFGHIJ\r\nabcdefghij\r\n");

        Assert.Equal("ABCDEFGHIJ", Row(emulator, 0));
        Assert.Equal("abcdefghij", Row(emulator, 1));
    }

    [Fact]
    public void NewlineModeMakesTheTerminalDoItItself()
    {
        // LNM is the terminal's own version of ONLCR: with it set, a line feed carries a carriage
        // return with it.
        var emulator = Build();

        Feed(emulator, ((char)0x1B) + "[20h");
        Feed(emulator, "ABCDEFGHIJ\nabcdefghij\n");

        Assert.Equal("ABCDEFGHIJ", Row(emulator, 0));
        Assert.Equal("abcdefghij", Row(emulator, 1));
    }

    [Fact]
    public void AFullWidthLineFillsItsRowWithoutSpillingOntoTheNext()
    {
        // Ten characters on a ten-column screen fill the row exactly. The wrap is ARMED but not
        // taken, so nothing has reached the next row yet.
        var emulator = Build();

        Feed(emulator, "ABCDEFGHIJ");

        Assert.Equal("ABCDEFGHIJ", Row(emulator, 0));
        Assert.Equal("..........", Row(emulator, 1));
    }

    [Fact]
    public void TheNextCharacterAfterThatTakesTheWrap()
    {
        var emulator = Build();

        Feed(emulator, "ABCDEFGHIJ" + "K");

        Assert.Equal("ABCDEFGHIJ", Row(emulator, 0));
        Assert.Equal("K.........", Row(emulator, 1));
    }

    [Fact]
    public void FillingTheLastRowDoesNotScrollUntilSomethingElseArrives()
    {
        // The armed wrap must not scroll on its own: nothing has gone past the bottom yet.
        var emulator = Build(rows: 2);

        Feed(emulator, "ABCDEFGHIJ\r\nabcdefghij");

        Assert.Equal("ABCDEFGHIJ", Row(emulator, 0));
        Assert.Equal("abcdefghij", Row(emulator, 1));
    }

    [Fact]
    public void OneMoreCharacterAfterThatScrollsExactlyOnce()
    {
        var emulator = Build(rows: 2);

        Feed(emulator, "ABCDEFGHIJ\r\nabcdefghij");
        Feed(emulator, "X");

        Assert.Equal("abcdefghij", Row(emulator, 0));
        Assert.Equal("X.........", Row(emulator, 1));
    }

    [Fact]
    public void AsManyFullRowsAsThereAreRowsFillTheScreenExactly()
    {
        // The shape of the corpus fixtures: one full-width line per row, the last without a
        // trailing newline. Nothing should scroll.
        var emulator = EmulatorFactory.CreateEmulator("XTERM", Columns, 25, 100);

        var stream = new StringBuilder();
        for (int i = 0; i < 25; i++)
        {
            stream.Append("0123456789");
            if (i < 24) stream.Append("\r\n");
        }
        Feed(emulator, stream.ToString());

        for (int row = 0; row < 25; row++)
        {
            Assert.Equal("0123456789", Row(emulator, row));
        }
    }
}
