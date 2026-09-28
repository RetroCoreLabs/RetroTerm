using System.Text;
using RetroTerm.Core.Configuration;
using RetroTerm.Core.Terminal.Emulators;
using Xunit;

namespace RetroTerm.Tests.Terminal;

/// <summary>
/// A CSI carrying an intermediate byte is not the same control as one without it.
/// </summary>
/// <remarks>
/// <para><b>What the standard says</b></para>
/// ECMA-48 names a control function by its intermediates AND its final byte together. The final
/// byte alone does not identify it, so <c>CSI Ps SP I</c> is not CHT and must not act like it.
///
/// <para><b>What we did before 28 August 2026</b></para>
/// Every intermediate this terminal knows was claimed by an explicit branch, and anything else fell
/// through to a switch that dispatched on the final byte alone. So an unknown intermediate was
/// silently discarded and the sequence was obeyed as though it had never carried one.
///
/// <para><b>How it was found, and what it cost</b></para>
/// By measuring the M6.1a comparison sheets. hackerb9's <c>textcursor.six</c> opens with
/// <c>ESC [ 2 SP I</c>. Run as CHT that moves the cursor two tab stops - column 16 - and a VT340
/// cell is 10 pixels wide, so the whole picture landed exactly 160 pixels right of where the
/// hardware puts it, with its last column clipped off the screen. <c>vaxrgl-lntest.six</c> sends
/// <c>ESC [ 7 SP I</c>, which would be 560 pixels.
///
/// Measured across all 114 files of the conformance corpora, only four CSI-with-intermediate shapes
/// exist at all: DECIC and DECDC, which were already handled, <c>CSI SP I</c> twice, and one
/// <c>CSI ? 20 SP J</c>.
///
/// <para><b>Red before green</b></para>
/// Remove the intermediate guard in <c>TerminalEmulatorBase</c> and
/// <c>AnUnknownIntermediateIsNotObeyedAsThoughItWereAbsent</c> fails with the cursor on column 16.
/// Confirmed by doing it.
/// </remarks>
public class CsiIntermediateIdentityTests
{
    /// <summary>
    /// The escape character, built from its code so no raw control byte lands in this file.
    /// </summary>
    private const char Escape = (char)0x1B;

    /// <summary>
    /// A VT340 on its own geometry.
    /// </summary>
    /// <returns>
    /// The emulator.
    /// </returns>
    private static TerminalEmulatorBase NewVt340()
        => EmulatorFactory.CreateEmulator("VT340", 80, 24, 100);

    [Fact]
    public void AnUnknownIntermediateIsNotObeyedAsThoughItWereAbsent()
    {
        // The exact sequence textcursor.six opens with.
        var emulator = NewVt340();
        emulator.ProcessData(Encoding.ASCII.GetBytes(Escape + "[H"));

        emulator.ProcessData(Encoding.ASCII.GetBytes(Escape + "[2 I"));

        Assert.Equal(0, emulator.Cursor.Column);
    }

    [Fact]
    public void TheSameFinalByteWithNoIntermediateIsStillCht()
    {
        // The other half of the rule. Removing the guard must not be the only way to keep CHT
        // working - a real CHT has no intermediate and must still tabulate.
        var emulator = NewVt340();
        emulator.ProcessData(Encoding.ASCII.GetBytes(Escape + "[H"));

        emulator.ProcessData(Encoding.ASCII.GetBytes(Escape + "[2I"));

        Assert.Equal(16, emulator.Cursor.Column);
    }

    [Fact]
    public void AnIgnoredIntermediateSequenceIsCountedRatherThanDropped()
    {
        // Counted, so it appears in UNHANDLED instead of vanishing. A dropped sequence is invisible
        // to anyone trying to find out what a real host actually sends.
        var emulator = NewVt340();
        int before = emulator.UnrecognisedSequences.Count;

        emulator.ProcessData(Encoding.ASCII.GetBytes(Escape + "[2 I"));

        Assert.True(emulator.UnrecognisedSequences.Count > before,
            "the ignored sequence was not counted");
    }

    [Fact]
    public void TheIntermediatesWeDoClaimStillWork()
    {
        // DECSCUSR is CSI Ps SP q - a SPACE intermediate that IS ours. The guard must sit below
        // every branch that claims one, not above them.
        var emulator = NewVt340();

        emulator.ProcessData(Encoding.ASCII.GetBytes(Escape + "[4 q"));

        Assert.Equal(RetroTerm.Core.Terminal.CursorStyle.Underline, emulator.Cursor.Style);
    }
}
