
using System.Text;
using RetroTerm.Core.Configuration;
using RetroTerm.Core.Terminal.Emulators;
using Xunit;

namespace RetroTerm.Tests.Terminal;

/// <summary>
/// Which DEC private modes a VT340 answers, checked against the documented list rather than against
/// what somebody remembered to test.
/// </summary>
/// <remarks>
/// <para><b>Why this file exists</b></para>
/// On 25 August 2026 a VT340 was asked to enter Tektronix mode and printed the plot bytes as text.
/// The cause was not a bug: <c>ESC[?38h</c> is not implemented. Nothing in the suite failed, because
/// every Tektronix test builds a Tek4014Emulator directly and none goes through a VT. The
/// UNHANDLED counter would have caught it the first time the sequence was sent to a VT, and it had
/// never been sent.
/// <para><b>So this test asks the whole question, not one instance of it</b></para>
/// It walks every private mode DEC documents and asserts the counter stays quiet. One forgotten mode
/// is a small thing. Not knowing WHICH modes are forgotten is the problem, and that is what this
/// closes.
/// <para><b>Where the list comes from</b></para>
/// The DECSET table in <c>spec\DEC\xterm-ctlseqs.txt</c>, which names the originating VT model for
/// each mode. Only modes attributed to a real DEC terminal are here - the rxvt and xterm-only ones
/// are somebody else's terminal and a VT340 is right to ignore them.
/// <para><b>Why the gaps are listed rather than simply asserted missing</b></para>
/// A test that asserts a feature is absent goes red on the day somebody implements it, and reads
/// like a regression. That has already cost this project three times. So the gaps are a named list
/// with a reason, and the failure message tells the reader what to do: take it off the list.
/// </remarks>
public class DecPrivateModeCoverageTests
{
    /// <summary>
    /// The escape character, built from its code so no raw control byte lands in this file.
    /// </summary>
    private const char Escape = (char)0x1B;

    /// <summary>
    /// One row of the DECSET table.
    /// </summary>
    private readonly struct DocumentedMode
    {
        /// <summary>
        /// The mode number, as it appears after the question mark.
        /// </summary>
        public readonly int Number;

        /// <summary>
        /// The DEC mnemonic.
        /// </summary>
        public readonly string Name;

        /// <summary>
        /// The terminal the manual attributes it to.
        /// </summary>
        public readonly string Model;

        public DocumentedMode(int number, string name, string model)
        {
            Number = number;
            Name = name;
            Model = model;
        }
    }

    /// <summary>
    /// Every private mode the DECSET table attributes to a DEC terminal.
    /// </summary>
    /// <remarks>
    /// Transcribed from <c>spec\DEC\xterm-ctlseqs.txt</c> on 25 August 2026. Modes the table gives
    /// only to xterm or rxvt - 10, 13, 14, 30, 35, 40, 41, 1000 and up - are deliberately absent:
    /// they are not DEC's, and whether this emulator wants them is a separate question from whether
    /// it is a faithful VT.
    ///
    /// <para><b>Three numbers mean two different things, and they are not all decided the same way</b></para>
    /// DEC and xterm gave 45, 46 and 47 to different features. Which reading wins is a judgement per
    /// number, not one rule, because what it costs to be wrong differs:
    ///
    ///  - 45 is XTREVWRAP here, not DECGPCS. Reverse wraparound is used by live software every day
    ///    and the graphic print colour syntax by almost nothing.
    ///  - 46 is DECGPBM here, not XTLOGGING. The spec itself calls xterm's logging "normally
    ///    disabled by a compile-time option", this program has no such feature to collide with, and
    ///    the DEC reading sits naturally beside 43 and 44 which are already DEC's. Corrected on
    ///    25 August 2026: this entry used to be dismissed here as "46 logging", which read half the
    ///    table - both meanings are in it, on consecutive lines.
    ///  - 47 is the xterm Alternate Screen Buffer, not DECGRPM. Ronny decided this on 2026-08-17,
    ///    and it is the one where being wrong is expensive: a mis-read 47 silently swaps a user's
    ///    screen mid-session. It stays absent from this list because pretending either answer is
    ///    the faithful one would bake that trade-off into a test.
    /// </remarks>
    private static readonly DocumentedMode[] DocumentedModes = new[]
    {
        new DocumentedMode(1, "DECCKM", "VT100"),
        new DocumentedMode(2, "DECANM", "VT100"),
        new DocumentedMode(3, "DECCOLM", "VT100"),
        new DocumentedMode(4, "DECSCLM", "VT100"),
        new DocumentedMode(5, "DECSCNM", "VT100"),
        new DocumentedMode(6, "DECOM", "VT100"),
        new DocumentedMode(7, "DECAWM", "VT100"),
        new DocumentedMode(8, "DECARM", "VT100"),
        new DocumentedMode(18, "DECPFF", "VT220"),
        new DocumentedMode(19, "DECPEX", "VT220"),
        new DocumentedMode(25, "DECTCEM", "VT220"),
        new DocumentedMode(38, "DECTEK", "VT240"),
        new DocumentedMode(42, "DECNRCM", "VT220"),
        new DocumentedMode(43, "DECGEPM", "VT340"),
        new DocumentedMode(44, "DECGPCM", "VT340"),
        new DocumentedMode(46, "DECGPBM", "VT340"),
        new DocumentedMode(66, "DECNKM", "VT320"),
        new DocumentedMode(67, "DECBKM", "VT340"),
        new DocumentedMode(69, "DECLRMM", "VT420"),
        new DocumentedMode(80, "DECSDM", "VT330"),
    };

    /// <summary>
    /// The modes known to be unimplemented, each with why it has not been done.
    /// </summary>
    /// <remarks>
    /// Every entry here is a real absence, NOT a mode that is deliberately ignored. A mode that is
    /// swallowed and does nothing would be worse than one that is honestly counted as unhandled -
    /// the counter would go quiet while the behaviour stayed missing, which is the exact failure
    /// this file exists to prevent.
    /// </remarks>
    private static readonly DocumentedMode[] KnownGaps = new DocumentedMode[]
    {
        // EMPTY, as of 25 August 2026 - every private mode the DECSET table attributes to a DEC
        // terminal is answered. It is kept rather than deleted because the next gap wants a place
        // to be written down with its reason, and an empty list says "nothing is outstanding" far
        // more clearly than a missing one.
    };

    /// <summary>
    /// Whether a mode number is on the known-gaps list.
    /// </summary>
    /// <param name="mode">
    /// The mode number to look for.
    /// </param>
    /// <returns>
    /// True when the mode is a recorded gap.
    /// </returns>
    private static bool IsKnownGap(int mode)
    {
        for (int i = 0; i < KnownGaps.Length; i++)
        {
            if (KnownGaps[i].Number == mode) return true;
        }
        return false;
    }

    /// <summary>
    /// A fresh VT340 for one mode.
    /// </summary>
    /// <returns>
    /// The emulator.
    /// </returns>
    /// <remarks>
    /// One per mode on purpose. Several of these modes change how everything after them is parsed -
    /// resetting DECANM puts the terminal into VT52, where the sequences below would not even be
    /// recognised as sequences - so sharing an emulator would make the result depend on the order
    /// the table happens to be written in.
    /// </remarks>
    private static TerminalEmulatorBase NewVt340()
    {
        return EmulatorFactory.CreateEmulator("VT340", 80, 24, 100);
    }

    /// <summary>
    /// Sends one mode set-and-reset and reports what the counter says about it.
    /// </summary>
    /// <param name="mode">
    /// The mode number.
    /// </param>
    /// <returns>
    /// How many times the emulator counted that mode as unrecognised, set and reset together.
    /// </returns>
    private static int UnhandledCountFor(int mode)
    {
        int total = 0;

        var setter = NewVt340();
        setter.ProcessData(Encoding.ASCII.GetBytes(Escape + "[?" + mode + "h"));
        total += CountForMode(setter, mode);

        var resetter = NewVt340();
        resetter.ProcessData(Encoding.ASCII.GetBytes(Escape + "[?" + mode + "l"));
        total += CountForMode(resetter, mode);

        return total;
    }

    /// <summary>
    /// Adds up the counter entries that name one mode.
    /// </summary>
    /// <param name="emulator">
    /// The terminal to read.
    /// </param>
    /// <param name="mode">
    /// The mode number to look for.
    /// </param>
    /// <returns>
    /// The total count across the set and reset shapes.
    /// </returns>
    /// <remarks>
    /// The counter keys these as "private mode N set" and "private mode N reset", so both shapes are
    /// checked rather than assuming which direction was sent. The word PRIVATE matters: ANSI mode 4
    /// is IRM and private mode 4 is DECSCLM, so the two numbering spaces must not share a key.
    /// Matching on the whole key rather than a prefix keeps mode 4 out of mode 44's count.
    /// </remarks>
    private static int CountForMode(TerminalEmulatorBase emulator, int mode)
    {
        int total = 0;
        var counts = emulator.UnrecognisedSequences;

        if (counts.TryGetValue("private mode " + mode + " set", out int set)) total += set;
        if (counts.TryGetValue("private mode " + mode + " reset", out int reset)) total += reset;

        return total;
    }

    [Fact]
    public void EveryDocumentedModeThatIsNotAKnownGapIsAnswered()
    {
        // The test that would have caught DECTEK. It asks the question nothing else in the suite
        // asks: does a VT answer everything DEC says a VT answers?
        var missing = new StringBuilder();

        for (int i = 0; i < DocumentedModes.Length; i++)
        {
            DocumentedMode documented = DocumentedModes[i];
            if (IsKnownGap(documented.Number)) continue;

            if (UnhandledCountFor(documented.Number) > 0)
            {
                missing.Append("\n  mode ").Append(documented.Number)
                       .Append(" (").Append(documented.Name).Append(", ").Append(documented.Model)
                       .Append(") is documented but not handled");
            }
        }

        Assert.True(missing.Length == 0,
            "A VT340 does not answer these documented DEC private modes:" + missing +
            "\nEither implement them, or add them to KnownGaps with a reason.");
    }

    [Fact]
    public void TheKnownGapsAreStillGaps()
    {
        // The other half, and the reason the gaps are a LIST rather than a bare assertion. When
        // somebody implements one of these, this test fails with an instruction rather than looking
        // like a regression - which is what a plain assert-it-is-missing test does, and it has cost
        // this project three times already.
        var nowHandled = new StringBuilder();

        for (int i = 0; i < KnownGaps.Length; i++)
        {
            DocumentedMode gap = KnownGaps[i];
            if (UnhandledCountFor(gap.Number) == 0)
            {
                nowHandled.Append("\n  mode ").Append(gap.Number)
                          .Append(" (").Append(gap.Name).Append(") - ").Append(gap.Model);
            }
        }

        Assert.True(nowHandled.Length == 0,
            "These modes are listed as gaps but the emulator now answers them:" + nowHandled +
            "\nGood news - take them off KnownGaps in this file, and off the table in " +
            "docs\\QUALITY-PLAN-2026-08-25.md.");
    }

    [Fact]
    public void EnteringTektronixModeIsNotSilentlyIgnored()
    {
        // The one that started it, pinned on its own so the story is findable.
        //
        // This test has now been through both of its lives, on the same day. It was written when
        // ESC[?38h was unimplemented, and asserted only that the mode was COUNTED as unknown -
        // because a sequence that vanishes without trace is worse than one honestly reported as
        // unknown, and the counter is how a missing feature gets noticed at all. Its comment said
        // that when DECTEK was built, the assertion had to change to check the mode engaged.
        //
        // DECTEK was built a few hours later. This is that change, and the shape is worth copying:
        // a test that names what it will become is a handover note, where a bare
        // assert-it-is-missing is a landmine for whoever implements the feature.
        var emulator = NewVt340();

        emulator.ProcessData(Encoding.ASCII.GetBytes(Escape + "[?38h"));

        Assert.False(emulator.UnrecognisedSequences.ContainsKey("private mode 38 set"),
            "ESC[?38h is implemented now and should no longer be counted as unknown.");
        Assert.True(emulator.TektronixMode,
            "ESC[?38h stopped being counted but did not actually engage Tektronix mode - which " +
            "would be the worst of both: a silent sequence AND a missing feature.");
    }
}
