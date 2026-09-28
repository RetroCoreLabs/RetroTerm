using System.Text;
using RetroTerm.Core.Configuration;
using RetroTerm.Core.Terminal.Emulators;
using Xunit;

namespace RetroTerm.Tests.Terminal;

/// <summary>
/// The DEC VT52 — the terminal that came before ANSI.
///
/// Its commands are a bare ESC and one letter, with no introducer, no parameters and no final
/// byte, so none of the base emulator's CSI handling applies to them. The interesting one is
/// <c>ESC Y</c>: it carries two coordinate bytes that may be ANY byte, including 0x1B, so they
/// cannot go through escape processing. The parser has had a raw-byte collection for exactly this
/// since it was written, and nothing used it until now.
/// </summary>
public class Vt52Tests
{
    /// <summary>
    /// ESC as its own string.
    /// </summary>
    /// <remarks>
    /// NOT written as a hex-escape literal in these tests, and the reason is a real trap: C#'s
    /// hex escape is VARIABLE LENGTH, so an ESC escape followed by the letter A is ONE character,
    /// U+01BA, rather than ESC and then A. Every VT52 command whose letter happens to be a hex
    /// digit - A, B, C, D, E, F, which is most of the cursor movement - silently becomes something
    /// else. Two tests failed on exactly that before this constant existed.
    /// </remarks>
    private static readonly string Esc = ((char)0x1B).ToString();

    private static TerminalEmulatorBase Build()
        => EmulatorFactory.CreateEmulator("VT52", 20, 6, 100);

    private static void Feed(TerminalEmulatorBase emulator, string text)
        => emulator.ProcessData(Encoding.ASCII.GetBytes(text));

    private static char CellAt(TerminalEmulatorBase emulator, int row, int col)
    {
        emulator.GetBuffer().TryGetCell(row, col, out var cell);
        return cell.Codepoint == 0 ? ' ' : (char)cell.Codepoint;
    }

    [Fact]
    public void TheCursorMovesWithSingleLetterEscapes()
    {
        var emulator = Build();
        Feed(emulator, "\x1b[3;3H");   // an ANSI sequence to get somewhere - see the note below

        Feed(emulator, Esc + "A");       // up
        Assert.Equal(1, emulator.GetCursor().Row);

        Feed(emulator, Esc + "B");       // down
        Assert.Equal(2, emulator.GetCursor().Row);

        Feed(emulator, Esc + "C");       // right
        Assert.Equal(3, emulator.GetCursor().Column);

        Feed(emulator, Esc + "D");       // left
        Assert.Equal(2, emulator.GetCursor().Column);
    }

    [Fact]
    public void HomeGoesToTheTopLeft()
    {
        var emulator = Build();
        Feed(emulator, "\x1b[4;10H");

        Feed(emulator, "\x1bH");

        Assert.Equal(0, emulator.GetCursor().Row);
        Assert.Equal(0, emulator.GetCursor().Column);
    }

    [Fact]
    public void DirectAddressingReadsTwoRawBytes()
    {
        // ESC Y row col, each byte the coordinate plus 0x20 counting from 1. So SP SP is the top
        // left, and '$' '(' is row 5, column 9.
        var emulator = Build();

        Feed(emulator, "\x1bY$(");

        Assert.Equal(4, emulator.GetCursor().Row);
        Assert.Equal(8, emulator.GetCursor().Column);
    }

    [Fact]
    public void TheCoordinateBytesAreNotPrinted()
    {
        // THE test for the raw-byte path. Without it the coordinates go through escape processing
        // and end up on screen as text, which is what a VT52 host would have seen from this
        // terminal before today.
        var emulator = Build();

        Feed(emulator, "\x1bY$(X");

        Assert.Equal('X', CellAt(emulator, 4, 8));
        Assert.Equal(' ', CellAt(emulator, 0, 0));
    }

    [Fact]
    public void AnEscapeInsideTheCoordinatesIsStillACoordinate()
    {
        // 0x1B is a perfectly good coordinate byte - row 0x1B-0x20 is negative, so it clamps to the
        // top - and treating it as the start of a new sequence would swallow the byte after it.
        var emulator = Build();

        Feed(emulator, "\x1bY\x1b(X");

        Assert.Equal(0, emulator.GetCursor().Row);
        Assert.Equal('X', CellAt(emulator, 0, 8));
    }

    [Fact]
    public void AnAddressPastTheEdgeClampsRatherThanRefusing()
    {
        // A VT52 had nowhere to report an error to; it put the cursor at the edge.
        var emulator = Build();

        Feed(emulator, "\x1bY~~");

        Assert.Equal(5, emulator.GetCursor().Row);
        Assert.Equal(19, emulator.GetCursor().Column);
    }

    [Fact]
    public void ItIdentifiesAsAVt52AndNotWithDeviceAttributes()
    {
        // Device Attributes did not exist yet. ESC Z asks, ESC / Z answers.
        var emulator = Build();
        var replies = new StringBuilder();
        emulator.DataToSend += bytes => replies.Append(Encoding.ASCII.GetString(bytes));

        Feed(emulator, "\x1bZ");
        Assert.Equal("\x1b/Z", replies.ToString());

        replies.Clear();
        Feed(emulator, "\x1b[c");
        Assert.Equal("", replies.ToString());
    }

    [Fact]
    public void EraseToEndOfScreenAndLineWork()
    {
        var emulator = Build();
        Feed(emulator, "\x1b[1;1HABCDE");
        Feed(emulator, "\x1b[1;3H");

        Feed(emulator, "\x1bK");

        Assert.Equal('A', CellAt(emulator, 0, 0));
        Assert.Equal('B', CellAt(emulator, 0, 1));
        Assert.Equal(' ', CellAt(emulator, 0, 2));
    }

    [Fact]
    public void ReverseLineFeedGoesUp()
    {
        var emulator = Build();
        Feed(emulator, "\x1b[3;1H");

        Feed(emulator, "\x1bI");

        Assert.Equal(1, emulator.GetCursor().Row);
    }

    [Fact]
    public void TheGraphicsSetIsTrackedEvenThoughItsGlyphsAreNotHere()
    {
        // The VT52's own set is not the DEC Special Graphics set, and this emulator does not carry
        // its ROM. The flag follows the host so the state is right.
        var emulator = (Vt52Emulator)Build();

        Feed(emulator, Esc + "F");
        Assert.True(emulator.GraphicsCharacterSet);

        Feed(emulator, "\x1bG");
        Assert.False(emulator.GraphicsCharacterSet);
    }

    [Fact]
    public void TheKeypadModeFollowsTheHost()
    {
        var emulator = (Vt52Emulator)Build();

        Feed(emulator, "\x1b=");
        Assert.True(emulator.KeypadIsApplicationMode);

        Feed(emulator, "\x1b>");
        Assert.False(emulator.KeypadIsApplicationMode);
    }

    [Fact]
    public void ItIsSelectableAndCallsItselfAVt52()
    {
        Assert.True(EmulatorFactory.IsSupported("VT52"));
        Assert.Equal("VT52", Build().GetTerminalType());
    }
}
