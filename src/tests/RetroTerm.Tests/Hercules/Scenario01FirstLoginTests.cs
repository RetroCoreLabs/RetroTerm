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
    /// Guide 01 of the Hercules TK5 guides, "First login and 3270 basics", run against the live
    /// TK5 host: the logon panel, logging on, the ISPF menu and its moves, the READY prompt,
    /// PA1 out of a question, and logging off.
    /// </summary>
    /// <remarks>
    /// Needs TK5 running and the settings in <see cref="Tk5Settings"/>; outside the routine gate
    /// (Category Hercules), and FAILS when the host does not answer. Ronny, 22 November 2026.
    /// </remarks>
    [Trait("Category", "Hercules")]
    [Collection("Hercules")]
    public class Scenario01FirstLoginTests
    {
        private readonly ITestOutputHelper _output;

        public Scenario01FirstLoginTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public async Task TheFirstLoginScenarioRunsInRetroTerm()
        {
            Tk5Settings settings = Tk5Settings.Load();
            List<Tk5Step> steps = Steps(settings);

            await using RetroTermTk5Terminal retroTerm = await RetroTermTk5Terminal.OpenAsync(settings);
            List<Tk5Screen> screens = await Tk5Scenario.RunAsync(retroTerm, steps, _output);

            Assert.Equal(steps.Count, screens.Count);
        }

        [Fact]
        public async Task EveryScreenOfTheFirstLoginScenarioMatchesS3270()
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
            var steps = new List<Tk5Step>
            {
                // Section 3: the first screen is the logon panel, or Hercules' information panel
                // on a terminal address not used since MVS started; Reset then Clear leaves it.
                new Tk5Step("connect", t => Task.CompletedTask,
                    s => s.Contains("Logon ===>") || s.Contains("Hercules"), "the logon or Hercules panel", false),
                new Tk5Step("logon panel", async t =>
                {
                    Tk5Screen first = await t.ReadAsync();
                    if (!first.Contains("Logon ===>"))
                    {
                        await t.PressAsync("RESET");
                        await t.PressAsync("CLEAR");
                    }
                }, s => CursorIsInTheLogonField(s), "'Logon ===>' with the cursor in its field", true),

                // Section 4: user, password, welcome, fortune cookie, ISPF.
                new Tk5Step("user id", async t =>
                {
                    await t.TypeAsync(user);
                    await t.PressAsync("ENTER");
                }, s => s.Contains("ENTER CURRENT PASSWORD FOR " + user), "the password prompt", true),
                new Tk5Step("password", async t =>
                {
                    await t.TypeAsync(settings.Password);
                    await t.PressAsync("ENTER");
                }, s => s.Contains("LOGON IN PROGRESS") && s.Contains("***"), "the welcome screen", true),
                new Tk5Step("fortune cookie", t => t.PressAsync("ENTER"),
                    s => s.Contains("***") && !s.Contains("LOGON IN PROGRESS"), "the fortune cookie", false),
                new Tk5Step("ISPF menu", t => t.PressAsync("ENTER"),
                    s => s.Contains("ISPF primary option menu"), "the ISPF menu", true),

                // Section 5: option 3, PF3 back, =3.4 straight to the data set list.
                new Tk5Step("option 3", async t =>
                {
                    await t.TypeAsync("3");
                    await t.PressAsync("ENTER");
                }, s => s.Contains("Utility selection menu"), "the utility menu", true),
                new Tk5Step("PF3 back", t => t.PressAsync("PF3"),
                    s => s.Contains("ISPF primary option menu"), "the ISPF menu again", true),
                new Tk5Step("=3.4", async t =>
                {
                    await t.TypeAsync("=3.4");
                    await t.PressAsync("ENTER");
                }, s => s.Contains("DSLIST"), "the data set list", true),
                // =3.4 goes through the Utility menu, so PF3 from the list lands there.
                new Tk5Step("PF3 from the list", t => t.PressAsync("PF3"),
                    s => s.Contains("Utility selection menu"), "the utility menu after the list", true),
                new Tk5Step("PF3 to the menu", t => t.PressAsync("PF3"),
                    s => s.Contains("ISPF primary option menu"), "the ISPF menu", true),

                // Section 6: PF3 out of ISPF to READY, Clear, LISTCAT.
                new Tk5Step("PF3 to READY", t => t.PressAsync("PF3"),
                    s => s.Contains("READY"), "READY", true),
                new Tk5Step("LISTCAT", async t =>
                {
                    await t.PressAsync("CLEAR");
                    await t.WaitUntilAsync(s => true, "the cleared screen", Tk5Scenario.HostTimeoutMs);
                    await t.TypeAsync("LISTCAT");
                    await t.PressAsync("ENTER");
                }, s => s.Contains("IN CATALOG") && LastReadyAfterCatalog(s), "the catalog listing and READY", true),

                // Section 9: a command missing an operand asks for it; PA1 gets out.
                new Tk5Step("R asks a question", async t =>
                {
                    await t.TypeAsync("R");
                    await t.PressAsync("ENTER");
                }, s => s.Contains("ENTER DATA SET NAME"), "RENAME asking for a data set name", true),
                new Tk5Step("PA1", t => t.PressAsync("PA1"),
                    s => LastLineIsReady(s), "READY after PA1", true),

                // Section 7: LOGOFF back to the logon panel.
                new Tk5Step("LOGOFF", async t =>
                {
                    await t.TypeAsync("LOGOFF");
                    await t.PressAsync("ENTER");
                }, s => s.Contains("Logon ===>"), "the logon panel after LOGOFF", true),
            };
            return steps;
        }

        // The logon panel can arrive in more than one write after Reset and Clear: typing before
        // the write that puts the cursor in the field types onto the wrong place (found 22 November
        // 2026, when s3270's first run typed too early and the host simply showed the panel again).
        private static bool CursorIsInTheLogonField(Tk5Screen s)
        {
            for (int r = 0; r < Tk5Screen.Rows; r++)
            {
                int at = s.Lines[r].IndexOf("Logon ===>", System.StringComparison.Ordinal);
                if (at >= 0)
                {
                    return s.CursorRow == r && s.CursorColumn > at + 10;
                }
            }

            return false;
        }

        // READY printed after the IN CATALOG line, so the listing has finished.
        private static bool LastReadyAfterCatalog(Tk5Screen s)
        {
            int catalog = -1;
            int ready = -1;
            for (int r = 0; r < Tk5Screen.Rows; r++)
            {
                if (s.Lines[r].Contains("IN CATALOG", System.StringComparison.Ordinal))
                {
                    catalog = r;
                }

                if (s.Lines[r].TrimEnd() == " READY")
                {
                    ready = r;
                }
            }

            return catalog >= 0 && ready > catalog;
        }

        // The last non-blank line is READY: the question was answered by PA1.
        private static bool LastLineIsReady(Tk5Screen s)
        {
            for (int r = Tk5Screen.Rows - 1; r >= 0; r--)
            {
                string line = s.Lines[r].Trim();
                if (line.Length > 0)
                {
                    return line == "READY";
                }
            }

            return false;
        }
    }
}
