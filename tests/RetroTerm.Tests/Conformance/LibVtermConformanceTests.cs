using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Xunit;

namespace RetroTerm.Tests.Conformance;

/// <summary>
/// Runs libvterm's terminal conformance corpus against our ECMA-48 emulator.
///
/// Every other terminal test in this repo was written by us, from our own reading of the specs,
/// so a misreading we share with ourselves is invisible. This corpus is an OUTSIDE opinion —
/// written by libvterm's author and hardened by a decade of vim and neovim bug reports — and
/// eight of its files are DEC's own vttest screens turned into machine-checkable assertions,
/// which is the only way that suite can run without a human watching the screen.
///
/// HOW FAILURES ARE HANDLED. Each script is checked against a baseline count of disagreements
/// (<see cref="ExpectedFailures"/>). A regression pushes the count up and fails; a fix pushes it
/// down and ALSO fails, telling you to lower the number. Zero would be nicer, but hiding the
/// disagreements behind a Skip would be worse than counting them in public.
///
/// See <see cref="LibVtermScript"/> for the file format and for exactly which directives are not
/// executed and why.
/// </summary>
public class LibVtermConformanceTests
{
    private readonly ITestOutputHelper _output;

    public LibVtermConformanceTests(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// The corpus targets an xterm-like terminal; ANSI is our closest profile.
    /// </summary>
    private const string EmulatorType = "ANSI";

    /// <summary>
    /// Where the vendored .test files land next to the test assembly.
    /// </summary>
    private static string CorpusDirectory =>
        Path.Combine(AppContext.BaseDirectory, "Conformance", "libvterm");

    /// <summary>
    /// Known disagreements per script, as of the last time this was reviewed.
    ///
    /// A number here is NOT a blessing — it is a debt with an exact size. Any file missing from
    /// this table is expected to run clean.
    /// </summary>
    private static readonly Dictionary<string, int> ExpectedFailures = new(StringComparer.Ordinal)
    {
        // ── AFTER THE DEFERRED WRAP FIX: 97 disagreements became 68 ───────────────────────
        // Implementing the VT Last Column Flag (see Cursor.PendingWrap) cleared 29 of them and
        // emptied two whole files, one of which is a DEC vttest cursor-movement screen:
        //   20state_wrapping        3 -> 0
        //   90vttest_01-movement-2 19 -> 0
        //   11state_movecursor      8 -> 4
        //   16state_resize          3 -> 2
        //   32state_flow            2 -> 1
        //   90vttest_01-movement-1 11 -> 10
        // The two files that reached zero are no longer listed here at all — they must stay clean.
        //
        // WHAT STILL DIFFERS ABOUT WRAP, and why it is not a bug: libvterm REPRESENTS a pending
        // wrap by letting the cursor sit at column 80 or 81 on an 80-column screen. We keep the
        // cursor at column 79 and carry the flag beside it. Both models produce the same screen;
        // ours is what the terminal reports back to the host for DECXCPR, so it is the one we
        // want. Those readings are the remaining 16state_resize entries.
        // Then two more fixes took 68 to 63 and emptied two further files, so neither
        // 11state_movecursor nor 32state_flow appeared here any more:
        //   11state_movecursor  4 -> 0   the five missing ECMA-48 position sequences
        //                               (HPA `, HPR a, HPB j, VPR e, VPB k) now exist
        //   (11state_movecursor is back at 1 - VPB was later removed on the screen corpus's
        //    evidence, and its entry below says why)
        //   32state_flow        1 -> 0   erasing to end of line cancels the continuation flag
        // A fourth round took 59 to 49 and emptied 90vttest_01-movement-1 — DEC's own screen
        // alignment screen now matches exactly, every row. Two causes:
        //   - DECALN (ESC # 8) was decoded for the logs and never implemented, so the screen it
        //     fills with E's stayed blank and the E-frame in the middle never appeared.
        //   - "$SEQ  2  7:" is aligned with DOUBLE spaces, and the runner's Split(' ') turned
        //     that into rows 0..2 instead of 2..7. Five assertions never ran and two ran against
        //     the wrong rows. That file's assertion count went from 12 to 25 on the fix.
        // And a third round took 63 to 59, emptying 60screen_ascii. Two of its three were the
        // RUNNER lying rather than the emulator being wrong — see ReadRect and
        // DecodeExpectedRowValue in LibVtermConformanceRunner — and the third was real: CSI @
        // (ICH) had never been wired up, so a host opening a gap to insert text overwrote what
        // was already there.
        // 12state_scroll 1 -> 0, corpus now 48: LF and RI scrolled the scrolling region whenever
        // the cursor was OUTSIDE it, instead of only when sitting exactly on its boundary line.
        //
        // "Origin mode with DECSLRM": ESC[H after CSI ? 69 h and CSI 20;60 s should home to the
        // LEFT MARGIN, and does - on a terminal that has left and right margins. THIS CORPUS RUNS
        // AS "ANSI", whose profile does not claim TerminalFeatures.LeftRightMargins, so CSI ? 69 h
        // is refused and there are no margins to home into.
        //
        // The old note here said DECSLRM "is not implemented at all". That stopped being true when
        // the margins were built; the cursor rule was the last of it and was fixed on 2026-08-17
        // against the VT420 manual - see LeftRightMarginTests. What is left is a PROFILE question:
        // libvterm's terminal is a VT220-and-up, ours called ANSI is plain ECMA-48. Widening the
        // ANSI profile would change all 43 scripts at once and is not a cursor fix, so it stays
        // recorded here rather than quietly done.
        { "15state_mode.test", 1 },

        // ── THE TWO CORPORA DISAGREE, AND THIS IS THE ONE WE FOLLOW ───────────────────────
        // "Vertical Position Backward: PUSH \e[2k ?cursor = 4,2" - libvterm implements ECMA-48's
        // VPB (8.3.159, LINE POSITION BACKWARD) and moves the cursor up. xterm does not: its
        // ctlseqs lists CSI Ps e (VPR) and CSI Ps a (HPR) and has no entry for CSI Ps k at all,
        // and neither does DEC's VT330/VT340 programming manual.
        //
        // Two xterm.js screen fixtures settle what a real xterm does with it - nothing. In
        // t0032-VPB, "d ESC[k e ESC[k f ESC[k g" comes out as "defg" on ONE line, and in
        // t0033-VPB_scroll a CSI 36 k before the last line leaves that line exactly where it
        // was. Acting on the sequence spread one line of text over four rows, which is 13 rows
        // of screen wrong against 1 cursor reading here.
        //
        // So CSI k is ignored and this one assertion is expected to disagree. Note that HPB
        // (CSI Ps j) is in the same position in ctlseqs - absent - but NO fixture covers it, so
        // it is left implemented rather than removed on a guess.
        { "11state_movecursor.test", 1 },

        // ── THE SAME KIND OF DISAGREEMENT, DECIDED THE SAME WAY ───────────────────────────
        // "PUSH \e[L  ?cursor = 1,1" - after IL, libvterm leaves the column alone, and the script
        // carries its author's note: "ECMA-48 says we should move to line home, but neither xterm
        // nor xfce4-terminal do this".
        //
        // That is a claim about xterm, and a capture of xterm contradicts it in four places.
        // t0051-IL prints "QR" straight after CSI L and a real xterm starts it at column 0, not at
        // the column 2 the cursor was on; later in the same fixture a "b" after CSI L lands at
        // column 0 rather than at the column 79 where the wrap was armed. t0052-DL is the same
        // shape for CSI M, once from column 1 and once from column 79. ECMA-48 and DEC's manuals
        // both say to move to the line home position as well.
        //
        // So four rows of real screen beat one cursor reading, exactly as for VPB above, and the
        // column moves. See FinishLineEdit.
        { "13state_edit.test", 1 },

        // ── REFLOW: a feature we do not have at all ───────────────────────────────────────
        // libvterm re-flows wrapped paragraphs when the width changes. RetroTerm truncates.
        // Not a bug against any spec — DEC terminals could not resize — but it IS the behaviour
        // users expect from a windowed terminal, so the count is left visible rather than hidden.
        // REFLOW (TerminalBuffer.ResizeWithReflow) took the corpus from 47 to 28, and then
        // separating "never written" from "holds a written space" took it to 18. Both
        // 69screen_reflow (29 -> 0) and 90vttest_01-movement-3 (1 -> 0) are clean and gone from
        // this table; a cleared cell is codepoint 0 now, so a shell prompt "> " keeps the space
        // that was typed while the columns nothing ever touched still vanish.
        // 63screen_resize 9 -> 5, corpus now 14. A height change now slides the screen over the
        // history instead of keeping the top corner and cutting off the bottom — see
        // TerminalBuffer.ResizeHeightThroughHistory.
        //
        // FOUR of the five survivors are an ARCHITECTURAL difference, not a defect. In libvterm
        // the scrollback lives in the harness, OUTSIDE the terminal, so its RESET cannot clear it
        // and the script expects text from before the reset to pop back when the screen grows.
        // Our scrollback lives in the buffer and RIS clears it, which is what xterm does. Verified
        // by reading the script rather than assumed. The fifth is the pending-wrap representation.
        { "63screen_resize.test", 5 },
        { "16state_resize.test", 2 },    // the column-80/81 pending-wrap representation, see above

        // ── Remaining singles, each a genuinely separate question ─────────────────────────
        // 28state_dbl_wh 1 -> 0, corpus now 47: a double-width line holds half as many characters
        // and must wrap at column 39 of 80. Two causes, the second of which hid the first —
        // writing a character replaced the whole attribute word, so a line stopped being
        // double-width the moment anything was typed on it.
        // 61screen_unicode 4 -> 0, corpus now 9. Wide characters claim two cells and combining
        // marks claim none. The runner had to learn two things at the same time: the right-hand
        // half of a wide character is not a character and is skipped rather than reported as the
        // blank it physically is, and "é" written as U+0065 U+0301 is the same text as U+00E9,
        // so a normalisation difference is not a disagreement.
        // 65screen_protect 1 -> 0, corpus now 8: DECSCA (CSI Ps " q) and the selective erases
        // DECSED / DECSEL (CSI ? J / CSI ? K) are implemented. The private forms used to fall
        // into the private-MODE switch, which knows nothing about 'J' or 'K', so a host asking to
        // clear a form while keeping its labels cleared nothing at all.
    };

    /// <summary>
    /// Name used when the corpus has not been fetched, so the theory still has a case to run.
    /// </summary>
    private const string NotFetched = "(corpus not fetched)";

    public static IEnumerable<object[]> ScriptFiles()
    {
        // THE CORPUS IS NOT IN THIS REPOSITORY. It is libvterm's, fetched on demand by
        // tools\fetch-conformance-corpora.ps1, so a clean clone has none of it. A theory with no
        // cases is an error in xunit, and a missing corpus is not a failure - it only means fewer
        // outside opinions are available this run.
        if (!Directory.Exists(CorpusDirectory))
        {
            yield return new object[] { NotFetched };
            yield break;
        }

        string[] files = Directory.GetFiles(CorpusDirectory, "*.test");
        if (files.Length == 0)
        {
            yield return new object[] { NotFetched };
            yield break;
        }

        Array.Sort(files, StringComparer.Ordinal);

        for (int i = 0; i < files.Length; i++)
        {
            yield return new object[] { Path.GetFileName(files[i]) };
        }
    }

    [Fact]
    public void AFetchedCorpusIsCompleteRatherThanHalfThere()
    {
        // Absence is expected; a HALF-PRESENT corpus is not. It would quietly shrink the number of
        // scripts being run while every one of them still passed.
        if (!Directory.Exists(CorpusDirectory))
        {
            return;
        }

        string[] files = Directory.GetFiles(CorpusDirectory, "*.test");
        if (files.Length == 0)
        {
            return;
        }

        Assert.Equal(43, files.Length);
        Assert.True(File.Exists(Path.Combine(CorpusDirectory, "LICENSE-libvterm.txt")),
            "the MIT licence must sit beside the fetched corpus");
    }

    [Theory]
    [MemberData(nameof(ScriptFiles))]
    public void TheScriptAgreesWithUs(string fileName)
    {
        if (fileName == NotFetched)
        {
            _output.WriteLine("The libvterm corpus is not present. It is not committed to this "
                + "repository - fetch it with tools\\fetch-conformance-corpora.ps1.");
            return;
        }

        var runner = RunScript(fileName);

        ExpectedFailures.TryGetValue(fileName, out int expected);

        if (runner.Failures.Count != expected)
        {
            var report = new StringBuilder();
            report.Append(fileName).Append(": expected ").Append(expected)
                  .Append(" known disagreement(s), got ").Append(runner.Failures.Count)
                  .Append(" (").Append(runner.Passed).AppendLine(" assertions passed)");

            for (int i = 0; i < runner.Failures.Count; i++)
            {
                report.Append("  ").AppendLine(runner.Failures[i]);
            }

            Assert.Fail(report.ToString());
        }
    }

    [Fact]
    public void TheRunnerActuallyChecksSomething()
    {
        // The failure mode this guards against: a parser change that makes every line unparseable
        // would leave zero assertions run and every Theory case green. Count the real work.
        if (!Directory.Exists(CorpusDirectory))
        {
            return;     // fetched, not committed - see ScriptFiles
        }

        int passed = 0;
        int failed = 0;
        var unsupported = new Dictionary<string, int>(StringComparer.Ordinal);

        string[] files = Directory.GetFiles(CorpusDirectory, "*.test");
        if (files.Length == 0)
        {
            return;
        }

        Array.Sort(files, StringComparer.Ordinal);

        for (int i = 0; i < files.Length; i++)
        {
            string name = Path.GetFileName(files[i]);
            var runner = RunScript(name);
            passed += runner.Passed;
            failed += runner.Failures.Count;

            // Print the baselined disagreements themselves, not just their count. A number in
            // ExpectedFailures says how much debt there is; only the messages say what it IS,
            // and without them the next person has to break the baseline on purpose to find out.
            for (int f = 0; f < runner.Failures.Count; f++)
            {
                _output.WriteLine($"DISAGREES  {name}  {runner.Failures[f]}");
            }

            var enumerator = runner.Unsupported.GetEnumerator();
            while (enumerator.MoveNext())
            {
                unsupported.TryGetValue(enumerator.Current.Key, out int seen);
                unsupported[enumerator.Current.Key] = seen + enumerator.Current.Value;
            }
        }

        _output.WriteLine($"libvterm corpus: {passed} assertions passed, {failed} disagreed");
        _output.WriteLine("NOT executed (no equivalent in our architecture — see LibVtermScript):");

        var keys = new List<string>(unsupported.Keys);
        keys.Sort(StringComparer.Ordinal);
        for (int i = 0; i < keys.Count; i++)
        {
            _output.WriteLine($"  {keys[i]} x{unsupported[keys[i]].ToString(CultureInfo.InvariantCulture)}");
        }

        Assert.True(passed > 300, $"only {passed} assertions ran; the runner has stopped working");
    }

    private LibVtermConformanceRunner RunScript(string fileName)
    {
        string[] lines = File.ReadAllLines(Path.Combine(CorpusDirectory, fileName));
        var steps = LibVtermScript.Parse(lines);

        var runner = new LibVtermConformanceRunner(EmulatorType);
        runner.Run(steps);
        return runner;
    }
}
