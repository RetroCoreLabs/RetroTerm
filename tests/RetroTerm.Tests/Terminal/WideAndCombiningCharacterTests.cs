using System.Collections.Generic;
using System.Text;
using RetroTerm.Core.Configuration;
using RetroTerm.Core.Terminal;
using RetroTerm.Core.Terminal.Emulators;
using Xunit;

namespace RetroTerm.Tests.Terminal;

/// <summary>
/// Wide characters take two cells; combining marks take none.
///
/// A terminal grid assumes one character per cell. That holds for Latin text and fails for two
/// cases, and getting either wrong shifts every character after it one column out of place:
///
/// - Wide (CJK, fullwidth forms, most emoji). Drawn across TWO columns. We wrote them
///   into one, so the character was clipped and the following text overlapped it.
/// - Combining marks. A combining acute modifies the character before it and occupies
///   no column. We gave it a cell of its own, so "e" then U+0301 written over "0123" swallowed the
///   "1" — the mark did not attach AND it destroyed a character.
///
/// A cell holds one codepoint, so a mark is composed into its base where Unicode has a
/// precomposed form. Where it does not — Devanagari, Thai, stacked accents — the mark is dropped.
/// That limit is real and is stated in <see cref="UnicodeComposition"/> rather than hidden; it is
/// still strictly better than eating the next character's cell.
///
/// Found by libvterm's 61screen_unicode script — see tests\RetroTerm.Tests\Conformance.
/// </summary>
public class WideAndCombiningCharacterTests
{
    /// <summary>
    /// The terminals that actually receive UTF-8 — and deliberately NOT the TDV models.
    ///
    /// A TDV profile is <c>TransportEncoding.EightBit</c>, and the comment on it says why: "A TDV
    /// line is 8-bit. One typed character must leave as one byte, or an ND host reads two garbage
    /// characters for every national character typed." Those terminals interpret a high byte as a
    /// national character through ISO 646, not as part of a UTF-8 sequence, so feeding them a
    /// three-byte fullwidth zero is not a scenario that occurs — asserting Unicode behaviour there
    /// would be testing a mode the hardware does not have.
    /// </summary>
    public static IEnumerable<object[]> UnicodeEmulators()
    {
        yield return new object[] { "VT100" };
        yield return new object[] { "VT220" };
        yield return new object[] { "ANSI" };
    }

    private static TerminalEmulatorBase Build(string type) => EmulatorFactory.CreateEmulator(type, 20, 5, 100);

    /// <summary>
    /// Feeds UTF-8, so the emulator's own decoder is part of what is under test.
    /// </summary>
    private static void Feed(TerminalEmulatorBase emulator, string text)
        => emulator.ProcessData(Encoding.UTF8.GetBytes(text));

    private static uint At(TerminalEmulatorBase emulator, int row, int col)
    {
        Assert.True(emulator.GetBuffer().TryGetCell(row, col, out var cell), $"no cell at {row},{col}");
        return cell.Codepoint;
    }

    // ─────────────────────────────────────────────────────────────
    // The width table
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void TheWidthTableClassifiesTheThreeCases()
    {
        Assert.Equal(1, CharacterWidth.Of('A'));
        Assert.Equal(1, CharacterWidth.Of('0'));
        Assert.Equal(0, CharacterWidth.Of(0x0301));    // combining acute
        Assert.Equal(0, CharacterWidth.Of(0x200B));    // zero-width space
        Assert.Equal(2, CharacterWidth.Of(0xFF10));    // fullwidth digit zero
        Assert.Equal(2, CharacterWidth.Of(0x4E00));    // CJK ideograph
        Assert.Equal(2, CharacterWidth.Of(0x3042));    // hiragana A
    }

    [Fact]
    public void TheAsciiFastPathAgreesWithTheTables()
    {
        // Everything below U+0300 returns 1 without a lookup. If a range table ever claimed one of
        // those, the shortcut would silently disagree with it.
        for (uint cp = 0x20; cp < 0x0300; cp++)
        {
            Assert.Equal(1, CharacterWidth.Of(cp));
        }
    }

    // ─────────────────────────────────────────────────────────────
    // Wide characters
    // ─────────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(UnicodeEmulators))]
    public void AWideCharacterConsumesTwoCells(string emulatorType)
    {
        // The exact case from the corpus: a fullwidth zero written over "0123" must destroy the
        // "1" as well, because it is drawn across both columns.
        var emulator = Build(emulatorType);
        Feed(emulator, "0123");
        Feed(emulator, "\u001b[H");
        Feed(emulator, "\uFF10");

        Assert.Equal(0xFF10u, At(emulator, 0, 0));
        Assert.Equal(0u, At(emulator, 0, 1));          // the trailer holds nothing
        Assert.Equal((uint)'2', At(emulator, 0, 2));
        Assert.Equal((uint)'3', At(emulator, 0, 3));
    }

    [Theory]
    [MemberData(nameof(UnicodeEmulators))]
    public void AWideCharacterMovesTheCursorTwoColumns(string emulatorType)
    {
        var emulator = Build(emulatorType);
        Feed(emulator, "\uFF10");

        Assert.Equal(2, emulator.GetCursor().Column);
    }

    [Theory]
    [MemberData(nameof(UnicodeEmulators))]
    public void TheTwoHalvesAreMarkedAsLeadAndTrail(string emulatorType)
    {
        var emulator = Build(emulatorType);
        Feed(emulator, "\uFF10");

        Assert.True(emulator.GetBuffer().TryGetCell(0, 0, out var lead));
        Assert.True(emulator.GetBuffer().TryGetCell(0, 1, out var trail));

        Assert.True(lead.IsWideLead);
        Assert.False(lead.IsWideTrail);
        Assert.True(trail.IsWideTrail);
        Assert.False(trail.IsWideLead);
    }

    [Theory]
    [MemberData(nameof(UnicodeEmulators))]
    public void OverwritingHalfOfAWideCharacterDestroysAllOfIt(string emulatorType)
    {
        // Half a CJK glyph is not a character. Leaving the other half behind would put every
        // following column one place out.
        var emulator = Build(emulatorType);
        Feed(emulator, "\uFF10");
        Feed(emulator, "\u001b[1;2HX");     // land on the trailing half

        Assert.Equal(0u, At(emulator, 0, 0));
        Assert.Equal((uint)'X', At(emulator, 0, 1));

        Assert.True(emulator.GetBuffer().TryGetCell(0, 0, out var wasLead));
        Assert.False(wasLead.IsWideLead);
    }

    [Theory]
    [MemberData(nameof(UnicodeEmulators))]
    public void AWideCharacterWithOneColumnLeftWrapsRatherThanSplitting(string emulatorType)
    {
        // It cannot be drawn half on one line and half on the next, so it moves whole.
        var emulator = Build(emulatorType);
        Feed(emulator, "\u001b[1;20H");     // last column of a 20-column screen
        Feed(emulator, "\uFF10");

        Assert.Equal(0u, At(emulator, 0, 19));
        Assert.Equal(0xFF10u, At(emulator, 1, 0));
        Assert.Equal(1, emulator.GetCursor().Row);
    }

    // ─────────────────────────────────────────────────────────────
    // Combining marks
    // ─────────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(UnicodeEmulators))]
    public void ACombiningMarkAttachesWithoutConsumingACell(string emulatorType)
    {
        // The exact case from the corpus. Before this the mark took the "1"'s cell and the accent
        // was lost anyway — the worst of both.
        var emulator = Build(emulatorType);
        Feed(emulator, "0123");
        Feed(emulator, "\u001b[H");
        Feed(emulator, "e\u0301");

        Assert.Equal(0x00E9u, At(emulator, 0, 0));     // é, composed
        Assert.Equal((uint)'1', At(emulator, 0, 1));   // survived
        Assert.Equal((uint)'2', At(emulator, 0, 2));
        Assert.Equal((uint)'3', At(emulator, 0, 3));
    }

    [Theory]
    [MemberData(nameof(UnicodeEmulators))]
    public void ACombiningMarkDoesNotMoveTheCursor(string emulatorType)
    {
        var emulator = Build(emulatorType);
        Feed(emulator, "e");
        int before = emulator.GetCursor().Column;

        Feed(emulator, "\u0301");

        Assert.Equal(before, emulator.GetCursor().Column);
    }

    [Theory]
    [MemberData(nameof(UnicodeEmulators))]
    public void ManyCombiningMarksDoNotCrashOrConsumeCells(string emulatorType)
    {
        // The corpus has a "10 combining accents should not crash" case. Only the first can
        // compose; the rest have no precomposed form and are dropped. None may take a cell.
        var emulator = Build(emulatorType);
        Feed(emulator, "e");
        for (int i = 0; i < 10; i++)
        {
            Feed(emulator, "\u0301");
        }

        Feed(emulator, "X");

        Assert.Equal(0x00E9u, At(emulator, 0, 0));
        Assert.Equal((uint)'X', At(emulator, 0, 1));
    }

    [Theory]
    [MemberData(nameof(UnicodeEmulators))]
    public void ACombiningMarkAtTheStartOfALineIsDiscarded(string emulatorType)
    {
        // Nothing to attach to. It must not throw and must not write anything.
        var emulator = Build(emulatorType);
        Feed(emulator, "\u0301");

        Assert.Equal(0u, At(emulator, 0, 0));
        Assert.Equal(0, emulator.GetCursor().Column);
    }

    [Fact]
    public void CompositionLeavesTheBaseAloneWhenThereIsNoPrecomposedForm()
    {
        // Stated as a test rather than only as a comment, so the limit is visible.
        // é exists as U+00E9, so it composes into one cell.
        Assert.Equal(0x00E9u, UnicodeComposition.Compose('e', 0x0301));

        // "q" with an acute has no precomposed form, so the base is returned unchanged and the
        // mark is dropped. Checked against Normalize rather than assumed — the first base tried
        // here was "z", and ź turns out to exist as U+017A.
        Assert.Equal((uint)'q', UnicodeComposition.Compose('q', 0x0301));
    }
}
