using System;
using System.IO;
using RetroTerm.Core.Terminal.Emulators.TDV;
using Xunit;

namespace RetroTerm.Tests.Terminal;

/// <summary>
/// docs\TDV-KEYBOARD-COMPLETE-REFERENCE.md has to agree with the key registry.
/// </summary>
/// <remarks>
/// <para><b>Why this exists, 27 August 2026</b></para>
/// The nd-120 Verilog session came to build an FPGA terminal from that document and found its
/// navigation table written in VT220 shape: arrows as ESC[A/B/C/D with C0 codes only in 2115 mode,
/// function keys as ESC[nn~, PUSH keys as ESC[?n~, and Page Up, Page Down, Insert, Delete and End
/// keys that a TDV does not have. HOME had its two modes the wrong way round. Every one of those
/// was wrong, and the registry had been right the whole time.
///
/// It only came to light because that session refused to believe either side and went to the source.
/// That is not something to rely on twice.
///
/// <para><b>What it checks, and what it deliberately does not</b></para>
/// The keys flagged AlwaysSameCode - the arrows and HOME - because they are the ones the document
/// got wrong in the most damaging way, and because their whole point is that they carry the SAME
/// byte in every mode. A document that says otherwise leads somebody to build an escape parser for
/// a terminal that never sends one.
///
/// It does not try to verify the whole document. A test that asserted every row would have to
/// restate the registry, and then the test becomes the second copy that drifts.
/// </remarks>
public class TdvKeyboardDocumentMatchesTheRegistryTests
{
    /// <summary>
    /// The document under test, found from the assembly rather than the working directory.
    /// </summary>
    private static string DocumentPath => Path.GetFullPath(Path.Combine(
        Path.GetDirectoryName(typeof(TdvKeyboardDocumentMatchesTheRegistryTests).Assembly.Location) ?? "",
        "..", "..", "..", "..", "..", "docs", "TDV-KEYBOARD-COMPLETE-REFERENCE.md"));

    /// <summary>
    /// The keys whose code is the same in every mode, with the byte the registry gives them.
    /// </summary>
    /// <remarks>
    /// Written out rather than derived so that a change to the REGISTRY also fails here. If this
    /// read the registry for both sides it would agree with itself no matter what either said.
    /// </remarks>
    private static readonly (string Grid, string Name, byte Code)[] FixedKeys =
    {
        ("C48", "UP", 0x1C),
        ("A48", "DOWN", 0x0B),
        ("B47", "LEFT", 0x08),
        ("B49", "RIGHT", 0x18),
        ("B48", "HOME", 0x1D),
    };

    [Fact]
    public void TheRegistryStillGivesTheArrowsTheSameC0CodeInEveryMode()
    {
        // The claim the document contradicted. If this ever fails, the document may be right and
        // this test is what should be read first.
        //
        // SimpleAscii is checked here too, not just ExtNormal/ExtShift - added 31 August 2026
        // after this exact gap let HOME's SimpleAscii sit at 0x10 (DLE), disagreeing with its own
        // AlwaysSameCode flag and with this file's own stated rule, for a long time undetected.
        // See HomeAgreesWithItsOwnAlwaysSameCodeFlag below for that story.
        for (int i = 0; i < FixedKeys.Length; i++)
        {
            var (grid, name, code) = FixedKeys[i];

            Assert.True(TDV2200KeyRegistry.TryGetKey(grid, out var key), grid + " (" + name + ") is gone");
            Assert.NotNull(key);

            Assert.True((key!.Flags & TDVKeyFlags.AlwaysSameCode) != 0,
                name + " is no longer AlwaysSameCode");

            string expected = ((char)code).ToString();
            Assert.Equal(expected, key.ExtNormal);
            Assert.Equal(expected, key.ExtShift);
            Assert.Equal(expected, key.SimpleAscii);
        }
    }

    [Fact]
    public void HomeAgreesWithItsOwnAlwaysSameCodeFlag()
    {
        // Renamed from HomeIsTheOneKeyWhoseCodeChangesBetweenTheTwoModes on 31 August 2026. That
        // test pinned the registry's SimpleAscii for HOME at 0x10 (DLE) as a deliberate, documented
        // exception to the rule above - but it was never a deliberate exception, it was the same
        // shape of error this whole file exists to catch: a claim that disagreed with the
        // AlwaysSameCode flag sitting right beside it. Corrected by reading
        // spec\Keyboards\keyboard-spec.md section 6.8.3 directly, which cites the TDV-2200/9
        // User's Guide (ND-30.003.04 EN) and explains the 0x10 reading was an OCR error in an
        // early pass of section 7.2, superseded by a fresh OCR of section 9.1 marking HOME "is
        // always" GS in both modes. HOME belongs in FixedKeys above, not beside it as an exception.
        Assert.True(TDV2200KeyRegistry.TryGetKey("B48", out var home));
        Assert.NotNull(home);

        Assert.Equal("\x1D", home!.ExtNormal);
        Assert.Equal("\x1D", home.SimpleAscii);
    }

    [Fact]
    public void TheDocumentDoesNotClaimTheArrowsSendAnsiCursorSequences()
    {
        // The specific wrong sentences, by their text. A TDV never sends ESC[A or ESC O A, so the
        // document must not contain them as a claim about what a TDV arrow key produces.
        Assert.True(File.Exists(DocumentPath), "could not find " + DocumentPath);

        string text = File.ReadAllText(DocumentPath);

        string[] mustNotAppear =
        {
            "Arrow keys send ESC [ A/B/C/D sequences",
            "| A48 | Down Arrow | 40 (VK_DOWN) | `ESC [ B` |",
            "| Arrow keys (extended) | `ESC [ <char>` | Up = `ESC[A` |",
        };

        for (int i = 0; i < mustNotAppear.Length; i++)
        {
            Assert.True(text.IndexOf(mustNotAppear[i], StringComparison.Ordinal) < 0,
                "docs\\TDV-KEYBOARD-COMPLETE-REFERENCE.md has gone back to claiming a TDV sends ANSI "
                + "cursor sequences: \"" + mustNotAppear[i] + "\". The registry says these keys carry a "
                + "bare C0 byte in every mode.");
        }
    }

    [Fact]
    public void TheDocumentNamesTheActualC0CodesForTheArrows()
    {
        Assert.True(File.Exists(DocumentPath), "could not find " + DocumentPath);

        string text = File.ReadAllText(DocumentPath);

        // Each byte, written the way the table writes it. Not a strict format check - the point is
        // that a reader can find the real value somewhere in the document.
        string[] mustAppear = { "`0x1C`", "`0x0B`", "`0x08`", "`0x18`", "`0x1D`", "`0x10`" };

        for (int i = 0; i < mustAppear.Length; i++)
        {
            Assert.True(text.IndexOf(mustAppear[i], StringComparison.Ordinal) >= 0,
                "the arrow/HOME code " + mustAppear[i] + " is not in the keyboard reference any more");
        }
    }
}
