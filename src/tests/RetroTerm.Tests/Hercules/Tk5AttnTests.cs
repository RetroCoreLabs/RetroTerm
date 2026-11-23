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
    /// What Attn does on TK5 (an open question in PLAN.md): pressed at TSO's READY, in RetroTerm
    /// and in s3270, and every screen compared.
    /// </summary>
    /// <remarks>
    /// Needs TK5 running; category Hercules, outside the routine gate.
    /// </remarks>
    [Trait("Category", "Hercules")]
    [Collection("Hercules")]
    public class Tk5AttnTests
    {
        private readonly ITestOutputHelper _output;

        public Tk5AttnTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public async Task AttnAtReadyMatchesS3270()
        {
            Tk5Settings settings = Tk5Settings.Load().AsCompareUser();
            List<Tk5Step> steps = Steps(settings);

            List<Tk5Screen> reference;
            await using (S3270Terminal s3270 = await S3270Terminal.OpenAsync(settings))
            {
                reference = await Tk5Scenario.RunAsync(s3270, steps, _output);
            }

            List<Tk5Screen> ours;
            await using (RetroTermTk5Terminal retroTerm = await RetroTermTk5Terminal.OpenAsync(settings))
            {
                ours = await Tk5Scenario.RunAsync(retroTerm, steps, _output);

                // The records, so a difference after Attn can be read byte by byte.
                _output.WriteLine(retroTerm.WireText());
            }

            Tk5Scenario.AssertSameScreens(steps, ours, reference, "s3270");
        }

        private static List<Tk5Step> Steps(Tk5Settings settings)
        {
            var steps = new List<Tk5Step>();
            Tk5CommonSteps.AddLogon(steps, settings);
            steps.Add(new Tk5Step("PF3 to READY", t => t.PressAsync("PF3"),
                s => s.Contains("READY"), "READY", true));
            steps.Add(new Tk5Step("Clear", t => t.PressAsync("CLEAR"), s => true, "the cleared screen", true));
            steps.Add(new Tk5Step("Attn", t => t.PressAsync("ATTN"), s => true, "the screen after Attn", true));
            steps.Add(new Tk5Step("Enter after Attn", t => t.PressAsync("ENTER"), s => true, "the screen after Enter", true));
            steps.Add(new Tk5Step("Clear again", t => t.PressAsync("CLEAR"), s => true, "the cleared screen", true));
            steps.Add(Tk5CommonSteps.Logoff());
            return steps;
        }
    }
}
