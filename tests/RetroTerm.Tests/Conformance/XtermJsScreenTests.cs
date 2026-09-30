using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using RetroTerm.Core.Configuration;
using RetroTerm.Core.Terminal.Emulators;
using Xunit;

namespace RetroTerm.Tests.Conformance;

/// <summary>
/// Runs xterm.js's escape-sequence fixtures, whose expected screens were captured from REAL XTERM.
///
/// WHY THIS IS DIFFERENT FROM THE LIBVTERM CORPUS we already run. libvterm's scripts assert on
/// callbacks - "did a movecursor event fire with these coordinates" - which checks the plumbing.
/// These fixtures assert on the SCREEN: feed the bytes, then read what a person would see. A
/// terminal can raise every correct event and still paint the wrong picture, and nothing we had
/// could catch that.
///
/// The oracle is stronger too. xterm.js's own NOTES file says the .text files were made by running
/// the streams through a real xterm at 80x25 and copying the window contents out. So a disagreement
/// here means we differ from the thing everything else was written to imitate - not from somebody's
/// reading of a standard.
///
/// FORMAT. Each test is a pair: tNNNN-NAME.in is a raw byte stream, escape sequences and all, and
/// tNNNN-NAME.text is the screen it should produce, one line per row. There is no wrapper, no
/// harness protocol, nothing to parse.
///
/// HOW FAILURES ARE HANDLED, exactly as the libvterm runner does it: a baseline count per file in
/// <see cref="ExpectedFailures"/>. A regression pushes a count up and fails; a fix pushes it down
/// and ALSO fails, so the number has to be lowered deliberately. Counting the debt in public beats
/// hiding it behind a skip.
/// </summary>
public class XtermJsScreenTests
{
    private readonly ITestOutputHelper _output;

    public XtermJsScreenTests(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// The fixtures were captured from xterm, so xterm is what we answer them with.
    /// </summary>
    private const string EmulatorType = "XTERM";

    /// <summary>
    /// NOTES: "All tests are made for 80x25 terminal. Make sure to run tests with 80x25."
    /// </summary>
    private const int Columns = 80;
    private const int Rows = 25;

    /// <summary>
    /// Where the vendored fixtures land next to the test assembly.
    /// </summary>
    private static string CorpusDirectory =>
        Path.Combine(AppContext.BaseDirectory, "Conformance", "xtermjs");

    /// <summary>
    /// Fixtures whose expected screen we do not yet reproduce, with the number of rows that differ.
    ///
    /// A number here is a debt with an exact size, not a blessing. Any fixture missing from this
    /// table must match xterm exactly.
    /// </summary>
    private static readonly Dictionary<string, int> ExpectedFailures = new(StringComparer.Ordinal)
    {
        // 61 of 76 fixtures disagreed on the first run. Two corrections took that to 18:
        //
        //   the Last Column Flag was not cleared when the cursor LEFT a line, so a stream that
        //   filled the last column and then indexed gained an extra line advance later
        //
        //   the runner fed the raw bytes, but these screens were captured through a TTY, whose
        //   ONLCR turned every line feed into a carriage return and line feed. A bare line feed
        //   moves DOWN ONLY - correctly - so every full-width fixture stepped rightwards and
        //   differed on every row. That one was the runner's fault, not the emulator's.
        //
        // 58 fixtures and 879 rows became 25 fixtures and 226 rows. What is left is not one bug:
        // the tab stops were the largest group; HT and HTS are now clean and CHT and CBT are not.
        // CAN and SUB are gone. They were not recognised at all, so inside a CSI they ran as
        // ordinary controls and the sequence carried on - "ESC [ CAN D" finished as CUB. The
        // eighth line of each fixture needed a second fix: a CSI that has already gone wrong now
        // falls into ParserState.CsiIgnore instead of dropping to Ground and printing its own
        // remains. See CancelAndSubstituteTests.
        // Two things settled with those fixtures (2026-08-17): a real xterm shows NOTHING for SUB,
        // so the plan's earlier note about a replacement character was wrong; and ESC SUB is a
        // real Tektronix sequence, so the control byte is delivered BEFORE the ruined sequence is
        // abandoned.
        // The two VPB fixtures are gone: CSI Pn k is not a sequence xterm has, so ignoring it is
        // what makes "d ESC[k e ESC[k f" one line of text instead of four rows of one letter.
        // REP is gone, and it took IRM with it: CSI Pn b was not implemented at all, so every
        // repeat printed nothing. See RepeatCharacterTests for the two rules the fixture settled -
        // a control or a control sequence ends the run, and an explicit zero still repeats once.
        // Five two-row fixtures went together, and they were three separate rules:
        //
        //   t0050-ICH and t0055-EL - ICH and EL disarm the pending wrap. Both fixtures fill all 80
        //   columns, which arms the flag at column 79, then edit that column and print. A real
        //   xterm prints ON row, over the character the edit removed; we carried the flag through
        //   the edit and printed on the next row instead.
        //
        //   t0051-IL and t0052-DL - IL and DL move the cursor to the line home position after all.
        //   libvterm's script says they do not, and its author's note says xterm does not either;
        //   the capture of xterm says otherwise in all four rows. See FinishLineEdit, and the
        //   13state_edit entry in the libvterm baseline where that one reading now disagrees.
        //
        //   t0079-DECSTBM_VPR - VPR is not CUD. t0075-DECSTBM_CUU_CUD is the same stream with CSI
        //   25 B in place of CSI 25 e, and from the same row above the same scrolling region xterm
        //   answers row 19 for one and row 25 for the other. The bottom margin stops CUD and does
        //   not stop VPR. Trying the rule on CUD instead broke t0075 and t0078, which is how the
        //   two came to be separated rather than guessed at.
        // The alternate-screen save is gone: the DECSC slot belongs to the SCREEN, and there was
        // only one of them, so the alternate screen's save destroyed the main screen's. See
        // SavedCursorPerScreenTests.
        // Reverse wraparound is gone too - see ReverseWrapTests for the rule it needed.
        // t0084-CBT was the last name in this table, and it turned out to be the rule HT and CHT
        // already had, carried one step further: TABULATION DOES NOT CANCEL A PENDING WRAP EVEN
        // WHEN IT MOVES. The fixture proves both halves on its own - "at end:" leaves the "!" to
        // wrap onto the following row, while "at end with clipping:" sends ESC M ESC D in between,
        // which does cancel the wrap, and the "!" then lands at the very column CBT moved back to.
        // See TabulateToColumn and TabStopTests.
        //
        // THE TABLE IS NOW EMPTY: all 76 fixtures match xterm exactly. A name appearing here again
        // is a debt, and there is none.
    };

    /// <summary>
    /// Fixtures that are not run at all, with the reason.
    /// </summary>
    private static readonly Dictionary<string, string> Skipped = new(StringComparer.Ordinal)
    {
        // xterm.js's own NOTES: "t0031-HBP: no documentation at all about CSIj found - skipping".
        // Its .text file is in the corpus with no .in beside it, which is why the pair count is
        // 76 and 77 rather than equal.
        ["t0031-HPB"] = "xterm.js skipped it: no documentation for CSI j",
    };

    /// <summary>
    /// Name used when the corpus has not been fetched, so the theory still has a case to run.
    /// </summary>
    private const string NotFetched = "(corpus not fetched)";

    public static IEnumerable<object[]> Fixtures()
    {
        var directory = CorpusDirectory;
        if (!Directory.Exists(directory))
        {
            // THE CORPUS IS NOT IN THIS REPOSITORY. It belongs to another project and is fetched
            // on demand - see tools\fetch-conformance-corpora.ps1 - so a clean clone has none of
            // it. A theory with no cases is an error in xunit, and turning a missing corpus into a
            // failure would punish anyone who simply has not fetched it.
            yield return new object[] { NotFetched };
            yield break;
        }

        var inputs = Directory.GetFiles(directory, "*.in");
        if (inputs.Length == 0)
        {
            yield return new object[] { NotFetched };
            yield break;
        }

        Array.Sort(inputs, StringComparer.Ordinal);

        for (int i = 0; i < inputs.Length; i++)
        {
            yield return new object[] { Path.GetFileNameWithoutExtension(inputs[i]) };
        }
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void TheScreenMatchesWhatXtermProduced(string name)
    {
        if (name == NotFetched)
        {
            _output.WriteLine("The xterm.js fixtures are not present. They are not committed to "
                + "this repository - fetch them with tools\\fetch-conformance-corpora.ps1.");
            return;
        }

        if (Skipped.ContainsKey(name))
        {
            return;
        }

        var directory = CorpusDirectory;
        var input = File.ReadAllBytes(Path.Combine(directory, name + ".in"));
        var expected = ReadExpectedScreen(Path.Combine(directory, name + ".text"));

        var emulator = EmulatorFactory.CreateEmulator(EmulatorType, Columns, Rows, 1000);
        emulator.ProcessData(AsATtyWouldHaveDelivered(input));

        var actual = ReadScreen(emulator);

        int differing = 0;
        for (int row = 0; row < Rows; row++)
        {
            string want = row < expected.Count ? expected[row] : "";
            if (!string.Equals(want, actual[row], StringComparison.Ordinal))
            {
                differing++;
                _output.WriteLine("row " + row.ToString() + ":");
                _output.WriteLine("  xterm: [" + want + "]");
                _output.WriteLine("  ours:  [" + actual[row] + "]");
            }
        }

        ExpectedFailures.TryGetValue(name, out int allowed);

        Assert.True(differing == allowed,
            differing > allowed
                ? name + ": " + differing.ToString() + " rows differ from xterm, baseline allows " + allowed.ToString()
                : name + ": only " + differing.ToString() + " rows differ now (baseline says " + allowed.ToString()
                    + ") - lower the baseline");
    }

    [Fact]
    public void AFetchedCorpusIsCompleteRatherThanHalfThere()
    {
        // Absence is fine and expected - the corpus is fetched, not committed. A PARTIAL corpus is
        // not fine: it would quietly shrink the number of fixtures being checked while every one
        // of them still passed.
        var directory = CorpusDirectory;
        if (!Directory.Exists(directory))
        {
            return;
        }

        var inputs = Directory.GetFiles(directory, "*.in");
        if (inputs.Length == 0)
        {
            return;
        }

        Assert.Equal(76, inputs.Length);
    }

    /// <summary>
    /// Turns each bare line feed into a carriage return and line feed, the way the tty did when
    /// these screens were captured.
    /// </summary>
    /// <param name="raw">
    /// The fixture's bytes exactly as they are on disk.
    /// </param>
    /// <returns>
    /// The bytes xterm actually received.
    /// </returns>
    /// <remarks>
    /// THIS IS NOT A FUDGE, it is the missing half of the capture. xterm.js's run_tests.py writes
    /// each file to its standard output, which is a terminal in cooked mode, and the kernel's
    /// ONLCR turns every line feed into a carriage return and line feed on the way. So xterm never
    /// saw a bare line feed, and the expected screens have every line starting at column 0.
    ///
    /// Feeding the raw bytes instead made a line feed move DOWN WITHOUT RETURNING THE CARRIAGE -
    /// which is exactly right for a terminal, and exactly wrong as a reproduction of the capture.
    /// Every full-width fixture then differed on every row, and the emulator looked at fault when
    /// the runner was.
    ///
    /// A line feed already preceded by a carriage return is left alone: ONLCR does not double it.
    /// </remarks>
    private static byte[] AsATtyWouldHaveDelivered(byte[] raw)
    {
        const byte CarriageReturn = 0x0D;
        const byte LineFeed = 0x0A;

        int extra = 0;
        for (int i = 0; i < raw.Length; i++)
        {
            if (raw[i] == LineFeed && (i == 0 || raw[i - 1] != CarriageReturn))
            {
                extra++;
            }
        }

        if (extra == 0)
        {
            return raw;
        }

        var translated = new byte[raw.Length + extra];
        int write = 0;
        for (int i = 0; i < raw.Length; i++)
        {
            if (raw[i] == LineFeed && (i == 0 || raw[i - 1] != CarriageReturn))
            {
                translated[write++] = CarriageReturn;
            }
            translated[write++] = raw[i];
        }

        return translated;
    }

    /// <summary>
    /// Reads the expected screen.
    /// </summary>
    /// <param name="path">
    /// Path to a .text file.
    /// </param>
    /// <returns>
    /// One string per screen row, trailing blanks removed.
    /// </returns>
    /// <remarks>
    /// The files were made by copying a real xterm window into an editor, so trailing spaces on a
    /// row did not survive and the note in NOTES describes adding a 26th empty line by hand. Both
    /// sides are compared with their right-hand blanks removed, which is the only way a screen dump
    /// and a copied window can be compared at all.
    /// </remarks>
    private static List<string> ReadExpectedScreen(string path)
    {
        var lines = File.ReadAllLines(path);
        var rows = new List<string>(lines.Length);
        for (int i = 0; i < lines.Length; i++)
        {
            rows.Add(lines[i].TrimEnd());
        }
        return rows;
    }

    /// <summary>
    /// Dumps the emulator's screen the same way, one string per row.
    /// </summary>
    private static List<string> ReadScreen(TerminalEmulatorBase emulator)
    {
        var buffer = emulator.GetBuffer();
        var rows = new List<string>(Rows);
        var line = new StringBuilder(Columns);

        for (int row = 0; row < Rows; row++)
        {
            line.Clear();
            for (int col = 0; col < Columns; col++)
            {
                buffer.TryGetCell(row, col, out var cell);
                line.Append(cell.Codepoint == 0 ? ' ' : (char)cell.Codepoint);
            }
            rows.Add(line.ToString().TrimEnd());
        }

        return rows;
    }
}
