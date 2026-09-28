using System.Text;
using RetroTerm.Core.Configuration;
using RetroTerm.Core.Terminal;
using RetroTerm.Core.Terminal.Emulators;
using Xunit;

namespace RetroTerm.Tests.Terminal;

/// <summary>
/// DECSCUSR - <c>CSI Ps SP q</c>, the shape of the cursor, and the ECMA-48 spelling of SD.
/// </summary>
/// <remarks>
/// <para><b>Both were found by walking xterm's ctlseqs list</b></para>
/// DECSCUSR was missing outright. Every modern editor sends it - vim and neovim switch to a bar in
/// insert mode and back to a block in normal mode - so the cursor simply never changed shape. The
/// style enum and the renderer that reads it were both already there; only the sequence was not.
/// The TDV2200's own dispatch even carries a guard against swallowing this exact sequence, which
/// is how it stayed hidden for so long.
///
/// <c>CSI Ps ^</c> is SD written the way ECMA-48's 5th edition printed it by mistake. The 2003
/// correction moved SD to <c>CSI Ps T</c>, which DEC had used all along, and xterm answers to both
/// because software written against the erratum is still out there.
/// </remarks>
public class CursorStyleTests
{
    private static readonly string Esc = ((char)0x1B).ToString();

    private static TerminalEmulatorBase Build(string type = "XTERM")
        => EmulatorFactory.CreateEmulator(type, 10, 5, 100);

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

    [Theory]
    [InlineData(0, CursorStyle.BlinkingBlock)]      // "Ps = 0 -> blinking block."
    [InlineData(1, CursorStyle.BlinkingBlock)]      // "Ps = 1 -> blinking block (default)."
    [InlineData(2, CursorStyle.Block)]              // "Ps = 2 -> steady block."
    [InlineData(3, CursorStyle.BlinkingUnderline)]  // "Ps = 3 -> blinking underline."
    [InlineData(4, CursorStyle.Underline)]          // "Ps = 4 -> steady underline."
    [InlineData(5, CursorStyle.BlinkingBar)]        // "Ps = 5 -> blinking bar, xterm."
    [InlineData(6, CursorStyle.Bar)]                // "Ps = 6 -> steady bar, xterm."
    public void EachNumberSelectsTheShapeCtlseqsGivesIt(int parameter, CursorStyle expected)
    {
        var emulator = Build();

        Feed(emulator, Esc + "[" + parameter + " q");

        Assert.Equal(expected, emulator.GetCursor().Style);
    }

    [Fact]
    public void NoParameterMeansTheDefault()
    {
        // "Ps = 1 -> blinking block (default)", so a bare CSI SP q asks for that.
        var emulator = Build();
        Feed(emulator, Esc + "[2 q");                  // something else first

        Feed(emulator, Esc + "[ q");

        Assert.Equal(CursorStyle.BlinkingBlock, emulator.GetCursor().Style);
    }

    [Fact]
    public void AShapeThisTerminalDoesNotHaveIsIgnored()
    {
        // Rounding to a neighbour would be inventing an answer to a question the host asked
        // precisely.
        var emulator = Build();
        Feed(emulator, Esc + "[4 q");

        Feed(emulator, Esc + "[9 q");

        Assert.Equal(CursorStyle.Underline, emulator.GetCursor().Style);
    }

    [Fact]
    public void ItIsNotMistakenForDecsca()
    {
        // DECSCA is CSI Ps " q and DECSCUSR is CSI Ps SP q - one intermediate apart, and both end
        // in 'q'. Marking text protected must not change the cursor's shape.
        var emulator = Build("VT420");
        Feed(emulator, Esc + "[2 q");

        Feed(emulator, Esc + "[1\"q");

        Assert.Equal(CursorStyle.Block, emulator.GetCursor().Style);
    }

    // ── The ECMA-48 spelling of SD ──────────────────────────────────────────────────────────

    [Fact]
    public void TheCaretFormScrollsTheRegionDown()
    {
        var emulator = Build();
        Feed(emulator, Esc + "[1;1HA" + Esc + "[2;1HB" + Esc + "[3;1HC");

        Feed(emulator, Esc + "[2^");

        Assert.Equal("", Row(emulator, 0));
        Assert.Equal("", Row(emulator, 1));
        Assert.Equal("A", Row(emulator, 2));
        Assert.Equal("B", Row(emulator, 3));
    }

    [Fact]
    public void AndItNeedsNoParameterCountCheck()
    {
        // CSI Ps T has to count its parameters, because five of them mean xterm's highlight mouse
        // tracking instead. The caret form has no such twin, so a bare CSI ^ is one line down.
        var emulator = Build();
        Feed(emulator, Esc + "[1;1HA");

        Feed(emulator, Esc + "[^");

        Assert.Equal("", Row(emulator, 0));
        Assert.Equal("A", Row(emulator, 1));
    }
}
