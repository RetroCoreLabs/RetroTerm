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
    /// Guide 05 of the Hercules TK5 guides, "System and operator commands", section 6: the TSO
    /// commands an ordinary user has, run against the live TK5 host - TIME, LISTBC, a SEND to
    /// oneself (a line the host writes to the terminal unasked), WHO, DUPTIME and PROFILE.
    /// </summary>
    /// <remarks>
    /// The rest of guide 05 is typed on the operator console (Hercules or its web page) or needs
    /// HERC01/HERC02's operator rights, which the test users do not have; that is not 3270
    /// terminal work. Needs TK5 running and the settings in <see cref="Tk5Settings"/>; outside the
    /// routine gate (Category Hercules), and FAILS when the host does not answer. Screens that
    /// carry the clock, the uptime or address-space numbers are not compared.
    /// </remarks>
    [Trait("Category", "Hercules")]
    [Collection("Hercules")]
    public class Scenario05SystemCommandsTests
    {
        private readonly ITestOutputHelper _output;

        public Scenario05SystemCommandsTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public async Task TheSystemCommandScenarioRunsInRetroTerm()
        {
            Tk5Settings settings = Tk5Settings.Load();
            List<Tk5Step> steps = Steps(settings);

            await using RetroTermTk5Terminal retroTerm = await RetroTermTk5Terminal.OpenAsync(settings);
            List<Tk5Screen> screens = await Tk5Scenario.RunAsync(retroTerm, steps, _output);

            Assert.Equal(steps.Count, screens.Count);
        }

        [Fact]
        public async Task EveryScreenOfTheSystemCommandScenarioMatchesS3270()
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

            steps.Add(Tk5CommonSteps.Tso("TIME", "TIME", "TIME-", false));
            steps.Add(Tk5CommonSteps.Tso("LISTBC", "LISTBC", "NO BROADCAST MESSAGES", true));

            // A message to oneself: the host writes the line to the terminal on its own, between
            // the command and READY.
            steps.Add(Tk5CommonSteps.Tso("SEND to myself", "SEND 'RTT HELLO' USER(" + user + ")", "RTT HELLO", true));
            steps.Add(Tk5CommonSteps.Tso("WHO", "WHO", user, false));
            steps.Add(Tk5CommonSteps.Tso("DUPTIME", "DUPTIME", "HAS BEEN UP FOR", false));
            steps.Add(Tk5CommonSteps.Tso("PROFILE", "PROFILE LIST", "PROMPT", true));

            steps.Add(Tk5CommonSteps.Logoff());
            return steps;
        }
    }
}
