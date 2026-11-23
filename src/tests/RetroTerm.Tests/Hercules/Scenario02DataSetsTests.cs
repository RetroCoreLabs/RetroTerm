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
    /// Guide 02 of the Hercules TK5 guides, "Data sets and listing files", run against the live
    /// TK5 host: the RFE data set list with a typed prefix, LISTDS and LISTCAT at READY, and a
    /// sequential data set made with ATTRIB and ALLOC, renamed and deleted again.
    /// </summary>
    /// <remarks>
    /// Needs TK5 running and the settings in <see cref="Tk5Settings"/>; outside the routine gate
    /// (Category Hercules), and FAILS when the host does not answer. The test data set is
    /// user.RTT.SEQ; a run that failed half-way may leave it behind, so the first TSO steps delete
    /// it and its renamed form without looking at the answer.
    /// </remarks>
    [Trait("Category", "Hercules")]
    [Collection("Hercules")]
    public class Scenario02DataSetsTests
    {
        private readonly ITestOutputHelper _output;

        public Scenario02DataSetsTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public async Task TheDataSetScenarioRunsInRetroTerm()
        {
            Tk5Settings settings = Tk5Settings.Load();
            List<Tk5Step> steps = Steps(settings);

            await using RetroTermTk5Terminal retroTerm = await RetroTermTk5Terminal.OpenAsync(settings);
            List<Tk5Screen> screens = await Tk5Scenario.RunAsync(retroTerm, steps, _output);

            Assert.Equal(steps.Count, screens.Count);
        }

        [Fact]
        public async Task EveryScreenOfTheDataSetScenarioMatchesS3270()
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

        private static List<Tk5Step> Steps(Tk5Settings settings)
        {
            string user = settings.User;
            var steps = new List<Tk5Step>();
            Tk5CommonSteps.AddLogon(steps, settings);

            // Section 2a: =3.4, the prefix typed into its field, the list of the user's data sets.
            steps.Add(new Tk5Step("=3.4", async t =>
            {
                await t.TypeAsync("=3.4");
                await t.PressAsync("ENTER");
            }, s => s.Contains("RFE DSLIST") && s.Contains("Data set name prefix"), "the data set list panel", false));
            steps.Add(new Tk5Step("prefix", async t =>
            {
                // The panel puts the cursor in the prefix field, and the user's profile remembers
                // both fields from the last time (found 23 November 2026: SYS2.JCL was still in
                // the prefix). Erase EOF the prefix, type the user id, Tab to the volume field and
                // empty it, so the list is the same whatever an earlier session left there.
                await t.PressAsync("ERASEEOF");
                await t.TypeAsync(user);
                await t.PressAsync("TAB");
                await t.PressAsync("ERASEEOF");
                await t.PressAsync("ENTER");
            }, s => s.Contains("DATA-SET-NAME") && s.Contains(user + ".TEST.CNTL") && s.Contains("**END**"),
                "the list of " + user + "'s data sets", true));
            steps.Add(new Tk5Step("PF3 from the list", t => t.PressAsync("PF3"),
                s => s.Contains("RFE DSLIST") && s.Contains("Data set name prefix"), "the list panel", true));
            steps.Add(new Tk5Step("PF3 to the utilities", t => t.PressAsync("PF3"),
                s => s.Contains("Utility selection menu"), "the utility menu", true));
            steps.Add(new Tk5Step("PF3 to the menu", t => t.PressAsync("PF3"),
                s => s.Contains("ISPF primary option menu"), "the ISPF menu", true));
            steps.Add(new Tk5Step("PF3 to READY", t => t.PressAsync("PF3"),
                s => s.Contains("READY"), "READY", true));

            // Left over from a run that stopped half-way: delete without comparing the answer.
            steps.Add(Tk5CommonSteps.Tso("clean up RTT.SEQ", "DELETE RTT.SEQ", "", false));
            steps.Add(Tk5CommonSteps.Tso("clean up RTT.SEQ2", "DELETE RTT.SEQ2", "", false));

            // Section 1 and 2b: quoted and unquoted names, the catalog.
            steps.Add(Tk5CommonSteps.Tso("LISTDS quoted", "LISTDS 'SYS1.MACLIB'", "SYS1.MACLIB", true));
            steps.Add(Tk5CommonSteps.Tso("LISTDS unquoted", "LISTDS TEST.CNTL", user + ".TEST.CNTL", true));
            steps.Add(Tk5CommonSteps.Tso("LISTCAT LEVEL", "LISTCAT LEVEL(" + user + ")", user + ".TEST.CNTL", true));

            // Section 3b, 5: make, look at, rename and delete a sequential data set.
            steps.Add(Tk5CommonSteps.Tso("ATTRIB", "ATTRIB FB80 RECFM(F B) LRECL(80) BLKSIZE(3120) DSORG(PS)", "READY", true));
            steps.Add(Tk5CommonSteps.Tso("ALLOC", "ALLOC DA(RTT.SEQ) NEW CATALOG SPACE(1,1) TRACKS USING(FB80)", "READY", true));
            steps.Add(Tk5CommonSteps.Tso("FREE", "FREE DA(RTT.SEQ)", "READY", true));
            steps.Add(Tk5CommonSteps.Tso("LISTDS new", "LISTDS RTT.SEQ", user + ".RTT.SEQ", true));
            steps.Add(Tk5CommonSteps.Tso("RENAME", "RENAME RTT.SEQ RTT.SEQ2", "READY", true));
            steps.Add(Tk5CommonSteps.Tso("DELETE", "DELETE RTT.SEQ2", "ENTRY (A) " + user + ".RTT.SEQ2 DELETED", true));
            steps.Add(Tk5CommonSteps.Tso("FREE ATTRLIST", "FREE ATTRLIST(FB80)", "READY", true));

            steps.Add(Tk5CommonSteps.Logoff());
            return steps;
        }
    }
}
