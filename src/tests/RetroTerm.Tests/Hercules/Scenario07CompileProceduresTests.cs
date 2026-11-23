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
    /// Guide 07 of the Hercules TK5 guides, "Compiling and running programs", section 3, run against the live TK5 host:
    /// the job that lists the installed compile procedures (TSO in batch, IKJEFT01, LISTDS of
    /// SYS1.PROCLIB and SYS2.PROCLIB), typed with the line editor, submitted and watched with STATUS.
    /// </summary>
    /// <remarks>
    /// Needs TK5 running and the settings in <see cref="Tk5Settings"/>; outside the routine gate
    /// (Category Hercules), and FAILS when the host does not answer. The member is
    /// user.TEST.CNTL(RTTPROC), deleted first in case an earlier run stopped half-way. Screens that
    /// carry a JES2 job number are not compared: each run gets a new number.
    /// </remarks>
    [Trait("Category", "Hercules")]
    [Collection("Hercules")]
    public class Scenario07CompileProceduresTests
    {
        private readonly ITestOutputHelper _output;

        public Scenario07CompileProceduresTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public async Task TheProcedureListScenarioRunsInRetroTerm()
        {
            Tk5Settings settings = Tk5Settings.Load();
            List<Tk5Step> steps = Steps(settings);

            await using RetroTermTk5Terminal retroTerm = await RetroTermTk5Terminal.OpenAsync(settings);
            List<Tk5Screen> screens = await Tk5Scenario.RunAsync(retroTerm, steps, _output);

            Assert.Equal(steps.Count, screens.Count);
        }

        [Fact]
        public async Task EveryScreenOfTheProcedureListScenarioMatchesS3270()
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

        // Whether the last line with anything on it is the given prompt.
        private static bool LastLineIs(Tk5Screen s, string prompt)
        {
            for (int r = Tk5Screen.Rows - 1; r >= 0; r--)
            {
                string line = s.Lines[r].Trim();
                if (line.Length > 0)
                {
                    return line == prompt;
                }
            }

            return false;
        }

        // One line typed in the line editor, then Enter, then wait for the keyboard: line mode
        // TSO locks the keyboard after every Enter until it has answered.
        private static async Task LineAsync(ITk5Terminal t, string text)
        {
            if (text.Length > 0)
            {
                await t.TypeAsync(text);
            }

            await t.PressAsync("ENTER");
            await t.WaitUntilAsync(s => true, "the editor's answer", Tk5Scenario.HostTimeoutMs);
        }

        private static List<Tk5Step> Steps(Tk5Settings settings)
        {
            string user = settings.User;
            string job = user + "P";
            var steps = new List<Tk5Step>();
            Tk5CommonSteps.AddLogon(steps, settings);
            steps.Add(new Tk5Step("PF3 to READY", t => t.PressAsync("PF3"),
                s => s.Contains("READY"), "READY", true));
            steps.Add(Tk5CommonSteps.Tso("clean up RTTPROC", "DELETE TEST.CNTL(RTTPROC)", "", false));

            // Section 3: the job typed with the line editor, an empty line to leave input mode.
            steps.Add(new Tk5Step("EDIT NEW", async t =>
            {
                await t.PressAsync("CLEAR");
                await t.WaitUntilAsync(s => true, "the cleared screen", Tk5Scenario.HostTimeoutMs);
                await t.TypeAsync("EDIT TEST.CNTL(RTTPROC) CNTL NEW");
                await t.PressAsync("ENTER");
            }, s => s.Contains("INPUT") && s.Contains("00010"), "INPUT and line 00010", true));
            steps.Add(new Tk5Step("type the job", async t =>
            {
                await LineAsync(t, "//" + job + " JOB (ACCT),'LIST PROCS',CLASS=A,MSGCLASS=A,MSGLEVEL=(1,1)");
                await LineAsync(t, "//TSO      EXEC PGM=IKJEFT01");
                await LineAsync(t, "//SYSTSPRT DD SYSOUT=*");
                await LineAsync(t, "//SYSTSIN  DD *");
                await LineAsync(t, "LISTDS 'SYS1.PROCLIB' MEMBERS");
                await LineAsync(t, "LISTDS 'SYS2.PROCLIB' MEMBERS");
                await LineAsync(t, "/*");
                await LineAsync(t, string.Empty);
            }, s => LastLineIs(s, "EDIT"), "EDIT mode", true));
            // Seven lines typed and seven listed fill the screen, and TSO would stop at "***"
            // before READY; Clear first, as a user would.
            steps.Add(new Tk5Step("LIST", async t =>
            {
                await t.PressAsync("CLEAR");
                await t.WaitUntilAsync(s => true, "the cleared screen", Tk5Scenario.HostTimeoutMs);
                await LineAsync(t, "LIST");
            },
                s => s.Contains("00020 //TSO      EXEC PGM=IKJEFT01") && s.Contains("00070 /*") && LastLineIs(s, "END OF DATA"), "the seven lines listed", true));

            // SAVE answers with nothing at all: the step is done when the keyboard is free again.
            steps.Add(new Tk5Step("SAVE", t => LineAsync(t, "SAVE"),
                s => s.Contains("SAVE"), "the keyboard back after SAVE", true));
            steps.Add(new Tk5Step("END", t => LineAsync(t, "END"),
                s => Tk5CommonSteps.LastLineIsReady(s), "READY after END", true));

            // Sections 3 and 5: SUBMIT, then STATUS until the job has run, printed (MSGCLASS=A
            // prints at once and removes it from the spool) and gone.
            steps.Add(Tk5CommonSteps.Tso("SUBMIT", "SUBMIT TEST.CNTL(RTTPROC)", "JOB " + job + "(JOB", false));
            steps.Add(new Tk5Step("STATUS until gone", async t =>
            {
                for (int i = 0; i < 30; i++)
                {
                    await t.PressAsync("CLEAR");
                    await t.WaitUntilAsync(s => true, "the cleared screen", Tk5Scenario.HostTimeoutMs);
                    await t.TypeAsync("STATUS " + job);
                    await t.PressAsync("ENTER");
                    Tk5Screen s = await t.WaitUntilAsync(Tk5CommonSteps.LastLineIsReady, "READY after STATUS", Tk5Scenario.HostTimeoutMs);
                    if (s.Contains("NOT FOUND"))
                    {
                        return;
                    }
                }
            }, s => s.Contains("JOB " + job + " NOT FOUND"), "the job gone from JES2", false));

            steps.Add(Tk5CommonSteps.Tso("delete the member", "DELETE TEST.CNTL(RTTPROC)", "READY", false));
            steps.Add(Tk5CommonSteps.Logoff());
            return steps;
        }
    }
}
