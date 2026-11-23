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
    /// Guide 03 of the Hercules TK5 guides, "Editing", run against the live TK5 host: a test PDS
    /// made at READY, a new member opened in REVEDIT (ISPF option 2), lines typed into it, FIND,
    /// CHANGE and SAVE on the command line, PF3 back out, the member seen by LISTDS, and the PDS
    /// deleted again.
    /// </summary>
    /// <remarks>
    /// Needs TK5 running and the settings in <see cref="Tk5Settings"/>; outside the routine gate
    /// (Category Hercules), and FAILS when the host does not answer. The test PDS is user.RTT.CNTL;
    /// a run that stopped half-way may leave it behind, so it is deleted first without looking at
    /// the answer.
    /// </remarks>
    [Trait("Category", "Hercules")]
    [Collection("Hercules")]
    public class Scenario03EditingTests
    {
        private readonly ITestOutputHelper _output;

        public Scenario03EditingTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public async Task TheEditingScenarioRunsInRetroTerm()
        {
            Tk5Settings settings = Tk5Settings.Load();
            List<Tk5Step> steps = Steps(settings);

            await using RetroTermTk5Terminal retroTerm = await RetroTermTk5Terminal.OpenAsync(settings);
            List<Tk5Screen> screens = await Tk5Scenario.RunAsync(retroTerm, steps, _output);

            Assert.Equal(steps.Count, screens.Count);
        }

        [Fact]
        public async Task EveryScreenOfTheEditingScenarioMatchesS3270()
        {
            Tk5Settings settings = Tk5Settings.Load().AsCompareUser();
            List<Tk5Step> steps = Steps(settings);

            // One after the other, as the same user: two logons at once would be refused.
            List<Tk5Screen> reference;
            await using (S3270Terminal s3270 = await S3270Terminal.OpenAsync(settings))
            {
                reference = await Tk5Scenario.RunAsync(s3270, steps, _output);
            }

            List<Tk5Screen> ours;
            await using (RetroTermTk5Terminal retroTerm = await RetroTermTk5Terminal.OpenAsync(settings))
            {
                ours = await Tk5Scenario.RunAsync(retroTerm, steps, _output);
            }

            Tk5Scenario.AssertSameScreens(steps, ours, reference, "s3270");
        }

        // Whether the screen holds the text in any case: REVEDIT writes some messages in mixed case.
        private static bool Has(Tk5Screen s, string text)
            => s.Text().ToUpperInvariant().Contains(text.ToUpperInvariant(), System.StringComparison.Ordinal);

        // A primary command: Home puts the cursor on the Command line (the first input field),
        // Erase EOF empties it, then the command and Enter.
        private static Tk5Step Primary(string command, string expect)
        {
            return new Tk5Step(command, async t =>
            {
                await t.PressAsync("HOME");
                await t.PressAsync("ERASEEOF");
                await t.TypeAsync(command);
                await t.PressAsync("ENTER");
            }, s => Has(s, expect), "'" + expect + "'", true);
        }

        private static List<Tk5Step> Steps(Tk5Settings settings)
        {
            string user = settings.User;
            var steps = new List<Tk5Step>();
            Tk5CommonSteps.AddLogon(steps, settings);
            steps.Add(new Tk5Step("PF3 to READY", t => t.PressAsync("PF3"),
                s => s.Contains("READY"), "READY", true));

            // Guide 02 section 3b: a PDS to edit in. Left over from an earlier run: delete first.
            steps.Add(Tk5CommonSteps.Tso("clean up RTT.CNTL", "DELETE RTT.CNTL", "", false));
            steps.Add(Tk5CommonSteps.Tso("ATTRIB", "ATTRIB FB80 RECFM(F B) LRECL(80) BLKSIZE(3120)", "READY", true));
            steps.Add(Tk5CommonSteps.Tso("ALLOC PDS", "ALLOC DA(RTT.CNTL) NEW CATALOG SPACE(2,1) TRACKS DIR(5) USING(FB80)", "READY", true));
            steps.Add(Tk5CommonSteps.Tso("FREE", "FREE DA(RTT.CNTL)", "READY", true));
            steps.Add(Tk5CommonSteps.Tso("FREE ATTRLIST", "FREE ATTRLIST(FB80)", "READY", true));

            // Guide 01 section 5: back into ISPF from READY.
            steps.Add(new Tk5Step("ISPF", async t =>
            {
                await t.PressAsync("CLEAR");
                await t.WaitUntilAsync(s => true, "the cleared screen", Tk5Scenario.HostTimeoutMs);
                await t.TypeAsync("ISPF");
                await t.PressAsync("ENTER");
            }, s => s.Contains("ISPF primary option menu"), "the ISPF menu", true));

            // Section 2: option 2, the data set name, a new member.
            steps.Add(new Tk5Step("option 2", async t =>
            {
                await t.TypeAsync("2");
                await t.PressAsync("ENTER");
            }, s => Has(s, "REVEDIT- ENTRY PANEL"), "the REVEDIT entry panel", false));
            steps.Add(new Tk5Step("open a new member", async t =>
            {
                // The cursor starts in PROJECT and the profile remembers every field from the last
                // time, so PROJECT, GROUP, TYPE and MEMBER are emptied on the way to DATA SET
                // NAME, and VOLUME SERIAL after it.
                for (int i = 0; i < 4; i++)
                {
                    await t.PressAsync("ERASEEOF");
                    await t.PressAsync("TAB");
                }

                await t.PressAsync("ERASEEOF");
                await t.TypeAsync("'" + user + ".RTT.CNTL(HELLO)'");
                await t.PressAsync("TAB");
                await t.PressAsync("ERASEEOF");
                await t.PressAsync("ENTER");
                // The top line normally names the member, but REVEDIT puts a message there when an
                // earlier session left its backup data set behind (seen 23 November 2026 after a
                // cancelled session: "REVEDIT1.BACKUP NOT REUSED"). That depends on the host's
                // history, not the terminal, so wait for the edit screen itself and do not compare.
            }, s => Has(s, "REVEDIT  ") && Has(s, "COLUMNS 1 72") && Has(s, "TOP OF DATA"), "the empty member", false));

            // Section 3: type three lines into the empty lines and press Enter. The cursor starts
            // on the Command line; New Line goes to the next row's first input field, which on an
            // empty line is its number column, and Tab from there to its data. The TOP OF DATA
            // row has an input field of its own in the number column (line commands such as I5
            // are typed there), so the first line is two New Lines down from the Command line.
            steps.Add(new Tk5Step("type lines", async t =>
            {
                await t.PressAsync("NEWLINE");
                await t.PressAsync("NEWLINE");
                await t.PressAsync("TAB");
                await t.TypeAsync("LINE ONE");
                await t.PressAsync("NEWLINE");
                await t.PressAsync("TAB");
                await t.TypeAsync("LINE TWO");
                await t.PressAsync("NEWLINE");
                await t.PressAsync("TAB");
                await t.TypeAsync("LINE THREE");
                await t.PressAsync("ENTER");
            }, s => s.Contains("000001 LINE ONE") && s.Contains("000003 LINE THREE"), "three numbered lines", true));

            // Section 4: FIND, CHANGE, SAVE.
            steps.Add(Primary("FIND TWO", "FOUND"));
            steps.Add(Primary("CHANGE LINE ROW ALL", "CHANGED"));
            steps.Add(Primary("SAVE", "DATA SAVED"));

            // Section 5: PF3 out through the entry panel and the menu to READY.
            steps.Add(new Tk5Step("PF3 from the editor", t => t.PressAsync("PF3"),
                s => Has(s, "REVEDIT- ENTRY PANEL"), "the entry panel", true));
            steps.Add(new Tk5Step("PF3 to the menu", t => t.PressAsync("PF3"),
                s => s.Contains("ISPF primary option menu"), "the ISPF menu", true));
            steps.Add(new Tk5Step("PF3 back to READY", t => t.PressAsync("PF3"),
                s => s.Contains("READY"), "READY", true));

            // The member is there, then the PDS goes.
            steps.Add(Tk5CommonSteps.Tso("LISTDS MEMBERS", "LISTDS RTT.CNTL MEMBERS", "HELLO", true));
            steps.Add(Tk5CommonSteps.Tso("DELETE", "DELETE RTT.CNTL", "ENTRY (A) " + user + ".RTT.CNTL DELETED", true));

            steps.Add(Tk5CommonSteps.Logoff());
            return steps;
        }
    }
}
