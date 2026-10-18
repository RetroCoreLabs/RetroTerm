using System.Text;
using RetroTerm.Core.Configuration;
using RetroTerm.Core.Terminal.Emulators;
using Xunit;

namespace RetroTerm.Tests.Terminal;

/// <summary>
/// XTREVWRAP (DECSET 45) - a backspace that walks off the left edge, and CSI k which does nothing.
/// </summary>
/// <remarks>
/// xterm's ctlseqs names the mode - "Ps = 4 5 -&gt; Reverse-wraparound mode (XTREVWRAP), xterm" -
/// and then says nothing at all about where the cursor lands. The rule tested here comes from
/// xterm's own behaviour, captured in the reverse-wrap fixture of the screen corpus: the last
/// column of the line above, and from the top line round to the BOTTOM line.
///
/// CSI Pn k is in the same file because the fixture that found it sits beside the other. ECMA-48
/// calls it VPB and moves the cursor up; no terminal here has it, so it is ignored.
/// </remarks>
public class ReverseWrapTests
{
    private static readonly string Esc = ((char)0x1B).ToString();

    private static TerminalEmulatorBase Build()
        => EmulatorFactory.CreateEmulator("XTERM", 10, 5, 100);

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

    // ── the mode itself ─────────────────────────────────────────────────────────────────

    [Fact]
    public void WithoutTheModeABackspaceAtTheLeftEdgeStaysPut()
    {
        // The default, and the only thing a DEC terminal does.
        var emulator = Build();
        Feed(emulator, Esc + "[2;1H" + "\b\b\b");

        Assert.Equal(1, emulator.GetCursor().Row);
        Assert.Equal(0, emulator.GetCursor().Column);
    }

    [Fact]
    public void WithTheModeSetItGoesToTheEndOfTheLineAbove()
    {
        var emulator = Build();
        Feed(emulator, Esc + "[?45h" + Esc + "[2;1H" + "\b");

        Assert.Equal(0, emulator.GetCursor().Row);
        Assert.Equal(9, emulator.GetCursor().Column);
    }

    [Fact]
    public void AndFromTheTopLineItGoesRoundToTheBottom()
    {
        // THE part no document states. Seven backspaces from home is what the fixture does, and
        // the text it prints next runs off the bottom corner - so getting this wrong is not a
        // stray cursor, it is a screen that fails to scroll.
        var emulator = Build();
        Feed(emulator, Esc + "[?45h" + Esc + "[H" + "\b");

        Assert.Equal(4, emulator.GetCursor().Row);
        Assert.Equal(9, emulator.GetCursor().Column);
    }

    [Fact]
    public void SeveralBackspacesWalkBackThroughLines()
    {
        var emulator = Build();
        Feed(emulator, Esc + "[?45h" + Esc + "[3;2H" + "\b\b\b");

        // (2,1) -> (2,0) -> (1,9) -> (1,8)
        Assert.Equal(1, emulator.GetCursor().Row);
        Assert.Equal(8, emulator.GetCursor().Column);
    }

    [Fact]
    public void PrintingAfterAWrapRoundToTheBottomScrollsTheScreen()
    {
        // What the fixture actually checks: the wrap puts the cursor in the bottom-right corner,
        // so a two-character write there wraps and takes a line off the top.
        var emulator = Build();
        Feed(emulator, Esc + "[1;1HTOP");
        Feed(emulator, Esc + "[?45h" + Esc + "[H" + "\b" + "XY");

        Assert.Equal(".........X", Row(emulator, 3));   // X went to the bottom-right corner...
        Assert.Equal("Y.........", Row(emulator, 4));   // ...and Y wrapped, which scrolled X up a row
    }

    [Fact]
    public void ResettingTheModePutsTheOldBehaviourBack()
    {
        var emulator = Build();
        Feed(emulator, Esc + "[?45h" + Esc + "[?45l" + Esc + "[2;1H" + "\b");

        Assert.Equal(1, emulator.GetCursor().Row);
        Assert.Equal(0, emulator.GetCursor().Column);
    }

    [Fact]
    public void AnOrdinaryBackspaceIsUnaffectedByTheMode()
    {
        var emulator = Build();
        Feed(emulator, Esc + "[?45h" + Esc + "[2;5H" + "\b");

        Assert.Equal(1, emulator.GetCursor().Row);
        Assert.Equal(3, emulator.GetCursor().Column);
    }

    // ── CSI k ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public void VerticalPositionBackwardIsIgnored()
    {
        // THE defect: this used to move the cursor up a line, which spread one line of text over
        // four rows. xterm has no CSI Ps k, so a host sending it means nothing by it.
        var emulator = Build();
        Feed(emulator, "d" + Esc + "[ke" + Esc + "[kf" + Esc + "[kg");

        Assert.Equal("defg......", Row(emulator, 0));
        Assert.Equal(0, emulator.GetCursor().Row);
    }

    [Fact]
    public void AndSoIsEveryParameterFormOfIt()
    {
        var emulator = Build();
        Feed(emulator, Esc + "[3;1H" + Esc + "[0k0" + Esc + "[1k1" + Esc + "[2k2" + Esc + "[3;5k3");

        Assert.Equal("0123......", Row(emulator, 2));
    }

    [Fact]
    public void ButVerticalPositionRelativeStillWorks()
    {
        // The asymmetry is real rather than an oversight: ctlseqs has "CSI Ps e  Line Position
        // Relative ... (VPR)" and no entry for k.
        var emulator = Build();
        Feed(emulator, Esc + "[1;1H" + Esc + "[2e" + "X");

        Assert.Equal("X.........", Row(emulator, 2));
    }
}
