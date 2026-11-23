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
    /// Guide 09 of the Hercules TK5 guides, "Other software in TK5", the ISPF menu walked option
    /// by option against the live TK5 host: each panel opened from the primary menu and left with
    /// PF3, every screen compared with s3270.
    /// </summary>
    /// <remarks>
    /// Option 5 (BATCH) is left out: the guide saw it create a new CARDS data set just by opening
    /// the panel. Needs TK5 running and the settings in <see cref="Tk5Settings"/>; outside the
    /// routine gate (Category Hercules), and FAILS when the host does not answer.
    /// </remarks>
    [Trait("Category", "Hercules")]
    [Collection("Hercules")]
    public class Scenario09IspfMenuTests
    {
        private const string Menu = "ISPF primary option menu";

        private readonly ITestOutputHelper _output;

        public Scenario09IspfMenuTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public async Task TheIspfMenuWalkRunsInRetroTerm()
        {
            Tk5Settings settings = Tk5Settings.Load();
            List<Tk5Step> steps = Steps(settings);

            await using RetroTermTk5Terminal retroTerm = await RetroTermTk5Terminal.OpenAsync(settings);
            List<Tk5Screen> screens = await Tk5Scenario.RunAsync(retroTerm, steps, _output);

            Assert.Equal(steps.Count, screens.Count);
        }

        [Fact]
        public async Task EveryScreenOfTheIspfMenuWalkMatchesS3270()
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

        // Opens one option from the primary menu and leaves it with PF3: two steps.
        private static void AddOption(List<Tk5Step> steps, string option, string expect)
        {
            steps.Add(new Tk5Step("option " + option, async t =>
            {
                await t.TypeAsync(option);
                await t.PressAsync("ENTER");
            }, s => s.Contains(expect) && !s.Contains(Menu), "'" + expect + "' (option " + option + ")", true));
            steps.Add(new Tk5Step("PF3 from " + option, t => t.PressAsync("PF3"),
                s => s.Contains(Menu), "the primary menu again", true));
        }

        private static List<Tk5Step> Steps(Tk5Settings settings)
        {
            var steps = new List<Tk5Step>();
            Tk5CommonSteps.AddLogon(steps, settings);

            AddOption(steps, "0", "ISPF PARAMETER OPTIONS");
            AddOption(steps, "3", "Utility selection menu");
            AddOption(steps, "6", "TSO COMMAND PROCESSOR");
            AddOption(steps, "C", "SUMMARY OF CHANGES");
            AddOption(steps, "M", "Additional applications");
            AddOption(steps, "T1", "Summary of changes of the TK5 system");

            // M.7 TERMTEST, TK5's 3270 terminal check: it decodes this terminal's own Query Reply
            // (24 lines, 80 columns, structured fields, the DDM reply for IND$FILE). Not compared:
            // the reply is each terminal's own, and s3270's is different by design. PF keys are
            // its field-test keys, so it is left with END, as its title line says.
            steps.Add(new Tk5Step("option M.7", async t =>
            {
                await t.TypeAsync("M.7");
                await t.PressAsync("ENTER");
            }, s => s.Contains("TSO 3270 VDU TEST") && s.Contains("24 LINES   80 COLUMNS"), "the TERMTEST screen", false));
            steps.Add(new Tk5Step("END from TERMTEST", async t =>
            {
                await t.TypeAsync("END");
                await t.PressAsync("ENTER");
            }, s => s.Contains("Additional applications"), "the M menu, which END returns to", true));
            steps.Add(new Tk5Step("PF3 from M", t => t.PressAsync("PF3"),
                s => s.Contains(Menu), "the primary menu again", true));

            // Back to READY for the logoff.
            steps.Add(new Tk5Step("PF3 to READY", t => t.PressAsync("PF3"),
                s => s.Contains("READY"), "READY", true));
            steps.Add(Tk5CommonSteps.Logoff());
            return steps;
        }
    }
}
