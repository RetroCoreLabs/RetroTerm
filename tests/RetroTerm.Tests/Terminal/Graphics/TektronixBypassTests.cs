using System.Text;
using RetroTerm.Core.Terminal.Emulators.Tektronix;
using Xunit;

namespace RetroTerm.Tests.Terminal.Graphics;

/// <summary>
/// The bypass condition - <c>ESC CAN</c> - and the two sequences that clear it.
/// </summary>
/// <remarks>
/// <para><b>Where this comes from</b></para>
/// The "4010/4014 Mode" chapter of
/// <c>spec\DEC\EK-VT3XX-GP-002_VT330_VT340_Graphics_Programming_May88.pdf</c>: "This sequence
/// selects the bypass condition. In the bypass condition, the VT300 ignores any data received from
/// the host." Cleared by selecting alpha mode and by <c>ESC ETB</c>.
///
/// <para><b>The ESC is the whole difference</b></para>
/// A BARE CAN leaves graph mode, which the vector decoder handles and which is something else
/// entirely. Reading one as the other would strand a terminal ignoring its host, and the only thing
/// telling them apart is whether an ESC came first - the same distinction that separates a form
/// feed from erasing the tube.
/// </remarks>
public class TektronixBypassTests
{
    private static Tek4014Emulator Build() => new Tek4014Emulator(74, 35);

    private static void Feed(Tek4014Emulator emulator, string data)
        => emulator.ProcessData(Encoding.ASCII.GetBytes(data));

    private static string FirstRow(Tek4014Emulator emulator)
    {
        var buffer = emulator.GetBuffer();
        var text = new StringBuilder();

        for (int column = 0; column < buffer.Width; column++)
        {
            buffer.TryGetCell(0, column, out var cell);
            text.Append(cell.Codepoint == 0 ? ' ' : (char)cell.Codepoint);
        }

        return text.ToString().TrimEnd();
    }

    [Fact]
    public void ATerminalStartsListening()
    {
        var emulator = Build();

        Assert.False(emulator.IsBypassed);
    }

    [Fact]
    public void EscapeCancelStopsTheHostBeingHeard()
    {
        var emulator = Build();

        Feed(emulator, "\x1b\x18");

        Assert.True(emulator.IsBypassed);
    }

    [Fact]
    public void AndTextSentWhileBypassedIsNotDrawn()
    {
        var emulator = Build();
        Feed(emulator, "BEFORE");

        Feed(emulator, "\x1b\x18");
        Feed(emulator, "AFTER");

        Assert.Equal("BEFORE", FirstRow(emulator));
    }

    [Fact]
    public void ABareCancelIsNotBypassAtAll()
    {
        // It leaves graph mode. Treating it as bypass would silence the terminal every time a
        // drawing ended, which is every gnuplot stream in the corpus.
        var emulator = Build();

        Feed(emulator, "\x18");

        Assert.False(emulator.IsBypassed);
    }

    [Fact]
    public void SelectingAlphaModeStartsListeningAgain()
    {
        // "Selecting alpha mode ... clears the bypass condition."
        var emulator = Build();
        Feed(emulator, "\x1b\x18");

        Feed(emulator, "\x1b\x0c");

        Assert.False(emulator.IsBypassed);

        Feed(emulator, "HEARD");
        Assert.Equal("HEARD", FirstRow(emulator));
    }

    [Fact]
    public void AndSoDoesAskingForAHardCopy()
    {
        // "The sequence also clears the bypass condition." The hard copy itself needs a printer
        // port, which does not exist yet - but the side effect is free to honour, and a host that
        // used ETB to get talking to us again must not be left ignored.
        var emulator = Build();
        Feed(emulator, "\x1b\x18");

        Feed(emulator, "\x1b\x17");

        Assert.False(emulator.IsBypassed);
    }

    [Fact]
    public void EscapesStillRunWhileBypassed()
    {
        // The reading this rests on: bypass drops DISPLAYABLE data, not parsing. It has to, or
        // nothing could ever clear it - both clearing sequences are themselves data from the host.
        var emulator = Build();
        Feed(emulator, "VISIBLE");
        Feed(emulator, "\x1b\x18");

        // An erase arriving while bypassed must still erase.
        Feed(emulator, "\x1b\x0c");

        Assert.Equal("", FirstRow(emulator));
    }
}
