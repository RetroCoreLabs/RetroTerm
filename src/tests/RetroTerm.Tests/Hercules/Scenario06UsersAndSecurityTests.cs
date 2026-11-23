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
    /// Guide 06 of the Hercules TK5 guides, "Users and security", run against the live TK5 host
    /// as an ordinary user: RAKF refusing the users table, TSO refusing ACCOUNT, and the data set
    /// prefix that a name without quotes gets.
    /// </summary>
    /// <remarks>
    /// Adding users and changing passwords (sections 4 and 5) were never done on this system and
    /// change the shared host, so they are not here. Needs TK5 running and the settings in
    /// <see cref="Tk5Settings"/>; outside the routine gate (Category Hercules), and FAILS when the
    /// host does not answer.
    /// </remarks>
    [Trait("Category", "Hercules")]
    [Collection("Hercules")]
    public class Scenario06UsersAndSecurityTests
    {
        private readonly ITestOutputHelper _output;

        public Scenario06UsersAndSecurityTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public async Task TheSecurityScenarioRunsInRetroTerm()
        {
            Tk5Settings settings = Tk5Settings.Load();
            List<Tk5Step> steps = Steps(settings);

            await using RetroTermTk5Terminal retroTerm = await RetroTermTk5Terminal.OpenAsync(settings);
            List<Tk5Screen> screens = await Tk5Scenario.RunAsync(retroTerm, steps, _output);

            Assert.Equal(steps.Count, screens.Count);
        }

        [Fact]
        public async Task EveryScreenOfTheSecurityScenarioMatchesS3270()
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
            steps.Add(new Tk5Step("PF3 to READY", t => t.PressAsync("PF3"),
                s => s.Contains("READY"), "READY", true));

            // Section 3: RAKF says no to the users table.
            steps.Add(Tk5CommonSteps.Tso("RAKF refusal", "LIST 'SYS1.SECURE.CNTL(USERS)'", "USER NOT AUTHORIZED TO DATA SET", true));

            // Section 5: ACCOUNT is not for an ordinary user.
            steps.Add(Tk5CommonSteps.Tso("ACCOUNT refused", "ACCOUNT", "COMMAND NOT AUTHORIZED FOR " + user, true));

            // Common problems: without quotes the user id goes in front; with quotes it does not.
            steps.Add(Tk5CommonSteps.Tso("name without quotes", "LISTDS SYS2.JCLLIB", user + ".SYS2.JCLLIB", true));
            steps.Add(Tk5CommonSteps.Tso("name with quotes", "LISTDS 'SYS2.JCLLIB'", "--RECFM-LRECL-BLKSIZE-DSORG", true));

            steps.Add(Tk5CommonSteps.Logoff());
            return steps;
        }
    }
}
