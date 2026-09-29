using System;
using System.Text;
using RetroTerm.Core.Terminal.Emulators;
using RetroTerm.Core.Terminal.Emulators.TDV;
using Xunit;

namespace RetroTerm.Tests.TDV;

/// <summary>
/// Phase 4: characterising SS2/SS3 and the locking shifts across the TDV models.
///
/// The migration plan lists "merge the two SS2/SS3 implementations". There ARE two —
/// TDVCharacterSetManager's SS2/SS3 flags, used by TDV2200, and TDV2215's own
/// _pendingSingleShift byte — but reading them side by side shows they encode genuinely
/// DIFFERENT per-model behaviour rather than the same behaviour written twice:
///
///  - TDV2200 picks its font from the DESIGNATED set type (GraphicsI → 2, GraphicsII → 3);
///  - TDV2215 picks it from a LOCKED SET NUMBER, and additionally suppresses control-code
///    handling for codepoints below 0x20, because its charsets 3 and 4 have real glyphs there.
///
/// TDV2215Emulator's own class comment says so: "Character Sets (TDV2215/TDV2115 - different
/// from TDV2200!)". Merging them would flatten a documented model difference for no defect.
///
/// These tests pin what each model actually does, so the difference is visible and deliberate
/// rather than something the next reader has to re-derive.
/// </summary>
public class SingleShiftAndLockingShiftTests
{
    private static void Feed(TerminalEmulatorBase emulator, string s)
    {
        emulator.ProcessData(Encoding.ASCII.GetBytes(s));
    }

    private static byte FontAt(TerminalEmulatorBase emulator, int row, int col)
    {
        return emulator.Buffer.GetCell(row, col).FontNumber;
    }

    // ESC N / ESC O built from chars: "\x1bN" would be fine ('N' is not hex) but the explicit
    // form is the house style after the escape-length traps earlier in this work.
    private static readonly string EscN = new string(new[] { (char)0x1B, 'N' });
    private static readonly string EscO = new string(new[] { (char)0x1B, 'O' });

    // ─────────────────────────────────────────────────────────────
    // Single shift affects exactly one character, on both models
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void Tdv2200SingleShiftAppliesToOneCharacterOnly()
    {
        var emulator = new TDV2200Emulator(80, 24);

        Feed(emulator, EscN);      // SS2
        Feed(emulator, "AB");

        Assert.Equal(2, FontAt(emulator, 0, 0));   // 'A' shifted
        Assert.Equal(0, FontAt(emulator, 0, 1));   // 'B' back to normal
    }

    [Fact]
    public void Tdv2215SingleShiftAppliesToOneCharacterOnly()
    {
        var emulator = new TDV2215Emulator(80, 24);

        Feed(emulator, EscN);
        Feed(emulator, "AB");

        Assert.Equal(2, FontAt(emulator, 0, 0));
        Assert.Equal(0, FontAt(emulator, 0, 1));
    }

    [Fact]
    public void SingleShift3SelectsTheThirdSetOnBothModels()
    {
        var tdv2200 = new TDV2200Emulator(80, 24);
        var tdv2215 = new TDV2215Emulator(80, 24);

        Feed(tdv2200, EscO);
        Feed(tdv2200, "X");
        Feed(tdv2215, EscO);
        Feed(tdv2215, "X");

        Assert.Equal(3, FontAt(tdv2200, 0, 0));
        Assert.Equal(3, FontAt(tdv2215, 0, 0));
    }

    [Fact]
    public void APendingSingleShiftIsClearedByReset()
    {
        var emulator = new TDV2215Emulator(80, 24);

        Feed(emulator, EscN);
        emulator.ResetToInitialState();
        Feed(emulator, "A");

        Assert.Equal(0, FontAt(emulator, 0, 0));
    }

    // ─────────────────────────────────────────────────────────────
    // Locking shift persists, on both models
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void Tdv2215LockingShiftPersistsAcrossCharacters()
    {
        var emulator = new TDV2215Emulator(80, 24);

        Feed(emulator, new string(new[] { (char)0x1B, 'n' }));   // LS2
        Feed(emulator, "AB");

        Assert.Equal(2, FontAt(emulator, 0, 0));
        Assert.Equal(2, FontAt(emulator, 0, 1));   // still locked
    }

    [Fact]
    public void ASingleShiftOverridesTheLockForOneCharacter()
    {
        var emulator = new TDV2215Emulator(80, 24);

        Feed(emulator, new string(new[] { (char)0x1B, 'n' }));   // LS2 — lock to set 2
        Feed(emulator, EscO);                                    // SS3 for the next char only
        Feed(emulator, "AB");

        Assert.Equal(3, FontAt(emulator, 0, 0));   // single shift won
        Assert.Equal(2, FontAt(emulator, 0, 1));   // back to the lock
    }

    // ─────────────────────────────────────────────────────────────
    // Where the models genuinely differ: a RAW SI/SO byte
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void ARawShiftOutByteChangesNoFontOnEitherModel_ButForDifferentReasons()
    {
        // CHARACTERISATION ONLY. Read the note below before treating this as evidence of anything.
        //
        // Both models currently leave the font at 0 after a raw SO byte, so this test CANNOT
        // distinguish them — an earlier draft split it in two and asserted the same value twice,
        // which looked like proof of an asymmetry and proved nothing.
        //
        // The asymmetry is real but lives in the mechanism, and is visible only in the source:
        //   - TDV2200Emulator OVERRIDES HandleExecute to intercept SI/SO (0x0F/0x0E) and route
        //     them through InvokeCharacterSet, with a comment saying this is needed to keep
        //     FontNumber correct. G1 on a 2200 is US ASCII, so the font legitimately stays 0.
        //   - TDV2215Emulator does NOT intercept them. They reach
        //     TerminalEmulatorBase.HandleExecute, which sets the VT-style ActiveCharacterSet — a
        //     different variable from the _lockedCharacterSet that 2215's HandleCharacter reads.
        //     So on a 2215 a raw SO could not change the font even if G1 were a graphics set.
        //
        // Demonstrating the difference needs G1 designated as a graphics set, which needs the TDV
        // designation sequence, which I have not verified against the manuals. (The retired
        // reference document's ESC 1-9 table was partly invented; the manual is
        // spec\TDV1200\ND-12054-1-EN_combined.md section 5.54, which has ESC 1-6, 9, : and ;
        // only, and docs\TDV-MODES-AND-REPORTS-FROM-THE-MANUALS.md section 7 weighs the
        // manuals against each other.) Guessing a sequence to make a test look
        // conclusive would be worse than saying plainly that it is not.
        var tdv2200 = new TDV2200Emulator(80, 24);
        var tdv2215 = new TDV2215Emulator(80, 24);

        Feed(tdv2200, "\x0E");   // SO
        Feed(tdv2200, "A");
        Feed(tdv2215, "\x0E");
        Feed(tdv2215, "A");

        Assert.Equal(0, FontAt(tdv2200, 0, 0));
        Assert.Equal(0, FontAt(tdv2215, 0, 0));
    }

    [Fact]
    public void TheEscapeFormOfLockingShiftWorksOnTdv2215()
    {
        // The counterpart to the above: the ESC form does reach 2215's own state, so the
        // asymmetry is specifically about the raw C0 byte, not about locking shifts generally.
        var emulator = new TDV2215Emulator(80, 24);

        Feed(emulator, new string(new[] { (char)0x1B, 'n' }));
        Feed(emulator, "A");

        Assert.Equal(2, FontAt(emulator, 0, 0));
    }

    // ─────────────────────────────────────────────────────────────
    // The two mechanisms do not fight each other
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void TheSharedComponentIsNotDrivenBehindTheModelsBack()
    {
        // TDVInputProcessor deliberately does NOT intercept SS2/SS3 — it used to, and double-mapped
        // the character, producing a cell with the right FontNumber and no drawable glyph. Both
        // models now inherit that component from the base, so it is worth pinning that a single
        // shift still takes effect exactly once and through one path.
        var emulator = new TDV2215Emulator(80, 24);

        emulator.ProcessInput(Encoding.ASCII.GetBytes(EscN + "AB"));

        Assert.Equal(2, FontAt(emulator, 0, 0));
        Assert.Equal(0, FontAt(emulator, 0, 1));
    }
}
