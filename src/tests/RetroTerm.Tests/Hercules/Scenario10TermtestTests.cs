//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/RetroTerm
// RetroTerm — Connecting today's machines to yesterday's systems
//

using System.Collections.Generic;
using System.Threading.Tasks;

using Xunit;

namespace RetroTerm.Tests.Hercules
{
    /// <summary>
    /// TK5's M.7 TERMTEST ("Verify 3270 terminal capabilities", TERMTEST 11.3) walked against the
    /// live host: its field-test PF keys, its two symbol-set tables and its help page, every
    /// screen compared with s3270 as a 3279-2.
    /// </summary>
    /// <remarks>
    /// <para>
    /// TERMTEST is the one TK5 panel found that uses code page 310 (EBCDIC.md, "TK5"): after a
    /// field-test PF key its record ends with 29 characters under SA(character set X'F1') and a
    /// Repeat to Address of GE X'D3'; SYMSET1 draws the whole set under SA(X'F1').
    /// </para>
    /// <para>
    /// Rows left out of the text comparison, each for a stated reason: the rows that print this
    /// terminal's own query reply (each terminal lists its own features, by design), and the code
    /// page 310 bytes where EBCDIC.md's full table chooses a different Unicode character than
    /// x3270. Fields, colours, highlighting and cursor are compared on every step.
    /// </para>
    /// <para>
    /// Never type Z into a TERMTEST field: Z is its exit command. It is left with CAN, which ends
    /// it without saving anything to its profile. Needs TK5 and the settings in
    /// <see cref="Tk5Settings"/>; category Hercules, FAILS when the host does not answer.
    /// </para>
    /// </remarks>
    [Trait("Category", "Hercules")]
    [Collection("Hercules")]
    public class Scenario10TermtestTests
    {
        private const string QueryRows = "the rows that print each terminal's own query reply";

        private const string Cp310Rows = "code page 310 bytes where EBCDIC.md chooses another Unicode character than x3270 (X'C2' and X'C3' on this row)";

        // Row 8 (counted from 0): "CHAR. MATRIX: 9 WIDE 16 DEEP", the cell size from the query
        // reply (ours 16, s3270's 12). Row 22: the CHARACTER ATTRIBUTES line with code page 310.
        private static readonly int[] FieldTestRows = { 8, 22 };

        private readonly ITestOutputHelper _output;

        public Scenario10TermtestTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public async Task TheTermtestWalkDrawsCodePage310InRetroTerm()
        {
            Tk5Settings settings = Tk5Settings.Load();
            List<Tk5Step> steps = Steps(settings);

            List<Tk5Screen> screens;
            await using (RetroTermTk5Terminal retroTerm = await RetroTermTk5Terminal.OpenAsync(settings))
            {
                screens = await Tk5Scenario.RunAsync(retroTerm, steps, _output);
            }

            // After PF2: the SA(X'F1') run (underlined A P L, then the four arrows), and the
            // RA of GE X'D3' at the end of the last row.
            Tk5Screen afterPf2 = screens[IndexOf(steps, "X and PF2")];
            Assert.StartsWith("APL↑↓→←", afterPf2.Lines[22].Substring(51), System.StringComparison.Ordinal);
            Assert.Equal(new string('┼', 23), afterPf2.Lines[23].Substring(57, 23));
        }

        [Fact]
        public async Task EveryScreenOfTheTermtestWalkMatchesAColourS3270()
        {
            Tk5Settings settings = Tk5Settings.Load().AsCompareUser();
            List<Tk5Step> steps = Steps(settings);

            List<Tk5Screen> reference;
            await using (S3270Terminal s3270 = await S3270Terminal.OpenAsync(settings, "3279-2"))
            {
                reference = await Tk5Scenario.RunAsync(s3270, steps, _output);
            }

            List<Tk5Screen> ours;
            await using (RetroTermTk5Terminal retroTerm = await RetroTermTk5Terminal.OpenAsync(settings))
            {
                ours = await Tk5Scenario.RunAsync(retroTerm, steps, _output);
            }

            Tk5Scenario.AssertSameScreens(steps, ours, reference, "s3270 (3279)");
        }

        private static int IndexOf(List<Tk5Step> steps, string name)
        {
            for (int i = 0; i < steps.Count; i++)
            {
                if (steps[i].Name == name)
                {
                    return i;
                }
            }

            throw new Xunit.Sdk.XunitException("no step " + name);
        }

        private static Tk5Step Command(string text, string expect, int[] rows, string why) =>
            new Tk5Step(text, async t =>
            {
                await t.TypeAsync(text);
                await t.PressAsync("ENTER");
            }, s => s.Contains(expect), "'" + expect + "' after " + text, rows, why);

        private static List<Tk5Step> Steps(Tk5Settings settings)
        {
            var steps = new List<Tk5Step>();
            Tk5CommonSteps.AddLogon(steps, settings);

            // The first screen: rows 8, 12 and 15-23 print the query reply TERMTEST got.
            steps.Add(new Tk5Step("option M.7", async t =>
            {
                await t.TypeAsync("M.7");
                await t.PressAsync("ENTER");
            }, s => s.Contains("TSO 3270 VDU TEST") && s.Contains("QUERY-REPLY-FIELD-DETAILS"), "the TERMTEST screen",
                new[] { 8, 12, 15, 16, 17, 18, 19, 20, 21, 22, 23 }, QueryRows));

            // A modified field and a colour key, then a highlight key in the next field, then the
            // GE key: the field-test keys (its title line, "FIELD TEST PFKS: 10-12, 1-7, 8(GE)").
            steps.Add(new Tk5Step("X and PF2", async t =>
            {
                await t.TypeAsync("X");
                await t.PressAsync("PF2");
            }, s => s.Contains("AID=F2(2)"), "the PF2 field test", FieldTestRows, QueryRows + "; " + Cp310Rows));
            steps.Add(new Tk5Step("Y and PF10", async t =>
            {
                await t.PressAsync("TAB");
                await t.TypeAsync("Y");
                await t.PressAsync("PF10");
            }, s => s.Contains("AID=7A(:)"), "the PF10 field test", FieldTestRows, QueryRows + "; " + Cp310Rows));
            steps.Add(new Tk5Step("PF8", t => t.PressAsync("PF8"), s => s.Contains("AID=F8(8)"), "the PF8 field test",
                FieldTestRows, QueryRows + "; " + Cp310Rows));

            // Row 1: "SYMBOL-SET ID=X'..'", the base set's local ID from the query reply: X'F0'
            // in ours (GA23-0059-4 p. 6-26, BUGS.md B15), X'00' in s3270's.
            steps.Add(Command("SYMSET0", "TERMINAL STORAGE ID=00", new[] { 1 }, QueryRows));
            steps.Add(new Tk5Step("ENTER from SYMSET0", t => t.PressAsync("ENTER"), s => s.Contains("AID=7D('"), "TERMTEST again",
                FieldTestRows, QueryRows + "; " + Cp310Rows));

            // Rows 10, 11, 13, 14, 15: table rows 9, A, C, D, E, holding X'95', X'9C', X'A4',
            // X'C1'-X'C3', X'E2'-X'E7' (EBCDIC.md, "Every disagreement between the sources").
            steps.Add(Command("SYMSET1", "TERMINAL STORAGE ID=01", new[] { 10, 11, 13, 14, 15 },
                "code page 310 bytes where EBCDIC.md chooses another Unicode character than x3270"));
            steps.Add(new Tk5Step("ENTER from SYMSET1", t => t.PressAsync("ENTER"), s => s.Contains("AID=7D('"), "TERMTEST again",
                FieldTestRows, QueryRows + "; " + Cp310Rows));

            steps.Add(Command("HELP", "LIST OF SUBCOMMANDS", System.Array.Empty<int>(), "every row compared"));
            steps.Add(new Tk5Step("ENTER from HELP", t => t.PressAsync("ENTER"), s => s.Contains("TSO 3270 VDU TEST"), "TERMTEST again",
                FieldTestRows, QueryRows + "; " + Cp310Rows));

            steps.Add(new Tk5Step("CAN", async t =>
            {
                await t.TypeAsync("CAN");
                await t.PressAsync("ENTER");
            }, s => s.Contains("Additional applications"), "the M menu, which CAN returns to", true));
            steps.Add(new Tk5Step("PF3 from M", t => t.PressAsync("PF3"), s => s.Contains("ISPF primary option menu"), "the primary menu again", true));
            steps.Add(new Tk5Step("PF3 to READY", t => t.PressAsync("PF3"), s => s.Contains("READY"), "READY", true));
            steps.Add(Tk5CommonSteps.Logoff());
            return steps;
        }
    }
}
