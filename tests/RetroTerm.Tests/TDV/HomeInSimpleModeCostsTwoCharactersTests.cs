using System.Text;
using RetroTerm.Core.Configuration;
using RetroTerm.Core.Terminal.Emulators;
using RetroTerm.Core.Terminal.Emulators.TDV;
using Xunit;

namespace RetroTerm.Tests.TDV;

/// <summary>
/// What an echoed HOME costs on a TDV2200 - measured, not argued.
/// </summary>
/// <remarks>
/// <para><b>Renamed and rewritten 31 August 2026 - the "two candidates" story it was built on is
/// gone</b></para>
/// This file used to measure and compare TWO competing bytes for HOME - GS <c>0x1D</c> in
/// extended mode and DLE <c>0x10</c> in simple mode - because the registry disagreed with itself
/// (SimpleAscii <c>0x10</c> against an <c>AlwaysSameCode</c> flag that said it should not). Its
/// point was that neither byte was safe to echo, so "pick whichever is harmless" could not settle
/// which one was right.
///
/// That premise no longer holds. Reading <c>spec\Keyboards\keyboard-spec.md</c> section 6.8.3
/// directly, which cites the TDV-2200/9 User's Guide (ND-30.003.04 EN) and documents its own
/// OCR-correction history, settles it from the documentation: the <c>0x10</c> reading was an OCR
/// error in an early pass of section 7.2, superseded by a fresh OCR of section 9.1 which marks
/// HOME "is always" GS in both modes. There is one byte now, not two, and the registry's
/// SimpleAscii is corrected to match.
///
/// <para><b>What is still open</b></para>
/// Whether a real physical TDV2200 keyboard agrees with the User's Guide is still unmeasured -
/// that is case M4.2d of the manual document, and still needs the hardware. What this file keeps
/// is the one real, measured fact that survives the correction: GS is not a safe byte to echo on
/// a TDV2200, because the graphics side takes it unconditionally as the Tektronix
/// enter-graph-mode code before cursor-home ever gets a look at it.
/// </remarks>
public class HomeInSimpleModeCostsTwoCharactersTests
{
    /// <summary>
    /// Builds a TDV2200 at the geometry a real one has.
    /// </summary>
    /// <returns>
    /// The emulator.
    /// </returns>
    private static TerminalEmulatorBase Tdv()
        => EmulatorFactory.CreateEmulator("TDV2200", 80, 25, 100);

    /// <summary>
    /// What the HOME key sends, read from the registry rather than typed here.
    /// </summary>
    /// <param name="extendedMode">
    /// True for extended mode, false for simple ASCII.
    /// </param>
    /// <returns>
    /// The sequence.
    /// </returns>
    /// <remarks>
    /// Taken from <see cref="TDV2200KeyRegistry"/> on purpose, the same as before the rewrite. If
    /// a real keyboard settles M4.2d against the registry's current answer, this file follows it
    /// instead of asserting a stale byte.
    /// </remarks>
    private static string WhatHomeSends(bool extendedMode)
    {
        string? grid = TDV2200KeyRegistry.GetGridForName("HOME");
        Assert.NotNull(grid);

        string? sequence = TDV2200KeyRegistry.GetSequence(grid!, extendedMode,
            numPadFuncMode: false);
        Assert.NotNull(sequence);

        return sequence!;
    }

    /// <summary>
    /// How many of the three probe characters reached the screen.
    /// </summary>
    /// <param name="emulator">
    /// The terminal to look at.
    /// </param>
    /// <returns>
    /// The count, from zero to three.
    /// </returns>
    /// <remarks>
    /// The whole grid is walked rather than three named cells, because the code under test MOVES
    /// the cursor - looking only at row 0 would report a character that landed elsewhere as lost.
    /// </remarks>
    private static int PrintedCount(TerminalEmulatorBase emulator)
    {
        var buffer = emulator.Buffer;
        int printed = 0;

        for (int row = 0; row < emulator.Height; row++)
        {
            for (int col = 0; col < emulator.Width; col++)
            {
                uint codepoint = buffer.GetCell(row, col).Codepoint;
                if (codepoint == 'A' || codepoint == 'B' || codepoint == 'C')
                {
                    printed++;
                }
            }
        }

        return printed;
    }

    /// <summary>
    /// Feeds HOME's byte back to the terminal the way an echoing host would, then three ordinary
    /// characters.
    /// </summary>
    /// <param name="extendedMode">
    /// Which mode to read HOME's byte for - both now give the same answer, kept as a parameter so
    /// the two modes are still measured independently rather than assumed identical.
    /// </param>
    /// <returns>
    /// How many of the three characters reached the screen.
    /// </returns>
    private static int CharactersSurvivingAnEchoOf(bool extendedMode)
    {
        var emulator = Tdv();
        emulator.ProcessData(Encoding.ASCII.GetBytes(WhatHomeSends(extendedMode)));
        emulator.ProcessData(Encoding.ASCII.GetBytes("ABC"));

        return PrintedCount(emulator);
    }

    [Fact]
    public void AnEchoedHomeSwallowsEverythingAfterIt()
    {
        // GS is the Tektronix enter-graph-mode code on a TDV2200, and the graphics side takes it
        // unconditionally in Ground state - so all three characters become vector coordinates and
        // none of them is printed. Checked in both modes, because HOME sends the same byte in
        // both and both should measure the same.
        Assert.Equal(0, CharactersSurvivingAnEchoOf(extendedMode: false));
        Assert.Equal(0, CharactersSurvivingAnEchoOf(extendedMode: true));
    }

    [Fact]
    public void TheTwoModesSendTheSameByteForHome()
    {
        // The fact that replaces what this file used to measure. HOME is not an exception to
        // AlwaysSameCode - it never should have been. When a real TDV2200 settles M4.2d against
        // real hardware, this is the assertion to check first if it ever disagrees.
        Assert.Equal(WhatHomeSends(extendedMode: true), WhatHomeSends(extendedMode: false));
    }
}
